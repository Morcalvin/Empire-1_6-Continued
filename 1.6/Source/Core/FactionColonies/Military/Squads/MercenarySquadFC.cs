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
        private string name;
        public List<Mercenary> mercenaries = new List<Mercenary>();
        public List<Mercenary> animals = new List<Mercenary>();
        public WorldSettlementFC settlement;
        public bool isExtraSquad;
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
        public bool hasLord;
        public Map map;
        public Lord lord;

        /* -*-*-*-*- Squad-first refactor fields -*-*-*-*-
         * nextAvailableTick: per-squad cooldown expiry. Updated in MilitaryOperation.EnterCooldown.
         * hiredAtTick: tick at which the squad was hired (analytics + future age hooks).
         * autoDefend: per-squad opt-in to foreign-defender candidate selection. Replaces the
         * old per-settlement comp.autoDefend flag — auto-defend now lives on the squad. */
        public int nextAvailableTick;
        public int hiredAtTick;
        public bool autoDefend;

        /* Raw squad name, or null if unset. Use DisplayName for UI; only use Name when
           the caller explicitly needs the raw value (e.g., seeding a rename text box). */
        public string Name => name;

        /* User-facing name. Falls back to the source template's name when this squad's
           Name wasn't set, and finally to FCUnnamedSquad. Use this for any UI/message
           that identifies a specific deployed squad — never outfit.name directly, since
           outfit is a Scribe_References link and can resolve to null after load. */
        public string DisplayName => name ?? outfit?.name ?? "FCUnnamedSquad".Translate();

        public void SetName(string newName) => name = newName;

        public virtual void ExposeData()
        {
            Scribe_Values.Look(ref loadID, "loadID", -1);
            Scribe_Values.Look(ref name, "name");
            Scribe_Collections.Look(ref mercenaries, "mercenaries", LookMode.Deep);
            Scribe_Collections.Look(ref animals, "animals", LookMode.Deep);
            Scribe_Values.Look(ref isExtraSquad, "isExtraSquad");
            Scribe_Values.Look(ref hitMap, "hitMap");
            Scribe_References.Look(ref outfit, "outfit");
            Scribe_Values.Look(ref dead, "dead");
            Scribe_Collections.Look(ref UsedWeaponList, "UsedWeaponList", LookMode.Reference);
            Scribe_Collections.Look(ref UsedApparelList, "UsedApparelList", LookMode.Reference);
            Scribe_References.Look(ref settlement, "Settlement");
            Scribe_Values.Look(ref orderLocation, "orderLocation");
            Scribe_Values.Look(ref militaryOrder, "militaryOrder", MilitaryOrder.Undefined);
            Scribe_Values.Look(ref hasLord, "hasLord");
            Scribe_References.Look(ref map, "map");
            Scribe_References.Look(ref lord, "lord");
            Scribe_Values.Look(ref nextAvailableTick, "nextAvailableTick", 0);
            Scribe_Values.Look(ref hiredAtTick, "hiredAtTick", 0);
            Scribe_Values.Look(ref autoDefend, "autoDefend", false);
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

        /// <summary>Equipped mercs and animals that are eligible to be spawned into a battle map,
        /// excluding pawns that are currently downed.
        /// Used by Deploy, defense initial spawn, and defense reinforcement.</summary>
        public IEnumerable<Pawn> SpawnableMercenaryPawns =>
            AllEquippedMercenaryPawns.Where(p => p is object && !p.Downed);

        public IEnumerable<Pawn> AllDeployedMercenaryPawns =>
            DeployedMercenaries.Select(merc => merc.pawn)
                .Concat(DeployedMercenaryAnimals.Select(merc => merc.pawn));

        public IEnumerable<Mercenary> DeployedMercenaries =>
            mercenaries.Where(merc => merc?.pawn?.Map != null);

        public IEnumerable<Mercenary> DeployedMercenaryAnimals =>
            animals.Where(merc => merc?.pawn?.Map != null);

        /// <summary>True if any mercenary pawn is currently spawned on a map. Walks the
        /// merc list rather than reading <c>Operation.battlefieldRef</c> so it works for
        /// squads spawned outside an op (legacy paths, drop pods that haven't yet wired up
        /// their op).</summary>
        public bool IsPhysicallyDeployed() => mercenaries.Any(m => m?.pawn?.Map != null);

        /// <summary>The <see cref="MilitaryOperation"/> this squad is currently part of, if any.
        /// Returned via the <see cref="MilitaryOperationManager"/>'s squad index, so this is O(1)
        /// and reflects the canonical op state.</summary>
        public MilitaryOperation Operation => FactionCache.MilitaryManager?.GetOpForSquad(this);

        /// <summary>True when this squad is in any active op (offensive, defensive, deploy, or cooldown).</summary>
        public bool IsBusy => Operation is object;

        /// <summary>True when the squad's <see cref="SquadCostCalculator.DeploymentCost"/> exceeds
        /// its assigned settlement's max deploy cost. An underfunded squad stays assigned but
        /// can't take part in military operations. Distinct from <see cref="IsBusy"/>: this is a
        /// structural (cost) constraint, not a temporary deployment state.</summary>
        public bool IsUnderfunded
            => settlement is object
               && MilitaryCustomizationUtil.SquadExceedsSettlementBudget(this, settlement, out _, out _);

        /// <summary>True when this squad is currently assigned to a billet (settlement). False
        /// when the squad sits in the unassigned hire pool.</summary>
        public bool IsAssigned => settlement is object;

        /// <summary>True when the squad is assigned, not in any active op, not underfunded
        /// at its settlement, and past its post-op cooldown. Canonical "can launch a new
        /// op" gate.</summary>
        public bool IsAvailable
            => IsAssigned && !IsBusy && !IsUnderfunded && nextAvailableTick <= Find.TickManager.TicksGame;

        /// <summary>Syncs a merc's companion animal to <paramref name="target"/>'s animal:
        /// creates, replaces, or destroys the animal-merc as needed and keeps
        /// <see cref="animals"/> consistent (no orphaned entries). Shared by the per-pawn
        /// Upgrade path and the bulk <see cref="SquadUpgradeUtil.UpgradeToTemplate"/> — <c>EquipPawn</c> only
        /// touches apparel + weapons, so the animal has to be reconciled separately. A
        /// fresh-hire merc (<c>animal == null</c>) is handled too: it just creates the
        /// animal when the target has one.</summary>
        public void ReconcileAnimal(Mercenary merc, MilUnitFC target)
        {
            if (merc is null) return;
            PawnKindDef wanted = target?.animal;

            /* No animal wanted — drop any existing one. */
            if (wanted is null)
            {
                if (merc.animal != null)
                {
                    if (merc.animal.pawn != null && !merc.animal.pawn.Destroyed) merc.animal.pawn.Destroy();
                    animals?.Remove(merc.animal);
                    merc.animal = null;
                }
                return;
            }

            /* Correct animal already present — leave it. */
            if (merc.animal?.pawn?.kindDef == wanted) return;

            /* Wrong / missing animal — destroy the old one (if any), create the wanted one. */
            if (merc.animal != null)
            {
                if (merc.animal.pawn != null && !merc.animal.pawn.Destroyed) merc.animal.pawn.Destroy();
                animals?.Remove(merc.animal);
                merc.animal = null;
            }

            Mercenary animal = new Mercenary(true);
            MercenaryPawnFactory.CreateNewAnimal(this, ref animal, wanted);
            animal.handler = merc;
            merc.animal = animal;
            if (animals is null) animals = new List<Mercenary>();
            animals.Add(animal);
        }

        /* The squad's billet. settlement is the canonical source of truth in the squad-first
         * model; the property exists only as a stable accessor for external callers. */
        public WorldSettlementFC getSettlement => settlement;

        /// <summary>
        /// Initial population pass: creates 30 mercs (one per template slot, or blank if no
        /// outfit) and equips them via <see cref="OutfitSquad"/>. The canonical first call
        /// is from <see cref="MilitaryCustomizationUtil.HireSquad"/>, where a freshly-
        /// constructed squad has <c>mercenaries.Count == 0</c> so the early-return guard
        /// does not fire.
        /// <para>Strict-manual outfit policy: the guard exists for post-load reentry
        /// (<see cref="CheckInitialization"/>) so a squad with all-dead placeholder mercs
        /// is NOT silently regenerated. The player must explicitly Fill those slots.</para>
        /// </summary>
        public void InitiateSquad()
        {
            if (mercenaries != null && mercenaries.Count > 0) return;

            mercenaries = new List<Mercenary>();
            UsedApparelList = new List<Apparel>();
            UsedWeaponList = new List<ThingWithComps>();

            int cap = MilSquadFC.MaxSquadSize;
            int templateCount = (outfit?.Units != null) ? outfit.Units.Count : 0;
            int slotCount = (outfit == null) ? cap : Math.Min(cap, templateCount);

            for (int k = 0; k < slotCount; k++)
            {
                Mercenary placeholder = new Mercenary(true);
                MilUnitFC slot = (outfit != null && k < templateCount) ? outfit.Units[k] : null;

                // Generate a pawn only for slots that have a real (non-blank) unit assignment.
                // Blank slots stay as empty placeholders (pawn == null), refillable via
                // FillEmptySlots / Upgrade when the player assigns a real unit later.
                if (slot != null && !slot.isBlank)
                {
                    MercenaryPawnFactory.CreateNewPawn(this, ref placeholder, slot.pawnKind, slot.xenotype, slot.customXenotypeName);
                    if (placeholder.pawn == null)
                    {
                        LogUtil.Warning($"Failed to create mercenary {k + 1}/{slotCount} for unit {slot.name ?? "unknown"}; leaving slot empty.");
                    }
                }

                // Always add the placeholder so list.Count == slotCount and slot indices align with
                // outfit.Units indices. OutfitSquad / FillEmptySlots will fill empty placeholders
                // when there's a non-blank loadout to assign.
                mercenaries.Add(placeholder);
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
        /// Ensures the squad's mercenary list exists. Does NOT re-outfit, refill, or
        /// regenerate dead pawns — under strict-manual outfit policy, gear / pawn changes
        /// only happen on explicit player action (Hire / Fill / Upgrade / Edit). A list of
        /// all-empty placeholder slots is a valid state post-battle and stays that way until
        /// the player calls Fill.
        /// </summary>
        public void CheckInitialization()
        {
            if (mercenaries is null) InitiateSquad();
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

        /// <summary>Number of empty slots that <see cref="FillEmptySlots"/> would actually fill
        /// — pawn is null AND <see cref="Mercenary.BlueprintLoadout"/> is non-null and not blank.
        /// Pure placeholder slots (blank blueprint, kept to align indices with the template) are
        /// excluded so the UI count matches the action's effect.</summary>
        public int EmptySlotCount
        {
            get
            {
                int n = 0;
                if (mercenaries is null) return 0;
                foreach (Mercenary m in mercenaries)
                {
                    if (m is null || !m.IsEmptySlot) continue;
                    MilUnitFC blueprint = m.BlueprintLoadout;
                    if (blueprint is null || blueprint.isBlank) continue;
                    n++;
                }
                return n;
            }
        }

        /// <summary>Pays <see cref="SquadCostCalculator.FillEmptySlotsCost"/> silver and generates fresh
        /// pawns into every empty slot, equipping each from its resolved loadout. Slots with no
        /// loadout are skipped. Returns false (no payment) if the player can't afford the total.</summary>
        public bool FillEmptySlots()
        {
            int total = SquadCostCalculator.FillEmptySlotsCost(this);
            if (total > 0 && PaymentUtil.GetSilver() < total)
            {
                Messages.Message("FCSquadFillSlotsInsufficient".Translate(total),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (total > 0) PaymentUtil.PaySilver(total, PaymentUtil.Reason_SquadFillSlot, settlement);

            if (mercenaries != null)
            {
                if (UsedWeaponList == null) UsedWeaponList = new List<ThingWithComps>();
                if (UsedApparelList == null) UsedApparelList = new List<Apparel>();
                foreach (Mercenary m in mercenaries)
                {
                    if (m is null || !m.IsEmptySlot) continue;
                    MilUnitFC blueprint = m.BlueprintLoadout;
                    if (blueprint is null || blueprint.isBlank) continue;
                    Mercenary slot = m;
                    MercenaryPawnFactory.CreateNewPawn(this, ref slot, blueprint.pawnKind, blueprint.xenotype, blueprint.customXenotypeName);
                    if (slot.pawn != null) EquipPawn(slot, blueprint);
                    // Sync currentLoadout with what we just equipped — re-snap from the blueprint.
                    slot.currentLoadout = blueprint.Clone();
                    if (slot.pawn?.equipment?.AllEquipmentListForReading != null)
                        UsedWeaponList.AddRange(slot.pawn.equipment.AllEquipmentListForReading);
                    if (slot.pawn?.apparel?.WornApparel != null)
                        UsedApparelList.AddRange(slot.pawn.apparel.WornApparel);
                }
            }

            FactionCache.FactionComp?.militaryCustomizationUtil?.RebuildMercenaryPawnSet();
            LifecycleRegistry.InvokeOnSquadUpgraded(this);
            return true;
        }

        /// <summary>Dismisses a single mercenary: strips and destroys the pawn (and any animal
        /// handler), and clears the slot to an empty placeholder so the player can refill it
        /// later via <see cref="FillEmptySlots"/>. No silver is returned. The slot is preserved
        /// (not removed from the list) so its blueprint stays available. Returns false if the
        /// squad is busy or the slot is already empty.</summary>
        public bool DismissMercenary(Mercenary merc)
        {
            if (merc is null || merc.IsEmptySlot) return false;
            if (IsBusy)
            {
                Messages.Message("FCCannotDismissBusyMerc".Translate(merc.pawn?.LabelShortCap ?? "?"),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }

            /* Strip + destroy. Mirrors SquadUpgradeUtil.UpgradeToTemplate's fire-pass cleanup. */
            StripPawn(merc);
            if (merc.pawn != null && !merc.pawn.Destroyed) merc.pawn.Destroy();
            merc.pawn = null;
            if (merc.animal?.pawn != null && !merc.animal.pawn.Destroyed) merc.animal.pawn.Destroy();
            merc.animal = null;

            /* Reset transient/personalization state so the empty slot is a clean refill target.
               Keep `loadout` (pool reference) so Fill can reuse it. */
            merc.ownedLoadout = null;
            merc.currentLoadout = null;

            FactionCache.FactionComp?.militaryCustomizationUtil?.RebuildMercenaryPawnSet();
            Messages.Message("FCMercDismissed".Translate(), MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        /// <summary>Drops an empty slot from <see cref="mercenaries"/>, shrinking the squad's
        /// max slot count by one. Returns false if the slot is missing, filled, or the squad is busy.
        /// </summary>
        public bool RemoveEmptySlot(Mercenary merc)
        {
            if (merc is null) return false;
            if (merc.pawn is object) return false;
            if (mercenaries is null) return false;
            if (IsBusy) return false;
            if (!mercenaries.Remove(merc)) return false;

            FactionCache.FactionComp?.militaryCustomizationUtil?.RebuildMercenaryPawnSet();
            return true;
        }

        /// <summary>Vestigial. Auto-replacement was removed by the strict-manual outfit
        /// refactor — the player explicitly uses <see cref="FillEmptySlots"/> instead. This
        /// method is kept only so submods that previously called it (typically with
        /// <see cref="MercenaryDeathEvent.CancelReplacement"/> set) still link. Calling it
        /// is a no-op apart from a warning log.</summary>
        [System.Obsolete("Auto-replacement removed by the strict-manual outfit refactor; player must use FillEmptySlots.")]
        public void PassPawnToDeadMercenaries(Mercenary merc)
        {
            LogUtil.Warning("MercenarySquadFC.PassPawnToDeadMercenaries was called but is a no-op. " +
                            "Use FillEmptySlots to refill empty slots after a death.");
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

        /// <summary>Re-equips every mercenary slot from <paramref name="outfit"/>'s units,
        /// generating fresh pawns for empty / mismatched slots and stripping/re-applying gear
        /// on existing pawns. Called only from explicit player actions: <see cref="InitiateSquad"/>
        /// at hire, <see cref="SquadUpgradeUtil.UpgradeToTemplate"/>, <see cref="FillEmptySlots"/>, and the per-pawn
        /// editor in <c>Dialog_PawnLoadout</c>. Per the strict-manual outfit policy, no automatic
        /// path (death replacement, template propagation, pre-deploy refresh) re-enters this method.
        /// External submods may call it during their own player-driven flows.</summary>
        public virtual void OutfitSquad(MilSquadFC outfit)
        {
            int count = 0;
            this.outfit = outfit;
            UsedWeaponList = new List<ThingWithComps>();
            UsedApparelList = new List<Apparel>();
            animals = new List<Mercenary>();
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
                        MercenaryPawnFactory.CreateNewPawn(this, ref newMerc, loadout?.pawnKind, loadout?.xenotype, loadout?.customXenotypeName);
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
                        MercenaryPawnFactory.CreateNewPawn(this, ref pawn, loadout.pawnKind, loadout.xenotype, loadout.customXenotypeName);
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
                        EquipPawn(mercenaries[count], loadout);
                        if (loadout.animal != null)
                        {
                            Mercenary animal = new Mercenary(true);
                            MercenaryPawnFactory.CreateNewAnimal(this, ref animal, loadout.animal);
                            animal.handler = mercenaries[count];
                            mercenaries[count].animal = animal;
                            animals.Add(animal);
                        }

                        mercenaries[count].loadout = loadout;
                        // Sync currentLoadout with what we just equipped — clear any prior
                        // divergence since this is a fresh outfit pass.
                        mercenaries[count].ownedLoadout = null;
                        mercenaries[count].currentLoadout = loadout.Clone();
                    }

                    if (mercenaries[count]?.pawn?.equipment?.AllEquipmentListForReading != null)
                    {
                        UsedWeaponList.AddRange(mercenaries[count].pawn.equipment.AllEquipmentListForReading);
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

    }
}