using RimWorld;
using System.Linq;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Similar in function to Verse.Find. This class offers helpful, short accessors to comps and notable comp-subfields.
    /// <para>This cache needs to be invalidated any time the game loads or changes. Presently, this is done through a Harmony Postfix on Game.Dispose().</para>
    /// </summary>
    public static class FindFC
    {
        private static Faction _cachedColonyFaction = null;
        private static Faction _cachedPlayerFaction = null;
        private static FactionDef _cachedFactionDef = null;

        private static FactionFC _cachedFactionWorldComp = null;
        private static WorldComponent_EnemyPower _cachedEnemyPower = null;
        private static WorldComponent_Archive _cachedArchive = null;

        private static MilitaryOperationManager _cachedMilitaryManager = null;
        private static FCEventManager _cachedEventManager = null;
        private static TaxLedger _cachedTaxledger = null;

        public static Faction EmpireFaction => _cachedColonyFaction ?? (_cachedColonyFaction = Find.FactionManager?.FirstFactionOfDef(EmpireFactionDef));
        public static Faction PlayerFaction => _cachedPlayerFaction ?? (_cachedPlayerFaction = Find.FactionManager?.AllFactions?.FirstOrDefault(faction => faction.IsPlayer));
        public static FactionDef EmpireFactionDef => _cachedFactionDef ?? (_cachedFactionDef = DefDatabase<FactionDef>.GetNamed("PColony"));

        public static FactionFC FactionComp => _cachedFactionWorldComp ?? (_cachedFactionWorldComp = Find.World?.GetComponent<FactionFC>());
        public static WorldComponent_EnemyPower EnemyPower => _cachedEnemyPower ?? (_cachedEnemyPower = Find.World?.GetComponent<WorldComponent_EnemyPower>());
        public static WorldComponent_Archive Archive => _cachedArchive ?? (_cachedArchive = Find.World?.GetComponent<WorldComponent_Archive>());

        public static MilitaryOperationManager MilitaryManager => _cachedMilitaryManager ?? (_cachedMilitaryManager = FactionComp?.militaryOperationManager);
        public static FCEventManager EventManager => _cachedEventManager ?? (_cachedEventManager = FactionComp?.eventManager);
        public static TaxLedger TaxLedger => _cachedTaxledger ?? (_cachedTaxledger = FactionComp?.taxLedger);

        public static Map TaxMap => FactionComp?.TaxMap;

        public static void Invalidate()
        {
            _cachedColonyFaction = null;
            _cachedPlayerFaction = null;
            _cachedFactionDef = null;

            _cachedFactionWorldComp = null;
            _cachedEnemyPower = null;
            _cachedArchive = null;

            _cachedMilitaryManager = null;
            _cachedEventManager = null;
            _cachedTaxledger = null;
        }
        public static bool IsEmpireFaction(Faction f) => !(EmpireFaction is null) && f == EmpireFaction;
    }
}
