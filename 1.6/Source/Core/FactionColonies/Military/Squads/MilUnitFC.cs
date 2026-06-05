using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public class MilUnitFC : IExposable, ILoadReferenceable
    {
        public int loadID;
        public string name;
        public bool isBlank;
        public double equipmentTotalCost;
        public int tickChanged = -1;
        public PawnKindDef animal;
        public PawnKindDef pawnKind;
        public XenotypeDef xenotype;
        public string customXenotypeName;

        // Race hint that survives clone-defName remapping by BackCompatPatches.
        // RandomPawnKind() returns a runtime clone (e.g. PColony_Fighter_Milira) that
        // isn't registered in DefDatabase, so on load Scribe collapses it to the base
        // template (race=Human). Saving the race here lets PostLoadInit restore the
        // correct race-specific clone via PawnKindTemplateUtil.
        private ThingDef pawnKindRaceHint;

        // Def-based equipment storage
        public List<SavedThing> weapons = new List<SavedThing>();
        public List<SavedThing> apparel = new List<SavedThing>();
        public List<SavedThing> inventory = new List<SavedThing>();
        public List<SavedImplant> implants = new List<SavedImplant>();
        public bool HasWeapon => weapons.Any(w => w.thing != null);

        // Forced gender for spawned pawns (null = any).
        public Gender? forcedGender;

        // Lazy preview pawn for UI rendering only — not serialized
        private Pawn previewPawn;
        private bool pawnIdentityDirty = true;    // Needs new PawnGenerator call (race/xeno change)
        private bool pawnEquipmentDirty = true;   // Needs equipment refresh on same pawn

        /* Monotonic edit counter bumped on every ChangeTick(). Tick-independent so
         * same-tick edits (and edits made while paused) are still detected by UI
         * code that wants to refresh derived state. */
        public int editVersion;

        public MilUnitFC()
        {
        }

        public MilUnitFC(bool blank)
        {
            loadID = FindFC.Military.NextUnitId();
            isBlank = blank;
            equipmentTotalCost = 0;

            try
            {
                Faction playerFaction = FindFC.EmpireFaction;
                if (playerFaction != null && playerFaction.def.pawnGroupMakers.Any() &&
                    playerFaction.def.pawnGroupMakers.Any(pgm => pgm.options?.Any() == true))
                {
                    pawnKind = playerFaction.RandomPawnKind();
                }
                else
                {
                    var pColonyDef = DefDatabase<FactionDef>.GetNamed("PColony");
                    if (pColonyDef?.pawnGroupMakers?.Any(pgm => pgm.options?.Any() == true) == true)
                    {
                        pawnKind = pColonyDef.pawnGroupMakers.RandomElement().options.RandomElement().kind;
                    }
                    else
                    {
                        pawnKind = PawnKindDefOf.Colonist;
                    }
                }

                if (!pawnKind.ValidPawnKindDef())
                {
                    LogUtil.Warning($"MilUnitFC: selected pawnKind failed validation. Falling back to Colonist.");
                    pawnKind = PawnKindDefOf.Colonist;
                }
            }
            catch (Exception ex)
            {
                LogUtil.Error($"Error creating MilUnitFC: {ex.Message}");
                pawnKind = PawnKindDefOf.Colonist;
            }

            if (!isBlank)
            {
                xenotype = XenotypeDefOf.Baseliner;
            }
        }

        public string GetUniqueLoadID()
        {
            return $"MilUnitFC_{loadID}";
        }

        // --- Xenotype Helpers ---

        public bool IsCustomXenotype => customXenotypeName != null;

        public CustomXenotype ResolveCustomXenotype()
        {
            if (customXenotypeName == null) return null;
            CustomXenotype result = null;
            FactionCache.CustomXenotypesDecoder?.TryGetValue(customXenotypeName, out result);
            return result;
        }

        public List<GeneDef> GetXenotypeGenes()
        {
            if (xenotype != null) return xenotype.genes;
            CustomXenotype custom = ResolveCustomXenotype();
            if (custom != null) return custom.genes;
            return null;
        }

        public string GetXenotypeLabel()
        {
            if (xenotype != null) return xenotype.label.CapitalizeFirst();
            if (customXenotypeName != null) return customXenotypeName.CapitalizeFirst();
            return "None";
        }

        public Texture2D GetXenotypeIcon()
        {
            if (xenotype != null) return xenotype.Icon;
            CustomXenotype custom = ResolveCustomXenotype();
            if (custom != null) return custom.IconDef.Icon;
            return null;
        }

        public void SetXenotype(XenotypeDef def)
        {
            xenotype = def;
            customXenotypeName = null;
            pawnIdentityDirty = true;
            pawnEquipmentDirty = true;
            RevalidateImplants();
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public void SetCustomXenotype(CustomXenotype custom)
        {
            xenotype = null;
            customXenotypeName = custom.name;
            FactionCache.EnsureInGameDatabase(custom);
            pawnIdentityDirty = true;
            pawnEquipmentDirty = true;
            RevalidateImplants();
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        /// <summary>
        /// Marks the preview pawn for a full regeneration (new PawnGenerator call). Use when
        /// identity-affecting state changes (race, xenotype, gender, implants) — distinct from
        /// <see cref="MarkEquipmentDirty"/>, which only re-applies apparel/weapons.
        /// </summary>
        public void MarkIdentityDirty()
        {
            pawnIdentityDirty = true;
            pawnEquipmentDirty = true;
        }

        public virtual void ExposeData()
        {
            Scribe_Values.Look(ref loadID, "loadID");
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref isBlank, "blank");
            Scribe_Values.Look(ref equipmentTotalCost, "equipmentTotalCost", -1);
            Scribe_Values.Look(ref tickChanged, "tickChanged");
            Scribe_Defs.Look(ref pawnKind, "PawnKind");
            Scribe_Defs.Look(ref animal, "animal");
            Scribe_Defs.Look(ref xenotype, "xenotype");
            Scribe_Values.Look(ref customXenotypeName, "customXenotypeName");

            if (Scribe.mode == LoadSaveMode.Saving)
                pawnKindRaceHint = pawnKind?.race;
            Scribe_Defs.Look(ref pawnKindRaceHint, "pawnKindRace");

            // Def-based equipment storage
            Scribe_Collections.Look(ref weapons, "weapons", LookMode.Deep);
            Scribe_Collections.Look(ref apparel, "apparel", LookMode.Deep);
            Scribe_Collections.Look(ref inventory, "inventory", LookMode.Deep);
            Scribe_Collections.Look(ref implants, "implants", LookMode.Deep);

            // forcedGender nullable — save only if set
            bool hasGender = forcedGender.HasValue;
            Gender genderVal = forcedGender ?? Gender.None;
            Scribe_Values.Look(ref hasGender, "hasForcedGender", false);
            if (hasGender)
                Scribe_Values.Look(ref genderVal, "forcedGender", Gender.None);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                forcedGender = hasGender ? genderVal : (Gender?)null;

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (weapons == null) weapons = new List<SavedThing>();
                if (apparel == null) apparel = new List<SavedThing>();
                if (inventory == null) inventory = new List<SavedThing>();
                if (implants == null) implants = new List<SavedImplant>();
                // Mutual exclusivity: prefer XenotypeDef if both are set
                if (xenotype != null && customXenotypeName != null)
                    customXenotypeName = null;

                RestoreClonedPawnKindIfRemapped();
            }
        }

        /// <summary>
        /// If pawnKind was a runtime clone (e.g. PColony_Fighter_Milira) when saved,
        /// Scribe's cross-ref resolution will have collapsed it to the base template
        /// (PColony_Fighter, race=Human) via BackCompatPatches. Use the saved race
        /// hint to re-fetch the correct race-specific clone.
        /// </summary>
        private void RestoreClonedPawnKindIfRemapped()
        {
            if (pawnKind is null || pawnKindRaceHint is null) return;
            if (pawnKind.race == pawnKindRaceHint) return;
            if (!PawnKindTemplateUtil.TryRestoreRaceSpecificClone(ref pawnKind, pawnKindRaceHint))
            {
                LogUtil.Warning($"MilUnitFC '{name}': could not restore race-specific clone for race '{pawnKindRaceHint.defName}' on template '{pawnKind.defName}'.");
            }
        }

        // --- Preview Pawn (UI only) ---

        public Pawn PreviewPawn
        {
            get
            {
                if (previewPawn == null || pawnIdentityDirty)
                    RebuildPreviewPawn();
                else if (pawnEquipmentDirty)
                    RefreshPreviewEquipment();
                return previewPawn;
            }
        }

        public void MarkEquipmentDirty()
        {
            pawnEquipmentDirty = true;
        }

        private void RebuildPreviewPawn()
        {
            try
            {
                if (previewPawn != null)
                {
                    previewPawn.apparel?.DestroyAll();
                    previewPawn.equipment?.DestroyAllEquipment();
                    previewPawn.Destroy();
                }

                previewPawn = FCPawnGenerator.GenerateWithForcedXenotype(FCPawnGenerator.WorkerOrMilitaryRequestForUnit(this));

                if (previewPawn != null && previewPawn.Faction == null)
                {
                    Faction empireFaction = FindFC.EmpireFaction;
                    if (empireFaction != null)
                        previewPawn.SetFaction(empireFaction);
                }

                // Implants change the pawn's health/identity, so they are applied here at
                // generation time (not in RefreshPreviewEquipment, which runs on equipment-only
                // changes and would otherwise double-install).
                if (previewPawn != null)
                    ApplyImplantsToPawn(previewPawn, this);
            }
            catch (Exception ex)
            {
                LogUtil.Warning($"Failed to generate preview pawn for {name}: {ex.Message}");
                previewPawn = null;
            }

            pawnIdentityDirty = false;

            if (previewPawn == null)
            {
                pawnEquipmentDirty = false;
                return;
            }

            previewPawn.mindState.canFleeIndividual = false;
            RefreshPreviewEquipment();
        }

        protected virtual void RefreshPreviewEquipment()
        {
            if (previewPawn == null) return;
            ApplyEquipmentToPawn(previewPawn, this);
            pawnEquipmentDirty = false;
        }

        /* Strips and re-equips the target pawn from this unit's apparel/weapons.
         * Used by both the lazy preview pawn and Dialog_PawnLoadout's cloned-pawn
         * preview. Apparel colors are resolved via the player faction's color rules. */
        public static void ApplyEquipmentToPawn(Pawn target, MilUnitFC source)
        {
            if (target is null || source is null) return;

            target.apparel?.DestroyAll();
            target.equipment?.DestroyAllEquipment();

            FactionFC factionComp = FindFC.FactionComp;
            if (source.apparel != null && target.apparel != null)
            {
                foreach (SavedThing a in source.apparel)
                {
                    Thing t = a.CreateThing();
                    if (t is Apparel ap)
                    {
                        Color resolved = factionComp != null ? factionComp.ResolveApparelColor(a) : Color.white;
                        t.SetColor(resolved, reportFailure: false);
                        target.apparel.Wear(ap);
                    }
                }
            }

            if (source.weapons != null && target.equipment != null)
            {
                foreach (SavedThing w in source.weapons)
                {
                    Thing wt = w.CreateThing();
                    if (wt is ThingWithComps twc)
                        target.equipment.AddEquipment(twc);
                }
            }
        }

        /* Installs this unit's chosen implants on the target pawn, reusing the base game's own
         * surgery validation/application (Recipe_InstallImplant.ApplyOnPawn with a null billDoer
         * skips the fail/tale path and adds the hediff). The concrete BodyPartRecord is resolved
         * from the stored (recipe, index) against the target's body via GetPartsToApplyOn, whose
         * ordering is deterministic. Invalid entries (part missing / slot taken / incompatible on
         * this body) are silently skipped. Used by the preview pawn and the real spawned pawn. */
        public static void ApplyImplantsToPawn(Pawn target, MilUnitFC source)
        {
            if (target is null || source?.implants is null) return;
            if (target.health is null || target.Dead || target.Destroyed) return;

            foreach (SavedImplant im in source.implants)
            {
                if (im.recipe is null) continue;
                try
                {
                    BodyPartRecord part;
                    if (TryResolveImplant(target, im, out part))
                        im.recipe.Worker.ApplyOnPawn(target, part, null, null, null);
                }
                catch (Exception ex)
                {
                    LogUtil.Warning($"Failed to apply implant {im.recipe?.defName} to {target.LabelShortCap}: {ex.Message}");
                }
            }
        }

        /* Resolves the body part a stored implant should install on, and whether it's currently
         * installable. The part is located by its STABLE occurrence index within body.AllParts —
         * NOT an index into the filtered GetPartsToApplyOn list, which shifts with the pawn's
         * health state (battle injuries, other implants). This is what lets an implant chosen in
         * the designer (against a clean preview) install reliably on a real, possibly battle-worn
         * pawn. Validity (part present, slot free, recipe-compatible, AvailableOnNow) is still
         * delegated to the base game. Returns false (skip) for whole-body implants already present
         * or parts that aren't a legal target right now. */
        private static bool TryResolveImplant(Pawn pawn, SavedImplant im, out BodyPartRecord part)
        {
            part = null;
            if (pawn?.health is null || im.recipe?.addsHediff is null) return false;

            if (!im.recipe.targetsBodyPart)
            {
                if (pawn.health.hediffSet.HasHediff(im.recipe.addsHediff)) return false;
                return im.recipe.Worker.AvailableOnNow(pawn, null);
            }

            if (im.bodyPart is null) return false;
            part = NthBodyPartOfDef(pawn, im.bodyPart, im.bodyPartIndex);
            if (part is null) return false;

            // The resolved part must be a legal target for this recipe right now (not missing,
            // slot not already filled, no incompatible hediff) — GetPartsToApplyOn enforces that.
            bool legalTarget = false;
            foreach (BodyPartRecord p in im.recipe.Worker.GetPartsToApplyOn(pawn, im.recipe))
            {
                if (p == part) { legalTarget = true; break; }
            }
            if (!legalTarget) return false;

            return im.recipe.Worker.AvailableOnNow(pawn, part);
        }

        /// <summary>Occurrence index of <paramref name="part"/> among same-def parts in the body's
        /// AllParts order. Stable regardless of installed hediffs. -1 if not found.</summary>
        public static int BodyPartOccurrenceIndex(Pawn pawn, BodyPartRecord part)
        {
            if (pawn?.RaceProps?.body is null || part is null) return -1;
            int occ = 0;
            foreach (BodyPartRecord p in pawn.RaceProps.body.AllParts)
            {
                if (p == part) return occ;
                if (p.def == part.def) occ++;
            }
            return -1;
        }

        private static BodyPartRecord NthBodyPartOfDef(Pawn pawn, BodyPartDef def, int n)
        {
            if (pawn?.RaceProps?.body is null || def is null) return null;
            int occ = 0;
            foreach (BodyPartRecord p in pawn.RaceProps.body.AllParts)
            {
                if (p.def == def)
                {
                    if (occ == n) return p;
                    occ++;
                }
            }
            return null;
        }

        /* Removes the given implants from a pawn, restoring the natural body part. Borrows the
         * base game's Pawn_HealthTracker.RestorePart (the same call behind the dev "heal" action
         * and surgery) which recursively strips every hediff off the part and its children — so
         * the added bionic part disappears and the natural part comes back. The actual installed
         * hediff is located by its addsHediff def (not by stored index, which shifts once a slot
         * is filled). Whole-body implants (no targeted part) are removed directly. */
        public static void RemoveImplantsFromPawn(Pawn target, List<SavedImplant> implants)
        {
            if (target?.health is null || implants is null) return;
            if (target.Dead || target.Destroyed) return;

            foreach (SavedImplant im in implants)
            {
                if (im.recipe?.addsHediff is null) continue;
                try
                {
                    Hediff found = null;
                    List<Hediff> hediffs = target.health.hediffSet.hediffs;
                    for (int i = 0; i < hediffs.Count; i++)
                    {
                        if (hediffs[i].def == im.recipe.addsHediff) { found = hediffs[i]; break; }
                    }
                    if (found is null) continue;

                    if (found.Part != null)
                        target.health.RestorePart(found.Part);
                    else
                        target.health.RemoveHediff(found);
                }
                catch (Exception ex)
                {
                    LogUtil.Warning($"Failed to remove implant {im.recipe?.defName} from {target.LabelShortCap}: {ex.Message}");
                }
            }
        }

        /* Brings a live pawn's installed implants in line with the desired loadout WITHOUT
         * regenerating the pawn (identity preserved): clears the previously-applied set back to a
         * clean body via RestorePart, then re-applies the desired set on the clean base — which is
         * exactly the index semantics the designer preview uses, so additions and removals are both
         * handled. Used by the squad-upgrade paths when only implants changed. */
        public static void ReconcileImplantsOnPawn(Pawn target, MilUnitFC desired, MilUnitFC previous)
        {
            if (target?.health is null || target.Dead || target.Destroyed) return;
            if (previous != null) RemoveImplantsFromPawn(target, previous.implants);
            if (desired != null) ApplyImplantsToPawn(target, desired);
        }

        // --- Equipment Mutation Methods ---

        public void ChangeTick()
        {
            tickChanged = Find.TickManager.TicksGame;
            editVersion++;
            costDirty = true;
        }

        public void SetWeapon(ThingDef def, ThingDef stuff)
        {
            weapons.Clear();
            weapons.Add(new SavedThing(def, stuff));
            pawnEquipmentDirty = true;
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public void ClearWeapon()
        {
            weapons.Clear();
            pawnEquipmentDirty = true;
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public void SetApparel(ThingDef def, ThingDef stuff)
        {
            // Remove conflicting apparel using RimWorld's static check
            BodyDef body = pawnKind?.race?.race?.body ?? BodyDefOf.Human;
            apparel.RemoveAll(existing =>
                !ApparelUtility.CanWearTogether(existing.thing, def, body));
            apparel.Add(new SavedThing(def, stuff));
            pawnEquipmentDirty = true;
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public void RemoveApparel(ThingDef def)
        {
            apparel.RemoveAll(s => s.thing == def);
            pawnEquipmentDirty = true;
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public virtual void ClearAllEquipment()
        {
            weapons.Clear();
            apparel.Clear();
            inventory.Clear();
            implants.Clear();
            pawnEquipmentDirty = true;
            pawnIdentityDirty = true; // implants cleared — preview pawn must regenerate
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        // --- Inventory ---

        /* Adds (or tops up) a carried inventory entry. Hard-blocks the add when it would push
         * total carried mass over the unit's 70% carry cap (see CarryCapacity) — the player can
         * never overload a designed unit. Returns false (with a message) when rejected. */
        public bool AddInventory(ThingDef def, ThingDef stuff, int count)
        {
            if (def is null || count <= 0) return false;

            float addedMass = MassOf(def, stuff) * count;
            if (CurrentInventoryMass + addedMass > CarryCapacity + 0.0001f)
            {
                Messages.Message("fcInventoryOverweight".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            // Merge with an existing matching row (same thing + stuff, no specified quality).
            for (int i = 0; i < inventory.Count; i++)
            {
                SavedThing existing = inventory[i];
                if (existing.thing == def && existing.stuff == stuff && !existing.quality.HasValue)
                {
                    existing.count = Mathf.Max(1, existing.count) + count;
                    inventory[i] = existing;
                    pawnEquipmentDirty = true;
                    ChangeTick();
                    MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
                    return true;
                }
            }

            inventory.Add(new SavedThing(def, stuff, count));
            pawnEquipmentDirty = true;
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
            return true;
        }

        public void SetInventoryCount(int index, int count)
        {
            if (index < 0 || index >= inventory.Count) return;
            if (count <= 0) { RemoveInventory(index); return; }

            SavedThing item = inventory[index];
            // Compute prospective mass excluding this row, then re-add at the requested count.
            float massWithout = CurrentInventoryMass - MassOf(item.thing, item.stuff) * Mathf.Max(1, item.count);
            float prospective = massWithout + MassOf(item.thing, item.stuff) * count;
            if (prospective > CarryCapacity + 0.0001f)
            {
                Messages.Message("fcInventoryOverweight".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            item.count = count;
            inventory[index] = item;
            pawnEquipmentDirty = true;
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public void RemoveInventory(int index)
        {
            if (index < 0 || index >= inventory.Count) return;
            inventory.RemoveAt(index);
            pawnEquipmentDirty = true;
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        private static float MassOf(ThingDef def, ThingDef stuff)
        {
            if (def is null) return 0f;
            return def.GetStatValueAbstract(StatDefOf.Mass, stuff);
        }

        /// <summary>Carry cap = 70% of a typical pawn-of-this-xenotype's carrying capacity.</summary>
        public float CarryCapacity
        {
            get
            {
                Pawn p = PreviewPawn;
                return p != null ? MassUtility.Capacity(p) * 0.7f : 0f;
            }
        }

        /// <summary>Total carried mass counted against the cap: inventory items plus the equipped
        /// weapon(s). Worn apparel is excluded (it's worn, not carried).</summary>
        public float CurrentInventoryMass
        {
            get
            {
                float total = 0f;
                foreach (SavedThing i in inventory)
                    total += MassOf(i.thing, i.stuff) * Mathf.Max(1, i.count);
                foreach (SavedThing w in weapons)
                    total += MassOf(w.thing, w.stuff) * Mathf.Max(1, w.count);
                return total;
            }
        }

        // --- Implants ---

        public void AddImplant(RecipeDef recipe, BodyPartDef bodyPart, int bodyPartIndex)
        {
            if (recipe is null) return;
            implants.Add(new SavedImplant(recipe, bodyPart, bodyPartIndex));
            MarkIdentityDirty();
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public void RemoveImplant(int index)
        {
            if (index < 0 || index >= implants.Count) return;
            implants.RemoveAt(index);
            MarkIdentityDirty();
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        /* Drops stored implants that no longer resolve to a valid slot on the current body
         * (e.g. after a race/xenotype change removed or altered the target part). Validation
         * is delegated to the base game via a freshly-built preview pawn. */
        public void RevalidateImplants()
        {
            if (implants.Count == 0) return;

            // Force a clean preview pawn WITHOUT implants so we can test each one independently.
            pawnIdentityDirty = true;
            List<SavedImplant> snapshot = implants;
            implants = new List<SavedImplant>();
            Pawn testPawn = PreviewPawn; // rebuilt with no implants
            implants = snapshot;

            if (testPawn is null) return;

            List<SavedImplant> kept = new List<SavedImplant>();
            foreach (SavedImplant im in snapshot)
            {
                if (im.recipe is null) continue;
                try
                {
                    BodyPartRecord part;
                    if (!TryResolveImplant(testPawn, im, out part)) continue;

                    // Install on the test pawn so later implants validate against a cumulative body.
                    im.recipe.Worker.ApplyOnPawn(testPawn, part, null, null, null);
                    kept.Add(im);
                }
                catch (Exception ex)
                {
                    LogUtil.Warning($"RevalidateImplants: dropping implant {im.recipe?.defName}: {ex.Message}");
                }
            }

            implants = kept;
            pawnIdentityDirty = true; // preview must rebuild with the surviving implants
        }

        // --- Gender ---

        public void SetForcedGender(Gender? gender)
        {
            forcedGender = gender;
            MarkIdentityDirty();
            ChangeTick();
        }

        /* Re-marks identity dirty and revalidates implants after a race change. Called by the
         * race picker, which writes pawnKind directly. */
        public void OnRaceChanged()
        {
            MarkIdentityDirty();
            RevalidateImplants();
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        // --- Apparel Color ---

        public void SetApparelColor(ThingDef def, Color color)
        {
            for (int i = 0; i < apparel.Count; i++)
            {
                if (apparel[i].thing == def)
                {
                    SavedThing item = apparel[i];
                    item.color = color;
                    item.hasColor = true;
                    apparel[i] = item;
                    break;
                }
            }
            pawnEquipmentDirty = true;
            ChangeTick();
        }

        public void ClearApparelColor(ThingDef def)
        {
            for (int i = 0; i < apparel.Count; i++)
            {
                if (apparel[i].thing == def)
                {
                    SavedThing item = apparel[i];
                    item.color = Color.white;
                    item.hasColor = false;
                    apparel[i] = item;
                    break;
                }
            }
            pawnEquipmentDirty = true;
            ChangeTick();
        }

        public void SetAllApparelColors(Color color)
        {
            for (int i = 0; i < apparel.Count; i++)
            {
                SavedThing item = apparel[i];
                item.color = color;
                item.hasColor = true;
                apparel[i] = item;
            }
            pawnEquipmentDirty = true;
            ChangeTick();
        }

        public void ClearAllApparelColors()
        {
            for (int i = 0; i < apparel.Count; i++)
            {
                SavedThing item = apparel[i];
                item.color = Color.white;
                item.hasColor = false;
                apparel[i] = item;
            }
            pawnEquipmentDirty = true;
            ChangeTick();
        }

        // --- Cost ---

        protected bool costDirty = true;

        public double getTotalCost
        {
            get
            {
                if (costDirty)
                {
                    UpdateEquipmentTotalCost();
                    costDirty = false;
                }
                return equipmentTotalCost;
            }
        }

        public virtual void UpdateEquipmentTotalCost()
        {
            if (isBlank)
            {
                equipmentTotalCost = 0;
                return;
            }

            double totalCost = 0;

            if (pawnKind?.race != null)
            {
                float xenoFactor = xenotype is object
                    ? GeneValuationUtil.XenotypeFactor(xenotype)
                    : GeneValuationUtil.XenotypeFactor(ResolveCustomXenotype());
                totalCost += Math.Floor(pawnKind.race.BaseMarketValue * FCSettings.militaryRaceCostMultiplier * xenoFactor);
            }

            foreach (SavedThing a in apparel)
                totalCost += a.MarketValue;

            foreach (SavedThing w in weapons)
                totalCost += w.MarketValue;

            foreach (SavedThing inv in inventory)
                totalCost += inv.MarketValue;

            foreach (SavedImplant im in implants)
                totalCost += ImplantCost(im.recipe);

            if (animal != null)
                totalCost += Math.Floor(animal.race.BaseMarketValue * FCSettings.militaryAnimalCostMultiplier);

            equipmentTotalCost = Math.Ceiling(totalCost);
        }

        /* Approximate cost of an implant from its install recipe: the market value of the fixed
         * ingredient(s) it consumes (the implant item), falling back to the produced thing. */
        public static float ImplantCost(RecipeDef recipe)
        {
            if (recipe is null) return 0f;
            float cost = 0f;
            if (recipe.ingredients != null)
            {
                foreach (IngredientCount ing in recipe.ingredients)
                {
                    if (ing.IsFixedIngredient && ing.FixedIngredient != null)
                        cost += ing.FixedIngredient.BaseMarketValue * ing.GetBaseCount();
                }
            }
            if (cost <= 0f && recipe.ProducedThingDef != null)
                cost += recipe.ProducedThingDef.BaseMarketValue;
            return cost;
        }

        // --- Subclass-Aware Export/Import ---

        /* Creates the appropriate SavedUnitFC (or subclass) snapshot of this unit.
           Subclasses override to return their own SavedUnitFC subtype carrying their extra fields. */
        public virtual SavedUnitFC ToSavedUnit() => new SavedUnitFC(this);

        /* Called after base fields have been copied into a new instance during import.
           Subclasses override to pull their extra fields out of the SavedUnitFC subclass. */
        public virtual void LoadFromSaved(SavedUnitFC saved)
        {
        }

        // --- Unit Management ---

        public void RemoveUnit()
        {
            FindFC.Military.units.Remove(this);
        }

        /* Deep copy used by per-merc owned-loadout snapshots. The clone is owned by a
         * single Mercenary (not added to the pool), so it gets a fresh load id but no
         * registration with militaryCustomizationUtil.units. Subclasses that add fields
         * should override CopyExtraFieldsTo. */
        public virtual MilUnitFC Clone()
        {
            MilUnitFC copy = MilTemplateFactory.CreateUnit(isBlank);
            copy.name = name;
            copy.pawnKind = pawnKind;
            copy.xenotype = xenotype;
            copy.customXenotypeName = customXenotypeName;
            copy.animal = animal;
            copy.forcedGender = forcedGender;
            copy.weapons = new List<SavedThing>(weapons ?? new List<SavedThing>());
            copy.apparel = new List<SavedThing>(apparel ?? new List<SavedThing>());
            copy.inventory = new List<SavedThing>(inventory ?? new List<SavedThing>());
            copy.implants = new List<SavedImplant>(implants ?? new List<SavedImplant>());
            CopyExtraFieldsTo(copy);
            copy.ChangeTick();
            copy.UpdateEquipmentTotalCost();
            return copy;
        }

        /* Subclass hook for Clone — copy any extra fields onto the destination. */
        protected virtual void CopyExtraFieldsTo(MilUnitFC dest)
        {
        }

        /// <summary>
        /// Re-roll the preview pawn (new appearance) while keeping equipment.
        /// Used by "Roll New Pawn" and race/xeno change buttons.
        /// </summary>
        public void RerollPreviewPawn()
        {
            pawnIdentityDirty = true;
            pawnEquipmentDirty = true;
            ChangeTick();
        }
    }
}
