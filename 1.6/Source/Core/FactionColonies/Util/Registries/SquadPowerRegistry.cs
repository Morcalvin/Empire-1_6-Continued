using System;
using System.Collections.Generic;

namespace FactionColonies
{
    /// <summary>
    /// Resolves a <see cref="MercenarySquadFC"/> to its projected combat power
    /// (<see cref="SquadPower"/>) so the squad-first system can derive distinct
    /// per-squad forces — even for two squads stationed at the same settlement.
    /// <para>Submods register higher-priority <see cref="ISquadPowerProvider"/>s to
    /// layer veterancy, specialist bonuses, augmentations, etc. on top. The default
    /// provider runs at <c>Priority = int.MinValue</c> and always returns a value, so
    /// callers never need a null fallback.</para>
    /// </summary>
    public static class SquadPowerRegistry
    {
        private static readonly List<ISquadPowerProvider> _providers = new List<ISquadPowerProvider>();
        private static readonly DefaultSquadPowerProvider _default = new DefaultSquadPowerProvider();
        private static List<ISquadPowerProvider> _sortedCache;

        public static void Register(ISquadPowerProvider provider)
        {
            if (provider is null) return;
            if (!_providers.Contains(provider))
            {
                _providers.Add(provider);
                _sortedCache = null;
            }
        }

        public static void Unregister(ISquadPowerProvider provider)
        {
            if (_providers.Remove(provider)) _sortedCache = null;
        }

        public static void ClearAll()
        {
            _providers.Clear();
            _sortedCache = null;
        }

        public static IReadOnlyList<ISquadPowerProvider> Providers => _providers;

        /// <summary>Resolves <paramref name="squad"/> to its projected power. Walks
        /// registered providers from highest <see cref="ISquadPowerProvider.Priority"/>
        /// down; first non-null wins. Always falls through to the default loadout-cost
        /// inversion, so this method never returns invalid output for a non-null squad.</summary>
        public static SquadPower Resolve(MercenarySquadFC squad)
        {
            if (squad is null) return new SquadPower(1, 1);

            List<ISquadPowerProvider> sorted = GetSortedProviders();
            for (int i = 0; i < sorted.Count; i++)
            {
                ISquadPowerProvider provider = sorted[i];
                try
                {
                    SquadPower? result = provider.GetSquadPower(squad);
                    if (result.HasValue) return result.Value;
                }
                catch (Exception e)
                {
                    LogUtil.Error($"ISquadPowerProvider {provider.GetType().Name} threw in GetSquadPower: {e}");
                }
            }
            return _default.GetSquadPower(squad) ?? new SquadPower(1, 1);
        }

        private static List<ISquadPowerProvider> GetSortedProviders()
        {
            if (_sortedCache != null) return _sortedCache;
            List<ISquadPowerProvider> copy = new List<ISquadPowerProvider>(_providers.Count + 1);
            copy.AddRange(_providers);
            copy.Add(_default);
            copy.Sort((a, b) => b.Priority.CompareTo(a.Priority)); // highest first
            _sortedCache = copy;
            return _sortedCache;
        }
    }

    /// <summary>Default squad-power provider. Maps the squad's current equipped loadout
    /// cost (<see cref="MercenarySquadFC.GetCurrentLoadoutCost"/>) to a military level on
    /// the same 1-9 scale as <see cref="WorldSettlementFC.settlementMilitaryLevel"/> by
    /// inverting <see cref="MilitaryCustomizationUtil.CalculateSquadBudget"/>:
    /// <code>cost = 1000 + 500·L + 600·L²  →  L = (-500 + √(250000 + 2400·(cost-1000))) / 1200</code>
    /// Combat efficiency is taken from the squad's billet stat
    /// (<see cref="FCStatDefOf.militaryCombatEfficiency"/>); unassigned squads default to 1.0.
    /// </summary>
    public class DefaultSquadPowerProvider : ISquadPowerProvider
    {
        public int Priority => int.MinValue;

        public SquadPower? GetSquadPower(MercenarySquadFC squad)
        {
            if (squad is null) return null;

            double level = LevelFromCost(squad.GetCurrentLoadoutCost());

            double efficiency = 1.0;
            if (squad.settlement is object)
            {
                FactionFC faction = FactionCache.FactionComp;
                if (faction is object)
                {
                    efficiency = faction.GetStatValue(FCStatDefOf.militaryCombatEfficiency, squad.settlement);
                }
            }

            return new SquadPower(level, efficiency);
        }

        /// <summary>Inverse of <c>1000 + 500·L + 600·L²</c>. Floored at 1 (matches the
        /// minimum settlement military level) so a near-zero-cost squad still has a
        /// projection. Result is intentionally a double so downstream can blend it.</summary>
        public static double LevelFromCost(double cost)
        {
            if (cost <= 1000) return 1.0;
            double disc = 250000.0 + 2400.0 * (cost - 1000.0);
            if (disc < 0) return 1.0;
            double level = (-500.0 + Math.Sqrt(disc)) / 1200.0;
            return Math.Max(1.0, level);
        }
    }
}
