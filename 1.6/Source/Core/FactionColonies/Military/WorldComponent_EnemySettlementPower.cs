using System.Collections.Generic;
using System.Linq;
using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Per-world cache of <see cref="EnemySettlementPower"/> entries, keyed by enemy
    /// <see cref="Settlement"/>. Decouples the squad-attack window's "estimated defender
    /// power" display from the battle's actual force generation so that:
    ///   1. Repeatedly opening the dialog produces the same value (no RNG side-channel),
    ///   2. The displayed range bounds the value the player will actually fight,
    ///   3. Empire Threat Level / threat adaptation still influence enemies over time
    ///      (refreshed every <see cref="RecomputeIntervalTicks"/>).
    /// </summary>
    public class WorldComponent_EnemySettlementPower : WorldComponent
    {
        public WorldComponent_EnemySettlementPower(World world) : base(world) { }

        public const int RecomputeIntervalTicks = GenDate.TicksPerDay * 5;

        private Dictionary<Settlement, EnemySettlementPower> powers
            = new Dictionary<Settlement, EnemySettlementPower>();

        /* Working lists for Scribe_Collections — required for Reference-keyed dictionaries.
         * See scribe-primer.md §5: the dict must be reconstructed in ResolvingCrossRefs from
         * two resolved lists. */
        private List<Settlement> _scribeKeys;
        private List<EnemySettlementPower> _scribeValues;

        private int nextRecomputeTick = -1;

        public IReadOnlyDictionary<Settlement, EnemySettlementPower> Powers => powers;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref powers, "powers",
                LookMode.Reference, LookMode.Deep,
                ref _scribeKeys, ref _scribeValues);
            Scribe_Values.Look(ref nextRecomputeTick, "nextRecomputeTick", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (powers is null) powers = new Dictionary<Settlement, EnemySettlementPower>();

                /* LookMode.Reference silently leaves entries with null keys when a referenced
                 * Settlement was destroyed or its load-ID didn't resolve. Drop those. */
                List<Settlement> stale = powers.Where(kv => kv.Key is null || kv.Key.Destroyed)
                                               .Select(kv => kv.Key).ToList();
                foreach (Settlement k in stale) powers.Remove(k);
            }
        }

        public override void WorldComponentTick()
        {
            if (Find.TickManager.TicksGame >= nextRecomputeTick)
            {
                RecomputeAll();
                nextRecomputeTick = Find.TickManager.TicksGame + RecomputeIntervalTicks;
            }
        }

        /// <summary>
        /// Returns the cached entry for <paramref name="settlement"/>, computing one on the
        /// spot if missing. Catches settlements created between recompute ticks.
        /// </summary>
        public EnemySettlementPower GetOrCompute(Settlement settlement)
        {
            if (settlement is null || settlement.Destroyed) return null;
            EnemySettlementPower p;
            if (powers.TryGetValue(settlement, out p)) return p;
            p = new EnemySettlementPower();
            ComputeBaseline(settlement, p);
            powers[settlement] = p;
            return p;
        }

        /// <summary>
        /// Refreshes baselines for all current non-Empire settlements and prunes stale entries.
        /// </summary>
        public void RecomputeAll()
        {
            HashSet<Settlement> current = new HashSet<Settlement>();
            foreach (Settlement s in Find.WorldObjects.Settlements)
            {
                if (s is null || s.Destroyed) continue;
                if (s is WorldSettlementFC) continue;       // skip Empire's own settlements
                if (s.Faction is null) continue;
                current.Add(s);
            }

            List<Settlement> stale = powers.Keys.Where(k => !current.Contains(k)).ToList();
            foreach (Settlement k in stale) powers.Remove(k);

            foreach (Settlement s in current)
            {
                EnemySettlementPower p;
                if (!powers.TryGetValue(s, out p))
                {
                    p = new EnemySettlementPower();
                    powers[s] = p;
                }
                ComputeBaseline(s, p);
            }
        }

        private static void ComputeBaseline(Settlement settlement, EnemySettlementPower power)
        {
            Faction faction = settlement.Faction;
            if (faction is null || faction.def is null) return;

            double level, efficiency;
            MilitaryUtil.ComputeFactionBaselinePower(faction, FactionCache.FactionComp,
                out level, out efficiency);

            power.level = level;
            power.efficiency = efficiency;
            power.levelVariance = MilitaryUtil.DefaultLevelVariance;
            power.efficiencyVariance = MilitaryUtil.DefaultEfficiencyVariance;
            power.lastComputedTick = Find.TickManager.TicksGame;
        }
    }
}
