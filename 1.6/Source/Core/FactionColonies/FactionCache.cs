using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Static cache to hold on to frequently-accessed fields that change infrequently, or never.
    ///
    /// <para>This cache needs to be invalidated any time the game loads or changes. Presently, this is done through a Harmony Postfix on Game.Dispose().</para>
    /// <para>NOTE: DefDatabase[PawnKindDef].AllDefsListForReading is cached here, filtered through <see cref="PawnKindDefExtensions.ValidPawnKindDef"/> so malformed defs from other mods never reach downstream consumers. Nevertheless, def caching means that def hotloading is a no-no.</para>
    /// </summary>
    public static class FactionCache
    {
        private static Faction _cachedColonyFaction = null;
        private static Faction _cachedPlayerFaction = null;
        private static FactionFC _cachedFactionWorldComp = null;
        private static MilitaryOperationManager _cachedMilitaryManager = null;
        private static WorldComponent_EnemyPower _cachedEnemyPower = null;
        private static FactionDef _cachedFactionDef = null;
        private static List<PawnKindDef> _cachedPawnKindDefs = null;
        private static Dictionary<(Type, string), FieldInfo> _cachedFields = new Dictionary<(Type, string), FieldInfo>();
        private static List<XenotypeDef> _cachedXenotypeList = null;
        private static List<XenotypeDef> _cachedViolentXenotypeList = null;
        private static List<CustomXenotype> _cachedCustomXenotypeList = null;
        private static List<CustomXenotype> _cachedViolentCustomXenotypeList = null;
        private static Dictionary<string, CustomXenotype> _cachedCustomXenotypeDecoder = null;
        private static List<ThingDef> _cachedRaceList = null;
        private static List<PawnKindDef> _cachedAnimalKinds = null;
        private static List<PawnKindDef> _cachedCombatAnimalKinds = null;
        private static List<PawnKindDef> _cachedPackAnimalKinds = null;
        private static bool _checkedForNonViolentXenos = false;
        private static bool _cachedNonViolentXenosExist = false;
        private static Dictionary<XenotypeDef, bool> _cachedXenotypeViolenceDict = null;
        private static Dictionary<string, bool> _cachedCustomXenotypeViolenceDict = null;
        private static List<FCPolicyDef> _cachedFCPolicyDefs = null;
        private static Dictionary<FCPolicyDef, string> _cachedFCPolicyDescs = null;
        private static Dictionary<BuildingFCDef, List<BuildingUpgradeEntry>> _cachedUpgradeTrees = null;
        private static Dictionary<BuildingFCDef, HashSet<BuildingFCDef>> _cachedUpgradeDescendants = null;
        private static Dictionary<BuildingFCDef, List<BuildingFCDef>> _cachedRequiredByMap = null;
        private static List<FCEventCategoryDef> _cachedEventCategoryDefs = null;
        private static List<MilitaryJobDef> _cachedHostileMilitaryJobs = null;
        private static Dictionary<TechLevel, TechLevelBarrier> _cachedTechBarriers = null;
        private static ResearchProjectDef _cachedTransportPods = null;

        public static FactionFC FactionComp => _cachedFactionWorldComp ?? (_cachedFactionWorldComp = Find.World?.GetComponent<FactionFC>());

        /// <summary>
        /// Shortcut to <see cref="FactionFC.militaryOperationManager"/>. Returns null if the
        /// faction component itself is not yet loaded.
        /// </summary>
        public static MilitaryOperationManager MilitaryManager
            => _cachedMilitaryManager ?? (_cachedMilitaryManager = FactionComp?.militaryOperationManager);
        /// <summary>
        /// Per-world cache of enemy-faction and enemy-settlement power baselines, plus the
        /// only sanctioned site for invoking <see cref="BattleModifierRegistry"/>. Used by
        /// the squad-attack window (range display), battle engagement (variance roll +
        /// modifier pass), and AI-attack force generation.
        /// </summary>
        public static WorldComponent_EnemyPower EnemyPower =>
            _cachedEnemyPower ??
            (_cachedEnemyPower = Find.World?.GetComponent<WorldComponent_EnemyPower>());
        /// <summary>
        /// The NPC Empire faction that the player created and controls.
        /// </summary>
        public static Faction PlayerColonyFaction => _cachedColonyFaction ??
                                                     (_cachedColonyFaction = Find.FactionManager?.FirstFactionOfDef(EmpireFactionDef));
        public static bool IsPlayerColonyFaction(Faction f) => !(PlayerColonyFaction is null) && f == PlayerColonyFaction;
        /// <summary>
        /// The player faction itself.
        /// </summary>
        public static Faction PlayerFaction => _cachedPlayerFaction ??
                                               (_cachedPlayerFaction = Find.FactionManager?.AllFactions?.FirstOrDefault(faction => faction.IsPlayer));
        public static List<PawnKindDef> AllPawnKindDefs
        {
            get
            {
                if (_cachedPawnKindDefs is null || _cachedPawnKindDefs.Count == 0)
                {
                    List<PawnKindDef> all = DefDatabase<PawnKindDef>.AllDefsListForReading;
                    _cachedPawnKindDefs = new List<PawnKindDef>(all.Count);
                    int skipped = 0;
                    foreach (PawnKindDef def in all)
                    {
                        if (def.ValidPawnKindDef()) _cachedPawnKindDefs.Add(def);
                        else skipped++;
                    }
                    if (skipped > 0)
                        LogUtil.Warning($"FactionCache.AllPawnKindDefs: skipped {skipped} malformed PawnKindDef(s). See preceding warnings for offending defs and source mods.");
                }
                return _cachedPawnKindDefs;
            }
        }
        public static Dictionary<(Type, string), FieldInfo> FieldCache => _cachedFields;
        public static FieldInfo GetFieldCacheValue(Type typ, string field)
        {
            if (FieldCache.TryGetValue((typ, field), out FieldInfo fieldInfo))
            {
                return fieldInfo;
            }
            fieldInfo = typ.GetField(field);
            if (fieldInfo == null)
            {
                LogUtil.Warning($"FactionCache.GetFieldCacheValue: field '{field}' not found on type '{typ.FullName}'");
            }
            FieldCache.Add((typ, field), fieldInfo);
            return fieldInfo;
        }
        public static FactionDef EmpireFactionDef => _cachedFactionDef ?? (_cachedFactionDef = DefDatabase<FactionDef>.GetNamed("PColony"));
        public static List<XenotypeDef> XenotypeDefs => _cachedXenotypeList ?? (_cachedXenotypeList = DefDatabase<XenotypeDef>.AllDefsListForReading);
        public static List<CustomXenotype> CustomXenotypes
        {
            get
            {
                if (_cachedCustomXenotypeList != null)
                    return _cachedCustomXenotypeList;

                List<CustomXenotype> list = BuildMergedCustomXenotypeList();

                // Only cache when the Scribe is inactive. During loading, the disk read is
                // skipped (see BuildMergedCustomXenotypeList), so the list is incomplete.
                // Returning without caching ensures the next post-load access rebuilds fully.
                if (Scribe.mode == LoadSaveMode.Inactive)
                    _cachedCustomXenotypeList = list;

                return list;
            }
        }

        private static List<CustomXenotype> BuildMergedCustomXenotypeList()
        {
            var perSave = Current.Game?.customXenotypeDatabase?.customXenotypes;
            var merged = new List<CustomXenotype>();
            var seenNames = new HashSet<string>();

            // Per-save entries first (preferred)
            if (perSave != null)
            {
                foreach (CustomXenotype x in perSave)
                {
                    if (x?.name != null && seenNames.Add(x.name))
                        merged.Add(x);
                }
            }

            // Global disk entries (fill in anything not already in per-save).
            // Skip during active Scribe loading. CharacterCardUtility.CustomXenotypesForReading
            // reads files via InitLoadingMetaHeaderOnly, which calls Scribe.ForceStop() when the
            // Scribe is already active, destroying the entire save-load pipeline.
            if (Scribe.mode == LoadSaveMode.Inactive)
            {
                try
                {
                    List<CustomXenotype> disk = CharacterCardUtility.CustomXenotypesForReading;
                    if (disk != null)
                    {
                        foreach (CustomXenotype x in disk)
                        {
                            if (x?.name != null && seenNames.Add(x.name))
                                merged.Add(x);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.Warning($"Failed to load disk custom xenotypes: {ex.Message}");
                }
            }
            else
            {
                LogUtil.Warning($"BuildMergedCustomXenotypeList called while Scribe mode is not Inactive. Skipping CustomXenotypesForReading");
            }

            return merged;
        }

        /// <summary>
        /// Adds a CustomXenotype to the per-save database if not already present.
        /// This ensures disk-only xenotypes are persisted in the save file when used.
        /// </summary>
        public static void EnsureInGameDatabase(CustomXenotype xenotype)
        {
            if (xenotype is null) return;
            List<CustomXenotype> db = Current.Game?.customXenotypeDatabase?.customXenotypes;
            if (db is null) return;

            foreach (CustomXenotype existing in db)
            {
                if (existing.name == xenotype.name)
                    return;
            }

            db.Add(xenotype);
            InvalidateCustomXenotypeCache();
        }
        public static Dictionary<string, CustomXenotype> CustomXenotypesDecoder
        {
            get
            {
                if (_cachedCustomXenotypeDecoder is null)
                {
                    Dictionary<string, CustomXenotype> decoder = new Dictionary<string, CustomXenotype>();
                    List<CustomXenotype> xenos = CustomXenotypes;
                    if (xenos != null)
                    {
                        foreach (CustomXenotype xenotype in xenos)
                        {
                            decoder[xenotype.name] = xenotype;
                        }
                    }
                    // Only cache when Scribe is inactive (matches CustomXenotypes behavior).
                    // During loading, disk xenotypes are unavailable so the decoder is incomplete.
                    if (Scribe.mode == LoadSaveMode.Inactive)
                        _cachedCustomXenotypeDecoder = decoder;
                    return decoder;
                }
                return _cachedCustomXenotypeDecoder;
            }
        }
        public static CustomXenotype GetCustomXenotype(string name)
        {
            CustomXenotype xenotype = null;
            if (!CustomXenotypesDecoder.TryGetValue(name, out xenotype))
            {
                LogUtil.Warning($"Custom xenotype {name} does not appear in the xenotype decoder dictionary");
            }
            return xenotype;
        }
        public static List<ThingDef> HumanlikeRaces
        {
            get
            {
                if (_cachedRaceList == null)
                {
                    _cachedRaceList = new List<ThingDef>();
                    foreach (PawnKindDef pawnKind in AllPawnKindDefs)
                    {
                        if (pawnKind.race != null && !_cachedRaceList.Any(r => r.defName == pawnKind.race.defName) && (pawnKind.race == ThingDefOf.Human || pawnKind.IsHumanLikeRace()))
                        {
                            _cachedRaceList.Add(pawnKind.race);
                        }
                    }
                }
                return _cachedRaceList;
            }
        }
        // Technically there should *always* be at least one race: ThingDefOf.Human. But it probably can't hurt to null-check, just in case of edge cases...
        public static int HumanlikeRacesCount => HumanlikeRaces?.Count ?? 0;
        public static List<PawnKindDef> AllAnimalKindDefs => _cachedAnimalKinds ??
                                                             (_cachedAnimalKinds = AllPawnKindDefs.Where(kind => kind.IsAnimalAndAllowed()).ToList());
        public static List<PawnKindDef> AllCombatAnimalKindDefs => _cachedCombatAnimalKinds ??
                                                                   (_cachedCombatAnimalKinds = AllAnimalKindDefs.Where(kind => kind.IsCombatAnimal()).ToList());
        public static List<PawnKindDef> AllPackAnimalKinds => _cachedPackAnimalKinds ??
                                                              (_cachedPackAnimalKinds = AllAnimalKindDefs.Where(kind => kind.IsPackAnimal()).ToList());
        public static bool NonViolentXenotypesExist
        {
            get
            {
                if (!_checkedForNonViolentXenos)
                {
                    if (XenotypeDefs?.Count > 0)
                    {
                        foreach (XenotypeDef xenotype in XenotypeDefs)
                        {
                            if (XenotypeFilter.IsXenotypeNonViolent(xenotype))
                            {
                                _cachedNonViolentXenosExist = true;
                                break;
                            }
                        }
                    }
                    if (!_cachedNonViolentXenosExist && CustomXenotypes?.Count > 0)
                    {
                        foreach (CustomXenotype xenotype in CustomXenotypes)
                        {
                            if (XenotypeFilter.IsCustomXenotypeNonViolent(xenotype.name))
                            {
                                _cachedNonViolentXenosExist = true;
                                break;
                            }
                        }
                    }
                    _checkedForNonViolentXenos = true;
                }
                return _cachedNonViolentXenosExist;
            }
        }
        public static Dictionary<XenotypeDef, bool> XenotypeViolence
        {
            get
            {
                if (_cachedXenotypeViolenceDict is null && XenotypeDefs?.Count > 0)
                {
                    _cachedXenotypeViolenceDict = new Dictionary<XenotypeDef, bool>();
                    foreach (XenotypeDef xenotype in XenotypeDefs)
                    {
                        _cachedXenotypeViolenceDict.Add(xenotype, !XenotypeFilter.IsXenotypeNonViolent(xenotype));
                    }
                }
                return _cachedXenotypeViolenceDict;
            }
        }
        public static Dictionary<string, bool> CustomXenotypeViolence
        {
            get
            {
                if (_cachedCustomXenotypeViolenceDict is null && CustomXenotypes?.Count > 0)
                {
                    _cachedCustomXenotypeViolenceDict = new Dictionary<string, bool>();
                    foreach (CustomXenotype xenotype in CustomXenotypes)
                    {
                        _cachedCustomXenotypeViolenceDict.Add(xenotype.name, !XenotypeFilter.IsCustomXenotypeNonViolent(xenotype.name));
                    }
                }
                return _cachedCustomXenotypeViolenceDict;
            }
        }
        public static bool XenotypeIsNonViolent(XenotypeDef xenotype)
        {
            if (XenotypeViolence?.TryGetValue(xenotype, out bool violent) == true)
            {
                return !violent;
            }
            return false;
        }
        public static bool CustomXenotypeIsNonViolent(string xenotypeName)
        {
            if (CustomXenotypeViolence?.TryGetValue(xenotypeName, out bool violent) == true)
            {
                return !violent;
            }
            return false;
        }
        public static bool CustomXenotypeIsNonViolent(CustomXenotype xenotype)
        {
            return CustomXenotypeIsNonViolent(xenotype.name);
        }
        public static List<FCPolicyDef> AllFCPolicies => _cachedFCPolicyDefs ?? (_cachedFCPolicyDefs = DefDatabase<FCPolicyDef>.AllDefsListForReading);
        public static Dictionary<FCPolicyDef, string> FCPolicyDescs
        {
            get
            {
                if (_cachedFCPolicyDescs is null)
                {
                    _cachedFCPolicyDescs = new Dictionary<FCPolicyDef, string>();
                    foreach (FCPolicyDef policy in AllFCPolicies)
                    {
                        _cachedFCPolicyDescs.Add(policy, policy.PolicyDesc());
                    }
                }
                return _cachedFCPolicyDescs;
            }
        }
        public static List<XenotypeDef> ViolentXenotypeDefs => _cachedViolentXenotypeList ??
                                                               (_cachedViolentXenotypeList = XenotypeDefs.Where(x => !XenotypeIsNonViolent(x)).ToList());
        public static List<CustomXenotype> ViolentCustomXenotypes => _cachedViolentCustomXenotypeList ??
                                                                     (_cachedViolentCustomXenotypeList = CustomXenotypes.Where(x => !CustomXenotypeIsNonViolent(x)).ToList());

        /* Tech caching */
        public static Dictionary<TechLevel, TechLevelBarrier> TechBarriers
        {
            get
            {
                if (_cachedTechBarriers is null)
                {
                    _cachedTechBarriers = new Dictionary<TechLevel, TechLevelBarrier>();
                    foreach (TechProgressionDef def in DefDatabase<TechProgressionDef>.AllDefsListForReading)
                    {
                        if (def.barriers is null) continue;
                        foreach (TechLevelBarrier b in def.barriers)
                        {
                            // Last-writer-wins if multiple Defs declare the same level, so override Defs take priority.
                            _cachedTechBarriers[b.techLevel] = b;
                        }
                    }
                }
                return _cachedTechBarriers;
            }
        }

        public static TechLevelBarrier GetTechBarrier(TechLevel level)
        {
            return TechBarriers.TryGetValue(level, out TechLevelBarrier b) ? b : null;
        }

        public static ResearchProjectDef TechTransportPods => _cachedTransportPods ??
                                                              (_cachedTransportPods = DefDatabase<ResearchProjectDef>.GetNamed("TransportPod", false));

        /// <summary>
        /// For each building, a flattened list of all upgrades reachable through the upgrade tree.
        /// </summary>
        public static Dictionary<BuildingFCDef, List<BuildingUpgradeEntry>> UpgradeTrees
        {
            get
            {
                if (_cachedUpgradeTrees is null)
                {
                    _cachedUpgradeTrees = new Dictionary<BuildingFCDef, List<BuildingUpgradeEntry>>();
                    foreach (BuildingFCDef building in DefDatabase<BuildingFCDef>.AllDefsListForReading)
                    {
                        if (building.upgrades is null || building.upgrades.Count == 0) continue;
                        List<BuildingUpgradeEntry> tree = new List<BuildingUpgradeEntry>();
                        CollectUpgradeTree(building, 0, null, tree);
                        _cachedUpgradeTrees[building] = tree;
                    }
                }
                return _cachedUpgradeTrees;
            }
        }

        private static void CollectUpgradeTree(BuildingFCDef building, int depth, BuildingFCDef parent, List<BuildingUpgradeEntry> result)
        {
            if (building.upgrades is null) return;
            foreach (BuildingFCDef upgrade in building.upgrades)
            {
                result.Add(new BuildingUpgradeEntry { def = upgrade, depth = depth, parent = parent ?? building });
                CollectUpgradeTree(upgrade, depth + 1, upgrade, result);
            }
        }

        /// <summary>
        /// For each building that has upgrades, the set of all transitive upgrade descendants.
        /// Used for O(1) "does this building satisfy a requirement for that building?" checks.
        /// </summary>
        public static Dictionary<BuildingFCDef, HashSet<BuildingFCDef>> UpgradeDescendants
        {
            get
            {
                if (_cachedUpgradeDescendants is null)
                {
                    _cachedUpgradeDescendants = new Dictionary<BuildingFCDef, HashSet<BuildingFCDef>>();
                    foreach (var kvp in UpgradeTrees)
                    {
                        HashSet<BuildingFCDef> set = new HashSet<BuildingFCDef>();
                        foreach (BuildingUpgradeEntry entry in kvp.Value)
                        {
                            set.Add(entry.def);
                        }
                        _cachedUpgradeDescendants[kvp.Key] = set;
                    }
                }
                return _cachedUpgradeDescendants;
            }
        }

        /// <summary>
        /// Returns true if <paramref name="candidate"/> is the same as <paramref name="required"/>,
        /// or is a transitive upgrade of it.
        /// </summary>
        public static bool SatisfiesRequirementFor(BuildingFCDef candidate, BuildingFCDef required)
        {
            if (candidate == required) return true;
            if (UpgradeDescendants.TryGetValue(required, out HashSet<BuildingFCDef> descendants))
                return descendants.Contains(candidate);
            return false;
        }

        /// <summary>
        /// Returns true if <paramref name="candidate"/> satisfies any entry in the given requirements list
        /// (i.e. equals or is an upgrade of any required building).
        /// </summary>
        public static bool SatisfiesAnyRequirement(BuildingFCDef candidate, List<BuildingFCDef> requirements)
        {
            if (requirements is null || requirements.Count == 0) return false;
            foreach (BuildingFCDef req in requirements)
            {
                if (SatisfiesRequirementFor(candidate, req)) return true;
            }
            return false;
        }

        /// <summary>
        /// Reverse lookup: for each building, all buildings that list it in their requiredBuildings.
        /// </summary>
        public static Dictionary<BuildingFCDef, List<BuildingFCDef>> RequiredByBuildingMap
        {
            get
            {
                if (_cachedRequiredByMap is null)
                {
                    _cachedRequiredByMap = new Dictionary<BuildingFCDef, List<BuildingFCDef>>();
                    foreach (BuildingFCDef building in DefDatabase<BuildingFCDef>.AllDefsListForReading)
                    {
                        if (building.requiredBuildings == null) continue;
                        foreach (BuildingFCDef req in building.requiredBuildings)
                        {
                            if (!_cachedRequiredByMap.TryGetValue(req, out List<BuildingFCDef> list))
                            {
                                list = new List<BuildingFCDef>();
                                _cachedRequiredByMap[req] = list;
                            }
                            list.Add(building);
                        }
                    }
                    // Propagate: if X requires Beta, then Beta_V2 (upgrade of Beta) also
                    // effectively satisfies that requirement — so show X in Beta_V2's
                    // "Required By" list as well.
                    foreach (var kvp in UpgradeDescendants)
                    {
                        if (!_cachedRequiredByMap.TryGetValue(kvp.Key, out List<BuildingFCDef> baseRequiredBy)) continue;
                        foreach (BuildingFCDef descendant in kvp.Value)
                        {
                            if (!_cachedRequiredByMap.TryGetValue(descendant, out List<BuildingFCDef> descList))
                            {
                                descList = new List<BuildingFCDef>();
                                _cachedRequiredByMap[descendant] = descList;
                            }
                            foreach (BuildingFCDef dep in baseRequiredBy)
                            {
                                if (!descList.Contains(dep)) descList.Add(dep);
                            }
                        }
                    }
                }
                return _cachedRequiredByMap;
            }
        }
        public static List<FCEventCategoryDef> FCEventCategoryDefs => _cachedEventCategoryDefs ??
                                    (_cachedEventCategoryDefs = DefDatabase<FCEventCategoryDef>.AllDefsListForReading);

        /// <summary>
        /// MilitaryJobDefs that have a floatMenuLabelKey, i.e. hostile operations shown in the world gizmo menu.
        /// </summary>
        public static List<MilitaryJobDef> HostileMilitaryJobs
        {
            get
            {
                if (_cachedHostileMilitaryJobs is null)
                {
                    _cachedHostileMilitaryJobs = new List<MilitaryJobDef>();
                    foreach (MilitaryJobDef job in DefDatabase<MilitaryJobDef>.AllDefsListForReading)
                    {
                        if (job.floatMenuLabelKey != null)
                            _cachedHostileMilitaryJobs.Add(job);
                    }
                }
                return _cachedHostileMilitaryJobs;
            }
        }

        public static void InvalidateCache()
        {
            LogUtil.Message("Invalidating FactionCache...");
            _cachedColonyFaction = null;
            _cachedPlayerFaction = null;
            _cachedPawnKindDefs = null;
            _cachedFactionWorldComp = null;
            _cachedMilitaryManager = null;
            _cachedEnemyPower = null;
            _cachedFactionDef = null;
            _cachedFields.Clear();
            _cachedRaceList = null;
            _cachedXenotypeList = null;
            _cachedViolentXenotypeList = null;
            _cachedAnimalKinds = null;
            _cachedCombatAnimalKinds = null;
            _cachedPackAnimalKinds = null;
            _cachedXenotypeViolenceDict = null;
            _cachedFCPolicyDefs = null;
            _cachedFCPolicyDescs = null;
            _cachedUpgradeTrees = null;
            _cachedUpgradeDescendants = null;
            _cachedRequiredByMap = null;
            BuildingFCDef.ClearCompatibleSettlementCache();
            _cachedEventCategoryDefs = null;
            _cachedHostileMilitaryJobs = null;

            _cachedTechBarriers = null;
            _cachedTransportPods = null;

            InvalidateCustomXenotypeCache();
        }
        /* Custom xenotypes are actually expected to change while the game is loaded, and thus we may have to refresh that specific cache more frequently than the rest.
         * Hence, it gets its own function. */
        public static void InvalidateCustomXenotypeCache()
        {
            _cachedCustomXenotypeList = null;
            _cachedViolentCustomXenotypeList = null;
            _cachedCustomXenotypeDecoder = null;
            _cachedCustomXenotypeViolenceDict = null;

            _checkedForNonViolentXenos = false;
            _cachedNonViolentXenosExist = false;

            GeneValuationUtil.InvalidateCustomXenotypeCache();
        }
    }
}
