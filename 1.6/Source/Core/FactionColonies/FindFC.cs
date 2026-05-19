using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
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
        /*-*-*-*-*/
        /* Cached references: stable for the world/comp lifetime, invalidated in Invalidate() */
        /*-*-*-*-*/
        private static Faction _cachedColonyFaction = null;
        private static Faction _cachedPlayerFaction = null;
        private static FactionDef _cachedFactionDef = null;

        private static FactionFC _cachedFactionWorldComp = null;
        private static WorldComponent_EnemyPower _cachedEnemyPower = null;
        private static WorldComponent_Archive _cachedArchive = null;

        private static MilitaryOperationManager _cachedMilitaryManager = null;
        private static FCEventManager _cachedEventManager = null;
        private static TaxLedger _cachedTaxledger = null;
        private static PolicyManager _cachedPolicyManager = null;

        private static MilitaryFC _cachedMilitary = null;
        private static FCRoadBuilder _cachedRoadBuilder = null;
        private static EmpireThreatAdaptation _cachedThreatAdaptation = null;

        /*-*-*-*-*/
        /* Faction + def accessors */
        /*-*-*-*-*/
        public static Faction EmpireFaction => _cachedColonyFaction ?? (_cachedColonyFaction = Find.FactionManager?.FirstFactionOfDef(EmpireFactionDef));
        public static Faction PlayerFaction => _cachedPlayerFaction ?? (_cachedPlayerFaction = Find.FactionManager?.AllFactions?.FirstOrDefault(faction => faction.IsPlayer));
        public static FactionDef EmpireFactionDef => _cachedFactionDef ?? (_cachedFactionDef = DefDatabase<FactionDef>.GetNamed("PColony"));

        /*-*-*-*-*/
        /* World components */
        /*-*-*-*-*/
        public static FactionFC FactionComp => _cachedFactionWorldComp ?? (_cachedFactionWorldComp = Find.World?.GetComponent<FactionFC>());
        public static WorldComponent_EnemyPower EnemyPower => _cachedEnemyPower ?? (_cachedEnemyPower = Find.World?.GetComponent<WorldComponent_EnemyPower>());
        public static WorldComponent_Archive Archive => _cachedArchive ?? (_cachedArchive = Find.World?.GetComponent<WorldComponent_Archive>());

        /*-*-*-*-*/
        /* FactionFC sub-managers (stable field references, cached) */
        /*-*-*-*-*/
        public static MilitaryOperationManager MilitaryManager => _cachedMilitaryManager ?? (_cachedMilitaryManager = FactionComp?.militaryOperationManager);
        public static FCEventManager EventManager => _cachedEventManager ?? (_cachedEventManager = FactionComp?.eventManager);
        public static TaxLedger TaxLedger => _cachedTaxledger ?? (_cachedTaxledger = FactionComp?.taxLedger);
        public static PolicyManager PolicyManager => _cachedPolicyManager ?? (_cachedPolicyManager = FactionComp?.policyManager);
        public static MilitaryFC Military => _cachedMilitary ?? (_cachedMilitary = FactionComp?.military);
        public static FCRoadBuilder RoadBuilder => _cachedRoadBuilder ?? (_cachedRoadBuilder = FactionComp?.roadBuilder);
        public static EmpireThreatAdaptation ThreatAdaptation => _cachedThreatAdaptation ?? (_cachedThreatAdaptation = FactionComp?.threatAdaptation);

        /*-*-*-*-*/
        /* Mutable passthroughs: scalar/list-content fields that change at runtime; NEVER cache */
        /*-*-*-*-*/
        public static Map TaxMap => FactionComp?.TaxMap;
        public static TechLevel TechLevel => FactionComp?.techLevel ?? TechLevel.Undefined;
        public static int FactionLevel => FactionComp?.factionLevel ?? 0;
        public static IReadOnlyList<FCEvent> Events => FactionComp?.Events;
        public static PlanetTile CapitalLocation => FactionComp?.capitalLocation ?? PlanetTile.Invalid;
        public static List<WorldSettlementFC> Settlements => FactionComp?.settlements;

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
            _cachedPolicyManager = null;

            _cachedMilitary = null;
            _cachedRoadBuilder = null;
            _cachedThreatAdaptation = null;
        }
        public static bool IsEmpireFaction(Faction f) => !(EmpireFaction is null) && f == EmpireFaction;
    }
}
