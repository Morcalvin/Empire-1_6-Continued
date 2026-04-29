using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Per-pawn loadout editor. Mutates <see cref="Mercenary.currentLoadout"/> in place;
    /// every edit also stamps <see cref="Mercenary.ownedLoadout"/> with a clone as the
    /// "this pawn diverged" marker. Does NOT clear <see cref="MercenarySquadFC.outfit"/> —
    /// only this merc has diverged, the squad's other mercs still match the template.
    ///
    /// Reuses <see cref="FCWindow_ItemStuffPicker"/> for weapon and apparel pickers (same UX
    /// as the unit designer). Pawn identity (kindDef / xenotype) is preserved — gear changes
    /// only re-equip; they don't regenerate the pawn. Use the per-pawn Upgrade button on the
    /// inspection window or "Pick from pool unit" to swap pool references.
    /// </summary>
    public class Dialog_PawnLoadout : Window
    {
        public override Vector2 InitialSize => new Vector2(640f, 600f);

        private readonly MercenarySquadFC squad;
        private readonly Mercenary merc;
        private Vector2 apparelScroll;

        public Dialog_PawnLoadout(MercenarySquadFC squad, Mercenary merc)
        {
            this.squad = squad;
            this.merc = merc;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            draggable = true;
        }

        /* Returns the merc's currentLoadout, creating one as a clone of the best
         * available source on first access. Edits mutate this in place. After mutating,
         * call MarkDiverged to stamp ownedLoadout as the divergence flag. */
        private MilUnitFC EnsureCurrentLoadout()
        {
            if (merc is null) return null;
            if (merc.currentLoadout != null) return merc.currentLoadout;

            MilUnitFC source = merc.ownedLoadout ?? merc.loadout;
            MilUnitFC clone;
            if (source != null)
            {
                clone = source.Clone();
            }
            else
            {
                clone = MilTemplateFactory.CreateUnit(false);
                clone.name = (string)"FCSquadInspectionPersonalLoadoutDefaultName".Translate();
            }
            merc.currentLoadout = clone;
            return merc.currentLoadout;
        }

        /* Stamp ownedLoadout from the (already-mutated) currentLoadout. Called after every
         * direct edit. squad.outfit is preserved — only this merc has diverged. */
        private void MarkDiverged()
        {
            if (merc?.currentLoadout != null)
                merc.ownedLoadout = merc.currentLoadout.Clone();
        }

        private void RefreshPawnEquipment()
        {
            if (merc?.pawn is null) return;
            MilUnitFC current = merc.currentLoadout;
            if (current is null) return;
            squad.StripPawn(merc);
            squad.EquipPawn(merc, current);
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (merc is null) { Close(); return; }

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Header
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            string title = merc.pawn != null
                ? (string)"FCDialogPawnLoadoutTitle".Translate(merc.pawn.LabelShortCap)
                : (string)"FCDialogPawnLoadoutTitleEmpty".Translate();
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), title);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            string sub = merc.ownedLoadout != null
                ? (string)"FCDialogPawnLoadoutDivergedSubtitle".Translate()
                : (string)"FCDialogPawnLoadoutInheritedSubtitle".Translate(merc.loadout?.name ?? (string)"FCNone".Translate());
            Widgets.Label(new Rect(inRect.x, inRect.y + 28f, inRect.width, 18f), sub);

            // Layout: portrait + slots on the left, apparel list on the right
            float topY = inRect.y + 54f;
            float bottomBtnH = 36f;

            float leftW = 220f;
            Rect leftPanel = new Rect(inRect.x, topY, leftW, inRect.height - (topY - inRect.y) - bottomBtnH - 8f);
            Rect rightPanel = new Rect(inRect.x + leftW + 10f, topY, inRect.width - leftW - 10f, leftPanel.height);

            DrawLeftPanel(leftPanel);
            DrawApparelPanel(rightPanel);

            // Bottom: Pick from pool + Reset to pool + Done
            Rect bottomRect = new Rect(inRect.x, inRect.yMax - bottomBtnH, inRect.width, bottomBtnH);
            float bx = bottomRect.x;
            float gap = 8f;
            float wideW = 220f;

            if (Widgets.ButtonText(new Rect(bx, bottomRect.y, wideW, bottomRect.height),
                "FCDialogPawnLoadoutPickFromPool".Translate()))
            {
                OpenPickFromPoolMenu();
            }
            bx += wideW + gap;

            // Reset (snaps currentLoadout to merc.loadout's current pool state, clears ownedLoadout)
            bool canReset = merc.loadout != null;
            Color colorBefore = GUI.color;
            if (!canReset) GUI.color = Color.gray;
            Rect resetRect = new Rect(bx, bottomRect.y, wideW, bottomRect.height);
            if (Widgets.ButtonText(resetRect, "FCDialogPawnLoadoutResetToPool".Translate(), true, true, canReset))
            {
                merc.currentLoadout = merc.loadout.Clone();
                merc.ownedLoadout = null;
                RefreshPawnEquipment();
            }
            GUI.color = colorBefore;

            float doneW = 120f;
            Rect doneRect = new Rect(bottomRect.xMax - doneW, bottomRect.y, doneW, bottomRect.height);
            if (Widgets.ButtonText(doneRect, "OK".Translate())) Close();

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Left panel: pawn portrait + weapon/animal slots ---

        private void DrawLeftPanel(Rect rect)
        {
            const float portraitH = 130f;
            const float slotSize = 50f;
            const float gap = 14f;

            Rect portraitRect = new Rect(rect.x + (rect.width - 100f) / 2f, rect.y, 100f, portraitH);
            if (merc.pawn != null)
                UIUtil.DrawPawnPortrait(portraitRect, merc.pawn);
            else
                Widgets.DrawMenuSection(portraitRect);

            float slotsY = portraitRect.yMax + gap + 14f;
            float slotsTotalW = slotSize * 2 + 16f;
            float slotsX = rect.x + (rect.width - slotsTotalW) / 2f;
            Rect animalSlot = new Rect(slotsX, slotsY, slotSize, slotSize);
            Rect weaponSlot = new Rect(slotsX + slotSize + 16f, slotsY, slotSize, slotSize);

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(animalSlot.x, animalSlot.y - 14f, animalSlot.width, 14f), "fcLabelAnimal".Translate());
            Widgets.Label(new Rect(weaponSlot.x, weaponSlot.y - 14f, weaponSlot.width, 14f), "fcLabelWeapon".Translate());
            Widgets.DrawMenuSection(animalSlot);
            Widgets.DrawMenuSection(weaponSlot);
            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;

            MilUnitFC current = merc.currentLoadout;
            if (current?.HasWeapon == true)
                Widgets.ButtonImage(weaponSlot, current.weapons[0].thing.uiIcon);
            if (current?.animal != null)
                Widgets.ButtonImage(animalSlot, current.animal.race.uiIcon);

            // Click weapon slot to open picker (mutates currentLoadout)
            if (Widgets.ButtonInvisible(weaponSlot))
            {
                OpenWeaponPicker();
            }
            // Click animal slot to open animal picker
            if (Widgets.ButtonInvisible(animalSlot))
            {
                OpenAnimalPicker();
            }
        }

        // --- Right panel: apparel list + add button ---

        private void DrawApparelPanel(Rect rect)
        {
            const float headerH = 24f;
            const float rowH = 28f;
            const float iconSize = 24f;

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 110f, headerH), "fcEquippedApparel".Translate());

            Rect addBtnRect = new Rect(rect.xMax - 100f, rect.y, 100f, headerH);
            if (Widgets.ButtonText(addBtnRect, "fcAddApparel".Translate()))
            {
                OpenApparelPicker();
            }

            MilUnitFC current = merc.currentLoadout;
            List<SavedThing> apparel = current?.apparel?.Where(a => a.thing != null).ToList() ?? new List<SavedThing>();

            Rect listRect = new Rect(rect.x, rect.y + headerH + 4f, rect.width, rect.height - headerH - 4f);
            float viewH = apparel.Count * rowH;
            Rect viewRect = new Rect(0, 0, listRect.width - 16f, viewH);
            Widgets.BeginScrollView(listRect, ref apparelScroll, viewRect);
            for (int i = 0; i < apparel.Count; i++)
            {
                SavedThing item = apparel[i];
                Rect row = new Rect(0, i * rowH, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawHighlight(row);

                Rect iconRect = new Rect(row.x + 4f, row.y + 2f, iconSize, iconSize);
                Widgets.ThingIcon(iconRect, item.thing, item.stuff);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(iconRect.xMax + 6f, row.y, row.width - iconSize - 50f, rowH),
                    item.thing.LabelCap);

                Rect removeRect = new Rect(row.xMax - 28f, row.y + 4f, 24f, rowH - 8f);
                if (Widgets.ButtonText(removeRect, "X"))
                {
                    SavedThing captured = item;
                    MilUnitFC target = EnsureCurrentLoadout();
                    if (target != null)
                    {
                        target.RemoveApparel(captured.thing);
                        MarkDiverged();
                        RefreshPawnEquipment();
                    }
                }
            }
            Widgets.EndScrollView();

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Pickers ---

        private void OpenWeaponPicker()
        {
            MilUnitFC target = EnsureCurrentLoadout();
            if (target is null) return;
            List<ThingDef> weaponDefs = DefDatabase<ThingDef>.AllDefs
                .Where(t => t.IsWeapon && t.BaseMarketValue != 0
                    && !CraftUtil.WeaponBlockedForMercs(t)
                    && t.generateAllowChance > 0f
                    && CraftUtil.CanCraftItem(t)
                    && HARUtil.CanRaceUseWeapon(target.pawnKind?.race, t))
                .OrderBy(t => t.label)
                .ToList();

            SavedThing? currentWeapon = target.HasWeapon ? target.weapons[0] : (SavedThing?)null;
            Find.WindowStack.Add(new FCWindow_ItemStuffPicker(
                weaponDefs,
                onConfirm: (item, stuff) =>
                {
                    target.SetWeapon(item, stuff);
                    MarkDiverged();
                    RefreshPawnEquipment();
                },
                onUnequip: () =>
                {
                    target.ClearWeapon();
                    MarkDiverged();
                    RefreshPawnEquipment();
                },
                titleKey: "fcPickWeapon",
                initialItem: currentWeapon?.thing,
                initialStuff: currentWeapon?.stuff
            ));
        }

        private void OpenApparelPicker()
        {
            MilUnitFC target = EnsureCurrentLoadout();
            if (target is null) return;
            BodyDef body = target.pawnKind?.race?.race?.body ?? BodyDefOf.Human;
            List<ThingDef> apparelDefs = DefDatabase<ThingDef>.AllDefs
                .Where(t => t.IsApparel
                    && t.apparel.PawnCanWear(Gender.None, DevelopmentalStage.Adult)
                    && CraftUtil.CanCraftItem(t)
                    && HARUtil.CanRaceWearApparel(target.pawnKind?.race, t)
                    && !target.apparel.Any(a => a.thing == t))
                .OrderBy(t => t.label)
                .ToList();
            Find.WindowStack.Add(new FCWindow_ItemStuffPicker(
                apparelDefs,
                onConfirm: (item, stuff) =>
                {
                    target.SetApparel(item, stuff);
                    MarkDiverged();
                    RefreshPawnEquipment();
                },
                titleKey: "fcPickApparel"
            ));
        }

        private void OpenAnimalPicker()
        {
            MilUnitFC target = EnsureCurrentLoadout();
            if (target is null) return;
            // The animal picker mutates the unit immediately on confirm. We can't directly
            // hook into that to re-stamp ownedLoadout, but the animal data is informational
            // for the per-pawn editor — the actual animal companion attachment to a pawn is
            // rebuilt only on full outfit changes (Upgrade All / Fill).
            Find.WindowStack.Add(new FCWindow_AnimalPicker(target));
        }

        // --- Pick from pool ---

        private void OpenPickFromPoolMenu()
        {
            MilitaryCustomizationUtil util = FactionCache.FactionComp?.militaryCustomizationUtil;
            if (util?.units is null) return;
            double currentCost = merc.EffectiveLoadout?.getTotalCost ?? 0;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (MilUnitFC unit in util.units)
            {
                MilUnitFC captured = unit;
                double newCost = captured.getTotalCost;
                int diff = newCost > currentCost
                    ? (int)Math.Round((newCost - currentCost) * FCSettings.squadUpgradeCostMultiplier)
                    : 0;
                string label = diff > 0
                    ? captured.name + " (+$" + diff + ")"
                    : captured.name;
                options.Add(new FloatMenuOption(label, delegate
                {
                    // Pool-unit swap: not a divergence, so template association is preserved.
                    if (diff > 0)
                    {
                        if (PaymentUtil.GetSilver() < diff)
                        {
                            Messages.Message("FCSquadUpgradeInsufficientSilver".Translate(diff),
                                MessageTypeDefOf.RejectInput, false);
                            return;
                        }
                        PaymentUtil.PaySilver(diff, PaymentUtil.Reason_SquadUpgrade, squad?.settlement);
                    }
                    merc.loadout = captured;
                    merc.ownedLoadout = null;
                    merc.currentLoadout = captured.Clone();
                    RefreshPawnEquipment();
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("FCNoUnitAvailable".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
