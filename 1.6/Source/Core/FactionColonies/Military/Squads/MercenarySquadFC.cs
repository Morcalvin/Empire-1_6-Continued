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
        public int dead;
        public MilSquadFC outfit;
        public SquadEquipmentTracker Equipment;
        public SquadDeploymentState Deployment;

        /* -*-*-*-*- Squad-first refactor fields -*-*-*-*-
         * nextAvailableTick: per-squad cooldown expiry. Updated in MilitaryOperation.EnterCooldown.
         * hiredAtTick: tick at which the squad was hired (analytics + future age hooks).
         * autoDefend: per-squad opt-in to foreign-defender candidate selection. Replaces the
         * old per-settlement comp.autoDefend flag — auto-defend now lives on the squad. */
        public int nextAvailableTick;
        public int hiredAtTick;
        public bool autoDefend;

        /* Migration buffers — pre-refactor saves wrote these as top-level Scribe nodes.
           Read in LoadingVars; drained into the matching sub-object in PostLoadInit. */
        [Unsaved] private List<ThingWithComps> _legacyUsedWeaponList;
        [Unsaved] private List<Apparel> _legacyUsedApparelList;
        [Unsaved] private Lord _legacyLord;
        [Unsaved] private Map _legacyMap;
        [Unsaved] private MilitaryOrder _legacyMilitaryOrder = MilitaryOrder.Undefined;
        [Unsaved] private IntVec3 _legacyOrderLocation;

        public MercenarySquadFC()
        {
            Equipment = CreateEquipment();
            Deployment = CreateDeployment();
        }

        /* Factory hooks so subclasses can install custom sub-objects. */
        protected virtual SquadEquipmentTracker CreateEquipment() => new SquadEquipmentTracker(this);
        protected virtual SquadDeploymentState CreateDeployment() => new SquadDeploymentState(this);

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
            Scribe_References.Look(ref outfit, "outfit");
            Scribe_Values.Look(ref dead, "dead");
            Scribe_Deep.Look(ref Equipment, "equipment", new object[] { this });
            Scribe_Deep.Look(ref Deployment, "deployment", new object[] { this });
            Scribe_References.Look(ref settlement, "Settlement");
            Scribe_Values.Look(ref nextAvailableTick, "nextAvailableTick", 0);
            Scribe_Values.Look(ref hiredAtTick, "hiredAtTick", 0);
            Scribe_Values.Look(ref autoDefend, "autoDefend", false);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                /* Pre-refactor saves stored these as top-level fields on the squad.
                   Capture into [Unsaved] buffers; drained in PostLoadInit. */
                Scribe_Collections.Look(ref _legacyUsedWeaponList, "UsedWeaponList", LookMode.Reference);
                Scribe_Collections.Look(ref _legacyUsedApparelList, "UsedApparelList", LookMode.Reference);
                Scribe_References.Look(ref _legacyLord, "lord");
                Scribe_References.Look(ref _legacyMap, "map");
                Scribe_Values.Look(ref _legacyMilitaryOrder, "militaryOrder", MilitaryOrder.Undefined);
                Scribe_Values.Look(ref _legacyOrderLocation, "orderLocation");
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Equipment is null) Equipment = CreateEquipment();
                Equipment.AdoptLegacyLists(_legacyUsedWeaponList, _legacyUsedApparelList);
                _legacyUsedWeaponList = null;
                _legacyUsedApparelList = null;

                if (Deployment is null) Deployment = CreateDeployment();
                /* Always-drain: a fresh-hire squad has all-default legacy buffers, which
                   match SquadDeploymentState's own defaults — adopting is a no-op. */
                Deployment.AdoptLegacyValues(_legacyLord, _legacyMap,
                                             _legacyMilitaryOrder, _legacyOrderLocation);
                _legacyLord = null;
                _legacyMap = null;
                _legacyMilitaryOrder = MilitaryOrder.Undefined;
                _legacyOrderLocation = default(IntVec3);
            }
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

        /// <summary>The <see cref="MilitaryOperation"/> this squad is currently part of, if any.
        /// Returned via the <see cref="MilitaryOperationManager"/>'s squad index, so this is O(1)
        /// and reflects the canonical op state.</summary>
        public MilitaryOperation Operation => FindFC.MilitaryManager?.GetOpForSquad(this);

        /// <summary>True when this squad is in any active op (offensive, defensive, deploy, or cooldown).</summary>
        public bool IsBusy => Operation is object;

        /// <summary>True when the squad's <see cref="SquadCostExtensions.DeploymentCost"/> exceeds
        /// its assigned settlement's max deploy cost. An underfunded squad stays assigned but
        /// can't take part in military operations. Distinct from <see cref="IsBusy"/>: this is a
        /// structural (cost) constraint, not a temporary deployment state.</summary>
        public bool IsUnderfunded
            => settlement is object
               && MilitaryFC.SquadExceedsSettlementBudget(this, settlement, out _, out _);

        /// <summary>True when this squad is currently assigned to a billet (settlement). False
        /// when the squad sits in the unassigned hire pool.</summary>
        public bool IsAssigned => settlement is object;

        /// <summary>True when the squad is assigned, not in any active op, not underfunded
        /// at its settlement, and past its post-op cooldown. Canonical "can launch a new
        /// op" gate.</summary>
        public bool IsAvailable
            => IsAssigned && !IsBusy && !IsUnderfunded && nextAvailableTick <= Find.TickManager.TicksGame;

        /* The squad's billet. settlement is the canonical source of truth in the squad-first
         * model; the property exists only as a stable accessor for external callers. */
        public WorldSettlementFC getSettlement => settlement;

        /// <summary>
        /// Initial population pass: creates 30 mercs (one per template slot, or blank if no
        /// outfit) and equips them via <see cref="SquadEquipmentTracker.OutfitSquad"/>. The canonical first call
        /// is from <see cref="MilitaryFC.HireSquad"/>, where a freshly-
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
                loadID = FindFC.FactionComp.GetNextMercenarySquadID();
            }

            if (outfit != null)
            {
                Equipment.OutfitSquad(outfit);
            }
            else
            {
                FindFC.Military.RebuildMercenaryPawnSet();
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

        /// <summary>Pays <see cref="SquadCostExtensions.FillEmptySlotsCost"/> silver and generates fresh
        /// pawns into every empty slot, equipping each from its resolved loadout. Slots with no
        /// loadout are skipped. Returns false (no payment) if the player can't afford the total.</summary>
        public bool FillEmptySlots()
        {
            int total = this.FillEmptySlotsCost();
            if (total > 0 && PaymentUtil.GetSilver() < total)
            {
                Messages.Message("FCSquadFillSlotsInsufficient".Translate(total),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (total > 0) PaymentUtil.PaySilver(total, PaymentUtil.Reason_SquadFillSlot, settlement);

            if (mercenaries != null)
            {
                foreach (Mercenary m in mercenaries)
                {
                    if (m is null || !m.IsEmptySlot) continue;
                    MilUnitFC blueprint = m.BlueprintLoadout;
                    if (blueprint is null || blueprint.isBlank) continue;
                    Mercenary slot = m;
                    MercenaryPawnFactory.CreateNewPawn(this, ref slot, blueprint.pawnKind, blueprint.xenotype, blueprint.customXenotypeName);
                    if (slot.pawn != null) Equipment.EquipPawn(slot, blueprint);
                    // Sync currentLoadout with what we just equipped — re-snap from the blueprint.
                    slot.currentLoadout = blueprint.Clone();
                }
            }
            FindFC.FactionComp?.military?.RebuildMercenaryPawnSet();
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
            Equipment.StripPawn(merc);
            if (merc.pawn != null && !merc.pawn.Destroyed) merc.pawn.Destroy();
            merc.pawn = null;
            if (merc.animal?.pawn != null && !merc.animal.pawn.Destroyed) merc.animal.pawn.Destroy();
            merc.animal = null;

            /* Reset transient/personalization state so the empty slot is a clean refill target.
               Keep `loadout` (pool reference) so Fill can reuse it. */
            merc.ownedLoadout = null;
            merc.currentLoadout = null;
            FindFC.FactionComp?.military?.RebuildMercenaryPawnSet();
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
            FindFC.FactionComp?.military?.RebuildMercenaryPawnSet();
            return true;
        }

        public void DebugMercenarySquad()
        {
            LogUtil.MessageForce("Debug Mercenary Squad");
            foreach (Mercenary merc in mercenaries)
            {
                if (merc?.pawn == null)
                {
                    LogUtil.MessageForce("\t[empty]");
                    continue;
                }
                LogUtil.MessageForce($"\t{merc.pawn} \t{merc.pawn.health.Dead.ToString()} \t{merc.pawn.apparel.WornApparelCount} \t{merc.pawn.equipment.AllEquipmentListForReading.Count()}");
            }
        }

        public Mercenary ReturnPawn(Pawn pawn)
        {
            foreach (Mercenary merc in mercenaries)
            {
                if (merc?.pawn == pawn) return merc;
            }
            return null;
        }

    }
}