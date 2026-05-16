using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public class FactionFC : WorldComponent, ILifecycleParticipant
    {
        #region Fields & Properties

        /* Core Identity */
        public string name = "FCPlayerFaction".Translate();
        public string title = "FCBastion".Translate();
        public Texture2D factionIcon = TexLoad.factionIcons[0];
        public string factionIconPath = TexLoad.factionIcons[0].name;
        public Color factionColorPrimary = Color.white;
        public Color factionColorSecondary = Color.white;
        public bool hasFactionColor;
        public bool hasFactionColorSecondary;
        public bool factionCreated;
        private int foundingTick = 0;
        public int FoundingTick => foundingTick;
        private Vector2 startingLongLat = new Vector2();
        public Vector2 StartingLongLat => startingLongLat;

        /* Capital & Maps */
        public PlanetTile capitalLocation = PlanetTile.Invalid;
        public string capitalPlanet;
        private Map taxMap;

        public Map TaxMap
        {
            get
            {
                if (taxMap is object) return taxMap;

                FactionFC comp = FactionCache.FactionComp;
                Map map = null;
                if (comp is object)
                {
                    map = Find.WorldObjects.SettlementAt(comp.capitalLocation)?.Map;
                }

                if (map is null)
                {
                    Map currentMap = Find.CurrentMap;
                    if (currentMap is object && currentMap.IsPlayerHome)
                    {
                        map = currentMap;
                    }
                    else
                    {
                        map = Find.AnyPlayerHomeMap;
                    }

                    if (map is object)
                    {
                        LogUtil.MessageForce(
                            "Unable to find a player-set tax map or a valid location for the capital. Please open the faction main menu tab and set the capital and tax map. Taxes were sent to the following random PlayerHomeMap " +
                            map.Parent.LabelCap);
                    }
                    else
                    {
                        LogUtil.Warning("TaxMap: No player home map found. Taxes cannot be delivered.");
                    }
                }

                return map;
            }
        }

        /* Settlements */
        /// <summary>
        /// Used by other mods to find our settlements. Move, rename, or otherwise modify at your own peril
        /// </summary>
        public List<WorldSettlementFC> settlements = new List<WorldSettlementFC>();

        /* Timing & Scheduling */
        public int taxTimeDue = Find.TickManager.TicksGame;
        public int timeStart = Find.TickManager.TicksGame;
        public int uiTimeUpdate;
        public int militaryTimeDue;
        public const int MercenaryHealTickInterval = GenDate.TicksPerHour;
        private bool firstTick = true;

        /* Lazy-Cached Averages */
        /* Faction averages — lazy-cached via dirtyAveragesCache */
        private double _averageHappiness = 100;
        private double _averageLoyalty = 100;
        private double _averageUnrest = 0;
        private double _averageProsperity = 100;
        private bool dirtyAveragesCache = true;
        public double averageHappiness { get { if (dirtyAveragesCache) RecomputeAverages(); return _averageHappiness; } }
        public double averageLoyalty { get { if (dirtyAveragesCache) RecomputeAverages(); return _averageLoyalty; } }
        public double averageUnrest { get { if (dirtyAveragesCache) RecomputeAverages(); return _averageUnrest; } }
        public double averageProsperity { get { if (dirtyAveragesCache) RecomputeAverages(); return _averageProsperity; } }

        /* Lazy-Cached Profit */
        /* Faction profit — lazy-cached via dirtyFactionProfitCache */
        private double _income;
        private double _upkeep;
        private double _profit;
        private bool dirtyFactionProfitCache = true;
        public double income { get { if (dirtyFactionProfitCache) RecomputeTotalProfit(); return _income; } }
        public double upkeep { get { if (dirtyFactionProfitCache) RecomputeTotalProfit(); return _upkeep; } }
        public double profit { get { if (dirtyFactionProfitCache) RecomputeTotalProfit(); return _profit; } }

        /* Period-averaged faction-wide silver flows. Derived directly from each settlement's
         * averaged values (no separate cache needed — those are themselves cached). Edict upkeep
         * is stable, paid in full each tax cycle, so it's added directly to averaged upkeep. */
        public bool HasTaxAverageData => settlements.Any(s => s.HasTaxAverageData);
        public double averageIncome => settlements.Sum(s => s.averageTotalIncome);
        public double averageUpkeep => settlements.Sum(s => s.averageTotalUpkeep) + GetEdictUpkeep();
        public double averageProfit => averageIncome - averageUpkeep;

        /* Lazy-Cached Tech Level */
        /* Tech level — lazy-cached via dirtyTechLevelCache */
        private TechLevel _techLevel = TechLevel.Undefined;
        private bool dirtyTechLevelCache = true;
        public TechLevel techLevel
        {
            get
            {
                if (dirtyTechLevelCache) RecomputeTechLevel();
                return _techLevel;
            }
        }

        /* Lazy-Cached Grand Thing List */
        private bool DirtyGrandThingListFlag = true;
        private List<ThingDef> grandThingList = null;

        /* Stat & Behavior Caches */
        private Dictionary<FCStatDef, double> cachedFactionStatValues = new Dictionary<FCStatDef, double>();
        private List<FCPolicyBehavior> _cachedBehaviors = null;
        public List<FCPolicyBehavior> cachedBehaviors
        {
            get
            {
                if (_cachedBehaviors is null)
                    RebuildBehaviorCache();
                return _cachedBehaviors;
            }
        }
        private HashSet<FCActionType> _cachedBlockedActions;
        private HashSet<FCActionType> _cachedEnabledActions;
        private HashSet<MilitaryJobDef> _cachedBlockedJobs;
        private HashSet<MilitaryJobDef> _cachedEnabledJobs;

        /* Policies & Traits */
        public List<FCPolicy> policies = new List<FCPolicy>();
        public List<FCPolicy> factionTraits = new List<FCPolicy>
        {
            new FCPolicy(FCPolicyDefOf.empty),
            new FCPolicy(FCPolicyDefOf.empty),
            new FCPolicy(FCPolicyDefOf.empty),
            new FCPolicy(FCPolicyDefOf.empty),
            new FCPolicy(FCPolicyDefOf.empty)
        };

        /* Edicts (toggleable faction-level policies) */
        public Dictionary<FCPolicyCategory, FCPolicy> edicts = new Dictionary<FCPolicyCategory, FCPolicy>();
        private HashSet<FCPolicyCategory> pendingEdictActivations = new HashSet<FCPolicyCategory>();

        // Minimum faction level required to unlock each edict category
        public static readonly Dictionary<FCPolicyCategory, int> EdictCategoryUnlockLevels = new Dictionary<FCPolicyCategory, int>
        {
            { FCPolicyCategory.Social, 2 },
            { FCPolicyCategory.Tax, 3 },
            { FCPolicyCategory.Doctrine, 3 },
            { FCPolicyCategory.Military, 4 }
        };

        /* Events & Bills */
        // LEGACY: populated only when loading pre-manager saves. Migrated into
        // eventManager during ExposeData(ResolvingCrossRefs) and then nulled out.
        // DO NOT READ. Use the Events property instead.
        private List<FCEvent> events = new List<FCEvent>();

        public FCEventManager eventManager = new FCEventManager();

        // The canonical read path for the event queue. Delegates to the manager.
        public IReadOnlyList<FCEvent> Events => eventManager.Events;
        public int EventsVersion => eventManager.Version;

        /// <summary>
        /// Holds and indexes all active <see cref="MilitaryOperation"/>s. Single source of truth
        /// for military operation state. <c>SendMilitary</c> / <c>AttackPlayerSettlement</c>
        /// route through <c>CreateOffensiveOp</c> / <c>CreateDefensiveOp</c> on this manager.
        /// </summary>
        public MilitaryOperationManager militaryOperationManager = new MilitaryOperationManager();

        public float randomEventLastAdded = 0f;
        public List<BillFC> Bills = new List<BillFC>();
        public List<BillFC> OldBills = new List<BillFC>();
        public bool autoResolveBills;
        public bool allowLatePayments = true;

        /* Resources */
        public List<ResourcePool> resourcePools = new List<ResourcePool>();
        public ThingWithComps powerOutput;
        public List<ResourceDisplay> factionResources = new List<ResourceDisplay>();
        public List<ResourceDisplay> FactionResources => factionResources;

        /* Military & Roads */
        public MilitaryFC military = new MilitaryFC();
        private MilitaryFC _legacyMilitary = null;
        public EmpireThreatAdaptation threatAdaptation = new EmpireThreatAdaptation();
        public FCRoadBuilder roadBuilder = new FCRoadBuilder();

        /* Caravans */
        public List<PlanetTile> settlementCaravansList = new List<PlanetTile>(); //list of locations caravans already sent to
        /// <summary>
        /// Player-selected caravan types. Strings are logical identifiers:
        /// resource defNames (e.g. "RTD_Food"), "Exotic", or "Slaver".
        /// Resolved to actual TraderKindDefs in UpdateFactionDef().
        /// </summary>
        public List<string> enabledCaravanTypes = new List<string>();

        /* Leveling */
        public const int MaxFactionLevel = 5;
        public int factionLevel = 1;
        public float factionXPCurrent = 0;
        public float factionXPGoal = 100;

        /* ID Counters */
        private int nextUnitId;
        private int nextSquadId;
        public int NextUnitID => ++nextUnitId;
        public int NextSquadID => ++nextSquadId;
        public int nextSettlementFCID = 1;
        public int nextMercenarySquadID = 1;
        public int nextMercenaryID = 1;
        public int nextTaxID = 1;
        public int nextBillID = 1;
        public int nextEventID = 1;
        public int nextPrisonerID = 1;
        public int nextMilitaryFireSupportID = 1;

        /* Filters & Misc */
        public XenotypeFilter xenotypeFilter;
        public AnimalFilter animalFilter;
        public List<PlanetLayerDef> layersForTilePicker = null;
        public float tradedAmount = 0;

        #endregion

        #region Constructor & Lifecycle

        public FactionFC(World world) : base(world)
        {
            // We used to do the harmony patching here, but I moved it to HarmonyPatcher.cs with a
            // [StaticConstructoreOnStartup] tag. Honestly not sure why the harmony patch was run here.
            // Leaving this comment mostly for posterity.
        }

        /// <summary>
        /// Called when the Empire faction is created.
        /// </summary>
        public void OnCreation()
        {
            foundingTick = Find.TickManager.TicksGame;
        }

        public string GetFoundingDate(bool full = true)
        {
            if (full)
            {
                return GenDate.DateFullStringAt(foundingTick, startingLongLat);
            }
            return GenDate.DateShortStringAt(foundingTick, startingLongLat);
        }

        #endregion

        #region Serialization & Initialization

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref title, "title");
            Scribe_Values.Look(ref foundingTick, "foundingTick", defaultValue: 0);
            Scribe_Values.Look(ref startingLongLat, "foundingLongLat");
            Scribe_Values.Look(ref capitalLocation, "capitalLocation");
            Scribe_Values.Look(ref capitalPlanet, "capitalPlanet");
            Scribe_References.Look(ref taxMap, "taxMap");
            Scribe_Values.Look(ref factionCreated, "factionCreated");

            Scribe_Values.Look(ref _averageHappiness, "averageHappiness");
            Scribe_Values.Look(ref _averageLoyalty, "averageLoyalty");
            Scribe_Values.Look(ref _averageUnrest, "averageUnrest");
            Scribe_Values.Look(ref _averageProsperity, "averageProsperity");

            Scribe_Values.Look(ref _income, "income");
            Scribe_Values.Look(ref _upkeep, "upkeep");
            Scribe_Values.Look(ref _profit, "profit");

            Scribe_Values.Look(ref taxTimeDue, "taxTimeDue");
            Scribe_Values.Look(ref timeStart, "timeStart", -1);
            Scribe_Values.Look(ref uiTimeUpdate, "uiTimeUpdate");
            Scribe_Values.Look(ref militaryTimeDue, "militaryTimeDue", -1);
            Scribe_Values.Look(ref _techLevel, "techLevel");
            Scribe_Values.Look(ref factionIconPath, "factionIconPath", "Base");
            Scribe_Values.Look(ref hasFactionColor, "hasFactionColor", false);
            Scribe_Values.Look(ref factionColorPrimary, "factionColorPrimary", Color.white);
            Scribe_Values.Look(ref hasFactionColorSecondary, "hasFactionColorSecondary", false);
            Scribe_Values.Look(ref factionColorSecondary, "factionColorSecondary", Color.white);

            Scribe_Collections.Look(ref settlements, "settlements", LookMode.Reference);
            Scribe_Collections.Look(ref policies, "factionPolicies", LookMode.Deep);

            // Legacy field. Still scribed under its original "events" name so old saves
            // load into it. The ResolvingCrossRefs block below moves its contents into
            // eventManager and nulls it out.
            Scribe_Collections.Look(ref events, "events", LookMode.Deep);

            // Manager owns events / cooldowns / fire counts going forward.
            Scribe_Deep.Look(ref eventManager, "eventManager");
            if (eventManager is null) eventManager = new FCEventManager();

            // Migrate pre-manager saves: move legacy events list into the manager.
            // Runs during ResolvingCrossRefs so it completes BEFORE any PostLoadInit
            // consumer (e.g. WorldSettlementFC stat-modifier reapply) reads Events.
            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs
                && events != null && events.Count > 0)
            {
                eventManager.SeedFromLegacy(events);
                events = null;
                LogUtil.MessageForce("FactionFC: migrated legacy events list into FCEventManager.");
            }

            Scribe_Deep.Look(ref militaryOperationManager, "militaryOperationManager");
            if (militaryOperationManager is null) militaryOperationManager = new MilitaryOperationManager();

            Scribe_Collections.Look(ref settlementCaravansList, "settlementCaravansList", LookMode.Value);
            Scribe_Collections.Look(ref enabledCaravanTypes, "enabledCaravanTypes", LookMode.Value);

            //New Production types
            Scribe_Collections.Look(ref resourcePools, "resourcePools", LookMode.Deep);
            Scribe_References.Look(ref powerOutput, "powerOutput");

            //save resources
            Scribe_Collections.Look(ref factionResources, "factionResources", LookMode.Deep);

            Scribe_Deep.Look(ref xenotypeFilter, "xenotypeFilter");
            Scribe_Deep.Look(ref animalFilter, "animalFilter");

            //Update
            Scribe_Values.Look(ref nextSettlementFCID, "nextSettlementFCID");

            //Military Data
            // Empire Refactored v1.5 renamed MilitaryCustomizationUtil to MilitaryFC. This block of code
            // handles migrating save data from a pre-1.5 save.
            Scribe_Deep.Look(ref military, "militaryFC");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Deep.Look(ref _legacyMilitary, "militaryCustomizationUtil");
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (military is null && _legacyMilitary is object) military = _legacyMilitary;
                if (military is null) military = new MilitaryFC();
                _legacyMilitary = null;
            }

            //Load ID tracking
            Scribe_Values.Look(ref nextMilitaryFireSupportID, "nextMilitaryFireSupportID", 1);
            Scribe_Values.Look(ref nextUnitId, "nextUnitID", 1);
            Scribe_Values.Look(ref nextSquadId, "nextSquadID", 1);
            Scribe_Values.Look(ref nextMercenaryID, "nextMercenaryID", 1);
            Scribe_Values.Look(ref nextMercenarySquadID, "nextMercenarySquadID", 1);
            Scribe_Values.Look(ref nextPrisonerID, "nextPrisonerID", 1);

            //New Tax Stuff
            Scribe_Values.Look(ref nextTaxID, "nextTaxID", 1);
            Scribe_Values.Look(ref nextBillID, "nextBillID", 1);
            Scribe_Values.Look(ref nextEventID, "nextEventID", 1);


            Scribe_Collections.Look(ref Bills, "Bills", LookMode.Deep);
            Scribe_Collections.Look(ref OldBills, "OldBills", LookMode.Deep);
            Scribe_Values.Look(ref autoResolveBills, "autoResolveBills");
            Scribe_Values.Look(ref allowLatePayments, "allowLatePayments", true);

            //Road builder
            Scribe_Deep.Look(ref roadBuilder, "roadBuilder");

            //Threat adaptation
            Scribe_Deep.Look(ref threatAdaptation, "threatAdaptation");
            if (threatAdaptation == null) threatAdaptation = new EmpireThreatAdaptation();

            // Legacy trait Scribe_Values removed — state is now in FCPolicyBehavior subclasses,
            // serialized via FCPolicy.ExposeData -> FCPolicyBehavior.ExposeData.

            //Settlement Leveling
            Scribe_Values.Look(ref factionLevel, "factionLevel");
            Scribe_Values.Look(ref factionXPCurrent, "factionXPCurrent");
            Scribe_Values.Look(ref factionXPGoal, "factionXPGoal");
            Scribe_Collections.Look(ref factionTraits, "factionTraits", LookMode.Deep);

            Scribe_Collections.Look(ref edicts, "edicts", LookMode.Value, LookMode.Deep);
            if (edicts == null) edicts = new Dictionary<FCPolicyCategory, FCPolicy>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) PostLoadInit();

            //Research Trading
            Scribe_Values.Look(ref tradedAmount, "tradedAmount");

            //Random Event
            Scribe_Values.Look(ref randomEventLastAdded, "randomEventLastAddedTick");
            // eventCooldowns / eventFireCounts now live on eventManager (scribed above).
        }

        private void ScrubNullSettlements(string caller = "")
        {
            int removed = settlements.RemoveAll(s => s is null);
            if (removed > 0)
                LogUtil.Warning($"{caller}: Removed {removed} null settlement reference(s) from save data.");
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            LogUtil.MessageForce($"Finalizing init of FactionFC. fromload: {fromLoad}");

            /* Shared init. Runs on both new-game and load paths; all idempotent. */
            RebuildFactionResources();
            EnsureCaravanTypesPopulated();
            EnsureResourcePools();
            LifecycleRegistry.Register(this);

            // Rebuild op indices from `active` whether we just migrated a save or not — cheap
            // and always-correct even on a fresh-game start (no-op when active is empty).
            militaryOperationManager?.RebuildIndices();

            if (fromLoad)
            {
                /* Load path. Scribe.mode == LoadingVars here; cross-refs NOT resolved,
                 * maps NOT loaded. Filter finalize is deferred to firstTick because
                 * xenotypeFilter.FinalizeInit reaches into CustomXenotypesForReading,
                 * which calls Scribe.ForceStop if Scribe is still active. And if the
                 * Scribe is ForceStopped during load, then everything breaks.
                 * And I do mean everything. The game straight-up crashes. */
                ApplySavedTechLevelToFactionDef();
            }
            else
            {
                /* New-world path. Scribe is Inactive, disk I/O is legal. */
                EnsureFiltersInitialized();
            }

            /* Rebuild caravan trader kinds last, once factionResources, settlements, and tech
             * level are settled. If production hasn't computed yet (new world, or load path
             * where caches still warm up), the helper preserves the FactionDef's existing list
             * rather than clobbering it with an empty result. */
            RebuildCaravanTraderKinds();
        }

        /* Rebuilt on each game init from DefDatabase, ensures defs stay in sync across load. */
        private void RebuildFactionResources()
        {
            factionResources.Clear();
            foreach (ResourceTypeDef resourceTypeDef in DefDatabase<ResourceTypeDef>.AllDefs)
            {
                factionResources.Add(new ResourceDisplay(resourceTypeDef));
                LogUtil.Message($"Added ResourceDisplay for resourceTypeDef {resourceTypeDef} to FactionFC.factionResources");
            }
            factionResources.Sort(ResourceDisplay.SortForUI);
        }

        private void EnsureCaravanTypesPopulated()
        {
            if (enabledCaravanTypes.NullOrEmpty())
            {
                LogUtil.Warning("Null or empty enabledCaravanTypes - Creating and filling list");
                InitEnabledCaravanTypes();
            }
        }

        /* Apply saved tech level to FactionDef early — must happen before anything
         * reads faction.def.techLevel directly. Calls UpdateFactionDef directly instead
         * of going through RecomputeTechLevel, which has side effects (xenotypeFilter
         * FinalizeInit) that depend on deferred initialization. */
        private void ApplySavedTechLevelToFactionDef()
        {
            if (_techLevel <= TechLevel.Undefined) return;
            Faction playerColonyfaction = FactionCache.PlayerColonyFaction;
            if (playerColonyfaction != null && playerColonyfaction.def.techLevel < _techLevel)
            {
                UpdateFactionDef(_techLevel, ref playerColonyfaction);
            }
        }

        /* Ensures both filters exist and are initialized. Idempotent (safe to call
         * multiple times). Called from FinalizeInit on the new-world path and from
         * FirstTick on the load path.
         *
         * MUST be called with Scribe.mode == Inactive. xenotypeFilter.FinalizeInit
         * reads custom xenotypes from disk via InitLoadingMetaHeaderOnly, which calls
         * Scribe.ForceStop() when Scribe is active, destroying the active save-load
         * pipeline and nulling all cross-references.
         * In other words, the game crashes and burns. */
        private void EnsureFiltersInitialized()
        {
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                LogUtil.Error($"EnsureFiltersInitialized called with Scribe.mode={Scribe.mode}. Skipping to avoid Scribe.ForceStop trap.");
                return;
            }

            bool animalWasInitialized = false;
            if (animalFilter is null)
            {
                LogUtil.Warning("Null animalFilter detected - Creating new one");
                animalFilter = new AnimalFilter();
            }
            if (!animalFilter.IsInitialized)
            {
                animalFilter.FinalizeInit();
                animalWasInitialized = true;
            }

            if (xenotypeFilter is null)
            {
                LogUtil.Warning("Null xenotypeFilter detected - Creating new one");
                xenotypeFilter = new XenotypeFilter(this);
            }
            /* Force xenotype re-finalize if animal filter was just initialized; xeno
             * filter depends on animal filter state during its own finalization. */
            if (!xenotypeFilter.IsInitialized || animalWasInitialized)
            {
                xenotypeFilter.FinalizeInit(this);
            }
        }

        /* Cross-ref-dependent post-load work. Invoked from ExposeData's PostLoadInit
         * branch — by this point settlements/edicts/events cross-refs are all resolved. */
        private void PostLoadInit()
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit)
            {
                LogUtil.Error($"FactionFC.PostLoadInit called during Scribe mode {Scribe.mode}");
                return;
            }
            ScrubNullSettlements("FactionFC.PostLoadInit");
            RebuildPendingEdictActivations();

            // Squad-first refactor migration: bind any legacy comp.militarySquad onto the squad
            // itself (sets squad.settlement) and propagate comp.autoDefend to squad.autoDefend.
            // Runs unconditionally because the legacy buffers are [Unsaved] — once drained, they
            // stay null on subsequent loads.
            MilitaryMigrationUtil.MigrateLegacyComp_MilitarySquad(this);

            // Drain pre-refactor military operation state into the new MilitaryOperationManager.
            // Idempotent: only runs when the manager is empty AND legacy state is present in the
            // loaded save (post-refactor saves write the manager directly and skip migration).
            if (militaryOperationManager is object && militaryOperationManager.IsEmpty
                && MilitaryMigrationUtil.AnyLegacyStatePresent(this))
            {
                MilitaryMigrationUtil.Migrate(this);
            }
            militaryOperationManager?.RebuildIndices();
        }

        private void RebuildPendingEdictActivations()
        {
            pendingEdictActivations.Clear();
            foreach (var kvp in edicts)
            {
                if (kvp.Value != null && !kvp.Value.IsFullyActive)
                    pendingEdictActivations.Add(kvp.Key);
            }
        }

        #endregion

        public override void WorldComponentUpdate()
        {
            if (roadBuilder.shouldDrawPaths)
            {
                roadBuilder.DrawPaths();
            }
        }

        #region Tick Loop

        private void FirstTick(Faction faction)
        {
            // Settlement resource assignments aren't actually available when we first create the resource display list
            //   in FinalizeInit. So set the display caches as dirty here so they get properly calculated the next time
            //   the UI shows up (or anything else tries to access them)
            SetAllDirtyResourceDisplayCaches();
            
            /* Scribe.mode is Inactive by firstTick — filter FinalizeInit is safe.
             * On the load path, this is where deferred filter init actually happens.
             * On the new-world path, FinalizeInit already initialized them; this is a no-op. */
            EnsureFiltersInitialized();

            /* Re-register with LifecycleRegistry in case ClearCaches ran after FinalizeInit
             * (happens during Game.InitNewGame; ClearCaches postfix clears the registry
             * after World.FinalizeInit already registered us during world generation). */
            LifecycleRegistry.Register(this);

            /* Built-in stateless squad-assignment validators. */
            SquadAssignmentRegistry.Register(new SquadCapValidator());
            SquadAssignmentRegistry.Register(new SquadSizeValidator());
            SquadAssignmentRegistry.Register(new SquadValueValidator());

            roadBuilder.FirstTick();

            if (!(faction is null))
            {
                _ = techLevel;
                if (TexLoad.factionIcons.Any())
                {
                    factionIcon = TexLoad.factionIcons.FirstOrFallback(obj => obj.name == factionIconPath,
                        TexLoad.factionIcons[0]);
                    UpdateFactionIcon(ref faction, "FactionIcons/" + factionIcon.name);
                    factionIconPath = factionIcon.name;
                }
                else
                {
                    LogUtil.Error("No faction icons loaded. Cannot set faction icon.");
                }

                if (!name.NullOrEmpty() && faction.Name != name)
                {
                    faction.Name = name;
                }

                if (hasFactionColor)
                    faction.color = factionColorPrimary;
            }

            military.CheckMilitaryUtilForErrors();

            // Auto-open patch notes for each mod that has new entries exceeding the player's threshold
            if (FCSettings.patchNoteAutoOpenThreshold != PatchNoteType.Undefined)
            {
                HashSet<string> modIds = new HashSet<string>();
                foreach (PatchNoteDef def in DefDatabase<PatchNoteDef>.AllDefsListForReading)
                    modIds.Add(def.modId);

                foreach (string modId in modIds)
                {
                    PatchNoteDef latest = PatchNoteDef.GetLatestForMod(modId);
                    if (latest is null) continue;
                    FCSettings.GetLastSeenVersion(modId, out int maj, out int min, out int pat);
                    if (latest.IsNewerThan(maj, min, pat)
                        && latest.GetPatchNoteType >= FCSettings.patchNoteAutoOpenThreshold)
                    {
                        Find.WindowStack.Add(new PatchNotesDisplayWindow(modId));
                    }
                }
            }

            /* Get the longlat of the player's starting location. This will be used when calculating founding dates. */
            Map playerHome = Find.AnyPlayerHomeMap;
            if (playerHome is null)
            {
                LogUtil.Warning("Found NULL for player map on first tick. This probably shouldn't happen...");
                startingLongLat = default(Vector2);
            }
            else
            {
                startingLongLat = Find.WorldGrid.LongLatOf(playerHome.Tile);
            }

            /* Rebuild caravan trader kinds last, once factionResources, settlements, and tech
             * level are settled. If production hasn't computed yet (new world, or load path
             * where caches still warm up), the helper preserves the FactionDef's existing list
             * rather than clobbering it with an empty result. */
            RebuildCaravanTraderKinds();
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            Faction faction = FactionCache.PlayerColonyFaction;
            if (firstTick)
            {
                FirstTick(faction);
                firstTick = false;
            }
            int ticksGame = Find.TickManager.TicksGame;

            // The FireSupportTick governs when artillery shells enter the map, which requires tick precision.
            // The function will early-return if there are no active fire supports, so the overhead in the no-active-support case is hopefully minimal
            FireSupportTick();
            if (faction is object)
            {
                // TickActions dispatches the tick to interfaces and registries, so it has to run every tick.
                TickActions();
            }

            // Rare tick
            if (ticksGame % 250 == 0)
            {
                FCEventMaker.ProcessEvents();
                BillUtility.ProcessBills();
                if (pendingEdictActivations.Count > 0)
                    CheckEdictActivations();
                if (faction is object)
                    roadBuilder.RoadTick();
            }

            // Hourly tick
            if (ticksGame % MercenaryHealTickInterval == 0)
            {
                military?.TickMercenaryHealing(MercenaryHealTickInterval);
                military?.TickAnimalReplacement();
            }

            // Daily tick
            if (ticksGame % GenDate.TicksPerDay == 0 && !(faction is null))
            {
                ValidateSettlementCaravansList();
                RecoverOrphanedConstructions(ticksGame);

                if (faction.leader is null || faction.leader.Dead)
                    ColonyUtil.CreatePlayerFactionLeader(faction);

                // Just for future's sake; StatTick expects a non-null faction. If it's ever moved from this if-block, remember to keep the null check
                StatTick();
            }

            // These checks have variable tick times, so they're in charge of their own tick guards
            TaxTick(faction);
            MilitaryTick(faction);
            threatAdaptation.Tick();
        }
        public void TaxTick(Faction faction)
        {
            if (faction is null || Find.TickManager.TicksGame < taxTimeDue)
                return;

            AddTax();
            taxTimeDue = Find.TickManager.TicksGame + FCSettings.timeBetweenTaxes;

            if (autoResolveBills)
                PaymentUtil.AutoresolveBills(Bills);

            // Rebuild caravan trader kinds to reflect current worker assignments
            RebuildCaravanTraderKinds();
        }

        public void StatTick()
        {
            // Tick guard moved up into the world component tick
            UpdateSettlementStats();
            AccumulateDailyProduction();
            DirtyAveragesCache();
            SyncGoodwillWithAverages();
            RelationsUtilFC.ResetPlayerColonyRelations();
            UpdateDailyResourcePools();
            MakeRandomEvent();
        }

        public void MilitaryTick(Faction faction)
        {
            if (Find.TickManager.TicksGame >= militaryTimeDue)
            {
                if (faction != null &&
                    !FCSettings.disableHostileMilitaryActions &&
                    Find.TickManager.TicksGame > (timeStart + GenDate.TicksPerSeason))
                {
                    //if military actions not disabled or game has not passed through the first season

                    if (settlements.Any() || RaidTargetRegistry.Targets.Count > 0)
                    {
                        List<WorldSettlementFC> validSettlements = settlements
                            .Where(s => s.MilitaryComp?.isUnderAttack != true && s.settlementDef.canBeRaided)
                            .ToList();
                        List<IRaidTarget> validExternalTargets = RaidTargetRegistry.Targets
                            .Where(t => !t.IsUnderAttack)
                            .ToList();

                        if (validSettlements.Any() || validExternalTargets.Any())
                        {
                            double etl = ThreatScalingUtil.ComputeEmpireThreatLevel(this);
                            Faction enemy = ThreatScalingUtil.PickWeightedEnemyFaction(etl);
                            if (enemy != null)
                            {
                                List<WorldSettlementFC> raidableSettlements = validSettlements
                                    .Where(s => s.settlementDef.GetSettlementTypeExtension()?.CanBeRaidedByFaction(enemy) != false)
                                    .ToList();

                                float settlementTotalWeight = raidableSettlements.Sum(
                                    s => (float)GetMilitaryTargetWeight(s.settlementMilitaryLevel) * s.settlementDef.raidTargetingWeight
                                         * RaidWeightRegistry.GetCombinedWeight(s, enemy));
                                float externalTotalWeight = validExternalTargets.Sum(
                                    t => (float)GetMilitaryTargetWeight(t.MilitaryLevel));
                                float totalWeight = settlementTotalWeight + externalTotalWeight;

                                EnemyPower attackerEntry = FactionCache.EnemyPower?.GetOrCompute(enemy);
                                MilitaryForce attackingForce = attackerEntry?.SampleBattleForce(enemy, handicap: true);
                                if (attackingForce is null)
                                {
                                    LogUtil.Warning($"AI attack from {enemy?.Name} aborted: no power entry resolvable.");
                                }
                                else if (Rand.Value * totalWeight < settlementTotalWeight && raidableSettlements.Any())
                                {
                                    WorldSettlementFC target = raidableSettlements.RandomElementByWeight(
                                        s => (float)GetMilitaryTargetWeight(s.settlementMilitaryLevel) * s.settlementDef.raidTargetingWeight
                                             * RaidWeightRegistry.GetCombinedWeight(s, enemy));
                                    MilitaryOperationsUtil.AttackPlayerSettlement(attackingForce, target, enemy);
                                }
                                else if (validExternalTargets.Any())
                                {
                                    IRaidTarget target = validExternalTargets.RandomElementByWeight(
                                        t => (float)GetMilitaryTargetWeight(t.MilitaryLevel));
                                    MilitaryOperationsUtil.AttackRaidTarget(attackingForce, target, enemy);
                                }
                            }
                        }
                    }
                }

                militaryTimeDue = Find.TickManager.TicksGame + (GenDate.TicksPerDay * ThreatScalingUtil.ComputeScaledAttackInterval(this));
            }
        }

        private static int GetMilitaryTargetWeight(int militaryLevel)
        {
            switch (militaryLevel)
            {
                case 0:
                case 1:
                    return 10;
                case 2:
                case 3:
                    return 7;
                case 4:
                case 5:
                    return 3;
                default:
                    return 1;
            }
        }

        public void FireSupportTick()
        {
            if (military.fireSupport is null)
            {
                military.fireSupport = new List<MilitaryFireSupport>();
            }
            if (military.fireSupport.Count == 0)
            {
                return;
            }

            //Process ongoing fire supports
            military.fireSupport.RemoveAll(support => support.ShouldBeOver);
            military.fireSupport.ForEach(support => support.Process());
        }

        public void TickActions()
        {
            // Dispatch Tick to all active behavior instances
            ForEachBehavior(b => b.Tick(this));
        }

        #endregion

        #region Lazy Caches

        public void DirtyFactionProfitCache()
        {
            dirtyFactionProfitCache = true;
        }

        private void RecomputeTotalProfit()
        {
            _income = settlements.Sum(s => s.totalIncome);
            _upkeep = settlements.Sum(s => s.totalUpkeep) + GetEdictUpkeep();
            _profit = _income - _upkeep;
            dirtyFactionProfitCache = false;
        }

        public void DirtyAveragesCache()
        {
            dirtyAveragesCache = true;
        }

        private void RecomputeAverages()
        {
            double avgHappiness = 0;
            double avgLoyalty = 0;
            double avgUnrest = 0;
            double avgProsperity = 0;

            if (settlements.Count > 0)
            {
                foreach (WorldSettlementFC settlement in settlements)
                {
                    if (settlement is null) continue;
                    avgHappiness += settlement.happiness;
                    avgLoyalty += settlement.loyalty;
                    avgUnrest += settlement.unrest;
                    avgProsperity += settlement.prosperity;
                }

                avgHappiness /= settlements.Count;
                avgLoyalty /= settlements.Count;
                avgUnrest /= settlements.Count;
                avgProsperity /= settlements.Count;
            }

            _averageHappiness = avgHappiness;
            _averageLoyalty = avgLoyalty;
            _averageUnrest = avgUnrest;
            _averageProsperity = avgProsperity;
            dirtyAveragesCache = false;
        }

        public void DirtyTechLevelCache()
        {
            dirtyTechLevelCache = true;
        }

        private static readonly TechLevel[] TechLevelDescending =
        {
            TechLevel.Archotech,
            TechLevel.Ultra,
            TechLevel.Spacer,
            TechLevel.Industrial,
            TechLevel.Medieval,
            TechLevel.Neolithic,
        };

        private void RecomputeTechLevel()
        {
            TechLevel curTechLevel = _techLevel;
            bool medievalOnly = FCSettings.medievalTechOnly;
            TechLevel newLevel;
            TechLevel playerTech = FactionCache.PlayerFaction?.def?.techLevel ?? TechLevel.Neolithic;

            if (FCSettings.mirrorPlayerTechLevel)
            {
                // Mirror mode: pin Empire tech to the player faction's tech level.
                if (playerTech < TechLevel.Neolithic) playerTech = TechLevel.Neolithic;
                newLevel = playerTech;
                LogUtil.Message("updateTechLevel: Mirroring player tech " + newLevel);
            }
            else
            {
                // Research-barrier cascade: the highest satisfied barrier wins.
                ResearchManager researchManager = Find.ResearchManager;
                newLevel = TechLevel.Undefined;
                foreach (TechLevel tl in TechLevelDescending)
                {
                    if (medievalOnly && tl > TechLevel.Medieval) continue;
                    TechLevelBarrier barrier = FactionCache.GetTechBarrier(tl);
                    if (barrier is null) continue;
                    if (barrier.IsSatisfied(researchManager))
                    {
                        newLevel = tl;
                        LogUtil.Message("updateTechLevel: " + tl);
                        break;
                    }
                }
                // Safety floor if no barriers matched at all.
                if (newLevel == TechLevel.Undefined) newLevel = TechLevel.Neolithic;

                // Floor: Empire tech level should never be below the player faction's tech level.
                if (medievalOnly && playerTech > TechLevel.Medieval) playerTech = TechLevel.Medieval;
                if (playerTech > TechLevel.Undefined && newLevel < playerTech)
                {
                    newLevel = playerTech;
                    LogUtil.Message("updateTechLevel: Matched player faction tech level " + playerTech);
                }
            }

            // medievalTechOnly cap applies to both mirror and cascade paths.
            if (medievalOnly && newLevel > TechLevel.Medieval) newLevel = TechLevel.Medieval;

            // Never downgrade the faction's tech level.
            if (newLevel > _techLevel) _techLevel = newLevel;

            if (_techLevel != curTechLevel)
            {
                xenotypeFilter.FinalizeInit(this);
                DirtyAllTitheCaches();
            }

            Faction playerColonyfaction = FactionCache.PlayerColonyFaction;
            bool techLevelChanged = playerColonyfaction?.def.techLevel < _techLevel;
            if (techLevelChanged)
            {
                LogUtil.Message("Updating Tech Level");
                UpdateFactionDef(_techLevel, ref playerColonyfaction);
            }

            dirtyTechLevelCache = false;

            // Refresh caravan trader kinds after a tech-level bump (must run after
            // dirtyTechLevelCache is cleared to avoid re-entering RecomputeTechLevel
            // through the techLevel property).
            if (techLevelChanged)
                RebuildCaravanTraderKinds();
        }

        public void DirtyAllTitheCaches()
        {
            foreach (WorldSettlementFC settlement in settlements)
            {
                foreach (ResourceFC resource in settlement.Resources)
                {
                    resource.SetDirtyRandomTitheCache();
                }
            }
        }

        /// <summary>
        /// Returns a list of *all* things that this faction can produce.
        /// </summary>
        public List<ThingDef> GetGrandThingList()
        {
            if (DirtyGrandThingListFlag)
            {
                grandThingList = new List<ThingDef>();
                foreach (WorldSettlementFC settlement in settlements)
                {
                    if (settlement is null) continue;
                    grandThingList.AddRange(settlement.GetGrandThingList());
                }
                grandThingList = grandThingList.Distinct().ToList();
                DirtyGrandThingListFlag = false;
            }
            return grandThingList;
        }

        public void DirtyGrandThingList()
        {
            DirtyGrandThingListFlag = true;
        }

        public List<ThingDef> GetStuffListForThingDef(ThingDef thing)
        {
            return CraftUtil.GetThingStuffs(thing, GetGrandThingList());
        }

        #endregion

        #region Stat System

        /// <summary>
        /// Entry point for stat queries. Combines settlement-level and faction-level cached partials,
        /// then applies uncached behavior ModifyStat adjustments.
        /// </summary>
        public double GetStatValue(FCStatDef stat, WorldSettlementFC settlement = null)
        {
            double value;
            double factionPart = GetFactionStatValue(stat);

            if (settlement != null && stat.appliesToSettlements)
            {
                double settlementPart = settlement.GetSettlementStatValue(stat);
                if (stat.aggregation == FCStatAggregation.Additive)
                    value = settlementPart + factionPart;
                else
                    value = settlementPart * factionPart;
            }
            else
            {
                value = factionPart;
            }

            // Apply runtime-dependent behavior modifiers (uncached — may depend on settlement state)
            foreach (FCPolicyBehavior b in cachedBehaviors)
            {
                try
                {
                    value = b.ModifyStat(stat, value, settlement);
                }
                catch (Exception e)
                {
                    LogUtil.Error($"Behavior ModifyStat error for stat '{stat.defName}': {e}");
                }
            }

            return value;
        }

        private double AccumulateStatModifiersValue(double value, FCStatDef stat, List<FCStatModifier> statModifiers)
        {
            foreach (FCStatModifier mod in statModifiers)
            {
                bool isAdditive = stat.aggregation == FCStatAggregation.Additive;
                if (mod.stat == stat)
                {
                    if (isAdditive)
                        value += mod.value;
                    else
                        value *= mod.value;
                }
            }

            return value;
        }

        /// <summary>
        /// Computes and caches the faction-level stat partial (policies, traits, edicts, and faction-wide events).
        /// Starts from stat.IdentityValue, applies only faction-level modifiers.
        /// </summary>
        public double GetFactionStatValue(FCStatDef stat)
        {
            if (cachedFactionStatValues.TryGetValue(stat, out double cached))
                return cached;

            double value = stat.IdentityValue;

            foreach (FCPolicy p in policies)
            {
                if (p?.def is null) continue;
                value = AccumulateStatModifiersValue(value, stat, p.def.statModifiers);
            }
            foreach (FCPolicy p in factionTraits)
            {
                if (p?.def is null || p.def == FCPolicyDefOf.empty) continue;
                value = AccumulateStatModifiersValue(value, stat, p.def.statModifiers);
            }
            foreach (FCPolicy edict in edicts.Values)
            {
                if (edict?.def is null || !edict.IsFullyActive) continue;
                value = AccumulateStatModifiersValue(value, stat, edict.def.statModifiers);
            }
            foreach (FCEvent evt in Events)
            {
                if (evt?.def is null) continue;
                if (evt.settlementTraitLocations.Count > 0) continue;
                value = AccumulateStatModifiersValue(value, stat, evt.def.statModifiers);
            }

            cachedFactionStatValues[stat] = value;
            return value;
        }
        private string AccumulateStatModifiersDesc(string desc, FCStatDef stat, List<FCStatModifier> statModifiers, string label, bool hardinvert = false)
        {
            bool isAdditive = stat.aggregation == FCStatAggregation.Additive;
            bool invert = stat.invertedForDisplay;
            foreach (FCStatModifier mod in statModifiers)
            {
                if (mod.stat != stat) continue;
                if (isAdditive)
                    desc += $"{TextUtil.ColorizeAdditiveBonus(mod.value, invert: invert, hardinvert: hardinvert)} - {label}\n";
                else
                    desc += $"{TextUtil.ColorizeMultiplierBonus(mod.value, invert: invert)} - {label}\n";
            }

            return desc;
        }

        /// <summary>
        /// Builds a description string for faction-level stat contributions (policies, traits, edicts, and faction-wide events).
        /// Not cached — only used for UI tooltips.
        /// </summary>
        public string GetFactionStatDesc(FCStatDef stat, bool hardinvert = false)
        {
            string desc = "";
            bool isAdditive = stat.aggregation == FCStatAggregation.Additive;
            bool invert = stat.invertedForDisplay;

            foreach (FCPolicy p in policies)
            {
                if (p?.def is null) continue;
                desc = AccumulateStatModifiersDesc(desc, stat, p.def.statModifiers, p.def.LabelCap, hardinvert);
            }
            foreach (FCPolicy p in factionTraits)
            {
                if (p?.def is null || p.def == FCPolicyDefOf.empty) continue;
                desc = AccumulateStatModifiersDesc(desc, stat, p.def.statModifiers, p.def.LabelCap, hardinvert);
            }
            foreach (FCPolicy edict in edicts.Values)
            {
                if (edict?.def is null || !edict.IsFullyActive) continue;
                desc = AccumulateStatModifiersDesc(desc, stat, edict.def.statModifiers, $"{edict.def.LabelCap} ({"FCEdict".Translate()})", hardinvert);
            }
            foreach (FCEvent evt in Events)
            {
                if (evt?.def is null) continue;
                if (evt.settlementTraitLocations.Count > 0) continue;
                desc = AccumulateStatModifiersDesc(desc, stat, evt.def.statModifiers, $"{evt.def.LabelCap} ({"FCEvent".Translate()})", hardinvert);
            }

            return desc;
        }

        /// <summary>
        /// Clears the faction-level stat cache and dirties resource/desc caches on all settlements
        /// (since final combined stat values have changed).
        /// Does NOT clear settlement stat value caches — settlement-level modifiers are unaffected.
        /// </summary>
        public void InvalidateFactionStatCache()
        {
            cachedFactionStatValues.Clear();
            ScrubNullSettlements("InvalidateFactionStatCache");
            foreach (WorldSettlementFC s in settlements)
            {
                s.InvalidateDescCache();
                s.InvalidateResourceCaches();
            }
        }

        /// <summary>
        /// Invalidates stat and resource caches on all settlements.
        /// Called after faction-wide events (e.g., research completion) that may affect
        /// settlement-level stat or resource production providers.
        /// </summary>
        public void InvalidateAllSettlementStatCaches()
        {
            ScrubNullSettlements("InvalidateAllSettlementStatCaches");
            foreach (WorldSettlementFC s in settlements)
                s.InvalidateStatCache();
        }

        #endregion

        #region Behavior System

        /// <summary>
        /// Rebuilds the cached behavior list from active policies and traits.
        /// Order: policies first (in list order), then traits (in slot order).
        /// This order determines ModifyStat chaining — currently no two behaviors modify the same stat.
        /// </summary>
        public void RebuildBehaviorCache()
        {
            LogUtil.Message("Rebuilding faction behavior cache");
            _cachedBehaviors = new List<FCPolicyBehavior>();
            foreach (FCPolicy p in policies)
            {
                if (p?.behavior != null)
                    _cachedBehaviors.Add(p.behavior);
            }
            foreach (FCPolicy p in factionTraits)
            {
                if (p?.def is null || p.def == FCPolicyDefOf.empty) continue;
                if (p.behavior != null)
                    _cachedBehaviors.Add(p.behavior);
            }
            foreach (FCPolicy edict in edicts.Values)
            {
                if (edict?.behavior != null)
                    _cachedBehaviors.Add(edict.behavior);
            }

            RebuildActionCache();

            // Policy/trait changes affect faction-level stat values and behavior ModifyStat results
            InvalidateFactionStatCache();
        }

        private void RebuildActionCache()
        {
            _cachedBlockedActions = new HashSet<FCActionType>();
            _cachedEnabledActions = new HashSet<FCActionType>();
            _cachedBlockedJobs = new HashSet<MilitaryJobDef>();
            _cachedEnabledJobs = new HashSet<MilitaryJobDef>();
            foreach (FCPolicy p in policies)
            {
                if (p?.def is null) continue;
                if (p.def.blockedActions != null) foreach (var a in p.def.blockedActions) _cachedBlockedActions.Add(a);
                if (p.def.enabledActions != null) foreach (var a in p.def.enabledActions) _cachedEnabledActions.Add(a);
                if (p.def.blockedMilitaryJobs != null) foreach (var j in p.def.blockedMilitaryJobs) _cachedBlockedJobs.Add(j);
                if (p.def.enabledMilitaryJobs != null) foreach (var j in p.def.enabledMilitaryJobs) _cachedEnabledJobs.Add(j);
            }
            foreach (FCPolicy p in factionTraits)
            {
                if (p?.def is null || p.def == FCPolicyDefOf.empty) continue;
                if (p.def.blockedActions != null) foreach (var a in p.def.blockedActions) _cachedBlockedActions.Add(a);
                if (p.def.enabledActions != null) foreach (var a in p.def.enabledActions) _cachedEnabledActions.Add(a);
                if (p.def.blockedMilitaryJobs != null) foreach (var j in p.def.blockedMilitaryJobs) _cachedBlockedJobs.Add(j);
                if (p.def.enabledMilitaryJobs != null) foreach (var j in p.def.enabledMilitaryJobs) _cachedEnabledJobs.Add(j);
            }
            foreach (FCPolicy edict in edicts.Values)
            {
                if (edict?.def is null || !edict.IsFullyActive) continue;
                if (edict.def.blockedActions != null) foreach (var a in edict.def.blockedActions) _cachedBlockedActions.Add(a);
                if (edict.def.enabledActions != null) foreach (var a in edict.def.enabledActions) _cachedEnabledActions.Add(a);
                if (edict.def.blockedMilitaryJobs != null) foreach (var j in edict.def.blockedMilitaryJobs) _cachedBlockedJobs.Add(j);
                if (edict.def.enabledMilitaryJobs != null) foreach (var j in edict.def.enabledMilitaryJobs) _cachedEnabledJobs.Add(j);
            }
            _cachedEnabledActions.ExceptWith(_cachedBlockedActions);
            _cachedEnabledJobs.ExceptWith(_cachedBlockedJobs);
        }

        public void ForEachBehavior(Action<FCPolicyBehavior> action)
        {
            foreach (FCPolicyBehavior b in cachedBehaviors)
            {
                try
                {
                    action(b);
                }
                catch (Exception e)
                {
                    LogUtil.Error($"Policy behavior error: {e}");
                }
            }
        }

        public T FoldBehaviors<T>(T seed, Func<FCPolicyBehavior, T, T> folder)
        {
            foreach (FCPolicyBehavior b in cachedBehaviors)
            {
                try
                {
                    seed = folder(b, seed);
                }
                catch (Exception e)
                {
                    LogUtil.Error($"Policy behavior fold error: {e}");
                }
            }
            return seed;
        }

        /// <summary>
        /// Calls OnRemoved on all behaviors in the given policy list, then clears it.
        /// Use this instead of directly clearing/replacing policy lists.
        /// </summary>
        public void RemoveAllPolicies(List<FCPolicy> policyList)
        {
            foreach (FCPolicy p in policyList)
            {
                if (p?.behavior != null)
                {
                    try { p.behavior.OnRemoved(this); }
                    catch (Exception e) { LogUtil.Error($"FCPolicyBehavior.OnRemoved error for '{p.def?.defName}': {e}"); }
                }
            }
            policyList.Clear();
        }

        #endregion

        #region Policy & Action Checks

        public bool HasPolicy(FCPolicyDef def)
        {
            //Don't game the system
            if (policies.Count < 2)
            {
                return false;
            }

            foreach (FCPolicy policy in policies)
            {
                if (policy.def == def)
                    return true;
            }

            return false;
        }

        public bool HasTrait(FCPolicyDef def)
        {
            foreach (FCPolicy trait in factionTraits)
            {
                if (trait.def == def)
                    return true;
            }

            return false;
        }

        #region Edicts

        public bool IsEdictCategoryUnlocked(FCPolicyCategory category)
        {
            if (!EdictCategoryUnlockLevels.TryGetValue(category, out int required))
                return false;
            return factionLevel >= required;
        }

        public FCPolicy GetActiveEdict(FCPolicyCategory category)
        {
            edicts.TryGetValue(category, out FCPolicy edict);
            return edict;
        }

        public bool HasEdict(FCPolicyDef def)
        {
            if (!edicts.TryGetValue(def.category, out FCPolicy edict)) return false;
            return edict.def == def;
        }

        public void RecordEventCooldown(FCEventDef def) => eventManager.RecordCooldown(def);
        public bool IsEventOnCooldown(FCEventDef def) => eventManager.IsOnCooldown(def);
        public void RecordEventFired(FCEventDef def) => eventManager.RecordFired(def);
        public bool HasReachedMaxFireCount(FCEventDef def) => eventManager.HasReachedMaxFireCount(def);

        public void EnactEdict(FCPolicyDef def)
        {
            if (!def.IsEdict)
            {
                LogUtil.Error($"EnactEdict called on non-edict def '{def.defName}'");
                return;
            }
            if (!IsEdictCategoryUnlocked(def.category))
            {
                Messages.Message("FCEdictCategoryLocked".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            if (def.factionLevelRequirement > 0 && factionLevel < def.factionLevelRequirement)
            {
                Messages.Message("FCEdictLevelRequired".Translate(def.factionLevelRequirement), MessageTypeDefOf.RejectInput);
                return;
            }

            // Check incompatibility with active core policies and traits
            if (!def.incompatiblePolicies.NullOrEmpty())
            {
                foreach (FCPolicyDef blocked in def.incompatiblePolicies)
                {
                    if (HasPolicy(blocked) || HasTrait(blocked))
                    {
                        Messages.Message("FCEdictIncompatible".Translate(def.LabelCap, blocked.LabelCap), MessageTypeDefOf.RejectInput);
                        return;
                    }
                }
            }

            // Check policy prerequisites
            if (!def.MeetsPolicyRequirements(this, out string failReason))
            {
                Messages.Message(failReason, MessageTypeDefOf.RejectInput);
                return;
            }

            // Revoke existing edict in this category (if any)
            RevokeEdict(def.category, silent: true);

            FCPolicy edict = new FCPolicy(def);
            edicts[def.category] = edict;
            if (def.enactDuration > 0)
                pendingEdictActivations.Add(def.category);
            RebuildBehaviorCache();
            DirtyFactionProfitCache();
            Messages.Message("FCEdictEnacted".Translate(def.LabelCap), MessageTypeDefOf.PositiveEvent);
        }

        public void RevokeEdict(FCPolicyCategory category, bool silent = false)
        {
            if (!edicts.TryGetValue(category, out FCPolicy edict)) return;

            if (edict.behavior != null)
            {
                try { edict.behavior.OnRemoved(this); }
                catch (Exception e) { LogUtil.Error($"Edict behavior OnRemoved error for '{edict.def?.defName}': {e}"); }
            }

            string label = edict.def?.LabelCap ?? "";
            FCPolicyDef revokedDef = edict.def;
            edicts.Remove(category);
            pendingEdictActivations.Remove(category);
            RebuildBehaviorCache();
            DirtyFactionProfitCache();
            if (!silent)
                Messages.Message("FCEdictRevoked".Translate(label), MessageTypeDefOf.NeutralEvent);

            // Cascade: revoke any active edicts that depended on the one just removed
            if (revokedDef != null)
            {
                List<FCPolicyCategory> toRevoke = new List<FCPolicyCategory>();
                foreach (KeyValuePair<FCPolicyCategory, FCPolicy> kvp in edicts)
                {
                    if (kvp.Value.def.requiredPolicies.NullOrEmpty()) continue;
                    if (!kvp.Value.def.MeetsPolicyRequirements(this, out _))
                        toRevoke.Add(kvp.Key);
                }
                foreach (FCPolicyCategory cat in toRevoke)
                {
                    if (!edicts.TryGetValue(cat, out FCPolicy policy)) continue;
                    string depLabel = policy.def?.LabelCap ?? "";
                    Messages.Message("FCEdictRevokedDependency".Translate(depLabel, label), MessageTypeDefOf.NeutralEvent);
                    RevokeEdict(cat, silent: true);
                }
            }
        }

        public void RevokeAllEdicts()
        {
            // Copy keys to avoid modifying collection during iteration
            List<FCPolicyCategory> categories = new List<FCPolicyCategory>(edicts.Keys);
            foreach (FCPolicyCategory cat in categories)
            {
                RevokeEdict(cat, silent: true);
            }
        }

        public int GetEdictUpkeep()
        {
            int total = 0;
            foreach (FCPolicy edict in edicts.Values)
            {
                if (edict.IsFullyActive)
                    total += edict.def.upkeepSilver;
            }
            return total;
        }

        private void CheckEdictActivations()
        {
            bool anyActivated = false;
            List<FCPolicyCategory> toRemove = new List<FCPolicyCategory>();
            foreach (FCPolicyCategory cat in pendingEdictActivations)
            {
                if (!edicts.TryGetValue(cat, out FCPolicy edict) || edict.IsFullyActive)
                {
                    toRemove.Add(cat);
                    if (edicts.ContainsKey(cat))
                        anyActivated = true;
                }
            }
            foreach (FCPolicyCategory cat in toRemove)
                pendingEdictActivations.Remove(cat);

            if (anyActivated)
            {
                InvalidateFactionStatCache();
                DirtyFactionProfitCache();
            }
        }

        #endregion

        public bool AnyPolicyBlocks(FCActionType action) => _cachedBlockedActions?.Contains(action) ?? false;
        public bool AnyPolicyEnables(FCActionType action) => _cachedEnabledActions?.Contains(action) ?? false;

        /// <summary>
        /// Unified check: for opt-out actions, returns true unless blocked. For opt-in actions, returns true only if enabled.
        /// </summary>
        public bool IsActionAllowed(FCActionType action)
        {
            if (FCActionTypeUtil.RequiresEnable(action))
                return AnyPolicyEnables(action);
            return !AnyPolicyBlocks(action);
        }

        /// <summary>
        /// Checks if a specific military job is allowed. Respects defaultEnabled on the job def,
        /// plus policy/trait overrides. Does NOT check the DeployMilitary action gate — caller must check that separately.
        /// </summary>
        public bool IsMilitaryJobAllowed(MilitaryJobDef job)
        {
            if (_cachedBlockedJobs != null && _cachedBlockedJobs.Contains(job)) return false;
            if (!job.defaultEnabled)
                return _cachedEnabledJobs != null && _cachedEnabledJobs.Contains(job);
            return true;
        }

        /// <summary>
        /// Returns true if any active policy prevents building destruction on battle loss.
        /// Uses the new data-driven preventBuildingDestruction flag on FCPolicyDef.
        /// </summary>
        public bool AnyPolicyPreventsBuildingDestruction()
        {
            foreach (FCPolicy p in policies)
            {
                if (p?.def != null && p.def.preventBuildingDestruction)
                    return true;
            }
            foreach (FCPolicy p in factionTraits)
            {
                if (p?.def is null || p.def == FCPolicyDefOf.empty) continue;
                if (p.def.preventBuildingDestruction)
                    return true;
            }
            foreach (FCPolicy edict in edicts.Values)
            {
                if (edict?.def != null && edict.IsFullyActive && edict.def.preventBuildingDestruction)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if any active policy suppresses member death penalties.
        /// Uses the new data-driven suppressMemberDeathPenalty flag on FCPolicyDef.
        /// </summary>
        public bool AnyPolicySuppressesMemberDeathPenalty()
        {
            foreach (FCPolicy p in policies)
            {
                if (p?.def != null && p.def.suppressMemberDeathPenalty)
                    return true;
            }
            foreach (FCPolicy p in factionTraits)
            {
                if (p?.def is null || p.def == FCPolicyDefOf.empty) continue;
                if (p.def.suppressMemberDeathPenalty)
                    return true;
            }
            foreach (FCPolicy edict in edicts.Values)
            {
                if (edict?.def != null && edict.IsFullyActive && edict.def.suppressMemberDeathPenalty)
                    return true;
            }
            return false;
        }

        #endregion

        #region Lifecycle Dispatch

        // Bridges registry dispatch to policy behaviors so ColonyUtil only needs one call path.

        void ILifecycleParticipant.OnSettlementCreated(WorldSettlementFC settlement)
        {
            ForEachBehavior(b => b.OnSettlementCreated(this, settlement));
        }

        void ILifecycleParticipant.OnSettlementRemoved(WorldSettlementFC settlement)
        {
            ForEachBehavior(b => b.OnSettlementRemoved(this, settlement));
        }

        void ILifecycleParticipant.OnSettlementUpgraded(WorldSettlementFC settlement, int oldLevel, int newLevel)
        {
            ForEachBehavior(b => b.OnSettlementUpgraded(this, settlement, newLevel));
        }

        void ILifecycleParticipant.OnSettlementTypeChanged(WorldSettlementFC settlement, WorldSettlementDef oldDef, WorldSettlementDef newDef)
        {
            ForEachBehavior(b => b.OnSettlementTypeChanged(this, settlement, oldDef, newDef));
        }

        void ILifecycleParticipant.OnBuildingConstructed(WorldSettlementFC settlement, BuildingFCDef building, int slot)
        {
            ForEachBehavior(b => b.OnBuildingConstructed(this, settlement, building, slot));
        }

        void ILifecycleParticipant.OnBuildingDeconstructed(WorldSettlementFC settlement, BuildingFCDef building, int slot)
        {
            ForEachBehavior(b => b.OnBuildingDeconstructed(this, settlement, building, slot));
        }

        void ILifecycleParticipant.OnResearchCompleted(ResearchProjectDef project)
        {
            ForEachBehavior(b => b.OnResearchCompleted(this, project));
        }

        void ILifecycleParticipant.OnMercenaryDeath(MercenaryDeathEvent evt)
        {
            // No policy behavior hook for merc death currently — submods handle this via their own listener
        }

        /* -*-*-*-*- Military hooks -*-*-*-*-
         * The comp's military-related properties (militaryBusy / militaryJob / militaryLocation /
         * militaryEnemy / isUnderAttack) are read-only and derived from MilitaryOperationManager's
         * op indices, so these hooks have no comp-side state to update. They just dispatch to
         * policy behaviors and run squad injury bookkeeping.
         */

        void ILifecycleParticipant.OnOperationCreated(MilitaryOperation op)
        {
            if (op is null) return;

            // Fire on both sides when distinct: a foreign-defender op commits two settlements
            // (aggressor's home and defender's home), and listeners that track per-settlement
            // commitment need both notifications.
            WorldSettlementFC aggressorHome = op.aggressor?.homeSettlement;
            WorldSettlementFC defenderHome = op.defender?.homeSettlement;
            if (aggressorHome is object)
            {
                bool isExtra = op.aggressor?.squad?.isExtraSquad ?? false;
                ForEachBehavior(b => b.OnSquadDeployed(this, op, aggressorHome, isExtra));
            }
            if (defenderHome is object && defenderHome != aggressorHome)
            {
                bool isExtra = op.defender?.squad?.isExtraSquad ?? false;
                ForEachBehavior(b => b.OnSquadDeployed(this, op, defenderHome, isExtra));
            }
        }

        void ILifecycleParticipant.OnOperationResolved(MilitaryOperation op)
        {
            if (op is null) return;

            // Squad injuries are registered earlier, in op.CompleteBattle, so OnBattleResolved
            // listeners observe the post-battle injury counts.

            // Symmetric with OnOperationCreated: recall both sides when they're distinct settlements.
            WorldSettlementFC aggressorHome = op.aggressor?.homeSettlement;
            WorldSettlementFC defenderHome = op.defender?.homeSettlement;
            if (aggressorHome is object)
                ForEachBehavior(b => b.OnSquadRecalled(this, op, aggressorHome));
            if (defenderHome is object && defenderHome != aggressorHome)
                ForEachBehavior(b => b.OnSquadRecalled(this, op, defenderHome));
        }

        void ILifecycleParticipant.OnBattleResolved(MilitaryOperation op, bool victory, BattleResult result)
        {
            if (op is null) return;
            WorldSettlementFC settlement = op.aggressor?.homeSettlement ?? op.defender?.homeSettlement;
            if (settlement is null) return;

            ForEachBehavior(b => b.OnBattleResolved(this, settlement, op.kind, victory, result));
        }

        void ILifecycleParticipant.OnSquadHired(MercenarySquadFC squad)
        {
            if (squad is null) return;
            ForEachBehavior(b => b.OnSquadHired(this, squad));
        }

        void ILifecycleParticipant.OnSquadDismissed(MercenarySquadFC squad)
        {
            if (squad is null) return;
            ForEachBehavior(b => b.OnSquadDismissed(this, squad));
        }

        void ILifecycleParticipant.OnSquadUpgraded(MercenarySquadFC squad)
        {
            if (squad is null) return;
            MilitaryFC.NotifyIfUnderfunded(squad);
            ForEachBehavior(b => b.OnSquadUpgraded(this, squad));
        }

        #endregion

        #region Settlement Management

        public void AddSettlement(WorldSettlementFC settlement)
        {
            settlements.Add(settlement);
            DirtyFactionProfitCache();
            DirtyAveragesCache();
        }

        public WorldSettlementFC ReturnSettlementByLocation(PlanetTile location)
        {
            foreach (WorldSettlementFC settlement in settlements)
            {
                if (settlement.Tile == location)
                    return settlement;
            }

            return null;
        }

        public string GetSettlementName(PlanetTile location)
        {
            return ReturnSettlementByLocation(location)?.Name ?? "Null";
        }

        public void UpdateSettlementStats()
        {
            foreach (WorldSettlementFC settlement in settlements)
            {
                settlement.UpdateHappiness();
                settlement.UpdateLoyalty();
                settlement.UpdateUnrest();
                settlement.UpdateProsperity();
            }
        }

        public int ReturnHighestMilitaryLevel()
        {
            int max = 1;
            foreach (WorldSettlementFC settlement in settlements)
            {
                max = Math.Max(max, settlement.settlementMilitaryLevel);
            }

            return max;
        }

        #endregion

        #region Tax & Billing

        public void AddTax()
        {
            LogUtil.Message($"AddTax at tick {Find.TickManager.TicksGame}: settlements={settlements.Count}, timeBetweenTaxes={FCSettings.timeBetweenTaxes}");
            TaxTickRegistry.InvokePreTaxResolution(this);
            foreach (ResourcePool pool in resourcePools)
            {
                if (pool.resource.PoolResourceResetsAtTaxTime())
                {
                    pool.pool = 0;
                }
            }

            if (settlements.Count != 0)
            {
                foreach (WorldSettlementFC settlement in settlements)
                {
                    AddExperienceToFactionLevel(2f);

                    List<Thing> list = new List<Thing>();
                    list = settlement.CreateTax(out int silverAmount);
                    List<ResourcePool> resourcePools = settlement.CreateResourcePools();

                    BillFC bill = new BillFC(settlement);
                    bill.label = "FCBillKindTax".Translate();
                    bill.taxes.resourcePools = resourcePools;
                    bill.taxes.itemTithes.AddRange(list);
                    bill.taxes.silverAmount = silverAmount;
                    bill.AddUnpaidPenalty(BillPenaltyStat.Unrest, 10);
                    bill.AddUnpaidPenalty(BillPenaltyStat.Happiness, 10);
                    bill.AddLatePaidPenalty(BillPenaltyStat.Unrest, 4);
                    bill.AddLatePaidPenalty(BillPenaltyStat.Happiness, 4);

                    Bills.Add(bill);

                    TextUtil.GetTownTitle(settlement);
                    TaxTickPrisoner(settlement);
                    ForEachBehavior(b => b.OnTaxCollected(this, settlement));
                }

                Find.LetterStack.ReceiveLetter("FCTaxesBilledShort".Translate(), "FCTaxesBilledDesc".Translate(),
                    LetterDefOf.PositiveEvent);
                DirtyFactionProfitCache();
            }
            else
            {
                Messages.Message("FCNoSettlementsToTax".Translate(), MessageTypeDefOf.NeutralEvent);
            }

            // Deduct edict upkeep
            int edictUpkeep = GetEdictUpkeep();
            if (edictUpkeep > 0)
            {
                if (PaymentUtil.GetSilver() >= edictUpkeep)
                {
                    PaymentUtil.PaySilver(edictUpkeep, "EdictUpkeep");
                }
                else
                {
                    RevokeAllEdicts();
                    Messages.Message("FCEdictUpkeepUnpaid".Translate(), MessageTypeDefOf.NegativeEvent);
                }
            }

            TaxTickRegistry.InvokePostTaxResolution(this);
        }

        public void TaxTickPrisoner(WorldSettlementFC settlement)
        {
            int i = 0;
            while (i < settlement.prisonerList.Count)
            {
                FCPrisoner prisoner = settlement.prisonerList[i];
                bool dead = false;

                switch (prisoner.workload)
                {
                    case FCWorkLoad.Heavy:
                        if (prisoner.AdjustHealth(-20))
                            dead = true;
                        break;
                    case FCWorkLoad.Medium:
                        if (prisoner.AdjustHealth(-10))
                            dead = true;
                        break;
                    case FCWorkLoad.Light:
                        if (prisoner.AdjustHealth(4))
                            dead = true;
                        break;
                }

                /* Only increment if the prisoner hasn't died.
                 * If they *did* die, then AdjustHealth() will have removed them from the list already. So if we increment, then we'll actually skip the next prisoner. */
                if (!dead) i++;
            }
        }

        // resetTraitMercantileCaravanTime removed — mercantile caravan scheduling
        // is now handled by FCPolicyBehavior_Mercantile.Tick/OnEnacted.

        public double GetTotalIncome() => income;
        public double GetTotalUpkeep() => upkeep;
        public double GetTotalProfit() => profit;

        #endregion

        #region Events

        public void AddEvent(FCEvent fcevent)
        {
            if (fcevent == null) return;

            if (fcevent.goods != null && fcevent.goods.Count > 0)
            {
                fcevent.goods = FCEvent.ConsolidateGoods(fcevent.goods);
            }

            // Tax delivery interception: let registered interceptors redirect taxColony events
            if (fcevent.def == FCEventDefOf.taxColony && fcevent.source != PlanetTile.Invalid)
            {
                WorldSettlementFC sourceSettlement = ReturnSettlementByLocation(fcevent.source);
                TaxDeliveryRegistry.InvokeOnTaxEventCreated(new TaxDeliveryContext(fcevent, sourceSettlement));
            }

            //Add event to the manager queue
            eventManager.Enqueue(fcevent);

            LogUtil.Message($"AddEvent: adding new fcevent {fcevent.def.defName}");

            string sourceId = "event_" + fcevent.def.defName;

            //check if event has a location, if does, add stat modifiers to that specific location;
            if (fcevent.settlementTraitLocations.Count > 0) //if has specific locations
            {
                foreach (WorldSettlementFC location in fcevent.settlementTraitLocations)
                {
                    location.AddStatModifiers(fcevent.def.statModifiers, sourceId, fcevent.def.label);
                    if (fcevent.def.permanentStatModifiers.Count > 0)
                        location.AddPermanentModifiers(fcevent.def.permanentStatModifiers, sourceId, fcevent.def.label);
                }
            }
            else
            {
                //if no specific location then faction wide — apply to all settlements
                foreach (WorldSettlementFC settlement in settlements)
                {
                    settlement.AddStatModifiers(fcevent.def.statModifiers, sourceId, fcevent.def.label);
                    if (fcevent.def.permanentStatModifiers.Count > 0)
                        settlement.AddPermanentModifiers(fcevent.def.permanentStatModifiers, sourceId, fcevent.def.label);
                }
            }

            InvalidateFactionStatCache();
        }

        // Thin delegators to FCEventManager. Invariants (fired flag, version bump)
        // are enforced by the manager; see FCEventManager.Remove / RemoveWhere.
        public bool RemoveEvent(FCEvent evt) => eventManager.Remove(evt);
        public int RemoveEventsWhere(Predicate<FCEvent> match) => eventManager.RemoveWhere(match);

        // Indexed event queries — O(1) via FCEventManager's internal indexes.
        public IReadOnlyList<FCEvent> GetEventsByDef(FCEventDef def) => eventManager.GetByDef(def);
        public FCEvent FindEventByDefAndLocation(FCEventDef def, PlanetTile tile) => eventManager.FindFirstByDefAndLocation(def, tile);
        public IReadOnlyList<FCEvent> FindAllEventsByDefAndLocation(FCEventDef def, PlanetTile tile) => eventManager.GetByDefAndLocation(def, tile);
        public bool HasEventWithDefAndLocation(FCEventDef def, PlanetTile tile) => eventManager.AnyWithDefAndLocation(def, tile);

        private void MakeRandomEvent()
        {
            if (RandomEventsDisabledOrNoSettlements()) return;

            randomEventLastAdded += 1f;

            if (CanMakeRandomEventNow())
            {
                FCEvent tmpEvt = FCEventMaker.MakeRandomEvent(FCEventMaker.ReturnRandomEvent(), null);
                if (tmpEvt != null)
                {
                    FactionCache.FactionComp.AddEvent(tmpEvt);
                    randomEventLastAdded = 0f;

                    Find.LetterStack.ReceiveLetter("FCRandomEventLetterLabel".Translate(), FCEventMaker.BuildEventLetterBody(tmpEvt), LetterDefOf.NeutralEvent);
                }
            }
        }

        private bool CanMakeRandomEventNow()
        {
            if ((FCSettings.maxDaysTillRandomEvent - FCSettings.minDaysTillRandomEvent) == 0)
            {
                return randomEventLastAdded >= FCSettings.minDaysTillRandomEvent;
            }
            else
            {
                return Rand.Chance((randomEventLastAdded - FCSettings.minDaysTillRandomEvent) / (FCSettings.maxDaysTillRandomEvent - FCSettings.minDaysTillRandomEvent));
            }
        }

        private bool RandomEventsDisabledOrNoSettlements() => FactionCache.FactionComp.settlements.Count == 0 || FCSettings.disableRandomEvents;

        #endregion

        #region Resources

        private void EnsureResourcePools()
        {
            foreach (ResourceTypeDef def in DefDatabase<ResourceTypeDef>.AllDefs)
            {
                if (def.isPoolResource && !resourcePools.Any(p => p.resource == def))
                {
                    resourcePools.Add(new ResourcePool { resource = def, pool = 0 });
                }
            }
        }

        public void AddResourcePool(ResourcePool pool)
        {
            if (pool is null || pool.pool == 0) return;

            /* If the pool wants to do any pre-adding-to-global-pool shenanigans, let it do so now. */
            pool.pool = pool.resource.PreAddToGlobalPool(pool.pool);

            ResourcePool rpool = resourcePools.Find((ResourcePool p) => p.resource == pool.resource);
            if (rpool != null)
            {
                rpool.pool += pool.pool;
            }
            else
            {
                resourcePools.Add(pool);
            }
            pool.resource.AddedToGlobalPool(pool.pool);
        }
        public void AddResourcePools(List<ResourcePool> pools)
        {
            foreach (ResourcePool pool in pools)
            {
                AddResourcePool(pool);
            }
        }
        public double GetResourcePoolValue(ResourceTypeDef res)
        {
            ResourcePool rpool = resourcePools.Find((ResourcePool p) => p.resource == res);
            if (rpool is null)
            {
                LogUtil.Warning($"Tried to get resource pool value for ResourceTypeDef {res}, but there was no faction resource pool");
                return 0;
            }
            return rpool.pool;
        }

        public IEnumerable<FloatMenuOption> GetFactionMenuResourcePoolFloatMenuOptions()
        {
            foreach (ResourcePool pool in resourcePools)
            {
                IEnumerable<FloatMenuOption> options = pool.resource.GetFactionMenuFloatMenuOptions(pool);
                if (options != null)
                {
                    foreach (FloatMenuOption option in options)
                    {
                        yield return option;
                    }
                }
            }
        }
        private void AccumulateDailyProduction()
        {
            foreach (WorldSettlementFC settlement in settlements)
            {
                settlement.AccumulateDailyProduction();
            }
        }

        public void UpdateDailyResourcePools()
        {
            foreach (ResourcePool pool in resourcePools)
            {
                LogUtil.Message($"Daily ResourcePool update for resourceTypeDef {pool.resource.defName}. Pool size: {pool.pool}");
                pool.resource.DailyUpdate(pool);
                LogUtil.Message($"Post-Daily ResourcePool update for resourceTypeDef {pool.resource.defName}. New Pool size: {pool.pool}");
            }
        }

        public ResourceDisplay ReturnResource(ResourceTypeDef resourceTypeDef)
        {
            ResourceDisplay res = factionResources.Find((ResourceDisplay rfc) => rfc.resourceDef == resourceTypeDef);
            if (res == null)
            {
                /* This should never happen! */
                LogUtil.Error($"Requested resource {resourceTypeDef.defName} is not in the list of faction resources!");
            }
            return res;
        }

        public void SetDirtyResourceDisplayCache(ResourceTypeDef rdef)
        {
            ResourceDisplay rdisplay = factionResources.Find((ResourceDisplay rd) => rd.resourceDef == rdef);
            if (rdisplay != null)
            {
                rdisplay.SetDirtyCache();
            }
        }

        public void SetAllDirtyResourceDisplayCaches()
        {
            foreach (ResourceDisplay rdis in factionResources)
            {
                rdis.SetDirtyCache();
            }
        }

        #endregion

        #region ID Generation

        public int GetNextSettlementFCID()
        {
            nextSettlementFCID++;
            return nextSettlementFCID;
        }

        public int GetNextMercenaryID()
        {
            nextMercenaryID++;
            return nextMercenaryID;
        }

        public int GetNextMilitaryFireSupportID()
        {
            nextMilitaryFireSupportID++;
            return nextMilitaryFireSupportID;
        }

        public int GetNextMercenarySquadID()
        {
            nextMercenarySquadID++;
            return nextMercenarySquadID;
        }

        public int GetNextTaxID()
        {
            nextTaxID++;
            return nextTaxID;
        }

        public int GetNextEventID()
        {
            nextEventID++;
            return nextEventID;
        }

        public int GetNextBillID()
        {
            nextBillID++;
            return nextBillID;
        }

        public int GetNextPrisonerID()
        {
            nextPrisonerID++;
            return nextPrisonerID;
        }

        #endregion

        #region Leveling

        public float UpdateFactionLevelGoalXP(int currentLevel)
        {
            return SettlementFormulas.CalculateFactionLevelGoalXP(currentLevel);
        }

        public bool AddExperienceToFactionLevel(float xp)
        {
            if (factionLevel >= MaxFactionLevel)
            {
                factionXPCurrent = factionXPGoal;
                return false;
            }

            bool leveled = false;
            factionXPCurrent += xp;

            while (factionXPCurrent >= factionXPGoal && factionLevel < MaxFactionLevel)
            {
                factionXPCurrent -= factionXPGoal;
                factionLevel += 1;
                LogUtil.Message($"Faction leveled up to {factionLevel} at tick {Find.TickManager.TicksGame} (gained {xp} XP)");
                Find.LetterStack.ReceiveLetter("FCFactionLevelUp".Translate(),
                    "FCFactionLevelUpDesc".Translate(name, factionLevel), LetterDefOf.PositiveEvent);
                leveled = true;
                factionXPGoal = UpdateFactionLevelGoalXP(factionLevel);
            }

            if (factionLevel >= MaxFactionLevel)
            {
                factionXPCurrent = factionXPGoal;
            }

            return leveled;
        }

        #endregion

        #region Capital Management

        public void SetCapital()
        {
            // Check if there's an active capital spot first
            Building_CapitalSpot activeCapitalSpot = GetActiveCapitalSpot();
            if (activeCapitalSpot != null)
            {
                Messages.Message("FCCapitalAlreadyEstablished".Translate(activeCapitalSpot.Map.Parent.LabelCap), MessageTypeDefOf.RejectInput);
                return;
            }

            if (Find.CurrentMap != null && Find.CurrentMap.IsPlayerHome)
            {
                capitalLocation = Find.CurrentMap.Parent.Tile;

                Messages.Message("FCSetAsFactionCapital".Translate(Find.CurrentMap.Parent.LabelCap), MessageTypeDefOf.NeutralEvent);
            }
            else
            {
                Messages.Message("FCUnableToSetCapitalHere".Translate(), MessageTypeDefOf.NegativeEvent);
            }
        }

        public bool HasActiveCapitalSpot()
        {
            return !(GetActiveCapitalSpot() is null);
        }

        public Building_CapitalSpot GetActiveCapitalSpot()
        {
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;

                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    if (building is Building_CapitalSpot capitalSpot && capitalSpot.IsActiveCapitalSpot)
                    {
                        return capitalSpot;
                    }
                }
            }
            return null;
        }

        public Map ReturnCapitalMap()
        {
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                if (Find.Maps[i].Tile == capitalLocation)
                {
                    return Find.Maps[i];
                }
            }

            LogUtil.Message("FCCouldNotFindMapOfCapital".Translate());
            return null;
        }

        #endregion

        #region Faction Definition

        public Color ResolveApparelColor(SavedThing savedThing)
        {
            if (savedThing.hasColor)
                return savedThing.color;
            return ResolveApparelColor(savedThing.thing);
        }

        public Color ResolveApparelColor(ThingDef apparelDef)
        {
            // Primary = outer/armor layers (Middle, Shell)
            // Secondary = base clothing + accessories (OnSkin, Belt, Overhead, EyeCover)
            bool isPrimarySlot = apparelDef != null
                && apparelDef.IsApparel
                && (apparelDef.apparel.layers.Contains(ApparelLayerDefOf.Middle)
                    || apparelDef.apparel.layers.Contains(ApparelLayerDefOf.Shell));

            if (isPrimarySlot)
            {
                if (hasFactionColor) return factionColorPrimary;
            }
            else
            {
                if (hasFactionColorSecondary) return factionColorSecondary;
                if (hasFactionColor) return factionColorPrimary;
            }

            return Color.white;
        }

        //TODO: this whole function is playing with defs. Doesn't seem great. Not sure if there's another way to set icons, though. Need to investigate
        public void UpdateFactionIcon(ref Faction faction, string iconPath)
        {
            LogUtil.Message("Updated Icon - " + iconPath);
            if (faction?.def != null)
            {
                faction.def.factionIconPath = iconPath;
            }
            if (settlements.Any() && settlements[0]?.def != null && UnityData.IsInMainThread)
            {
                //TODO: not sure if this will interact wierdly with the new SettlementDef. Keep an eye on this
                WorldSettlementFC.traitCachedIcon.SetValue(settlements[0].def, ContentFinder<Texture2D>.Get(iconPath));
            }

            foreach (WorldSettlementFC settlement in settlements)
            {
                if (settlement?.def != null)
                {
                    settlement.def.expandingIconTexture = iconPath;
                }
                if (settlement?.Faction?.def != null)
                {
                    settlement.Faction.def.factionIconPath = iconPath;
                }
            }
        }

        public void UpdateFactionDef(TechLevel tech, ref Faction faction)
        {
            FactionDef replacingDef;
            ThingFilter apparelStuffFilter = new ThingFilter();
            FactionDef def = faction.def;

            switch (tech)
            {
                case TechLevel.Archotech:
                case TechLevel.Ultra:
                case TechLevel.Spacer:
                    replacingDef = DefDatabase<FactionDef>.GetNamedSilentFail("OutlanderCivil");
                    break;
                case TechLevel.Industrial:
                    replacingDef = DefDatabase<FactionDef>.GetNamedSilentFail("OutlanderCivil");
                    break;
                case TechLevel.Medieval:
                    if (FCSettings.IsModLoaded("OskarPotocki.VanillaFactionsExpanded.MedievalModule"))
                    {
                        replacingDef = DefDatabase<FactionDef>.GetNamedSilentFail("VFEM_KingdomCivil");
                    }
                    else
                    {
                        replacingDef = DefDatabase<FactionDef>.GetNamedSilentFail("TribeCivil");
                    }

                    break;
                default:
                    replacingDef = DefDatabase<FactionDef>.GetNamedSilentFail("TribeCivil");
                    break;
            }
            if (replacingDef.backstoryFilters != null && replacingDef.backstoryFilters.Count != 0)
                def.backstoryFilters = replacingDef.backstoryFilters;
            def.techLevel = tech;
            def.basicMemberKind = replacingDef.basicMemberKind;
            if (replacingDef.apparelStuffFilter != null)
                def.apparelStuffFilter = replacingDef.apparelStuffFilter;


            if (tech >= TechLevel.Spacer && def.apparelStuffFilter != null)
            {
                def.apparelStuffFilter.SetAllow(DefDatabase<StuffCategoryDef>.GetNamedSilentFail("Synthread"), true);
                def.apparelStuffFilter.SetAllow(DefDatabase<StuffCategoryDef>.GetNamedSilentFail("Hyperweave"), true);
                def.apparelStuffFilter.SetAllow(DefDatabase<StuffCategoryDef>.GetNamedSilentFail("Plasteel"), true);
            }
            UpdateFactionIcon(ref faction, "FactionIcons/" + factionIconPath);

            LogUtil.Message("FactionFC.UpdateFactionDef - Completed tech update");
        }

        private void InitEnabledCaravanTypes()
        {
            enabledCaravanTypes = new List<string>();
            foreach (ResourceTypeDef rtd in DefDatabase<ResourceTypeDef>.AllDefs)
            {
                if (!rtd.isPoolResource && rtd.CanTithe && rtd.ResourceTypeAllowedByTech(_techLevel))
                    enabledCaravanTypes.Add(rtd.defName);
            }
        }

        /// <summary>
        /// Rebuilds <c>faction.def.caravanTraderKinds</c> from current state. If the build returns
        /// an empty list (e.g., 0 production across all enabled resource types, or called too early
        /// during load before production caches are settled), the existing list is left intact so
        /// the FactionDef's XML default isn't clobbered with an empty list.
        /// </summary>
        public void RebuildCaravanTraderKinds()
        {
            Faction faction = FactionCache.PlayerColonyFaction;
            if (faction is null) return;
            List<TraderKindDef> result = BuildCaravanTraderKinds(techLevel);
            if (result.Count > 0)
                faction.def.caravanTraderKinds = result;

            if (result.Count == 0)
                LogUtil.Warning($"RebuildCaravanTraderKinds produced an empty list. enabledCaravanTypes: {enabledCaravanTypes?.Count ?? 0}");
            else
                LogUtil.Message($"RebuildCaravanTraderKinds produced a list of {enabledCaravanTypes?.Count ?? 0} caravan types");
        }

        /// <summary>
        /// Builds the caravanTraderKinds list from <see cref="enabledCaravanTypes"/>.
        /// Resource types resolve to Caravan_Empire_{Name} defs.
        /// Exotic/Slaver resolve to tech-appropriate vanilla defs with policy/level gating.
        /// </summary>
        private List<TraderKindDef> BuildCaravanTraderKinds(TechLevel tech)
        {
            List<TraderKindDef> result = new List<TraderKindDef>();
            bool isNeolithic = tech <= TechLevel.Medieval;

            if (enabledCaravanTypes.NullOrEmpty())
            {
                LogUtil.Warning("enabledCaravanTypes null or empty in BuildCaravanTraderKinds");
                InitEnabledCaravanTypes();
            }

            foreach (string typeId in enabledCaravanTypes)
            {
                TraderKindDef resolved = null;

                if (typeId == "Exotic")
                {
                    bool hasLevel = factionLevel >= 4;
                    bool hasMercantile = HasPolicy(FCPolicyDefOf.mercantile);
                    if (!hasLevel && !hasMercantile)
                        continue;

                    string defName = isNeolithic
                        ? "Caravan_Neolithic_ShamanMerchant"
                        : "Caravan_Outlander_Exotic";
                    resolved = DefDatabase<TraderKindDef>.GetNamedSilentFail(defName);
                }
                else if (typeId == "Slaver")
                {
                    if (HasPolicy(FCPolicyDefOf.pacifist) || HasPolicy(FCPolicyDefOf.egalitarian))
                        continue;

                    string defName = isNeolithic
                        ? "Caravan_Neolithic_Slaver"
                        : "Caravan_Outlander_PirateMerchant";
                    resolved = DefDatabase<TraderKindDef>.GetNamedSilentFail(defName);
                }
                else
                {
                    // Resource-based: RTD_Food -> FC_Caravan_Empire_Food
                    ResourceTypeDef rtd = DefDatabase<ResourceTypeDef>.GetNamedSilentFail(typeId);
                    if (rtd is null || !rtd.ResourceTypeAllowedByTech(tech))
                        continue;

                    // Skip if this resource has no production
                    if ((ReturnResource(rtd)?.amount ?? 0) == 0)
                        continue;

                    string suffix = rtd.defName.Replace("RTD_", "");
                    resolved = DefDatabase<TraderKindDef>.GetNamedSilentFail("FC_Caravan_Empire_" + suffix);
                }

                if (resolved is object)
                    result.Add(resolved);
            }

            if (result.Count == 0)
                LogUtil.Warning($"BuildCaravanTraderKinds produced an empty list. enabledCaravanTypes: {enabledCaravanTypes?.Count ?? 0}, techLevel: {tech}");

            return result;
        }

        public string ReturnNextTechToLevel()
        {
            switch (techLevel)
            {
                case TechLevel.Ultra:
                    return "FCReachedMaxLevel".Translate();
                case TechLevel.Spacer:
                    return "FCShipBasics".Translate();
                case TechLevel.Industrial:
                    return "FCFabrication".Translate();
                case TechLevel.Medieval:
                    return "FCElectricity".Translate();
                case TechLevel.Neolithic:
                    return "FCSmithing".Translate();
                default:
                    return "N/A";
            }
        }

        #endregion

        #region Misc

        public void SetName(string name)
        {
            this.name = name;
        }

        public void GainHappiness(double amount)
        {
            foreach (WorldSettlementFC settlement in settlements)
            {
                settlement.GainHappiness(amount);
            }
        }

        public void GainUnrestForReason(Message msg, double amount)
        {
            Messages.Message(msg);
            foreach (WorldSettlementFC settlement in settlements)
            {
                settlement.GainUnrest(amount);
            }
        }

        public bool SendDiplomaticEnvoy(Faction faction)
        {
            if (faction.def.permanentEnemy)
            {
                Messages.Message("FCCannotImproveRelationsWithType".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            // Try new behavior system first
            bool handled = false;
            ForEachBehavior(b =>
            {
                if (!handled)
                    handled = b.HandleDiplomaticEnvoy(this, faction);
            });
            return handled;
        }

        /// <summary>
        /// Syncs faction goodwill with average happiness. Should only be called from StatTick (daily).
        /// </summary>
        private void SyncGoodwillWithAverages()
        {
            if (settlements.Any() && FactionCache.PlayerColonyFaction != null)
            {
                FactionCache.PlayerColonyFaction.TryAffectGoodwillWith(Find.FactionManager.OfPlayer,
                    (Convert.ToInt32(averageHappiness) - FactionCache.PlayerColonyFaction.PlayerGoodwill));
            }
        }

        public bool CheckSettlementCaravansList(PlanetTile location) //list of destinations caravans gone to
        {
            for (int i = 0; i < settlementCaravansList.Count; i++)
            {
                if (location == settlementCaravansList[i] || Find.WorldGrid.IsNeighbor(location, settlementCaravansList[i]))
                {
                    return true; // is on list
                }
            }

            return false; //is not on list
        }
        /// <summary>
        /// Checks the settlementCaravansList to see if it has any orphaned tile locations, and removes them.
        /// </summary>
        /// <returns>TRUE if an orphaned location was found. FALSE otherwise.</returns>
        public bool ValidateSettlementCaravansList()
        {
            bool foundInvalidCaravan = false;
            List<PlanetTile> matched = new List<PlanetTile>();
            List<PlanetTile> toAdd = new List<PlanetTile>();
            List<PlanetTile> toRemove = new List<PlanetTile>();

            foreach (FCEvent evt in Events)
            {
                if (evt.def.defName == "settleNewColony")
                {
                    if (settlementCaravansList.Contains(evt.location))
                    {
                        matched.Add(evt.location);
                    }
                    else
                    {
                        LogUtil.Warning($"ValidateSettlementCaravansList: found settleNewColony event at tile {evt.location}, NOT in settlementCaravansList. Adding.");
                        toAdd.Add(evt.location);
                    }
                }
            }

            if (matched.Count != settlementCaravansList.Count)
            {
                foundInvalidCaravan = true;
                foreach (PlanetTile tile in settlementCaravansList)
                {
                    if (!matched.Contains(tile))
                    {
                        toRemove.Add(tile);
                    }
                }

                foreach (PlanetTile tile in toRemove)
                {
                    LogUtil.Warning($"ValidateSettlementCaravansList: removing orphaned tile {tile} from settlementCaravansList");
                    settlementCaravansList.Remove(tile);
                }
            }
            else if (toAdd.Count == 0)
            {
                LogUtil.Message($"ValidateSettlementCaravansList: all settleNewColony events have valid locations");
            }

            if (toAdd.Count > 0)
            {
                settlementCaravansList.AddRange(toAdd);
            }

            return foundInvalidCaravan;
        }

        private void RecoverOrphanedConstructions(int currentTick)
        {
            const int gracePeriod = 500;
            IReadOnlyList<FCEvent> constructEvents = eventManager.GetByDef(FCEventDefOf.constructBuilding);
            IReadOnlyList<FCEvent> upgradeEvents = eventManager.GetByDef(FCEventDefOf.upgradeSettlement);

            foreach (WorldSettlementFC settlement in settlements)
            {
                // Check for orphaned construction slots
                if (settlement.BuildingsComp is object)
                {
                    List<BuildingFC> buildings = settlement.BuildingsComp.Buildings;
                    for (int slot = 0; slot < buildings.Count; slot++)
                    {
                        BuildingFC building = buildings[slot];
                        if (building.def != BuildingFCDefOf.Construction) continue;
                        if (building.completionTick + gracePeriod >= currentTick) continue;

                        bool hasMatchingEvent = constructEvents.Any(evt => evt.source == settlement.Tile && evt.buildingSlot == slot);

                        if (!hasMatchingEvent)
                        {
                            try
                            {
                                settlement.ConstructBuilding(building.underConstructionDef, slot);
                                LogUtil.Warning($"RecoverOrphanedConstructions: auto-completed orphaned construction " +
                                    $"'{building.underConstructionDef?.defName ?? "NULL"}' in slot {slot} at {settlement.Name}");
                            }
                            catch (Exception ex)
                            {
                                LogUtil.Error($"RecoverOrphanedConstructions: failed to recover slot {slot} at {settlement.Name}: {ex}");
                            }
                        }
                    }
                }

                // Check for orphaned upgrade state
                if (settlement.IsUpgrading && settlement.FinishUpgradeTick + gracePeriod < currentTick)
                {
                    bool hasMatchingEvent = upgradeEvents.Any(t => t.location == settlement.Tile);

                    if (!hasMatchingEvent)
                    {
                        try
                        {
                            settlement.UpgradeSettlement(setFlags: true);
                            LogUtil.Warning($"RecoverOrphanedConstructions: auto-completed orphaned upgrade at {settlement.Name}");
                        }
                        catch (Exception ex)
                        {
                            LogUtil.Error($"RecoverOrphanedConstructions: failed to recover upgrade at {settlement.Name}: {ex}");
                        }
                    }
                }
            }
        }

        #endregion
    }
}
