using FactionColonies.util;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Per-pawn loadout editor. Mutates <see cref="Mercenary.ownedLoadout"/> only —
    /// the merc's *target/assigned* loadout. Does NOT touch
    /// <see cref="Mercenary.currentLoadout"/> (equipped snapshot) or the pawn's
    /// actual gear. The per-pawn Upgrade button on the inspection window does the
    /// assigned → equipped transition (paying the silver cost diff).
    ///
    /// Reuses <see cref="FCWindow_ItemStuffPicker"/> for weapon and apparel pickers (same UX
    /// as the unit designer). Pawn identity (kindDef / xenotype) is preserved.
    /// </summary>
    public class Dialog_PawnLoadout : Window
    {
        public override Vector2 InitialSize => new Vector2(640f, 680f);

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

        /* Returns the merc's ownedLoadout (the target/assigned loadout), creating
         * it as a clone of the squad template on first edit. Only call from
         * confirm/apply paths — calling on picker open would break association
         * even when the user cancels. */
        private MilUnitFC EnsureOwnedLoadout()
        {
            if (merc is null) return null;
            if (merc.ownedLoadout != null) return merc.ownedLoadout;

            MilUnitFC source = merc.loadout ?? merc.currentLoadout;
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
            merc.ownedLoadout = clone;
            return merc.ownedLoadout;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (merc is null) { Close(); return; }

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            MilUnitFC current = merc.BlueprintLoadout;

            // Header bar (full-width highlight behind the title; right-inset
            // leaves room for the close X which overlaps inRect's top-right).
            Rect headerBar = new Rect(inRect.x, inRect.y, inRect.width - 26f, 35f);
            Widgets.DrawHighlight(headerBar);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            string title = merc.pawn != null
                ? (string)"FCDialogPawnLoadoutTitle".Translate(merc.pawn.LabelShortCap)
                : (string)"FCDialogPawnLoadoutTitleEmpty".Translate();
            Widgets.Label(new Rect(headerBar.x + 5f, headerBar.y, headerBar.width - 10f, headerBar.height), title);

            // Subtitle: template association state
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            string sub = merc.ownedLoadout != null
                ? (string)"FCDialogPawnLoadoutDivergedSubtitle".Translate()
                : (string)"FCDialogPawnLoadoutInheritedSubtitle".Translate(merc.loadout?.name ?? (string)"FCNone".Translate());
            Widgets.Label(new Rect(inRect.x, headerBar.yMax + 4f, inRect.width, 18f), sub);

            // Info: race + xenotype (read-only — pawn identity is preserved here)
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            string raceName = current?.pawnKind?.race?.label?.CapitalizeFirst() ?? "Unknown";
            string infoLine;
            if (ModsConfig.BiotechActive)
            {
                string xenoName = current?.GetXenotypeLabel() ?? "";
                infoLine = "Race".Translate() + ": " + raceName + "   ·   " + "Xenotype".Translate() + ": " + xenoName;
            }
            else
            {
                infoLine = "Race".Translate() + ": " + raceName;
            }
            Widgets.Label(new Rect(inRect.x, headerBar.yMax + 26f, inRect.width, 20f), infoLine);

            // Total equipment cost (read-only — Upgrade pays cost diff on equip)
            float totalCost = current != null ? (float)current.getTotalCost : 0f;
            Widgets.Label(new Rect(inRect.x, headerBar.yMax + 48f, inRect.width, 20f),
                "FCTotalEquipmentCostLabel".Translate() + totalCost.ToString("F0"));

            // Layout: portrait + slots on the left, apparel list on the right
            float topY = inRect.y + 105f;
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

            // Reset: clear ownedLoadout so BlueprintLoadout falls back to the squad
            // template (loadout). Pawn equipment is untouched — Upgrade does the sync.
            bool canReset = merc.ownedLoadout != null && merc.loadout != null;
            Color colorBefore = GUI.color;
            if (!canReset) GUI.color = Color.gray;
            Rect resetRect = new Rect(bx, bottomRect.y, wideW, bottomRect.height);
            if (Widgets.ButtonText(resetRect, "FCDialogPawnLoadoutResetToPool".Translate(), true, true, canReset))
            {
                merc.ownedLoadout = null;
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

            float slotsY = portraitRect.yMax + gap + 18f;
            float slotsTotalW = slotSize * 2 + 16f;
            float slotsX = rect.x + (rect.width - slotsTotalW) / 2f;
            Rect animalSlot = new Rect(slotsX, slotsY, slotSize, slotSize);
            Rect weaponSlot = new Rect(slotsX + slotSize + 16f, slotsY, slotSize, slotSize);

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(animalSlot.x - 15f, animalSlot.y - 18f, animalSlot.width + 30f, 18f), "fcLabelAnimal".Translate());
            Widgets.Label(new Rect(weaponSlot.x - 15f, weaponSlot.y - 18f, weaponSlot.width + 30f, 18f), "fcLabelWeapon".Translate());
            Widgets.DrawMenuSection(animalSlot);
            Widgets.DrawMenuSection(weaponSlot);
            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;

            // Display the target (assigned) loadout — what the player is editing.
            MilUnitFC current = merc.BlueprintLoadout;
            // Use non-interactive draws so ButtonInvisible below handles all clicks.
            if (current?.HasWeapon == true)
                Widgets.DrawTextureFitted(weaponSlot, current.weapons[0].thing.uiIcon, 1f);
            if (current?.animal != null)
                Widgets.DrawTextureFitted(animalSlot, current.animal.race.uiIcon, 1f);

            if (Widgets.ButtonInvisible(weaponSlot))
            {
                OpenWeaponPicker();
            }
            if (Widgets.ButtonInvisible(animalSlot))
            {
                OpenAnimalPicker();
            }
        }

        // --- Right panel: apparel list (shared widget) ---

        private void DrawApparelPanel(Rect rect)
        {
            ApparelListWidget.Draw(rect, merc.BlueprintLoadout, ref apparelScroll, new ApparelListWidget.Options
            {
                canEdit = true,
                showHeaderButtons = true,
                getEditTarget = EnsureOwnedLoadout,
            });
        }

        // --- Pickers ---

        /* All pickers defer EnsureOwnedLoadout into their confirm callbacks so that
         * opening and cancelling does not break the template association. Display
         * filters read from BlueprintLoadout (which falls back to the squad template). */

        private void OpenWeaponPicker()
        {
            MilUnitFC source = merc.BlueprintLoadout;
            ThingDef raceDef = source?.pawnKind?.race;
            List<ThingDef> weaponDefs = DefDatabase<ThingDef>.AllDefs
                .Where(t => t.IsWeapon && t.BaseMarketValue != 0
                    && !CraftUtil.WeaponBlockedForMercs(t)
                    && t.generateAllowChance > 0f
                    && CraftUtil.CanCraftItem(t)
                    && HARUtil.CanRaceUseWeapon(raceDef, t))
                .OrderBy(t => t.label)
                .ToList();

            SavedThing? currentWeapon = source?.HasWeapon == true ? source.weapons[0] : (SavedThing?)null;
            Find.WindowStack.Add(new FCWindow_ItemStuffPicker(
                weaponDefs,
                onConfirm: (item, stuff) =>
                {
                    MilUnitFC target = EnsureOwnedLoadout();
                    if (target != null) target.SetWeapon(item, stuff);
                },
                onUnequip: () =>
                {
                    MilUnitFC target = EnsureOwnedLoadout();
                    if (target != null) target.ClearWeapon();
                },
                titleKey: "fcPickWeapon",
                initialItem: currentWeapon?.thing,
                initialStuff: currentWeapon?.stuff
            ));
        }

        private void OpenAnimalPicker()
        {
            MilUnitFC source = merc.BlueprintLoadout;
            Find.WindowStack.Add(new FCWindow_AnimalPicker(
                initialAnimal: source?.animal,
                onConfirm: picked =>
                {
                    MilUnitFC target = EnsureOwnedLoadout();
                    if (target is null) return;
                    target.animal = picked;
                    target.ChangeTick();
                },
                onUnequip: () =>
                {
                    MilUnitFC target = EnsureOwnedLoadout();
                    if (target is null) return;
                    target.animal = null;
                    target.ChangeTick();
                }
            ));
        }

        // --- Pick from pool ---

        private void OpenPickFromPoolMenu()
        {
            MilitaryCustomizationUtil util = FactionCache.FactionComp?.militaryCustomizationUtil;
            if (util?.units is null) return;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (MilUnitFC unit in util.units)
            {
                MilUnitFC captured = unit;
                options.Add(new FloatMenuOption(captured.name, delegate
                {
                    // Personalize this merc to use the picked pool unit's gear as their
                    // assigned loadout. Squad ref (loadout) and equipped state
                    // (currentLoadout) are untouched — Upgrade does the equipment sync.
                    merc.ownedLoadout = captured.Clone();
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("FCNoUnitAvailable".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
