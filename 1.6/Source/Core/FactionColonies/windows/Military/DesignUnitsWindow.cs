using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public class DesignUnitsWindow : MilitaryWindow
    {
        public override MilitaryWindowSlot Slot => MilitaryWindowSlot.Units;

        protected readonly MilitaryCustomizationUtil util;
        protected readonly FactionFC faction;
        protected MilUnitFC selectedUnit;

        private Vector2 unitListScrollPos;
        private string unitSearchTerm = "";
        private Vector2 apparelListScrollPos;
        private bool isSelectedUnitDeployed;
        private string selectedUnitDeployReason = "";

        // Layout sizing constants
        private const float SidebarWidth = 250f;
        private const float GearWidth = 310f;
        private const float RowHeight = 30f;
        private const float SearchBarHeight = 28f;
        private const float IconSize = 24f;
        private const float margin = 5f;
        private const float ButtonHeight = 30f;

        public DesignUnitsWindow(MilitaryCustomizationUtil util, FactionFC faction)
        {
            this.util = util;
            this.faction = faction;

            selectedText = "Select A Unit";

            util.CheckMilitaryUtilForErrors();
        }

        public override void Select(IExposable selecting)
        {
            MilUnitFC unit = (MilUnitFC)selecting;
            selectedUnit = unit;
            selectedText = unit.name;
        }

        public override void DrawTab(Rect rect)
        {
            isSelectedUnitDeployed = selectedUnit != null
                && IsUnitDeployed(selectedUnit, out selectedUnitDeployReason);

            Widgets.DrawLineHorizontal(rect.x, rect.y + 45, rect.width);

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            float contentTop = rect.y + 45f + margin;
            float rightEdge = rect.xMax - margin;

            // Layout Y metrics
            float belowHighlight = contentTop + 35f + margin;
            float gearTop = belowHighlight + 61f + margin;
            float gearBottom = gearTop + 305f;
            float contentBottom = rect.yMax - margin;

            // Left sidebar: search + unit list + action buttons
            Rect sidebarRect = new Rect(rect.x + margin, contentTop,
                SidebarWidth, contentBottom - contentTop);
            DrawSidebar(sidebarRect);

            // Content area starts after sidebar + gap
            float contentLeft = sidebarRect.xMax + 10f;
            Rect gearRect = new Rect(contentLeft, gearTop, GearWidth, 305f);

            if (selectedUnit != null)
            {
                Rect headerRect = new Rect(contentLeft, contentTop,
                    rightEdge - contentLeft, 85f);
                DrawUnitHeader(headerRect);

                Rect buttonsRect = new Rect(contentLeft + GearWidth + 10f, belowHighlight,
                    rightEdge - contentLeft - GearWidth - 10f, 61f);
                DrawActionButtons(buttonsRect);

                DrawGearPanel(gearRect);

                Rect apparelRect = new Rect(gearRect.xMax + 10f, gearRect.y,
                    rightEdge - gearRect.xMax - 10f, gearRect.height);
                DrawApparelList(apparelRect, selectedUnit);
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Sidebar ---

        private void DrawSidebar(Rect rect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Search bar
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect searchRect = new Rect(rect.x, rect.y, rect.width, SearchBarHeight);
            unitSearchTerm = Widgets.TextField(searchRect, unitSearchTerm);

            // Unit list (fills space between search bar and buttons)
            float buttonsHeight = ButtonHeight * 2 + margin;
            float listHeight = rect.yMax - searchRect.yMax - margin - buttonsHeight - margin;
            Rect listOutRect = new Rect(rect.x, searchRect.yMax + margin, rect.width, listHeight);
            Widgets.DrawMenuSection(listOutRect);

            List<MilUnitFC> filteredUnits = string.IsNullOrEmpty(unitSearchTerm)
                ? util.units
                : util.units.Where(u => (u.name ?? "").IndexOf(unitSearchTerm, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            float viewHeight = filteredUnits.Count * RowHeight;
            Rect scrollViewRect = ScrollUtil.BeginScrollView(listOutRect, ref unitListScrollPos, viewHeight);

            for (int i = 0; i < filteredUnits.Count; i++)
            {
                MilUnitFC unit = filteredUnits[i];
                Rect row = new Rect(scrollViewRect.x, scrollViewRect.y + i * RowHeight, scrollViewRect.width, RowHeight);

                bool isDeployed = IsUnitDeployed(unit, out string deployReason);

                if (unit == selectedUnit)
                    Widgets.DrawHighlightSelected(row);
                else if (i % 2 == 0)
                    Widgets.DrawHighlight(row);

                Color colorBefore = GUI.color;
                if (isDeployed) GUI.color = Color.gray;

                // Weapon icon
                Rect iconRect = new Rect(row.x + 2f, row.y + 3f, IconSize, IconSize);
                if (unit.HasWeapon)
                    Widgets.DefIcon(iconRect, unit.weapons[0].thing, unit.weapons[0].stuff);
                // Name label
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Rect labelRect = new Rect(iconRect.xMax + 4f, row.y, row.xMax - iconRect.xMax - 6f, RowHeight);
                Widgets.Label(labelRect, unit.name);

                if (isDeployed) GUI.color = colorBefore;

                if (Widgets.ButtonInvisible(row))
                {
                    selectedUnit = unit;
                    selectedText = unit.name;
                }
            }

            ScrollUtil.EndScrollView();

            // Action buttons (2x2 grid)
            float btnY = listOutRect.yMax + margin;
            float buttonW = (rect.width - margin) / 2f;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            Rect createBtn = new Rect(rect.x, btnY, buttonW, ButtonHeight);
            Rect importBtn = new Rect(rect.x + buttonW + margin, btnY, buttonW, ButtonHeight);
            Rect deleteBtn = new Rect(rect.x, btnY + ButtonHeight + margin, buttonW, ButtonHeight);
            Rect exportBtn = new Rect(rect.x + buttonW + margin, btnY + ButtonHeight + margin, buttonW, ButtonHeight);

            if (Widgets.ButtonText(createBtn, "FCCreateNewUnit".Translate()))
            {
                MilUnitFC newUnit = MilTemplateFactory.CreateUnit(false);
                newUnit.name = $"New Unit {util.units.Count + 1}";
                selectedText = newUnit.name;
                selectedUnit = newUnit;
                util.units.Add(newUnit);
            }

            if (Widgets.ButtonText(importBtn, "FCImportUnit".Translate()))
            {
                Find.WindowStack.Add(new Dialog_ManageUnitExportsFC(
                    FactionColoniesMilitary.SavedUnits.ToList()));
            }

            if (selectedUnit != null)
            {
                if (Widgets.ButtonText(deleteBtn, "FCDeleteUnitButton".Translate()))
                {
                    MilUnitFC unitToDelete = selectedUnit;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "FCConfirmDeleteUnit".Translate((NamedArgument)unitToDelete.name),
                        delegate
                        {
                            unitToDelete.RemoveUnit();
                            util.CheckMilitaryUtilForErrors();
                            if (selectedUnit == unitToDelete)
                            {
                                selectedUnit = null;
                                selectedText = "FCSelectAUnitButton".Translate();
                            }
                        }));
                }

                if (Widgets.ButtonText(exportBtn, "FCExportUnitButton".Translate()))
                {
                    FactionColoniesMilitary.SaveUnit(selectedUnit.ToSavedUnit());
                    Messages.Message("FCExportUnit".Translate(), MessageTypeDefOf.TaskCompletion);
                }
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Unit Header ---

        private void DrawUnitHeader(Rect rect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Highlight banner behind unit name
            Rect highlightBar = new Rect(rect.x, rect.y, rect.width, 35f);
            Widgets.DrawHighlight(highlightBar);

            // Unit name (large label)
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect nameRect = new Rect(rect.x + margin, rect.y, 400f, 30f);
            Widgets.Label(nameRect, selectedUnit.name);

            // Pencil icon to trigger rename
            float nameTextWidth = Text.CalcSize(selectedUnit.name).x;
            Rect pencilRect = new Rect(rect.x + Mathf.Min(nameTextWidth + 8f + margin, rect.width - 22f), rect.y + 4f, 22f, 22f);
            if (!isSelectedUnitDeployed && Widgets.ButtonImage(pencilRect, TexButton.Rename))
            {
                Find.WindowStack.Add(new FCWindow_Rename(selectedUnit.name, "FCRenameUnit", name => selectedUnit.name = name));
            }

            // Race / Xeno info line
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect infoRect = new Rect(rect.x, highlightBar.yMax + margin, rect.width, 20f);
            string raceName = selectedUnit.pawnKind?.race?.label?.CapitalizeFirst() ?? "Unknown";
            if (ModsConfig.BiotechActive)
            {
                string xenoName = selectedUnit.GetXenotypeLabel();
                Widgets.Label(infoRect, "Race".Translate() + ": " + raceName + "   ·   " + "Xenotype".Translate() + ": " + xenoName);
            }
            else
            {
                Widgets.Label(infoRect, "Race".Translate() + ": " + raceName);
            }

            // Equipment cost
            float totalCost = (float)selectedUnit.getTotalCost;
            Rect costRect = new Rect(rect.x, infoRect.yMax + margin, rect.width, 20f);
            Widgets.Label(costRect, "FCTotalEquipmentCostLabel".Translate() + totalCost.ToString("F0"));

            if (isSelectedUnitDeployed)
            {
                Color colorBefore = GUI.color;
                GUI.color = Color.yellow;
                Rect viewOnlyRect = new Rect(rect.x, costRect.yMax + 2f, rect.width, 23f);
                Widgets.Label(viewOnlyRect, "FCCantBeModified".Translate(selectedUnit.name, selectedUnitDeployReason));
                GUI.color = colorBefore;
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Action Buttons ---

        private void DrawActionButtons(Rect rect)
        {
            float btnH = 28f;
            float gap = 5f;
            float btnW = (rect.width - gap) / 2f;
            bool canEdit = !isSelectedUnitDeployed;
            /* If the unit can't be edited, then don't even render the action buttons. */
            if (!canEdit) return;

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            float raceButtonWidth = ModsConfig.BiotechActive ? btnW : (2 * btnW) + gap;

            if (Widgets.ButtonText(new Rect(rect.x, rect.y, raceButtonWidth, btnH), "FCChangeUnitRaceButton".Translate(), true, true))
            {
                Find.WindowStack.Add(new FCWindow_RacePicker(selectedUnit, faction));
            }

            if (ModsConfig.BiotechActive &&
                Widgets.ButtonText(new Rect(rect.x + btnW + gap, rect.y, btnW, btnH), "FCChangeUnitXenoButton".Translate(), true, true))
            {
                Find.WindowStack.Add(new FCWindow_XenoPicker(selectedUnit));
            }

            float y2 = rect.y + btnH + gap;

            if (Widgets.ButtonText(new Rect(rect.x, y2, btnW, btnH), "FCRollANewUnitButton".Translate(), true, true))
            {
                selectedUnit.RerollPreviewPawn();
            }

            if (Widgets.ButtonText(new Rect(rect.x + btnW + gap, y2, btnW, btnH), "FCResetUnitToDefaultButton".Translate(), true, true))
            {
                selectedUnit.ClearAllEquipment();
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Deployment Check ---

        private bool IsUnitDeployed(MilUnitFC unit, out string reason)
        {
            reason = "";
            FactionFC factionFC = FactionCache.FactionComp;
            List<MilSquadFC> squadsContainingUnit = factionFC?.militaryCustomizationUtil?.squads
                ?.Where(squad => squad?.Units != null && squad.Units.Contains(unit)).ToList();

            if (squadsContainingUnit == null || squadsContainingUnit.Count == 0) return false;

            List<WorldSettlementFC> settlementsContainingSquad = factionFC?.settlements
                ?.FindAll(settlement => settlement?.MilitaryComp?.militarySquad?.outfit != null &&
                    squadsContainingUnit.Any(squad => settlement.MilitaryComp.militarySquad.outfit == squad));

            if (settlementsContainingSquad == null || settlementsContainingSquad.Count == 0) return false;

#pragma warning disable 0618 // legacy isDeployed — gating unit-edit warnings by on-map presence
            if (settlementsContainingSquad.Any(s => s.MilitaryComp.militarySquad.isDeployed))
#pragma warning restore 0618
            {
                reason = "FCReasonDeployed".Translate();
                return true;
            }

            if (settlementsContainingSquad.Any(s => s.MilitaryComp.isUnderAttack
                && s.MilitaryComp.defenderForce?.homeSettlement is object
                && settlementsContainingSquad.Contains(s.MilitaryComp.defenderForce.homeSettlement)))
            {
                reason = "FCReasonDefending".Translate();
                return true;
            }

            return false;
        }

        // --- Gear Panel ---

        private void DrawGearPanel(Rect gearArea)
        {
            const float pawnWidth = 100f;
            const float pawnHeight = 130f;
            const float slotSize = 50f;
            const float slotGap = 15f;

            // Pawn preview centered near the top of the gear area
            Rect unitIcon = new Rect(
                gearArea.x + (gearArea.width - pawnWidth) / 2f,
                gearArea.y + 5f,
                pawnWidth, pawnHeight);

            // Weapon and animal slots below the pawn preview, side by side
            float slotsY = unitIcon.yMax + slotGap + 15f; // +15 for label above
            float slotsWidth = slotSize * 2 + 20f;
            float slotsStartX = gearArea.x + (gearArea.width - slotsWidth) / 2f;

            Rect AnimalCompanion = new Rect(slotsStartX, slotsY, slotSize, slotSize);
            Rect EquipmentWeapon = new Rect(slotsStartX + slotSize + 20f, slotsY, slotSize, slotSize);

            // CE ammo slot below weapon
            Rect AmmoSlot = new Rect(EquipmentWeapon.x, EquipmentWeapon.yMax + 20f, slotSize, slotSize);
            bool showAmmoSlot = CombatExtendedUtil.IsCELoaded
                && selectedUnit != null
                && selectedUnit.HasWeapon
                && CombatExtendedUtil.GetAmmoOptionsForWeapon(selectedUnit.weapons[0].thing).Count > 0;

            // --- Always drawn: slot backgrounds and labels ---
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;

            Widgets.Label(new Rect(AnimalCompanion.x, AnimalCompanion.y - 15f, AnimalCompanion.width, 18f), "fcLabelAnimal".Translate());
            Widgets.DrawMenuSection(AnimalCompanion);
            Widgets.Label(new Rect(EquipmentWeapon.x, EquipmentWeapon.y - 15f, EquipmentWeapon.width, 18f), "fcLabelWeapon".Translate());
            Widgets.DrawMenuSection(EquipmentWeapon);

            if (showAmmoSlot)
            {
                Widgets.Label(new Rect(AmmoSlot.x, AmmoSlot.y - 15f, AmmoSlot.width, 15f), "fcLabelAmmo".Translate());
                Widgets.DrawMenuSection(AmmoSlot);
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;

            // --- Unit-selected content ---
            if (selectedUnit == null) return;

            // Draw Pawn Preview
            Pawn preview = selectedUnit.PreviewPawn;
            if (preview != null)
            {
                UIUtil.DrawPawnPortrait(unitIcon, preview, 1.2f);
            }

            // --- Animal Companion Slot ---
            if (!isSelectedUnitDeployed && Widgets.ButtonInvisible(AnimalCompanion))
            {
                Find.WindowStack.Add(new FCWindow_AnimalPicker(selectedUnit));
            }

            // --- Weapon Slot ---
            if (!isSelectedUnitDeployed && Widgets.ButtonInvisible(EquipmentWeapon))
            {
                List<ThingDef> weaponDefs = DefDatabase<ThingDef>.AllDefs
                    .Where(t => t.IsWeapon && t.BaseMarketValue != 0
                        && !CraftUtil.WeaponBlockedForMercs(t)
                        && t.generateAllowChance > 0f // blocks unique weapons
                        && CraftUtil.CanCraftItem(t)
                        && HARUtil.CanRaceUseWeapon(selectedUnit.pawnKind?.race, t))
                    .OrderBy(t => t.label)
                    .ToList();

                SavedThing? currentWeapon = selectedUnit.HasWeapon ? selectedUnit.weapons[0] : (SavedThing?)null;
                Find.WindowStack.Add(new FCWindow_ItemStuffPicker(
                    weaponDefs,
                    onConfirm: (item, stuff) => selectedUnit.SetWeapon(item, stuff),
                    onUnequip: () => selectedUnit.ClearWeapon(),
                    titleKey: "fcPickWeapon",
                    initialItem: currentWeapon?.thing,
                    initialStuff: currentWeapon?.stuff
                ));
            }

            // --- CE Ammo Slot ---
            if (showAmmoSlot)
            {
                if (!isSelectedUnitDeployed && Widgets.ButtonInvisible(AmmoSlot))
                {
                    var ammoOptions = CombatExtendedUtil.GetAmmoOptionsForWeapon(selectedUnit.weapons[0].thing);
                    var menuOptions = new List<FloatMenuOption>
                    {
                        new FloatMenuOption("fcAmmoAny".Translate(), () => selectedUnit.ClearPreferredAmmo())
                    };
                    foreach (ThingDef ammo in ammoOptions)
                    {
                        ThingDef captured = ammo;
                        menuOptions.Add(new FloatMenuOption(
                            captured.LabelCap,
                            () => selectedUnit.SetPreferredAmmo(captured),
                            captured.uiIcon,
                            Color.white));
                    }
                    Find.WindowStack.Add(new FloatMenu(menuOptions));
                }

                if (selectedUnit.preferredAmmo != null)
                {
                    GUI.DrawTexture(AmmoSlot, selectedUnit.preferredAmmo.uiIcon);
                }
                else
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(AmmoSlot, "fcAmmoAny".Translate());
                    Text.Font = fontBefore;
                    Text.Anchor = anchorBefore;
                }
            }

            // Animal icon
            if (selectedUnit.animal != null)
            {
                Widgets.ButtonImage(AnimalCompanion, selectedUnit.animal.race.uiIcon);
            }

            // Weapon icon
            if (selectedUnit.HasWeapon)
            {
                Widgets.ButtonImage(EquipmentWeapon, selectedUnit.weapons[0].thing.uiIcon);
            }
        }

        // --- Apparel List ---

        private void DrawApparelList(Rect rect, MilUnitFC unit)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            const float headerHeight = 25f;
            const float apparelRowHeight = 28f;
            const float removeButtonSize = 20f;

            // Header: "Equipped Apparel" label + "+ Add" button
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, headerHeight);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(headerRect, "fcEquippedApparel".Translate());
            float btnY = rect.y + headerHeight + 3f;
            float btnW = (rect.width - 4f) / 3f;

            if (!isSelectedUnitDeployed)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;

                float btnX = rect.xMax;

                btnX -= btnW;
                Rect addBtnRect = new Rect(btnX, btnY, btnW, headerHeight);
                if (Widgets.ButtonText(addBtnRect, "fcAddApparel".Translate()))
                {
                    OpenApparelPicker(unit);
                }

                if (unit.apparel.Any(a => a.thing != null))
                {
                    btnX -= btnW + 2f;
                    Rect setAllBtn = new Rect(btnX, btnY, btnW, headerHeight);
                    if (Widgets.ButtonText(setAllBtn, "fcSetAllColors".Translate()))
                    {
                        Color current = FactionCache.FactionComp?.hasFactionColor == true
                            ? FactionCache.FactionComp.factionColorPrimary : Color.white;
                        OpenColorPicker(current, delegate(Color c) { unit.SetAllApparelColors(c); });
                    }

                    if (unit.apparel.Any(a => a.hasColor))
                    {
                        btnX -= btnW + 2f;
                        Rect clearBtn = new Rect(btnX, btnY, btnW, headerHeight);
                        if (Widgets.ButtonText(clearBtn, "fcClearColors".Translate()))
                        {
                            unit.ClearAllApparelColors();
                        }
                    }
                }
            }

            // Scrollable apparel list
            Rect listOutRect = new Rect(rect.x, btnY + headerHeight + 2f, rect.width, rect.height - (2 * (headerHeight + 2f)));

            // Sort apparel: outermost layer first, then alphabetical
            List<SavedThing> sortedApparel = unit.apparel
                .Where(a => a.thing != null)
                .OrderByDescending(a => a.thing.apparel.layers.Max(l => l.drawOrder))
                .ThenBy(a => a.thing.label)
                .ToList();

            float viewHeight = sortedApparel.Count * apparelRowHeight;
            Rect scrollViewRect = ScrollUtil.BeginScrollView(listOutRect, ref apparelListScrollPos, viewHeight);

            for (int i = 0; i < sortedApparel.Count; i++)
            {
                SavedThing item = sortedApparel[i];
                Rect row = new Rect(scrollViewRect.x, scrollViewRect.y + i * apparelRowHeight, scrollViewRect.width, apparelRowHeight);

                if (i % 2 == 0) Widgets.DrawHighlight(row);

                // Icon
                Rect iconRect = new Rect(row.x + 2f, row.y + 2f, IconSize, IconSize);
                Widgets.ThingIcon(iconRect, item.thing, item.stuff);

                // Info card button
                const float infoBtnSize = 24f;
                Rect infoRect = new Rect(iconRect.xMax + 2f, row.y + (apparelRowHeight - infoBtnSize) / 2f, infoBtnSize, infoBtnSize);
                Widgets.InfoCardButton(infoRect.x, infoRect.y, item.thing, item.stuff);

                // Remove button (right side)
                float costWidth = 55f;
                const float swatchSize = 16f;
                Rect removeRect = Rect.zero;
                if (!isSelectedUnitDeployed)
                {
                    removeRect = new Rect(row.xMax - removeButtonSize - 2f, row.y + (apparelRowHeight - removeButtonSize) / 2f, removeButtonSize, removeButtonSize);
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    if (Widgets.ButtonText(removeRect, "X"))
                    {
                        unit.RemoveApparel(item.thing);
                    }
                }

                // Color swatch
                float swatchRightEdge = isSelectedUnitDeployed ? row.xMax - 4f : removeRect.x - 4f;
                Rect swatchRect = new Rect(swatchRightEdge - swatchSize, row.y + (apparelRowHeight - swatchSize) / 2f, swatchSize, swatchSize);
                FactionFC factionComp = FactionCache.FactionComp;
                Color resolvedColor = factionComp != null ? factionComp.ResolveApparelColor(item) : Color.white;
                Color outlineColor = item.hasColor ? Color.white : new Color(0.5f, 0.5f, 0.5f);
                Widgets.DrawBoxSolidWithOutline(swatchRect, resolvedColor, outlineColor);
                if (!isSelectedUnitDeployed && Widgets.ButtonInvisible(swatchRect))
                {
                    ThingDef capturedDef = item.thing;
                    OpenColorPicker(resolvedColor, delegate(Color c) { unit.SetApparelColor(capturedDef, c); });
                }

                // Cost
                Rect costRect = new Rect(swatchRect.x - costWidth - 2f, row.y, costWidth, apparelRowHeight);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(costRect, "$" + item.MarketValue.ToString("F0"));

                // Label
                Rect labelRect = new Rect(infoRect.xMax + 4f, row.y, costRect.x - infoRect.xMax - 8f, apparelRowHeight);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                string label = item.stuff != null
                    ? (string)(item.thing.LabelCap + " (" + item.stuff.LabelCap + ")")
                    : item.thing.LabelCap.ToString();
                Widgets.Label(labelRect, label);

                // Click row to open replace picker
                if (!isSelectedUnitDeployed)
                {
                    Rect clickRect = new Rect(row.x, row.y, costRect.x - row.x, apparelRowHeight);
                    if (Widgets.ButtonInvisible(clickRect))
                    {
                        OpenApparelReplacePicker(unit, item);
                    }
                }
            }

            ScrollUtil.EndScrollView();

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Apparel Picker ---

        private void OpenApparelPicker(MilUnitFC unit)
        {
            BodyDef body = unit.pawnKind?.race?.race?.body ?? BodyDefOf.Human;

            List<ThingDef> apparelDefs = DefDatabase<ThingDef>.AllDefs
                .Where(t => t.IsApparel
                    && t.apparel.PawnCanWear(Gender.None, DevelopmentalStage.Adult)
                    && CraftUtil.CanCraftItem(t)
                    && HARUtil.CanRaceWearApparel(unit.pawnKind?.race, t)
                    && !unit.apparel.Any(a => a.thing == t))
                .OrderBy(t => t.label)
                .ToList();

            Find.WindowStack.Add(new FCWindow_ItemStuffPicker(
                apparelDefs,
                onConfirm: (item, stuff) => unit.SetApparel(item, stuff),
                titleKey: "fcPickApparel",
                conflictTooltipFunc: t => GetConflictTooltip(unit.apparel, t, body)
            ));
        }

        private void OpenApparelReplacePicker(MilUnitFC unit, SavedThing current)
        {
            BodyDef body = unit.pawnKind?.race?.race?.body ?? BodyDefOf.Human;

            List<ThingDef> apparelDefs = DefDatabase<ThingDef>.AllDefs
                .Where(t => t.IsApparel
                    && t.apparel.PawnCanWear(Gender.None, DevelopmentalStage.Adult)
                    && CraftUtil.CanCraftItem(t)
                    && HARUtil.CanRaceWearApparel(unit.pawnKind?.race, t))
                .OrderBy(t => t.label)
                .ToList();

            List<SavedThing> otherApparel = unit.apparel.Where(a => a.thing != current.thing).ToList();
            Find.WindowStack.Add(new FCWindow_ItemStuffPicker(
                apparelDefs,
                onConfirm: (item, stuff) => unit.SetApparel(item, stuff),
                onUnequip: () => unit.RemoveApparel(current.thing),
                titleKey: "fcPickApparel",
                initialItem: current.thing,
                initialStuff: current.stuff,
                conflictTooltipFunc: t => GetConflictTooltip(otherApparel, t, body)
            ));
        }

        // --- Color Picker ---

        private void OpenColorPicker(Color current, Action<Color> onApply)
        {
            Find.WindowStack.Add(new FCWindow_ColorPicker(
                "fcChooseApparelColor".Translate(),
                current,
                onApply
            ));
        }

        /// <summary>
        /// Returns a tooltip listing which worn apparel would be replaced by the candidate, or null if compatible.
        /// </summary>
        private static string GetConflictTooltip(List<SavedThing> worn, ThingDef candidate, BodyDef body)
        {
            List<string> conflicts = worn
                .Where(a => a.thing != null && !ApparelUtility.CanWearTogether(a.thing, candidate, body))
                .Select(a => a.thing.LabelCap.ToString())
                .ToList();
            if (conflicts.Count == 0) return null;
            return "fcReplacesApparel".Translate() + ":\n" + string.Join("\n", conflicts.ToArray());
        }
    }
}
