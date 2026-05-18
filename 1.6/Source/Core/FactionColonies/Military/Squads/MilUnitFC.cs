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

        // Def-based equipment storage
        public List<SavedThing> weapons = new List<SavedThing>();
        public List<SavedThing> apparel = new List<SavedThing>();
        public bool HasWeapon => weapons.Any(w => w.thing != null);

        // CE ammo preference (null = equip random ammo)
        public ThingDef preferredAmmo;

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
            loadID = FindFC.FactionComp.NextUnitID;
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
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
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

            // Def-based equipment storage
            Scribe_Collections.Look(ref weapons, "weapons", LookMode.Deep);
            Scribe_Collections.Look(ref apparel, "apparel", LookMode.Deep);
            Scribe_Defs.Look(ref preferredAmmo, "preferredAmmo");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (weapons == null) weapons = new List<SavedThing>();
                if (apparel == null) apparel = new List<SavedThing>();
                // Mutual exclusivity: prefer XenotypeDef if both are set
                if (xenotype != null && customXenotypeName != null)
                    customXenotypeName = null;
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
            preferredAmmo = null;
            pawnEquipmentDirty = true;
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public void ClearWeapon()
        {
            weapons.Clear();
            preferredAmmo = null;
            pawnEquipmentDirty = true;
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public void SetPreferredAmmo(ThingDef ammo)
        {
            preferredAmmo = ammo;
            ChangeTick();
        }

        public void ClearPreferredAmmo()
        {
            preferredAmmo = null;
            ChangeTick();
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
            preferredAmmo = null;
            pawnEquipmentDirty = true;
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

            if (animal != null)
                totalCost += Math.Floor(animal.race.BaseMarketValue * FCSettings.militaryAnimalCostMultiplier);

            equipmentTotalCost = Math.Ceiling(totalCost);
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
            FindFC.FactionComp.military.units.Remove(this);
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
            copy.preferredAmmo = preferredAmmo;
            copy.weapons = new List<SavedThing>(weapons ?? new List<SavedThing>());
            copy.apparel = new List<SavedThing>(apparel ?? new List<SavedThing>());
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
