using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public class FCSettings : ModSettings
    {

        /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-* 
         *           ~  DEFAULTS  ~
         * for saving, reseting, and validation
         * Centralized for ease of editing, and to ensure that all references to these values
         *   are synced.
         *-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/
        /* Defaults by difficulty setting */
        public const int MINIMUM_TAX_INTERVAL_DAYS = 1;
        public const EmpireDifficultyLevel DEFAULT_DIFFICULTY_LEVEL = EmpireDifficultyLevel.AdventureStory;
        //Peaceful
        public const int DEFAULT_SILVER_PER_RESOURCE_PEACEFUL = 200;
        public const int DEFAULT_TAX_INTERVAL_DAYS_PEACEFUL = 2;
        public const int DEFAULT_PRODUCTION_TITHE_MOD_PEACEFUL = 50;
        public const int DEFAULT_WORKER_COST_PEACEFUL = 75;
        //Community Builder
        public const int DEFAULT_SILVER_PER_RESOURCE_COMMUNITYBUILDER = 150;
        public const int DEFAULT_TAX_INTERVAL_DAYS_COMMUNITYBUILDER = 5;
        public const int DEFAULT_PRODUCTION_TITHE_MOD_COMMUNITYBUILDER = 25;
        public const int DEFAULT_WORKER_COST_COMMUNITYBUILDER = 100;
        //Adventure Story
        public const int DEFAULT_SILVER_PER_RESOURCE_ADVENTURESTORY = 100;
        public const int DEFAULT_TAX_INTERVAL_DAYS_ADVENTURESTORY = 5;
        public const int DEFAULT_PRODUCTION_TITHE_MOD_ADVENTURESTORY = 25;
        public const int DEFAULT_WORKER_COST_ADVENTURESTORY = 100;
        //Strive to Survive
        public const int DEFAULT_SILVER_PER_RESOURCE_STRIVETOSURVIVE = 100;
        public const int DEFAULT_TAX_INTERVAL_DAYS_STRIVETOSURVIVE = 10;
        public const int DEFAULT_PRODUCTION_TITHE_MOD_STRIVETOSURVIVE = 20;
        public const int DEFAULT_WORKER_COST_STRIVETOSURVIVE = 125;
        //Blood and Dust
        public const int DEFAULT_SILVER_PER_RESOURCE_BLOODANDDUST = 80;
        public const int DEFAULT_TAX_INTERVAL_DAYS_BLOODANDDUST = 15;
        public const int DEFAULT_PRODUCTION_TITHE_MOD_BLOODANDDUST = 15;
        public const int DEFAULT_WORKER_COST_BLOODANDDUST = 125;
        //Losing is Fun
        public const int DEFAULT_SILVER_PER_RESOURCE_LOSINGISFUN = 70;
        public const int DEFAULT_TAX_INTERVAL_DAYS_LOSINGISFUN = 30;
        public const int DEFAULT_PRODUCTION_TITHE_MOD_LOSINGISFUN = 10;
        public const int DEFAULT_WORKER_COST_LOSINGISFUN = 150;
        // Global defaults
        // The default difficulty setting is Adventure Story, so set the global defaults accordingly
        public const int DEFAULT_SILVER_PER_RESOURCE = DEFAULT_SILVER_PER_RESOURCE_ADVENTURESTORY;
        public const int DEFAULT_TAX_INTERVAL_DAYS = DEFAULT_TAX_INTERVAL_DAYS_ADVENTURESTORY;
        public const int DEFAULT_PRODUCTION_TITHE_MOD = DEFAULT_PRODUCTION_TITHE_MOD_ADVENTURESTORY;
        public const int DEFAULT_WORKER_COST = DEFAULT_WORKER_COST_ADVENTURESTORY;
        /* Defaults for Research settings */
        public const bool DEFAULT_MEDIEVAL_TECH_ONLY = false;
        public const bool DEFAULT_MIRROR_PLAYER_TECH_LEVEL = false;
        /* Defaults for Settlement settings */
        public const bool DEFAULT_SHOW_SETTLE_CONFIRM = true;
        public const TaxDeliveryMode DEFAULT_TAX_DELIVERY_MODE = TaxDeliveryMode.None;
        public const TaxNotificationMode DEFAULT_TAX_NOTIFICATION_MODE = TaxNotificationMode.All;
        public static double DEFAULT_SETTLEMENT_FOUNDING_COST = 1000;
        public static double DEFAULT_SETTLEMENT_BASE_UPGRADE_COST = 1000;
        public static int DEFAULT_SETTLEMENT_MAX_LEVEL = 10;
        /* Defaults for Events & Military settings */
        public const bool DEFAULT_DISABLE_HOSTILE_MILITARY_ACTIONS = false;
        public const bool DEFAULT_DISABLE_RANDOM_EVENTS = false;
        public const bool DEFAULT_DISABLE_FORCED_PAUSING_DURING_EVENTS = true;
        public const float DEFAULT_EVENT_OPTION_DELAY_SECONDS = 1.0f;
        public const bool DEFAULT_DEAD_PAWNS_INCREASE_MILITARY_COOLDOWN = true;
        public const bool DEFAULT_USE_THREADED_ROAD_COMPUTATION = true;
        public const int DEFAULT_EDGES_PER_ROAD_TICK = 5;
        public const BattleMode DEFAULT_BATTLE_MODE = BattleMode.Auto;
        public const int DEFAULT_MIN_DAYS_TIL_MILITARY_ACTION = 4;
        public const int DEFAULT_MAX_DAYS_TIL_MILITARY_ACTION = 10;
        public const int DEFAULT_MIN_DAYS_TIL_RANDOM_EVENT = 2;
        public const int DEFAULT_MAX_DAYS_TIL_RANDOM_EVENT = 8;
        public const float DEFAULT_MAX_THREAT_MULTIPLIER = 3.0f;
        public const float DEFAULT_DEFENDER_ADVANTAGE = 1.15f;
        public const float DEFAULT_EFFICIENCY_DAMPING = 0.5f;
        /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-* 
         *           ~  DEFAULTS END ~
         *-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/

        public const int updateUiTimer = 150; // UI update interval in ticks

        public static int silverPerResource = DEFAULT_SILVER_PER_RESOURCE;
        public static double silverToCreateSettlement = DEFAULT_SETTLEMENT_FOUNDING_COST;

        private static int timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS;
        public static int timeBetweenTaxes => Math.Max(1, timeBetweenTaxes_days) * GenDate.TicksPerDay;


        public static int productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD;
        public static int workerCost = DEFAULT_WORKER_COST;

        public static EmpireDifficultyLevel difficultyLevel = DEFAULT_DIFFICULTY_LEVEL;

        public static double settlementBaseUpgradeCost = DEFAULT_SETTLEMENT_BASE_UPGRADE_COST;
        public static int settlementMaxLevel = DEFAULT_SETTLEMENT_MAX_LEVEL;

        public static bool showSettleConfirm = DEFAULT_SHOW_SETTLE_CONFIRM;
        public static bool medievalTechOnly = DEFAULT_MEDIEVAL_TECH_ONLY;
        public static bool mirrorPlayerTechLevel = DEFAULT_MIRROR_PLAYER_TECH_LEVEL;
        public static bool disableHostileMilitaryActions = DEFAULT_DISABLE_HOSTILE_MILITARY_ACTIONS;
        public static bool disableRandomEvents = DEFAULT_DISABLE_RANDOM_EVENTS;
        public static bool disableForcedPausingDuringEvents = DEFAULT_DISABLE_FORCED_PAUSING_DURING_EVENTS;
        public static float eventOptionDelaySeconds = DEFAULT_EVENT_OPTION_DELAY_SECONDS;
        public static bool deadPawnsIncreaseMilitaryCooldown = DEFAULT_DEAD_PAWNS_INCREASE_MILITARY_COOLDOWN;
        public static bool useThreadedRoadComputation = DEFAULT_USE_THREADED_ROAD_COMPUTATION;
        public static int edgesPerRoadTick = DEFAULT_EDGES_PER_ROAD_TICK;
        public static BattleMode battleMode = DEFAULT_BATTLE_MODE;
        public static TaxDeliveryMode forcedTaxDeliveryMode = DEFAULT_TAX_DELIVERY_MODE;
        public static TaxNotificationMode taxNotificationMode = DEFAULT_TAX_NOTIFICATION_MODE;

        public static int minDaysTillMilitaryAction = DEFAULT_MIN_DAYS_TIL_MILITARY_ACTION;
        public static int maxDaysTillMilitaryAction = DEFAULT_MAX_DAYS_TIL_MILITARY_ACTION;
        public static IntRange minMaxDaysTillMilitaryAction = new IntRange(minDaysTillMilitaryAction, maxDaysTillMilitaryAction);

        public static int minDaysTillRandomEvent = DEFAULT_MIN_DAYS_TIL_RANDOM_EVENT;
        public static int maxDaysTillRandomEvent = DEFAULT_MAX_DAYS_TIL_RANDOM_EVENT;
        public static IntRange minMaxDaysTillRandomEvent = new IntRange(minDaysTillRandomEvent, maxDaysTillRandomEvent);

        /* TODO: might be interesting to expose these values in the settings. Might be a bit much
         * for the user though. Perhaps can add an "advanced settings" tab that lets the user
         * fine-tune a lot of the smaller values? */
        public static double unrestBaseGain = 0;
        public static double unrestBaseLost = 1;
        public static double loyaltyBaseGain = 1;
        public static double loyaltyBaseLost = 0;
        public static double happinessBaseGain = 1;
        public static double happinessBaseLost = 0;
        public static double prosperityDriftRate = 1;
        public static int productionResearchBase = 100;
        public static double militaryAnimalCostMultiplier = 1.5;
        public static double militaryRaceCostMultiplier = 0.075;
        public static float mercenaryHealRatePerHour = 1f;

        public static float maxThreatMultiplier = DEFAULT_MAX_THREAT_MULTIPLIER;
        public static float defenderAdvantage = DEFAULT_DEFENDER_ADVANTAGE;
        public static float efficiencyDamping = DEFAULT_EFFICIENCY_DAMPING;

        /* Squad hiring economy. squadHireCostMultiplier scales the up-front silver paid when
         * hiring a squad from a template (1.0 = template's full equipment cost; 0.0 = free).
         * squadDismissalRefundFraction is how much of the recorded hire cost is returned when
         * a squad is dismissed (0.5 = 50%). squadUpgradeCostMultiplier scales the diff paid
         * to bring an existing hired squad's loadout up to its template's current cost. */
        public const float DEFAULT_SQUAD_HIRE_COST_MULTIPLIER = 1.0f;
        public const float DEFAULT_SQUAD_DISMISSAL_REFUND_FRACTION = 0.5f;
        public const float DEFAULT_SQUAD_UPGRADE_COST_MULTIPLIER = 1.0f;
        public const int DEFAULT_MAX_SQUAD_SIZE = 30;
        public static float squadHireCostMultiplier = DEFAULT_SQUAD_HIRE_COST_MULTIPLIER;
        public static float squadDismissalRefundFraction = DEFAULT_SQUAD_DISMISSAL_REFUND_FRACTION;
        public static float squadUpgradeCostMultiplier = DEFAULT_SQUAD_UPGRADE_COST_MULTIPLIER;
        public static int maxSquadSize = DEFAULT_MAX_SQUAD_SIZE;

        /* Squad deployment economy. squadDeploymentCostPercentage is the fraction of a
         * squad's current equipment value billed in silver each time it is deployed
         * (offensive op or call-in to a player map). Defensive ops are free. The charge
         * is created as a BillFC against the squad's home settlement, due after
         * deploymentBillLifespan_days days; an unpaid bill incurs the standard bill
         * penalties below. */
        public const float DEFAULT_SQUAD_DEPLOYMENT_COST_PERCENTAGE = 0.20f;
        public const int DEFAULT_DEPLOYMENT_BILL_LIFESPAN_DAYS = 5;
        public static float squadDeploymentCostPercentage = DEFAULT_SQUAD_DEPLOYMENT_COST_PERCENTAGE;
        public static int deploymentBillLifespan_days = DEFAULT_DEPLOYMENT_BILL_LIFESPAN_DAYS;

        /// <summary>Max simultaneous manual battle maps across all settlements. 0 = unlimited.</summary>
        public static int maxConcurrentBattleMaps = 0;

        /* Auto-resolve battle pacing. Defensive auto-resolve battles stay in the Engaged
         * phase for ComputeAutoResolveDuration(BattleResult) ticks before CompleteBattle
         * fires, so a sweep feels different from a grueling slog. Formula:
         *   ticks = clamp(autoResolveBaseTicks + rounds * autoResolveTicksPerRound,
         *                 [autoResolveMinTicks, autoResolveMaxTicks])
         * then IAutoResolveDurationProvider modifiers run via AutoResolveDurationRegistry. */
        public const int DEFAULT_AUTO_RESOLVE_BASE_TICKS = 5000;
        public const int DEFAULT_AUTO_RESOLVE_TICKS_PER_ROUND = 600;
        public const int DEFAULT_AUTO_RESOLVE_MIN_TICKS = 2500;
        public const int DEFAULT_AUTO_RESOLVE_MAX_TICKS = 120000;
        public static int autoResolveBaseTicks = DEFAULT_AUTO_RESOLVE_BASE_TICKS;
        public static int autoResolveTicksPerRound = DEFAULT_AUTO_RESOLVE_TICKS_PER_ROUND;
        public static int autoResolveMinTicks = DEFAULT_AUTO_RESOLVE_MIN_TICKS;
        public static int autoResolveMaxTicks = DEFAULT_AUTO_RESOLVE_MAX_TICKS;

        public static int maxPolicyCount = 2;

        /* Flag for debug/verbose logging. */
        private static bool printDebug = false;
        public static bool PrintDebug => printDebug;

        // Window size settings
        public static float buildingWindowWidth = 800f;
        public static float buildingWindowHeight = 600f;

        // Patch notes version tracking — per-mod dictionary of "major.minor.patch" strings
        public static Dictionary<string, string> lastSeenVersions = new Dictionary<string, string>();

        // Patch notes auto-open threshold
        public const PatchNoteType DEFAULT_PATCH_NOTE_AUTO_OPEN_THRESHOLD = PatchNoteType.Major;
        public static PatchNoteType patchNoteAutoOpenThreshold = DEFAULT_PATCH_NOTE_AUTO_OPEN_THRESHOLD;

        // Saved color picker colors (persisted across sessions)
        public static List<Color> savedPickerColors = new List<Color>();
        public const int MaxSavedPickerColors = 24;

        // Per-event disable list (defName strings)
        public static HashSet<string> disabledEventDefs = new HashSet<string>();
        public static bool IsEventDisabled(string defName) => disabledEventDefs.Contains(defName);

        // Settings tab state
        private static int settingsTab = 0;
        private static List<TabRecord> settingsTabs = new List<TabRecord>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref silverPerResource, "silverPerResource", DEFAULT_SILVER_PER_RESOURCE);
            Scribe_Values.Look(ref timeBetweenTaxes_days, "timeBetweenTaxes_days", DEFAULT_TAX_INTERVAL_DAYS);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (timeBetweenTaxes_days < 1)
                {
                    LogUtil.Warning($"Loaded suspicious timeBetweenTaxes_days={timeBetweenTaxes_days} from settings; resetting to DEFAULT_TAX_INTERVAL_DAYS ({DEFAULT_TAX_INTERVAL_DAYS}).");
                    timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS;
                }
                else
                {
                    LogUtil.Message($"Loaded timeBetweenTaxes_days={timeBetweenTaxes_days} from settings.");
                }
            }
            Scribe_Values.Look(ref productionTitheMod, "productionTitheMod", DEFAULT_PRODUCTION_TITHE_MOD);
            Scribe_Values.Look(ref workerCost, "workerCost", DEFAULT_WORKER_COST);
            Scribe_Values.Look(ref settlementMaxLevel, "settlementMaxLevel", DEFAULT_SETTLEMENT_MAX_LEVEL);
            Scribe_Values.Look(ref showSettleConfirm, "showSettleConfirm", DEFAULT_SHOW_SETTLE_CONFIRM);
            Scribe_Values.Look(ref medievalTechOnly, "medievalTechOnly", DEFAULT_MEDIEVAL_TECH_ONLY);
            Scribe_Values.Look(ref mirrorPlayerTechLevel, "mirrorPlayerTechLevel", DEFAULT_MIRROR_PLAYER_TECH_LEVEL);
            Scribe_Values.Look(ref disableHostileMilitaryActions, "disableHostileMilitaryActions", DEFAULT_DISABLE_HOSTILE_MILITARY_ACTIONS);
            Scribe_Values.Look(ref disableRandomEvents, "disableRandomEvents", DEFAULT_DISABLE_RANDOM_EVENTS);
            Scribe_Values.Look(ref disableForcedPausingDuringEvents, "disableForcedPausingDuringEvents", DEFAULT_DISABLE_FORCED_PAUSING_DURING_EVENTS);
            Scribe_Values.Look(ref eventOptionDelaySeconds, "eventOptionDelaySeconds", DEFAULT_EVENT_OPTION_DELAY_SECONDS);
            Scribe_Values.Look(ref forcedTaxDeliveryMode, "forcedTaxDeliveryMode", DEFAULT_TAX_DELIVERY_MODE);
            Scribe_Values.Look(ref taxNotificationMode, "taxNotificationMode", DEFAULT_TAX_NOTIFICATION_MODE);
            Scribe_Values.Look(ref deadPawnsIncreaseMilitaryCooldown, "deadPawnsIncreaseMilitaryCooldown", DEFAULT_DEAD_PAWNS_INCREASE_MILITARY_COOLDOWN);
            Scribe_Values.Look(ref useThreadedRoadComputation, "useThreadedRoadComputation", DEFAULT_USE_THREADED_ROAD_COMPUTATION);
            Scribe_Values.Look(ref edgesPerRoadTick, "edgesPerRoadTick", DEFAULT_EDGES_PER_ROAD_TICK);
            Scribe_Values.Look(ref battleMode, "battleMode", DEFAULT_BATTLE_MODE);
            Scribe_Values.Look(ref minDaysTillMilitaryAction, "minDaysTillMilitaryAction", DEFAULT_MIN_DAYS_TIL_MILITARY_ACTION);
            Scribe_Values.Look(ref maxDaysTillMilitaryAction, "maxDaysTillMilitaryAction", DEFAULT_MAX_DAYS_TIL_MILITARY_ACTION);
            Scribe_Values.Look(ref minDaysTillRandomEvent, "minDaysTillRandomEvent", DEFAULT_MIN_DAYS_TIL_RANDOM_EVENT);
            Scribe_Values.Look(ref maxDaysTillRandomEvent, "maxDaysTillRandomEvent", DEFAULT_MAX_DAYS_TIL_RANDOM_EVENT);
            Scribe_Values.Look(ref buildingWindowWidth, "buildingWindowWidth", 800f);
            Scribe_Values.Look(ref buildingWindowHeight, "buildingWindowHeight", 600f);
            Scribe_Values.Look(ref difficultyLevel, "difficultyLevel", DEFAULT_DIFFICULTY_LEVEL);
            Scribe_Values.Look(ref printDebug, "printDebug", false);
            Scribe_Values.Look(ref maxThreatMultiplier, "maxThreatMultiplier", DEFAULT_MAX_THREAT_MULTIPLIER);
            Scribe_Values.Look(ref defenderAdvantage, "defenderAdvantage", DEFAULT_DEFENDER_ADVANTAGE);
            Scribe_Values.Look(ref efficiencyDamping, "efficiencyDamping", DEFAULT_EFFICIENCY_DAMPING);
            Scribe_Values.Look(ref maxConcurrentBattleMaps, "maxConcurrentBattleMaps", 0);
            Scribe_Values.Look(ref autoResolveBaseTicks, "autoResolveBaseTicks", DEFAULT_AUTO_RESOLVE_BASE_TICKS);
            Scribe_Values.Look(ref autoResolveTicksPerRound, "autoResolveTicksPerRound", DEFAULT_AUTO_RESOLVE_TICKS_PER_ROUND);
            Scribe_Values.Look(ref autoResolveMinTicks, "autoResolveMinTicks", DEFAULT_AUTO_RESOLVE_MIN_TICKS);
            Scribe_Values.Look(ref autoResolveMaxTicks, "autoResolveMaxTicks", DEFAULT_AUTO_RESOLVE_MAX_TICKS);
            Scribe_Values.Look(ref mercenaryHealRatePerHour, "mercenaryHealRatePerHour", 1f);
            Scribe_Values.Look(ref squadHireCostMultiplier, "squadHireCostMultiplier", DEFAULT_SQUAD_HIRE_COST_MULTIPLIER);
            Scribe_Values.Look(ref squadDismissalRefundFraction, "squadDismissalRefundFraction", DEFAULT_SQUAD_DISMISSAL_REFUND_FRACTION);
            Scribe_Values.Look(ref squadUpgradeCostMultiplier, "squadUpgradeCostMultiplier", DEFAULT_SQUAD_UPGRADE_COST_MULTIPLIER);
            Scribe_Values.Look(ref maxSquadSize, "maxSquadSize", DEFAULT_MAX_SQUAD_SIZE);
            Scribe_Values.Look(ref squadDeploymentCostPercentage, "squadDeploymentCostPercentage", DEFAULT_SQUAD_DEPLOYMENT_COST_PERCENTAGE);
            Scribe_Values.Look(ref deploymentBillLifespan_days, "deploymentBillLifespan_days", DEFAULT_DEPLOYMENT_BILL_LIFESPAN_DAYS);
            if (Scribe.mode == LoadSaveMode.LoadingVars && maxSquadSize < 1)
            {
                LogUtil.Warning($"Loaded suspicious maxSquadSize={maxSquadSize}; resetting to {DEFAULT_MAX_SQUAD_SIZE}.");
                maxSquadSize = DEFAULT_MAX_SQUAD_SIZE;
            }
            Scribe_Collections.Look(ref lastSeenVersions, "lastSeenVersions", LookMode.Value, LookMode.Value);
            if (lastSeenVersions is null) lastSeenVersions = new Dictionary<string, string>();
            Scribe_Values.Look(ref patchNoteAutoOpenThreshold, "patchNoteAutoOpenThreshold", DEFAULT_PATCH_NOTE_AUTO_OPEN_THRESHOLD);
            Scribe_Collections.Look(ref disabledEventDefs, "disabledEventDefs", LookMode.Value);
            if (disabledEventDefs is null) disabledEventDefs = new HashSet<string>();

            Scribe_Collections.Look(ref savedPickerColors, "savedPickerColors", LookMode.Value);
            if (savedPickerColors is null) savedPickerColors = new List<Color>();

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                /* Re-construct the intranges */
                minMaxDaysTillMilitaryAction = new IntRange(minDaysTillMilitaryAction, maxDaysTillMilitaryAction);
                minMaxDaysTillRandomEvent = new IntRange(minDaysTillRandomEvent, maxDaysTillRandomEvent);

                // Migrate old single-mod lastSeenVersion fields
                int oldMajor = 0, oldMinor = 0, oldPatch = 0;
                Scribe_Values.Look(ref oldMajor, "lastSeenVersionMajor", -1);
                Scribe_Values.Look(ref oldMinor, "lastSeenVersionMinor", -1);
                Scribe_Values.Look(ref oldPatch, "lastSeenVersionPatch", -1);
                if (oldMajor >= 0 && !lastSeenVersions.ContainsKey("matathias.empire"))
                {
                    SetLastSeenVersion("matathias.empire", oldMajor, oldMinor, oldPatch);
                }
            }
        }

        public static string GetModVersion()
        {
            var mod = LoadedModManager.GetMod<FactionColoniesMod>();
            string version = mod?.Content?.ModMetaData?.ModVersion;
            return version.NullOrEmpty() ? "Unknown" : version;
        }

        public static void GetLastSeenVersion(string modId, out int major, out int minor, out int patch)
        {
            if (lastSeenVersions.TryGetValue(modId, out string v))
            {
                string[] parts = v.Split('.');
                if (parts.Length == 3
                    && int.TryParse(parts[0], out major)
                    && int.TryParse(parts[1], out minor)
                    && int.TryParse(parts[2], out patch))
                    return;
            }
            major = 0;
            minor = 0;
            patch = 0;
        }

        public static void SetLastSeenVersion(string modId, int major, int minor, int patch)
        {
            lastSeenVersions[modId] = major + "." + minor + "." + patch;
        }

        public static void ReapplyStatModifiers()
        {
            FactionFC faction = FactionCache.FactionComp;
            /* Clear stat modifiers for all settlements, and then reapply inherent/building modifiers */
            foreach (WorldSettlementFC settlement in faction.settlements)
            {
                settlement.ClearStatModifiers();
                settlement.BuildingsComp?.ReapplyBuildingStatModifiers();
                settlement.AddStatModifiers(settlement.settlementDef.statModifiers, "settlementType", settlement.settlementDef.label);
            }

            // Re-apply active event stat modifiers to settlements
            foreach (FCEvent evt in faction.Events)
            {
                string sourceId = "event_" + evt.def.defName;
                if (evt.settlementTraitLocations.Any())
                {
                    foreach (WorldSettlementFC location in evt.settlementTraitLocations)
                    {
                        if (location != null)
                            location.AddStatModifiers(evt.def.statModifiers, sourceId, evt.def.label);
                    }
                }
                else
                {
                    foreach (WorldSettlementFC settlement in faction.settlements)
                    {
                        settlement.AddStatModifiers(evt.def.statModifiers, sourceId, evt.def.label);
                    }
                }
            }
        }

        public static bool IsModLoaded(string packageID) => LoadedModManager.RunningModsListForReading.Any(mod => mod.PackageIdPlayerFacing == packageID);


        public static void DebugMarker(ref int i)
        {
            LogUtil.Message($"DebugMarker: {i}");
            i++;
        }

        // Difficulty preset values
        public static void ApplyDifficultyPreset(EmpireDifficultyLevel difficulty)
        {
            switch (difficulty)
            {
                case EmpireDifficultyLevel.Peaceful:
                    silverPerResource = DEFAULT_SILVER_PER_RESOURCE_PEACEFUL;
                    timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS_PEACEFUL;
                    productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD_PEACEFUL;
                    workerCost = DEFAULT_WORKER_COST_PEACEFUL;
                    break;
                case EmpireDifficultyLevel.CommunityBuilder:
                    silverPerResource = DEFAULT_SILVER_PER_RESOURCE_COMMUNITYBUILDER;
                    timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS_COMMUNITYBUILDER;
                    productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD_COMMUNITYBUILDER;
                    workerCost = DEFAULT_WORKER_COST_COMMUNITYBUILDER;
                    break;
                case EmpireDifficultyLevel.AdventureStory:
                    silverPerResource = DEFAULT_SILVER_PER_RESOURCE_ADVENTURESTORY;
                    timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS_ADVENTURESTORY;
                    productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD_ADVENTURESTORY;
                    workerCost = DEFAULT_WORKER_COST_ADVENTURESTORY;
                    break;
                case EmpireDifficultyLevel.StriveToSurvive:
                    silverPerResource = DEFAULT_SILVER_PER_RESOURCE_STRIVETOSURVIVE;
                    timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS_STRIVETOSURVIVE;
                    productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD_STRIVETOSURVIVE;
                    workerCost = DEFAULT_WORKER_COST_STRIVETOSURVIVE;
                    break;
                case EmpireDifficultyLevel.BloodAndDust:
                    silverPerResource = DEFAULT_SILVER_PER_RESOURCE_BLOODANDDUST;
                    timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS_BLOODANDDUST;
                    productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD_BLOODANDDUST;
                    workerCost = DEFAULT_WORKER_COST_BLOODANDDUST;
                    break;
                case EmpireDifficultyLevel.LosingIsFun:
                    silverPerResource = DEFAULT_SILVER_PER_RESOURCE_LOSINGISFUN;
                    timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS_LOSINGISFUN;
                    productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD_LOSINGISFUN;
                    workerCost = DEFAULT_WORKER_COST_LOSINGISFUN;
                    break;
                case EmpireDifficultyLevel.Custom:
                    // Don't change anything for custom
                    break;
            }
            LogUtil.Message($"ApplyDifficultyPreset({difficulty}): timeBetweenTaxes_days={timeBetweenTaxes_days}");
        }

        public static int DaysBetweenTaxesByDifficulty(EmpireDifficultyLevel difficulty)
        {
            switch (difficulty)
            {
                case EmpireDifficultyLevel.Peaceful:
                    return DEFAULT_TAX_INTERVAL_DAYS_PEACEFUL;
                case EmpireDifficultyLevel.CommunityBuilder:
                    return DEFAULT_TAX_INTERVAL_DAYS_COMMUNITYBUILDER;
                case EmpireDifficultyLevel.AdventureStory:
                    return DEFAULT_TAX_INTERVAL_DAYS_ADVENTURESTORY;
                case EmpireDifficultyLevel.StriveToSurvive:
                    return DEFAULT_TAX_INTERVAL_DAYS_STRIVETOSURVIVE;
                case EmpireDifficultyLevel.BloodAndDust:
                    return DEFAULT_TAX_INTERVAL_DAYS_BLOODANDDUST;
                case EmpireDifficultyLevel.LosingIsFun:
                    return DEFAULT_TAX_INTERVAL_DAYS_LOSINGISFUN;
                default:
                    return DEFAULT_TAX_INTERVAL_DAYS;
            }
        }
        public static int TicksBetweenTaxesByDifficulty(EmpireDifficultyLevel difficulty)
        {
            return DaysBetweenTaxesByDifficulty(difficulty) * GenDate.TicksPerDay;
        }

        string silverPerResource_buffer;
        string timeBetweenTaxes_buffer;
        string productionTitheMod_buffer;
        string workerCost_buffer;
        string settlementMaxLevel_buffer;

        private static int timeBetweenTaxes_lastSeen = DEFAULT_TAX_INTERVAL_DAYS;

        private Vector2 scrollVectorGeneral = new Vector2();
        private Vector2 scrollVectorEvents = new Vector2();
        private Vector2 scrollVectorMilitary = new Vector2();
        private Vector2 scrollVectorRoadBuilder = new Vector2();

        /// <summary>
        /// Creates an option for the list of ForcedTaxDeliveryOptions. Shuttles may not be used if royality is inactive
        /// </summary>
        private FloatMenuOption ShuttleOption
        {
            get
            {
                if (ModsConfig.RoyaltyActive)
                {
                    return new FloatMenuOption("FCTaxDeliveryModeShuttleDesc".Translate(), delegate () { forcedTaxDeliveryMode = TaxDeliveryMode.Shuttle; });
                }
                else
                {
                    return new FloatMenuOption("FCTaxDeliveryModeShuttleUnavailableDesc".Translate(), null);
                }
            }
        }

        /// <summary>
        /// Creates a list of options for forced tax delivery
        /// </summary>
        private List<FloatMenuOption> ForcedTaxDeliveryOptions
        {
            get
            {
                return new List<FloatMenuOption>()
                {
                    new FloatMenuOption("FCTaxDeliveryModeDefaultDesc".Translate(), delegate() {forcedTaxDeliveryMode = default;}),
                    new FloatMenuOption("FCTaxDeliveryModeTaxSpotDesc".Translate(), delegate() {forcedTaxDeliveryMode = TaxDeliveryMode.TaxSpot;}),
                    new FloatMenuOption("FCTaxDeliveryModeCaravanDesc".Translate(), delegate() {forcedTaxDeliveryMode = TaxDeliveryMode.Caravan;}),
                    new FloatMenuOption("FCTaxDeliveryModeDropPodDesc".Translate(), delegate() {forcedTaxDeliveryMode = TaxDeliveryMode.DropPod;}),
                    ShuttleOption
                };
            }
        }

        private List<FloatMenuOption> BattleModeOptions => new List<FloatMenuOption>
        {
            new FloatMenuOption("FCBattleModeAuto".Translate() + " - " + "FCBattleModeAutoDesc".Translate(), () => battleMode = BattleMode.Auto),
            new FloatMenuOption("FCBattleModeManual".Translate() + " - " + "FCBattleModeManualDesc".Translate(), () => battleMode = BattleMode.Manual),
            new FloatMenuOption("FCBattleModeHybrid".Translate() + " - " + "FCBattleModeHybridDesc".Translate(), () => battleMode = BattleMode.Hybrid)
        };

        /// <summary>
        /// Creates a list of options for tax notification mode
        /// </summary>
        private List<FloatMenuOption> TaxNotificationOptions => new List<FloatMenuOption>
        {
            new FloatMenuOption("FCTaxNotifyAll".Translate(), () => taxNotificationMode = TaxNotificationMode.All),
            new FloatMenuOption("FCTaxNotifyLetterOnly".Translate(), () => taxNotificationMode = TaxNotificationMode.LetterOnly),
            new FloatMenuOption("FCTaxNotifyMessageOnly".Translate(), () => taxNotificationMode = TaxNotificationMode.MessageOnly),
            new FloatMenuOption("FCTaxNotifyNone".Translate(), () => taxNotificationMode = TaxNotificationMode.None)
        };

        public void DoWindowContents(Rect inRect)
        {
            // Build tabs
            settingsTabs.Clear();
            settingsTabs.Add(new TabRecord("FCSettingsTabGeneral".Translate(), delegate { settingsTab = 0; }, settingsTab == 0));
            settingsTabs.Add(new TabRecord("FCSettingsTabEvents".Translate(), delegate { settingsTab = 1; }, settingsTab == 1));
            settingsTabs.Add(new TabRecord("FCSettingsTabMilitary".Translate(), delegate { settingsTab = 2; }, settingsTab == 2));
            settingsTabs.Add(new TabRecord("FCSettingsTabRoadBuilder".Translate(), delegate { settingsTab = 3; }, settingsTab == 3));

            Rect contentRect = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f);
            Widgets.DrawMenuSection(contentRect);
            TabDrawer.DrawTabs(contentRect, settingsTabs);

            // Inset the content area slightly for padding
            Rect innerRect = contentRect.ContractedBy(5f);

            switch (settingsTab)
            {
                case 0: DoGeneralTab(innerRect); break;
                case 1: DoEventsTab(innerRect); break;
                case 2: DoMilitaryTab(innerRect); break;
                case 3: DoRoadBuilderTab(innerRect); break;
            }
        }

        private void DoGeneralTab(Rect rect)
        {
            silverPerResource_buffer = silverPerResource.ToString();
            timeBetweenTaxes_buffer = timeBetweenTaxes_days.ToString();
            timeBetweenTaxes_lastSeen = timeBetweenTaxes_days;
            productionTitheMod_buffer = productionTitheMod.ToString();
            workerCost_buffer = workerCost.ToString();
            settlementMaxLevel_buffer = settlementMaxLevel.ToString();

            minMaxDaysTillMilitaryAction = new IntRange(minDaysTillMilitaryAction, maxDaysTillMilitaryAction);
            minMaxDaysTillRandomEvent = new IntRange(minDaysTillRandomEvent, maxDaysTillRandomEvent);

            Rect viewRect = ScrollUtil.BeginScrollView(rect, ref scrollVectorGeneral, float.MaxValue);
            Rect listRect = new Rect(viewRect.x, viewRect.y, viewRect.width, float.MaxValue);
            Listing_Standard ls = new Listing_Standard();
            ls.Begin(listRect);

            // Display mod version
            ls.Label("FCModVersion".Translate(GetModVersion()));
            ls.Gap(10f);

            // Empire Difficulty Selection
            ls.Label("FCSettingEmpireDifficulty".Translate());
            ls.Gap(5f);

            // Create difficulty options with descriptions
            var difficultyOptions = new List<(EmpireDifficultyLevel level, string nameKey, string descKey)>
            {
                (EmpireDifficultyLevel.Peaceful, "FCDifficultyPeaceful", "FCDifficultyPeacefulDesc"),
                (EmpireDifficultyLevel.CommunityBuilder, "FCDifficultyCommunityBuilder", "FCDifficultyCommunityBuilderDesc"),
                (EmpireDifficultyLevel.AdventureStory, "FCDifficultyAdventureStory", "FCDifficultyAdventureStoryDesc"),
                (EmpireDifficultyLevel.StriveToSurvive, "FCDifficultyStriveToSurvive", "FCDifficultyStriveToSurviveDesc"),
                (EmpireDifficultyLevel.BloodAndDust, "FCDifficultyBloodAndDust", "FCDifficultyBloodAndDustDesc"),
                (EmpireDifficultyLevel.LosingIsFun, "FCDifficultyLosingIsFun", "FCDifficultyLosingIsFunDesc"),
                (EmpireDifficultyLevel.Custom, "FCDifficultyCustom", "FCDifficultyCustomDesc")
            };

            foreach (var option in difficultyOptions)
            {
                bool isSelected = difficultyLevel == option.level;

                if (ls.RadioButton(option.nameKey.Translate(), isSelected))
                {
                    if (!isSelected) // Only change if not already selected
                    {
                        difficultyLevel = option.level;
                        if (option.level != EmpireDifficultyLevel.Custom)
                        {
                            ApplyDifficultyPreset(option.level);
                        }
                    }
                }
                // Add description as a separate indented label
                ls.Label("    " + option.descKey.Translate(), -1f);
            }

            ls.Gap(15f);

            // Show economic settings only if Custom is selected
            if (difficultyLevel == EmpireDifficultyLevel.Custom)
            {
                ls.Label("FCSettingSilverPerResource".Translate());
                ls.IntEntry(ref silverPerResource, ref silverPerResource_buffer);
                ls.Label("FCSettingDaysBetweenTax".Translate());
                ls.IntEntry(ref timeBetweenTaxes_days, ref timeBetweenTaxes_buffer);
                if (timeBetweenTaxes_days < 1)
                {
                    timeBetweenTaxes_days = 1;
                    timeBetweenTaxes_buffer = "1";
                }
                if (timeBetweenTaxes_days != timeBetweenTaxes_lastSeen)
                {
                    LogUtil.Message($"Settings UI: timeBetweenTaxes_days {timeBetweenTaxes_lastSeen} -> {timeBetweenTaxes_days}");
                    timeBetweenTaxes_lastSeen = timeBetweenTaxes_days;
                }
                ls.Label("FCSettingProductionTitheMod".Translate());
                ls.IntEntry(ref productionTitheMod, ref productionTitheMod_buffer);
                ls.Label("FCSettingWorkerCost".Translate());
                ls.IntEntry(ref workerCost, ref workerCost_buffer);
            }
            else
            {
                // Show current values as read-only labels for non-custom difficulties
                ls.Label($"FCSettingSilverPerResource".Translate() + ": " + silverPerResource);
                ls.Label($"FCSettingDaysBetweenTax".Translate() + ": " + timeBetweenTaxes_days);
                ls.Label($"FCSettingProductionTitheMod".Translate() + ": " + productionTitheMod);
                ls.Label($"FCSettingWorkerCost".Translate() + ": " + workerCost);
            }

            ls.Label("FCSettingMaxSettlementLevel".Translate());
            ls.IntEntry(ref settlementMaxLevel, ref settlementMaxLevel_buffer);
            ls.CheckboxLabeled("FCMedievalTechOnly".Translate(), ref medievalTechOnly);
            bool prevMirrorPlayerTechLevel = mirrorPlayerTechLevel;
            ls.CheckboxLabeled("FCMirrorPlayerTechLevel".Translate(), ref mirrorPlayerTechLevel, "FCMirrorPlayerTechLevelDesc".Translate());
            if (prevMirrorPlayerTechLevel != mirrorPlayerTechLevel)
            {
                FactionCache.FactionComp?.DirtyTechLevelCache();
            }
            ls.CheckboxLabeled("FCSettingShowSettleConfirm".Translate(), ref showSettleConfirm);
            if (ls.ButtonText("FCSelectTaxDeliveryModeButton".Translate() + forcedTaxDeliveryMode)) Find.WindowStack.Add(new FloatMenu(ForcedTaxDeliveryOptions));
            if (ls.ButtonText("FCTaxNotificationModeButton".Translate() + taxNotificationMode)) Find.WindowStack.Add(new FloatMenu(TaxNotificationOptions));

            ls.CheckboxLabeled("FCSettingEnableDebugLogging".Translate(), ref printDebug);

            if (ls.ButtonText("FCOpenPatchNotes".Translate())) DebugActionsMisc.PatchNotesDisplayWindow();

            string thresholdLabel;
            switch (patchNoteAutoOpenThreshold)
            {
                case PatchNoteType.Major: thresholdLabel = "Major only"; break;
                case PatchNoteType.Minor: thresholdLabel = "Minor and above"; break;
                case PatchNoteType.Hotfix: thresholdLabel = "Hotfix and above"; break;
                case PatchNoteType.Patch: thresholdLabel = "Patch and above"; break;
                default: thresholdLabel = "Never"; break;
            }
            if (ls.ButtonText("FCPatchNoteAutoOpenThreshold".Translate() + thresholdLabel))
            {
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("Major only", () => patchNoteAutoOpenThreshold = PatchNoteType.Major),
                    new FloatMenuOption("Minor and above", () => patchNoteAutoOpenThreshold = PatchNoteType.Minor),
                    new FloatMenuOption("Hotfix and above", () => patchNoteAutoOpenThreshold = PatchNoteType.Hotfix),
                    new FloatMenuOption("Patch and above", () => patchNoteAutoOpenThreshold = PatchNoteType.Patch),
                    new FloatMenuOption("Never", () => patchNoteAutoOpenThreshold = PatchNoteType.Undefined)
                }));
            }

            if (ls.ButtonText("FCSettingResetButton".Translate()))
            {
                silverPerResource = DEFAULT_SILVER_PER_RESOURCE;
                timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS;
                productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD;
                workerCost = DEFAULT_WORKER_COST;
                medievalTechOnly = DEFAULT_MEDIEVAL_TECH_ONLY;
                mirrorPlayerTechLevel = DEFAULT_MIRROR_PLAYER_TECH_LEVEL;
                settlementMaxLevel = DEFAULT_SETTLEMENT_MAX_LEVEL;
                minDaysTillMilitaryAction = DEFAULT_MIN_DAYS_TIL_MILITARY_ACTION;
                maxDaysTillMilitaryAction = DEFAULT_MAX_DAYS_TIL_MILITARY_ACTION;
                minDaysTillRandomEvent = DEFAULT_MIN_DAYS_TIL_RANDOM_EVENT;
                maxDaysTillRandomEvent = DEFAULT_MAX_DAYS_TIL_RANDOM_EVENT;
                disableRandomEvents = DEFAULT_DISABLE_RANDOM_EVENTS;
                deadPawnsIncreaseMilitaryCooldown = DEFAULT_DEAD_PAWNS_INCREASE_MILITARY_COOLDOWN;
                useThreadedRoadComputation = DEFAULT_USE_THREADED_ROAD_COMPUTATION;
                edgesPerRoadTick = DEFAULT_EDGES_PER_ROAD_TICK;
                battleMode = DEFAULT_BATTLE_MODE;
                maxThreatMultiplier = DEFAULT_MAX_THREAT_MULTIPLIER;
                defenderAdvantage = DEFAULT_DEFENDER_ADVANTAGE;
                efficiencyDamping = DEFAULT_EFFICIENCY_DAMPING;
                maxConcurrentBattleMaps = 0;
                autoResolveBaseTicks = DEFAULT_AUTO_RESOLVE_BASE_TICKS;
                autoResolveTicksPerRound = DEFAULT_AUTO_RESOLVE_TICKS_PER_ROUND;
                autoResolveMinTicks = DEFAULT_AUTO_RESOLVE_MIN_TICKS;
                autoResolveMaxTicks = DEFAULT_AUTO_RESOLVE_MAX_TICKS;
                mercenaryHealRatePerHour = 1f;
                squadHireCostMultiplier = DEFAULT_SQUAD_HIRE_COST_MULTIPLIER;
                squadDismissalRefundFraction = DEFAULT_SQUAD_DISMISSAL_REFUND_FRACTION;
                squadUpgradeCostMultiplier = DEFAULT_SQUAD_UPGRADE_COST_MULTIPLIER;
                maxSquadSize = DEFAULT_MAX_SQUAD_SIZE;
                squadDeploymentCostPercentage = DEFAULT_SQUAD_DEPLOYMENT_COST_PERCENTAGE;
                deploymentBillLifespan_days = DEFAULT_DEPLOYMENT_BILL_LIFESPAN_DAYS;
                disableForcedPausingDuringEvents = DEFAULT_DISABLE_FORCED_PAUSING_DURING_EVENTS;
                forcedTaxDeliveryMode = DEFAULT_TAX_DELIVERY_MODE;
                taxNotificationMode = DEFAULT_TAX_NOTIFICATION_MODE;
                difficultyLevel = DEFAULT_DIFFICULTY_LEVEL;
                patchNoteAutoOpenThreshold = DEFAULT_PATCH_NOTE_AUTO_OPEN_THRESHOLD;
                disabledEventDefs.Clear();
                ApplyDifficultyPreset(difficultyLevel);
                LogUtil.Message($"Settings reset: timeBetweenTaxes_days={timeBetweenTaxes_days}");
            }

            ls.End();

            ScrollUtil.EndScrollView();
        }

        private void DoEventsTab(Rect rect)
        {
            Rect viewRect = ScrollUtil.BeginScrollView(rect, ref scrollVectorEvents, float.MaxValue);
            Rect listRect = new Rect(viewRect.x, viewRect.y, viewRect.width, float.MaxValue);
            Listing_Standard ls = new Listing_Standard();
            ls.Begin(listRect);

            ls.CheckboxLabeled("FCSettingDisableRandomEvents".Translate(), ref disableRandomEvents);
            ls.CheckboxLabeled("FCSettingForcedPausing".Translate(), ref disableForcedPausingDuringEvents);
            eventOptionDelaySeconds = ls.SliderLabeled(
                "FCSettingEventOptionDelay".Translate(eventOptionDelaySeconds.ToString("0.0")),
                eventOptionDelaySeconds, 0f, 2f);
            eventOptionDelaySeconds = (float)Math.Round(eventOptionDelaySeconds, 1);

            ls.Gap(5f);

            minMaxDaysTillRandomEvent = new IntRange(minDaysTillRandomEvent, maxDaysTillRandomEvent);
            ls.Label("FCSettingMinMaxRandomEvent".Translate());
            ls.IntRange(ref minMaxDaysTillRandomEvent, 0, 30);
            minDaysTillRandomEvent = minMaxDaysTillRandomEvent.min;
            maxDaysTillRandomEvent = Math.Max(1, minMaxDaysTillRandomEvent.max);

            ls.GapLine();

            ls.Label("FCSettingConfigureEvents".Translate());
            ls.Gap(5f);

            // Build set of events that are follow-ups of other events (not independent triggers)
            HashSet<string> followUpDefNames = new HashSet<string>();
            foreach (FCEventDef def in DefDatabase<FCEventDef>.AllDefsListForReading)
            {
                if (def.followingEvent != null) followUpDefNames.Add(def.followingEvent.defName);
                if (def.followingEvent2 != null) followUpDefNames.Add(def.followingEvent2.defName);
            }

            List<FCEventDef> rootEvents = new List<FCEventDef>();
            foreach (FCEventDef def in DefDatabase<FCEventDef>.AllDefsListForReading)
            {
                if (followUpDefNames.Contains(def.defName)) continue;
                if (def.activateAtStart || (def.isRandomEvent && def.options.Count == 0))
                {
                    rootEvents.Add(def);
                }
            }

            // Group by category, then sort alphabetically within each group
            var grouped = new Dictionary<string, List<FCEventDef>>();
            foreach (FCEventDef def in rootEvents)
            {
                string catLabel = def.category != null ? def.category.LabelCap.ToString() : "Other";
                if (!grouped.ContainsKey(catLabel))
                    grouped[catLabel] = new List<FCEventDef>();
                grouped[catLabel].Add(def);
            }

            int rowIndex = 0;
            foreach (string catLabel in grouped.Keys.OrderBy(k => k))
            {
                ls.Gap(3f);
                ls.Label(catLabel);
                foreach (FCEventDef def in grouped[catLabel].OrderBy(d => d.label))
                {
                    Rect rowRect = ls.GetRect(Text.LineHeight);

                    if (rowIndex % 2 == 1)
                    {
                        Widgets.DrawLightHighlight(rowRect);
                    }
                    rowIndex++;

                    bool enabled = !disabledEventDefs.Contains(def.defName);
                    bool prev = enabled;
                    Widgets.CheckboxLabeled(rowRect, "  " + def.label, ref enabled);
                    if (enabled != prev)
                    {
                        if (enabled) disabledEventDefs.Remove(def.defName);
                        else disabledEventDefs.Add(def.defName);
                    }
                }
            }

            if (disabledEventDefs.Count > 0)
            {
                ls.Gap(10f);
                if (ls.ButtonText("FCSettingEnableAllEvents".Translate()))
                {
                    disabledEventDefs.Clear();
                }
            }

            ls.End();

            ScrollUtil.EndScrollView();
        }

        private void DoMilitaryTab(Rect rect)
        {
            minMaxDaysTillMilitaryAction = new IntRange(minDaysTillMilitaryAction, maxDaysTillMilitaryAction);

            Rect viewRect = ScrollUtil.BeginScrollView(rect, ref scrollVectorMilitary, float.MaxValue);
            Rect listRect = new Rect(viewRect.x, viewRect.y, viewRect.width, float.MaxValue);
            Listing_Standard ls = new Listing_Standard();
            ls.Begin(listRect);

            ls.CheckboxLabeled("FCSettingDisableHostileMilActions".Translate(), ref disableHostileMilitaryActions);
            ls.CheckboxLabeled("FCSettingDeadPawnsIncreaseMilCooldown".Translate(), ref deadPawnsIncreaseMilitaryCooldown);
            if (ls.ButtonText("FCSettingBattleMode".Translate() + battleMode)) Find.WindowStack.Add(new FloatMenu(BattleModeOptions));

            ls.Gap(10f);

            ls.Label("FCSettingMinMaxMilitaryAction".Translate());
            ls.IntRange(ref minMaxDaysTillMilitaryAction, 1, 30);
            minDaysTillMilitaryAction = minMaxDaysTillMilitaryAction.min;
            maxDaysTillMilitaryAction = Math.Max(1, minMaxDaysTillMilitaryAction.max);

            ls.Label("FCSettingMaxThreatScaling".Translate() + ": " + maxThreatMultiplier.ToString("0.0") + "x");
            maxThreatMultiplier = ls.Slider(maxThreatMultiplier, 1.0f, 5.0f);

            ls.Label("FCSettingDefenderAdvantage".Translate() + ": " + defenderAdvantage.ToString("0.00") + "x");
            defenderAdvantage = ls.Slider(defenderAdvantage, 1.0f, 1.5f);

            string concurrentLabel = maxConcurrentBattleMaps == 0 ? (string)"FCUnlimited".Translate() : maxConcurrentBattleMaps.ToString();
            ls.Label("FCSettingMaxConcurrentBattleMaps".Translate() + ": " + concurrentLabel, -1f, "FCSettingMaxConcurrentBattleMapsTip".Translate());
            maxConcurrentBattleMaps = (int)ls.Slider(maxConcurrentBattleMaps, 0f, 5f);

            ls.Label("FCSettingEfficiencyDamping".Translate() + ": " + efficiencyDamping.ToString("0.00"), -1f, "FCSettingEfficiencyDampingTooltip".Translate());
            efficiencyDamping = ls.Slider(efficiencyDamping, 0.0f, 1.0f);

            ls.Label("FCSettingMercHealRate".Translate() + ": " + mercenaryHealRatePerHour.ToString("0.0") + " HP/hr", -1f, "FCSettingMercHealRateTip".Translate());
            mercenaryHealRatePerHour = ls.Slider(mercenaryHealRatePerHour, 0.1f, 100f);

            ls.Gap(12f);
            ls.GapLine();
            Text.Font = GameFont.Medium;
            ls.Label("FCSettingSquadsHeader".Translate());
            Text.Font = GameFont.Small;

            ls.Label("FCSettingMaxSquadSize".Translate() + ": " + maxSquadSize.ToString(), -1f, "FCSettingMaxSquadSizeTip".Translate());
            maxSquadSize = (int)ls.Slider(maxSquadSize, 1f, 60f);

            ls.Label("FCSettingSquadHireCostMultiplier".Translate() + ": " + squadHireCostMultiplier.ToString("0.00") + "x", -1f, "FCSettingSquadHireCostMultiplierTip".Translate());
            squadHireCostMultiplier = ls.Slider(squadHireCostMultiplier, 0.0f, 5.0f);

            ls.Label("FCSettingSquadDismissalRefundFraction".Translate() + ": " + (squadDismissalRefundFraction * 100f).ToString("0") + "%", -1f, "FCSettingSquadDismissalRefundFractionTip".Translate());
            squadDismissalRefundFraction = ls.Slider(squadDismissalRefundFraction, 0.0f, 1.0f);

            ls.Label("FCSettingSquadUpgradeCostMultiplier".Translate() + ": " + squadUpgradeCostMultiplier.ToString("0.00") + "x", -1f, "FCSettingSquadUpgradeCostMultiplierTip".Translate());
            squadUpgradeCostMultiplier = ls.Slider(squadUpgradeCostMultiplier, 0.0f, 5.0f);

            ls.Label("FCSettingSquadDeploymentCostPercentage".Translate() + ": " + (squadDeploymentCostPercentage * 100f).ToString("0") + "%", -1f, "FCSettingSquadDeploymentCostPercentageTip".Translate());
            squadDeploymentCostPercentage = ls.Slider(squadDeploymentCostPercentage, 0.0f, 1.0f);

            ls.Label("FCSettingDeploymentBillLifespan".Translate() + ": " + deploymentBillLifespan_days.ToString() + " d", -1f, "FCSettingDeploymentBillLifespanTip".Translate());
            deploymentBillLifespan_days = (int)ls.Slider(deploymentBillLifespan_days, 1f, 60f);

            ls.End();

            ScrollUtil.EndScrollView();
        }

        private void DoRoadBuilderTab(Rect rect)
        {
            Rect viewRect = ScrollUtil.BeginScrollView(rect, ref scrollVectorRoadBuilder, float.MaxValue);
            Rect listRect = new Rect(viewRect.x, viewRect.y, viewRect.width, float.MaxValue);
            Listing_Standard ls = new Listing_Standard();
            ls.Begin(listRect);

            // Description box
            Rect descRect = ls.GetRect(Text.CalcHeight("FCSettingRoadBuilderDesc".Translate(), listRect.width - 16f) + 16f);
            Widgets.DrawBoxSolid(descRect, new Color(0.15f, 0.15f, 0.15f, 0.5f));
            Widgets.DrawBox(descRect);
            Text.Font = GameFont.Small;
            Widgets.Label(descRect.ContractedBy(8f), "FCSettingRoadBuilderDesc".Translate());

            ls.Gap(12f);

            ls.CheckboxLabeled("FCSettingUseThreadedRoadComputation".Translate(), ref useThreadedRoadComputation, "FCSettingUseThreadedRoadComputationDesc".Translate());
            if (!useThreadedRoadComputation)
            {
                string edgesLabel = edgesPerRoadTick <= 0
                    ? $"{"FCSettingEdgesPerRoadTick".Translate()}: {"Unlimited".Translate()}"
                    : $"{"FCSettingEdgesPerRoadTick".Translate()}: {edgesPerRoadTick}";
                edgesPerRoadTick = (int)ls.SliderLabeled(edgesLabel, edgesPerRoadTick, 0, 50);
            }

            ls.Gap(12f);
            FCRoadQueue queue = FactionCache.FactionComp?.roadBuilder?.roadQueue;
            if (queue is object && ls.ButtonText("FCSettingFlushRoadCache".Translate()))
            {
                queue.FlushCache();
            }

            ls.End();

            ScrollUtil.EndScrollView();
        }
    }


    public class FactionColoniesMod : Mod
    {
        public FCSettings settings = new FCSettings();

        public FactionColoniesMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<FCSettings>();
            
            string modVersion = content?.ModMetaData?.ModVersion;
            if (modVersion.NullOrEmpty())
            {
                LogUtil.MessageForce("Did not load a mod version");
            }
            else
            {
                LogUtil.MessageForce($"v{modVersion}");
            }
        }

        public override string SettingsCategory()
        {
            return "FCSettingsModName".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect) => settings.DoWindowContents(inRect);
    }
}
