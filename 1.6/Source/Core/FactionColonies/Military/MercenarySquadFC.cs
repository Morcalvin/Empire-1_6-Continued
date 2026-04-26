using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace FactionColonies
{

    public class MercenarySquadFC : IExposable, ILoadReferenceable
    {
        public int loadID = -1;
        public string name;
        public List<Mercenary> mercenaries = new List<Mercenary>();
        public List<Mercenary> animals = new List<Mercenary>();
        public WorldSettlementFC settlement;
        public bool isDeployed;
        public bool isExtraSquad;
        public int timeDeployed;
        /// <summary>Current target tile for the squad's deployment lord. Initially set to the
        /// drop position by <c>MilitaryUtil.SpawnSquad</c>; updated when the player issues a
        /// "move here" command via <see cref="DeployedMilitaryCommandMenu"/>.</summary>
        public IntVec3 orderLocation;
        /// <summary>Player-issued behavior order for the squad's deployment lord.
        /// <see cref="MilitaryOrder.Undefined"/> until the player issues a command (Attack /
        /// Move / Leave). Read by <c>LordJob_DeployMilitary</c>'s state-graph triggers.</summary>
        public MilitaryOrder militaryOrder = MilitaryOrder.Undefined;
        public bool hitMap;
        public int dead;
        public MilSquadFC outfit;
        public List<ThingWithComps> UsedWeaponList;
        public List<Apparel> UsedApparelList;
        public int tickChanged;
        public bool hasLord;
        public Map map;
        public Lord lord;
        public XenotypeDef xenotype1;
        public List<Gene> GeneList;

        public virtual void ExposeData()
        {
            Scribe_Values.Look(ref loadID, "loadID", -1);
            Scribe_Values.Look(ref name, "name");
            Scribe_Collections.Look(ref mercenaries, "mercenaries", LookMode.Deep);
            Scribe_Collections.Look(ref animals, "animals", LookMode.Deep);
            Scribe_Values.Look(ref isDeployed, "isDeployed");
            Scribe_Values.Look(ref isExtraSquad, "isExtraSquad");
            Scribe_Values.Look(ref hitMap, "hitMap");
            Scribe_References.Look(ref outfit, "outfit");
            Scribe_Values.Look(ref dead, "dead");
            Scribe_Collections.Look(ref UsedWeaponList, "UsedWeaponList", LookMode.Reference);
            Scribe_Collections.Look(ref UsedApparelList, "UsedApparelList", LookMode.Reference);
            Scribe_References.Look(ref settlement, "Settlement");
            Scribe_Values.Look(ref tickChanged, "tickChanged");
            Scribe_Values.Look(ref timeDeployed, "timeDeployed", -1);
            Scribe_Values.Look(ref orderLocation, "orderLocation");
            Scribe_Values.Look(ref militaryOrder, "militaryOrder", MilitaryOrder.Undefined);
            Scribe_Values.Look(ref hasLord, "hasLord");
            Scribe_References.Look(ref map, "map");
            Scribe_References.Look(ref lord, "lord");
        }

        public string GetUniqueLoadID()
        {
            return $"MercenarySquadFC_{loadID}";
        }

        public IEnumerable<Mercenary> EquippedMercenaries =>
            mercenaries.Where(merc => merc?.pawn?.apparel != null
                                       && merc.pawn.equipment != null
                                       && (merc.pawn.apparel.WornApparel.Any()
                                           || merc.pawn.equipment.AllEquipmentListForReading.Any()
                                           || merc.animal != null)
                                       && merc.deployable);

        public IEnumerable<Pawn> EquippedMercenaryPawns =>
            EquippedMercenaries.Select(merc => merc.pawn);

        public IEnumerable<Pawn> EquippedAnimalMercenaries =>
            animals.Where(animal => animal?.pawn != null).Select(animal => animal.pawn);

        public IEnumerable<Pawn> AllEquippedMercenaryPawns =>
            EquippedMercenaries.Select(merc => merc.pawn).Concat(EquippedAnimalMercenaries);

        public IEnumerable<Pawn> AllDeployedMercenaryPawns =>
            DeployedMercenaries.Select(merc => merc.pawn)
                .Concat(DeployedMercenaryAnimals.Select(merc => merc.pawn));

        public IEnumerable<Mercenary> DeployedMercenaries =>
            mercenaries.Where(merc => merc?.pawn?.Map != null);

        public IEnumerable<Mercenary> DeployedMercenaryAnimals =>
            animals.Where(merc => merc?.pawn?.Map != null);

        /// <summary>True if any mercenary pawn is currently spawned on a map.</summary>
        public bool IsPhysicallyDeployed() => mercenaries.Any(m => m?.pawn?.Map != null);

        /// <summary>The <see cref="MilitaryOperation"/> this squad is currently part of, if any.
        /// Returned via the <see cref="MilitaryOperationManager"/>'s squad index, so this is O(1)
        /// and reflects the canonical op state.</summary>
        public MilitaryOperation Operation => FactionCache.MilitaryManager?.GetOpForSquad(this);

        /// <summary>True when this squad is in any active op (offensive, defensive, or cooldown).
        /// Falls back to the legacy <see cref="isDeployed"/> flag during the Phase 2 transition
        /// window when not all entry points have been migrated yet.</summary>
        public bool IsBusy => Operation is object || isDeployed;

        public WorldSettlementFC getSettlement
        {
            get
            {
                if (settlement != null)
                {
                    return settlement;
                }

                FactionFC comp = FactionCache.FactionComp;
                if (comp is null) return null;

                foreach (WorldSettlementFC s in comp.settlements)
                {
                    if (s.MilitaryComp?.militarySquad != null && s.MilitaryComp?.militarySquad == this)
                    {
                        this.settlement = s;
                        return s;
                    }
                }

                return null;
            }
        }

        public void ChangeTick()
        {
            tickChanged = Find.TickManager.TicksGame;
        }

        public void InitiateSquad()
        {
            mercenaries = new List<Mercenary>();
            UsedApparelList = new List<Apparel>();
            UsedWeaponList = new List<ThingWithComps>();

            if (outfit == null)
            {
                for (int k = 0; k < MilSquadFC.MaxSquadSize; k++)
                {
                    Mercenary pawn = new Mercenary(true);
                    CreateNewPawn(ref pawn, null, null);
                    // Only add if pawn was successfully created
                    if (pawn?.pawn != null)
                    {
                        mercenaries.Add(pawn);
                    }
                    else
                    {
                        LogUtil.Warning($"Failed to create mercenary {k + 1}/30 during squad initiation.");
                    }
                }
            }
            else
            {
                for (int k = 0; k < MilSquadFC.MaxSquadSize; k++)
                {
                    Mercenary pawn = new Mercenary(true);
                    CreateNewPawn(ref pawn, outfit.Units[k].pawnKind, outfit.Units[k].xenotype, outfit.Units[k].customXenotypeName);
                    // Only add if pawn was successfully created
                    if (pawn?.pawn != null)
                    {
                        mercenaries.Add(pawn);
                    }
                    else
                    {
                        LogUtil.Warning($"Failed to create mercenary {k + 1}/30 for unit {outfit.Units[k]?.name ?? "unknown"}.");
                    }
                }
            }

            LogUtil.Message($"InitiateSquad mercenary count : {mercenaries.Count()}");
            if (loadID == -1)
            {
                loadID = FactionCache.FactionComp.GetNextMercenarySquadID();
            }

            if (outfit != null)
            {
                OutfitSquad(outfit);
            }
            else
            {
                FactionCache.FactionComp.militaryCustomizationUtil.RebuildMercenaryPawnSet();
            }
        }
        /// <summary>
        /// Checks if the squad is initialized, and initializes it if it isn't.
        /// </summary>
        public void CheckInitialization()
        {
            if (mercenaries == null || !mercenaries.Any(m => m?.pawn != null))
            {
                InitiateSquad();
            }
            else if (outfit != null && !EquippedMercenaries.Any())
            {
                OutfitSquad(outfit);
            }
        }

        public void ResetNeeds()
        {
            foreach (Pawn merc in AllEquippedMercenaryPawns)
            {
                if (merc.health == null)
                    merc.health = new Pawn_HealthTracker(merc);
                if (merc.needs == null)
                    merc.needs = new Pawn_NeedsTracker(merc);
                if (merc.needs.food == null)
                    merc.needs.food = new Need_Food(merc);
                if (merc.needs.rest == null)
                    merc.needs.rest = new Need_Rest(merc);
                if (!merc.AnimalOrWildMan() && merc.needs.joy == null)
                    merc.needs.joy = new Need_Joy(merc);
                merc.needs.food.CurLevel = merc.needs.food.MaxLevel;
                merc.needs.rest.CurLevel = merc.needs.rest.MaxLevel;
                if (!merc.AnimalOrWildMan())
                {
                    merc.needs.joy.CurLevel = merc.needs.joy.MaxLevel;
                    merc.needs.mood.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("FC_Mercenary"));
                }
            }
        }

        public void RemoveDroppedEquipment()
        {
            for (int i = UsedApparelList.Count - 1; i >= 0; i--)
            {
                Apparel apparel = UsedApparelList[i];
                if (apparel.ParentHolder is Pawn_ApparelTracker tracker)
                {
                    Pawn pawn = tracker.pawn;
                    if ((pawn.Faction == FactionCache.PlayerColonyFaction ||
                         pawn.Faction == Find.FactionManager.OfPlayer) && !pawn.Dead)
                        continue;
                }
                UsedApparelList.RemoveAt(i);
                if (apparel != null && !apparel.Destroyed)
                    apparel.Destroy();
            }

            for (int i = UsedWeaponList.Count - 1; i >= 0; i--)
            {
                ThingWithComps weapon = UsedWeaponList[i];
                if (weapon.ParentHolder is Pawn_EquipmentTracker tracker)
                {
                    Pawn pawn = tracker.pawn;
                    if ((pawn.Faction == FactionCache.PlayerColonyFaction ||
                         pawn.Faction == Find.FactionManager.OfPlayer) && !pawn.Dead)
                        continue;
                }
                UsedWeaponList.RemoveAt(i);
                if (weapon != null && !weapon.Destroyed)
                    weapon.Destroy();
            }
        }

        public void CreateNewAnimal(ref Mercenary merc, PawnKindDef race)
        {
            Pawn newPawn = PawnGenerator.GeneratePawn(FCPawnGenerator.AnimalRequest(race));

            merc.squad = this;
            merc.settlement = settlement;
            merc.pawn = newPawn;
        }

        /// <summary>
        /// If the mercenary's xenotype is non-violent and has security guards configured,
        /// auto-assigns a random guard animal from the xenotype's SecurityGuardList.
        /// </summary>
        private void TryAssignSecurityGuard(Mercenary merc)
        {
            if (merc?.pawn?.genes == null) return;
            // Don't overwrite a manually-assigned animal
            if (merc.animal != null) return;

            FactionFC factionFc = FactionCache.FactionComp;
            if (factionFc?.xenotypeFilter == null) return;

            XenotypeFilter xenoFilter = factionFc.xenotypeFilter;
            List<PawnKindDef> guardOptions = null;

            XenotypeDef mercXenotype = merc.pawn.genes.Xenotype;
            if (mercXenotype != null && FactionCache.XenotypeIsNonViolent(mercXenotype))
            {
                guardOptions = xenoFilter.GetSecurityGuardsForXenotype(mercXenotype);
            }
            else if (merc.pawn.genes.CustomXenotype != null)
            {
                string customName = merc.pawn.genes.CustomXenotype.name;
                if (FactionCache.CustomXenotypeIsNonViolent(customName))
                {
                    guardOptions = xenoFilter.GetSecurityGuardsForCustomXenotype(customName);
                }
            }

            if (guardOptions != null && guardOptions.Any())
            {
                PawnKindDef guardKind = guardOptions.RandomElement();
                Mercenary guardAnimal = new Mercenary(true);
                CreateNewAnimal(ref guardAnimal, guardKind);
                guardAnimal.handler = merc;
                merc.animal = guardAnimal;
                animals.Add(guardAnimal);
            }
        }

        public void CreateNewPawn(ref Mercenary merc, PawnKindDef race, XenotypeDef _xenotype, string _customXenotypeName = null)
        {
            XenotypeDef xenotypeChoice = _xenotype;
            PawnKindDef raceChoice = race;
            FactionFC factionFc = FactionCache.FactionComp;

            if (race == null || factionFc.xenotypeFilter.GetRaceWeight(raceChoice.race) <= 0)
            {
                raceChoice = PawnKindTemplateUtil.GetFighterForRace(ThingDefOf.Human);
            }

            // Try to generate pawn with the requested kind
            Pawn newPawn = null;
            try
            {
                PawnGenerationRequest request;
                if (_customXenotypeName != null)
                {
                    CustomXenotype custom = null;
                    FactionCache.CustomXenotypesDecoder?.TryGetValue(_customXenotypeName, out custom);

                    if (custom != null)
                        request = FCPawnGenerator.WorkerOrMilitaryRequest(raceChoice, custom);
                    else
                    {
                        LogUtil.Warning($"Custom xenotype '{_customXenotypeName}' not found, falling back to Baseliner");
                        request = FCPawnGenerator.WorkerOrMilitaryRequest(raceChoice, XenotypeDefOf.Baseliner);
                    }
                }
                else
                {
                    request = FCPawnGenerator.WorkerOrMilitaryRequest(raceChoice, xenotypeChoice);
                }
                newPawn = FCPawnGenerator.GenerateWithForcedXenotype(request);

                // Set faction after generation (since we generate without faction to avoid xenotype forcing)
                if (newPawn != null && newPawn.Faction == null)
                {
                    var empireFaction = FactionCache.PlayerColonyFaction;
                    if (empireFaction != null)
                    {
                        newPawn.SetFaction(empireFaction);
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.Warning($"Failed to generate pawn with kind {raceChoice?.defName}: {ex.Message}");
            }

            // Fallback 1: Try with Baseliner xenotype and NO faction (avoids faction xenotype forcing)
            if (newPawn == null)
            {
                LogUtil.Warning($"Pawn generation failed for {raceChoice?.defName}. Trying Baseliner fallback without faction.");
                try
                {
                    var simpleRequest = new PawnGenerationRequest(
                        kind: PawnKindDefOf.Colonist,
                        faction: null, // NO faction - this prevents faction xenotype forcing
                        context: PawnGenerationContext.NonPlayer,
                        tile: -1,
                        forceGenerateNewPawn: false,
                        allowDead: false,
                        allowDowned: false,
                        canGeneratePawnRelations: false, // No relations for factionless pawns
                        mustBeCapableOfViolence: true,
                        colonistRelationChanceFactor: 0,
                        forceAddFreeWarmLayerIfNeeded: false,
                        allowGay: true,
                        allowFood: true,
                        allowAddictions: false,
                        forcedXenotype: XenotypeDefOf.Baseliner // Force Baseliner - guaranteed violence capable
                    );
                    newPawn = PawnGenerator.GeneratePawn(simpleRequest);

                    // Set the faction after generation
                    if (newPawn != null)
                    {
                        var empireFaction = FactionCache.PlayerColonyFaction;
                        if (empireFaction != null)
                        {
                            newPawn.SetFaction(empireFaction);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.Warning($"Baseliner fallback also failed: {ex.Message}");
                }
            }

            // Fallback 2: Absolute minimal request - no faction, no xenotype, no violence requirement
            if (newPawn == null)
            {
                LogUtil.Warning("All standard generation failed. Trying minimal fallback.");
                try
                {
                    var fallbackRequest = new PawnGenerationRequest(
                        kind: PawnKindDefOf.Colonist,
                        faction: null, // NO faction
                        context: PawnGenerationContext.NonPlayer,
                        tile: -1,
                        forceGenerateNewPawn: false,
                        allowDead: false,
                        allowDowned: false,
                        canGeneratePawnRelations: false,
                        mustBeCapableOfViolence: false, // Allow non-violent as absolute last resort
                        colonistRelationChanceFactor: 0,
                        forceAddFreeWarmLayerIfNeeded: false,
                        allowGay: true,
                        allowFood: true,
                        allowAddictions: false
                    );
                    newPawn = PawnGenerator.GeneratePawn(fallbackRequest);

                    // Set the faction after generation
                    if (newPawn != null)
                    {
                        var empireFaction = FactionCache.PlayerColonyFaction;
                        if (empireFaction != null)
                        {
                            newPawn.SetFaction(empireFaction);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.Error($"Critical - all pawn generation attempts failed: {ex.Message}");
                }
            }

            // Final check - if still null, we cannot proceed
            if (newPawn == null)
            {
                LogUtil.Error("Critical error - could not generate any pawn for mercenary squad. Skipping this mercenary.");
                return;
            }

            if (newPawn.kindDef == null)
            {
                newPawn.kindDef = raceChoice ?? PawnKindDefOf.Colonist;
                LogUtil.Warning($"MercenarySquadFC.CreateNewPawn: detected null kindDef, setting to default");
            }

            newPawn.apparel?.DestroyAll();
            newPawn.equipment?.DestroyAllEquipment();
            merc.squad = this;
            merc.settlement = settlement;
            merc.pawn = newPawn;

        }
        public void UpdateSquadStats(int level)
        {
            foreach (Mercenary merc in mercenaries)
            {
                if (merc?.pawn?.skills == null) continue;

                var shooting = merc.pawn.skills.GetSkill(SkillDefOf.Shooting);
                var melee = merc.pawn.skills.GetSkill(SkillDefOf.Melee);
                var medicine = merc.pawn.skills.GetSkill(SkillDefOf.Medicine);

                if (shooting != null) shooting.Level = Math.Min(level * 2, 20);
                if (melee != null) melee.Level = Math.Min(level * 2, 20);
                if (medicine != null) medicine.Level = Math.Min(level * 1, 20);
            }
        }

        public void PassPawnToDeadMercenaries(Mercenary merc)
        {
            //If ever add past dead pawns, use this code
            /*MilitaryCustomizationUtil util = FactionCache.FactionComp.militaryCustomizationUtil;
            Mercenary pwn = new Mercenary(true);
            if (merc.animal != null)
            {
                Mercenary animal = new Mercenary(true);
                animal = merc.animal;
                util.deadPawns.Add(animal);
            }
            pwn = merc;*/

            //util.deadPawns.Add(pwn);
            Mercenary pawn2 = new Mercenary(true);
            PawnKindDef kindDef = merc?.pawn?.kindDef ?? PawnKindDefOf.Colonist;
            XenotypeDef xenotype = null;
            string customXenoName = null;

            // Recover xenotype — prefer loadout (most reliable), then pawn genes
            if (merc?.loadout != null)
            {
                xenotype = merc.loadout.xenotype;
                customXenoName = merc.loadout.customXenotypeName;
            }
            else if (merc?.pawn?.genes != null)
            {
                CustomXenotype customXeno = merc.pawn.genes.CustomXenotype;
                if (customXeno != null)
                    customXenoName = customXeno.name;
                else
                    xenotype = merc.pawn.genes.Xenotype;
            }

            if (xenotype == null && customXenoName == null)
                xenotype = XenotypeDefOf.Baseliner;

            CreateNewPawn(ref pawn2, kindDef, xenotype, customXenoName);

            // Only replace if new pawn was successfully created
            if (pawn2?.pawn != null)
            {
                mercenaries.Replace(merc, pawn2);
            }
            else
            {
                LogUtil.Warning("Failed to replace dead mercenary with new pawn.");
            }
            FactionCache.FactionComp.militaryCustomizationUtil.RebuildMercenaryPawnSet();
        }

        public void HealPawn(Mercenary merc)
        {
            if (merc?.pawn?.health != null)
            {
                merc.pawn.health.Reset();
            }
        }

        public void StripSquad()
        {
            for (int count = 0; count < mercenaries.Count && count < MilSquadFC.MaxSquadSize; count++)
            {
                if (mercenaries[count]?.pawn != null)
                {
                    StripPawn(mercenaries[count]);
                }
            }
        }

        public virtual void OutfitSquad(MilSquadFC outfit)
        {
            FactionFC faction = FactionCache.FactionComp;
            int count = 0;
            this.outfit = outfit;
            UsedWeaponList = new List<ThingWithComps>();
            UsedApparelList = new List<Apparel>();
            animals = new List<Mercenary>();
            GeneList = new List<Gene>();
            foreach (MilUnitFC loadout in outfit.Units)
            {
                try
                {
                    if (loadout == null)
                    {
                        count++;
                        continue;
                    }

                    // Ensure we have enough mercenaries in the list
                    while (mercenaries.Count <= count)
                    {
                        Mercenary newMerc = new Mercenary(true);
                        CreateNewPawn(ref newMerc, loadout?.pawnKind, loadout?.xenotype, loadout?.customXenotypeName);
                        if (newMerc?.pawn != null)
                        {
                            mercenaries.Add(newMerc);
                        }
                        else
                        {
                            LogUtil.Warning($"Could not create mercenary for slot {count}.");
                            break;
                        }
                    }

                    // Skip if we still don't have enough mercenaries
                    if (count >= mercenaries.Count || mercenaries[count]?.pawn == null)
                    {
                        LogUtil.Warning($"Skipping outfit slot {count} - no valid mercenary available.");
                        count++;
                        continue;
                    }

                    if (mercenaries[count].pawn.kindDef != loadout.pawnKind || mercenaries[count].pawn.Dead)
                    {
                        Mercenary pawn = new Mercenary(true);
                        CreateNewPawn(ref pawn, loadout.pawnKind, loadout.xenotype, loadout.customXenotypeName);
                        // Only replace if new pawn was successfully created
                        if (pawn?.pawn != null)
                        {
                            mercenaries.Replace(mercenaries[count], pawn);
                        }
                        else
                        {
                            LogUtil.Warning($"Failed to create replacement pawn for slot {count}.");
                        }
                    }

                    // Skip operations if pawn is null
                    if (mercenaries[count]?.pawn == null)
                    {
                        count++;
                        continue;
                    }

                    StripPawn(mercenaries[count]);
                    if (loadout != null)
                    {
                        //mercenaries[count];
                        //StripPawn(mercenaries[count]);
                        EquipPawn(mercenaries[count], loadout);
                        if (loadout.animal != null)
                        {
                            Mercenary animal = new Mercenary(true);
                            CreateNewAnimal(ref animal, loadout.animal);
                            animal.handler = mercenaries[count];
                            mercenaries[count].animal = animal;
                            animals.Add(animal);
                        }

                        mercenaries[count].loadout = loadout;
                        mercenaries[count].deployable = faction?.militaryCustomizationUtil != null
                            && mercenaries[count].loadout != faction.militaryCustomizationUtil.blankUnit;
                    }

                    if (mercenaries[count]?.pawn?.equipment?.AllEquipmentListForReading != null)
                    {
                        UsedWeaponList.AddRange(mercenaries[count].pawn.equipment.AllEquipmentListForReading);

                        //add single check at start of load and mark variable
                    }

                    if (mercenaries[count]?.pawn?.apparel?.WornApparel != null)
                    {
                        UsedApparelList.AddRange(mercenaries[count].pawn.apparel.WornApparel);
                    }

                }
                catch (Exception e)
                {
                    LogUtil.Error($"Something went wrong when outfitting a squad (slot {count}): {e}");
                    if (!mercenaries.NullOrEmpty())
                    {
                        LogUtil.Error($"Number of Mercs: {mercenaries.Count}, Any null pawn: {mercenaries.Any(m => m?.pawn == null)}");
                    }
                }
                count++;
            }

            FactionCache.FactionComp?.militaryCustomizationUtil?.RebuildMercenaryPawnSet();
        }


        public virtual void StripPawn(Mercenary merc)
        {
            if (merc?.pawn == null) return;

            try
            {
                merc.pawn.apparel?.DestroyAll();
                merc.pawn.equipment?.DestroyAllEquipment();
                merc.pawn.inventory?.innerContainer?.ClearAndDestroyContents();
            }
            catch (Exception e)
            {
                LogUtil.Error($"Error stripping pawn equipment (mod conflict likely): {e}");
            }
            CombatExtendedUtil.UpdateInventory(merc.pawn);
        }

        public virtual void EquipPawn(Mercenary merc, MilUnitFC loadout)
        {
            if (merc?.pawn == null || loadout == null) return;

            if (merc.pawn.apparel != null)
            {
                FactionFC factionComp = FactionCache.FactionComp;
                foreach (SavedThing apparelDef in loadout.apparel)
                {
                    Thing thing = apparelDef.CreateThing();
                    if (thing is Apparel ap)
                    {
                        Color resolved = factionComp?.ResolveApparelColor(apparelDef) ?? Color.white;
                        thing.SetColor(resolved, reportFailure: false);
                        merc.pawn.apparel.Wear(ap);
                    }
                }
            }

            if (merc.pawn.equipment != null)
            {
                foreach (SavedThing weaponDef in loadout.weapons)
                {
                    Thing weaponThing = weaponDef.CreateThing();
                    if (weaponThing is ThingWithComps twc)
                    {
                        merc.pawn.equipment.AddEquipment(twc);
                    }
                }

                if (CombatExtendedUtil.IsCELoaded && merc.pawn.equipment.Primary != null)
                {
                    if (loadout.preferredAmmo != null)
                        CombatExtendedUtil.EquipWeaponWithSpecificAmmo(merc.pawn, merc.pawn.equipment.Primary, loadout.preferredAmmo);
                    else
                        CombatExtendedUtil.EquipWeaponWithAmmo(merc.pawn, merc.pawn.equipment.Primary);
                }
            }
        }

        public void DebugMercenarySquad()
        {
            LogUtil.MessageForce("Debug Mercenary Squad");
            foreach (Mercenary merc in mercenaries)
            {
                LogUtil.MessageForce($"\t{merc.pawn} \t{merc.pawn.health.Dead.ToString()} \t{merc.pawn.apparel.WornApparelCount} \t{merc.pawn.equipment.AllEquipmentListForReading.Count()}");
            }
        }

        public Mercenary ReturnPawn(Pawn pawn)
        {
            foreach (Mercenary merc in mercenaries)
            {
                if (merc.pawn == pawn)
                {
                    return merc;
                }
            }

            return null;
        }

        /// <summary>
        /// Makes the squad go into cooldown. Only works on a settlement's main squad while deployed.
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public bool InitiateCooldownEvent()
        {
            if (isExtraSquad || !isDeployed) return false;
            settlement?.MilitaryComp?.CooldownMilitaryFinal();
            return true;
        }
    }
}