using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public class DesignSquadsWindow : MilitaryWindow
    {
        public override MilitaryWindowSlot Slot => MilitaryWindowSlot.Squads;

        private WorldSettlementFC settlementPointReference;
        protected readonly MilitaryCustomizationUtil util;
        protected MilSquadFC selectedSquad;

        private Vector2 squadListScrollPos;
        private string squadSearchTerm = "";
        private Vector2 unitListScrollPos;
        private bool isSelectedSquadDeployed;
        private string selectedSquadDeployReason = "";

        // Layout constants (matching DesignUnitsWindow)
        private const float SidebarWidth = 250f;
        private const float RowHeight = 30f;
        private const float SearchBarHeight = 28f;
        private const float IconSize = 24f;
        private const float margin = 5f;
        private const float ButtonHeight = 30f;
        private const float UnitRowHeight = 50f;

        public DesignSquadsWindow(MilitaryCustomizationUtil util)
        {
            this.util = util;
            selectedText = "FCSelectASquad".Translate();

            if (util.blankUnit is null)
            {
                util.blankUnit = MilTemplateFactory.CreateUnit(true);
            }

            util.CheckMilitaryUtilForErrors();
        }

        public override void Select(IExposable selecting)
        {
            MilSquadFC squad = (MilSquadFC)selecting;
            selectedSquad = squad;
            selectedText = squad.name;
        }

        public override void DrawTab(Rect rect)
        {
            isSelectedSquadDeployed = selectedSquad != null
                && IsSquadDeployed(selectedSquad, out selectedSquadDeployReason);

            Widgets.DrawLineHorizontal(rect.x, rect.y + 45, rect.width);

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            float contentTop = rect.y + 45f + margin;
            float contentBottom = rect.yMax - margin;
            float rightEdge = rect.xMax - margin;

            // Left sidebar
            Rect sidebarRect = new Rect(rect.x + margin, contentTop,
                SidebarWidth, contentBottom - contentTop);
            DrawSidebar(sidebarRect);

            // Content area (right of sidebar)
            float contentLeft = sidebarRect.xMax + 10f;
            float contentWidth = rightEdge - contentLeft;

            if (selectedSquad != null)
            {
                // Header
                float headerHeight = 60f + (isSelectedSquadDeployed ? 22f : 0f);
                Rect headerRect = new Rect(contentLeft, contentTop, contentWidth, headerHeight);
                DrawSquadHeader(headerRect);

                // Bottom bar
                float bottomBarHeight = ButtonHeight;
                Rect bottomRect = new Rect(contentLeft, contentBottom - bottomBarHeight,
                    contentWidth, bottomBarHeight);
                DrawBottomBar(bottomRect);

                // Unit list (between header and bottom bar)
                float unitListTop = headerRect.yMax + margin;
                float unitListBottom = bottomRect.y - margin;
                Rect unitListRect = new Rect(contentLeft, unitListTop,
                    contentWidth, unitListBottom - unitListTop);
                DrawUnitList(unitListRect);
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
            squadSearchTerm = Widgets.TextField(searchRect, squadSearchTerm);

            // Squad list (fills space between search bar and buttons)
            float buttonsHeight = ButtonHeight * 2 + margin;
            float listHeight = rect.yMax - searchRect.yMax - margin - buttonsHeight - margin;
            Rect listOutRect = new Rect(rect.x, searchRect.yMax + margin, rect.width, listHeight);
            Widgets.DrawMenuSection(listOutRect);

            List<MilSquadFC> filteredSquads = string.IsNullOrEmpty(squadSearchTerm)
                ? util.squads ?? new List<MilSquadFC>()
                : (util.squads ?? new List<MilSquadFC>())
                    .Where(s => (s.name ?? "").IndexOf(squadSearchTerm, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

            float viewHeight = filteredSquads.Count * RowHeight;
            Rect scrollViewRect = ScrollUtil.BeginScrollView(listOutRect, ref squadListScrollPos, viewHeight);

            for (int i = 0; i < filteredSquads.Count; i++)
            {
                MilSquadFC squad = filteredSquads[i];
                Rect row = new Rect(scrollViewRect.x, scrollViewRect.y + i * RowHeight,
                    scrollViewRect.width, RowHeight);

                bool isDeployed = IsSquadDeployed(squad, out _);

                if (squad == selectedSquad)
                    Widgets.DrawHighlightSelected(row);
                else if (i % 2 == 0)
                    Widgets.DrawHighlight(row);

                Color colorBefore = GUI.color;
                if (isDeployed) GUI.color = Color.gray;

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Rect labelRect = new Rect(row.x + 4f, row.y, row.width - 6f, RowHeight);
                Widgets.Label(labelRect, squad.name);

                GUI.color = colorBefore;

                if (Widgets.ButtonInvisible(row))
                {
                    selectedSquad = squad;
                    selectedText = squad.name;
                    selectedSquad.UpdateEquipmentTotalCost();
                }
            }

            ScrollUtil.EndScrollView();

            // CRUD buttons (2x2 grid)
            float btnY = listOutRect.yMax + margin;
            float buttonW = (rect.width - margin) / 2f;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            Rect createBtn = new Rect(rect.x, btnY, buttonW, ButtonHeight);
            Rect importBtn = new Rect(rect.x + buttonW + margin, btnY, buttonW, ButtonHeight);
            Rect deleteBtn = new Rect(rect.x, btnY + ButtonHeight + margin, buttonW, ButtonHeight);
            Rect exportBtn = new Rect(rect.x + buttonW + margin, btnY + ButtonHeight + margin,
                buttonW, ButtonHeight);

            if (Widgets.ButtonText(createBtn, "FCCreateNewSquad".Translate()))
            {
                if (util.squads is null)
                {
                    util.ResetSquads();
                }

                MilSquadFC newSquad = MilTemplateFactory.CreateSquad(true);
                newSquad.name = $"New Squad {(util.squads.Count + 1).ToString()}";
                selectedText = newSquad.name;
                selectedSquad = newSquad;
                selectedSquad.NewSquad();
                util.squads.Add(newSquad);
            }

            if (Widgets.ButtonText(importBtn, "FCImportSquad".Translate()))
            {
                Find.WindowStack.Add(new Dialog_ManageSquadExportsFC(
                    FactionColoniesMilitary.SavedSquads.ToList()));
            }

            if (selectedSquad != null)
            {
                if (Widgets.ButtonText(deleteBtn, "FCDeleteSquadButton".Translate()))
                {
                    MilSquadFC squadToDelete = selectedSquad;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "FCConfirmDeleteSquad".Translate((NamedArgument)squadToDelete.name),
                        delegate
                        {
                            squadToDelete.DeleteSquad();
                            util.CheckMilitaryUtilForErrors();
                            if (selectedSquad == squadToDelete)
                            {
                                selectedSquad = null;
                                selectedText = "FCSelectASquad".Translate();
                            }
                        }));
                }

                if (Widgets.ButtonText(exportBtn, "FCExportSquadButton".Translate()))
                {
                    FactionColoniesMilitary.SaveSquad(selectedSquad.ToSavedSquad());
                    Messages.Message("FCExportSquad".Translate(), MessageTypeDefOf.TaskCompletion);
                }
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Squad Header ---

        private void DrawSquadHeader(Rect rect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Highlight banner
            Rect highlightBar = new Rect(rect.x, rect.y, rect.width, 35f);
            Widgets.DrawHighlight(highlightBar);

            // Squad name
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect nameRect = new Rect(rect.x + margin, rect.y, 400f, 30f);
            Widgets.Label(nameRect, selectedSquad.name);

            // Pencil icon
            float nameTextWidth = Text.CalcSize(selectedSquad.name).x;
            Rect pencilRect = new Rect(
                rect.x + Mathf.Min(nameTextWidth + 8f + margin, rect.width - 22f),
                rect.y + 4f, 22f, 22f);
            if (!isSelectedSquadDeployed && Widgets.ButtonImage(pencilRect, TexButton.Rename))
            {
                Find.WindowStack.Add(new FCWindow_Rename(selectedSquad.name, "FCRenameSquad", name => selectedSquad.name = name));
            }

            // Cost line
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect costRect = new Rect(rect.x, highlightBar.yMax + margin, rect.width, 20f);

            if (settlementPointReference != null)
            {
                Widgets.Label(costRect, "FCTotalSquadEquipmentCost".Translate(
                    selectedSquad.GetEquipmentTotalCost(),
                    MilitaryCustomizationUtil.CalculateSquadBudget(settlementPointReference.settlementMilitaryLevel)));
            }
            else
            {
                Widgets.Label(costRect, "FCTotalSquadEquipmentCostNoRef".Translate(selectedSquad.GetEquipmentTotalCost()));
            }

            if (isSelectedSquadDeployed)
            {
                Color colorBefore = GUI.color;
                GUI.color = Color.yellow;
                Rect viewOnlyRect = new Rect(rect.x, costRect.yMax + 2f, rect.width, 23f);
                Widgets.Label(viewOnlyRect, "FCCantBeModified".Translate(selectedSquad.name, selectedSquadDeployReason));
                GUI.color = colorBefore;
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Unit List ---

        private void DrawUnitList(Rect rect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            Widgets.DrawMenuSection(rect);

            var groups = BuildUnitGroups(selectedSquad);

            float viewHeight = groups.Count * UnitRowHeight;
            Rect scrollViewRect = ScrollUtil.BeginScrollView(rect, ref unitListScrollPos, viewHeight);

            for (int i = 0; i < groups.Count; i++)
            {
                Rect row = new Rect(scrollViewRect.x, scrollViewRect.y + i * UnitRowHeight,
                    scrollViewRect.width, UnitRowHeight);
                DrawUnitRow(row, groups[i].unit, groups[i].count, i);
            }

            ScrollUtil.EndScrollView();

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        private void DrawUnitRow(Rect row, MilUnitFC unit, int count, int index)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            if (index % 2 == 0)
                Widgets.DrawHighlight(row);

            float x = row.x + margin;

            // Pawn preview icon
            Rect pawnRect = new Rect(x, row.y + 2f, UnitRowHeight - 4f, UnitRowHeight - 4f);
            Pawn preview = unit.PreviewPawn;
            if (preview != null)
            {
                UIUtil.DrawPawnPortrait(pawnRect, preview);
            }
            else if (unit.animal != null)
            {
                Widgets.ButtonImage(pawnRect, unit.animal.race.uiIcon);
            }
            x = pawnRect.xMax + 4f;

            // Weapon icon
            if (unit.HasWeapon)
            {
                Rect weaponRect = new Rect(x, row.y + (UnitRowHeight - IconSize) / 2f,
                    IconSize, IconSize);
                Widgets.DefIcon(weaponRect, unit.weapons[0].thing, unit.weapons[0].stuff);
                x = weaponRect.xMax + 4f;
            }

            // Unit name
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect nameRect = new Rect(x, row.y, 130f, UnitRowHeight);
            Widgets.Label(nameRect, unit.name);
            x = nameRect.xMax + 4f;

            // Xenotype
            string xenoLabel = unit.xenotype?.label?.CapitalizeFirst();
            if (!string.IsNullOrEmpty(xenoLabel))
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Rect xenoRect = new Rect(x, row.y, 80f, UnitRowHeight);
                Widgets.Label(xenoRect, xenoLabel);
            }

            // Cost (per-unit and line total) — right-aligned before controls
            float perUnitCost = (float)unit.getTotalCost;
            float lineTotalCost = perUnitCost * count;
            string costText = $"${(int)perUnitCost} ea. / ${(int)lineTotalCost}";

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            Rect costRect = new Rect(row.xMax - 246f, row.y, 100f, UnitRowHeight);
            Widgets.Label(costRect, costText);

            // +/- controls
            float btnSize = 24f;
            float btnY = row.y + (UnitRowHeight - btnSize) / 2f;
            float controlX = row.xMax - 141f;
            bool canEdit = !isSelectedSquadDeployed;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            Color colorBefore2 = GUI.color;
            if (!canEdit) GUI.color = Color.gray;

            // [-] button
            if (Widgets.ButtonText(new Rect(controlX, btnY, btnSize, btnSize), "-", true, true, canEdit))
            {
                DecrementUnit(unit);
            }

            // Count label
            Rect countRect = new Rect(controlX + btnSize + 2f, row.y, 26f, UnitRowHeight);
            Widgets.Label(countRect, count.ToString());

            // [+] button
            if (Widgets.ButtonText(new Rect(countRect.xMax + 2f, btnY, btnSize, btnSize), "+", true, true, canEdit))
            {
                IncrementUnit(unit);
            }

            // [X] remove-all button
            Rect removeRect = new Rect(row.xMax - 54f, btnY, 22f, 22f);
            if (!isSelectedSquadDeployed && Widgets.ButtonImage(removeRect, TexLoad.deleteX))
            {
                RemoveAllOfUnit(unit);
            }

            GUI.color = colorBefore2;

            // Gear icon — open unit in editor
            Rect gearRect = new Rect(row.xMax - 28f, btnY, 22f, 22f);
            TooltipHandler.TipRegion(gearRect, "FCEditUnitTooltip".Translate());
            if (Widgets.ButtonImage(gearRect, TexLoad.iconCustomize))
            {
                OpenUnitEditor(unit);
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        private void OpenUnitEditor(MilUnitFC unit)
        {
            Window currentWindow = Find.WindowStack.Windows
                .FirstOrDefault(w => w is FCWindow_Military);
            currentWindow?.Close();

            FactionFC fc = FactionCache.FactionComp;
            MilitaryWindow duw = MilitaryWindowRegistry.CreateUnits(fc.militaryCustomizationUtil, fc);
            FCWindow_Military newWindow = new FCWindow_Military(
                duw, "FCMilitaryTableButtonCreateUnit".Translate());
            Find.WindowStack.Add(newWindow);
            newWindow.SetActive(unit);
        }

        // --- Bottom Bar ---

        private void DrawBottomBar(Rect rect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            bool canEdit = !isSelectedSquadDeployed;

            float btnW = (rect.width - margin * 3) / 4f;

            // Add Unit button
            Rect addUnitBtn = new Rect(rect.x, rect.y, btnW, ButtonHeight);
            Color colorBefore = GUI.color;
            if (!canEdit) GUI.color = Color.gray;
            if (Widgets.ButtonText(addUnitBtn, "FCAddUnit".Translate(), true, true, canEdit))
            {
                Find.WindowStack.Add(new FCWindow_UnitPicker(util, AddUnitToSquad));
            }
            GUI.color = colorBefore;

            // Set Point Ref button
            Rect pointRefBtn = new Rect(addUnitBtn.xMax + margin, rect.y, btnW, ButtonHeight);
            if (Widgets.ButtonText(pointRefBtn, "FCSetPointRef".Translate()))
            {
                List<FloatMenuOption> settlementList = FactionCache.FactionComp
                    .settlements.Select(settlement => new FloatMenuOption(
                        settlement.Name + "FCMilitaryLevelLabel".Translate() +
                        settlement.settlementMilitaryLevel,
                        delegate
                        {
                            settlementPointReference = settlement;
                        }))
                    .ToList();

                if (!settlementList.Any())
                {
                    settlementList.Add(new FloatMenuOption("FCNoValidSettlements".Translate(), null));
                }

                FloatMenu floatMenu = new FloatMenu(settlementList) { vanishIfMouseDistant = true };
                Find.WindowStack.Add(floatMenu);
            }

            // Reset button
            Rect resetBtn = new Rect(pointRefBtn.xMax + margin, rect.y, btnW, ButtonHeight);
            colorBefore = GUI.color;
            if (!canEdit) GUI.color = Color.gray;
            if (Widgets.ButtonText(resetBtn, "FCResetToDefault".Translate(), true, true, canEdit))
            {
                selectedSquad.NewSquad();
                selectedSquad.UpdateEquipmentTotalCost();
                selectedSquad.ChangeTick();
            }
            GUI.color = colorBefore;

            // Unit count label
            int totalUnits = selectedSquad.Units.Count(u => !u.isBlank);
            Text.Anchor = TextAnchor.MiddleRight;
            Rect countLabel = new Rect(resetBtn.xMax + margin, rect.y, btnW, ButtonHeight);
            Widgets.Label(countLabel, "FCSquadUnitCount".Translate(totalUnits));

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Grouping Logic ---

        private struct UnitGroup
        {
            public MilUnitFC unit;
            public int count;
        }

        private List<UnitGroup> BuildUnitGroups(MilSquadFC squad)
        {
            return squad.Units
                .Where(u => !u.isBlank)
                .GroupBy(u => u)
                .Select(g => new UnitGroup { unit = g.Key, count = g.Count() })
                .ToList();
        }

        // --- Unit Mutation ---

        private void AddUnitToSquad(MilUnitFC unit)
        {
            int blankIndex = selectedSquad.FindUnitIndex(u => u.isBlank);
            if (blankIndex == -1)
            {
                Messages.Message("FCSquadFull".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            selectedSquad.SetUnit(blankIndex, unit);
        }

        private void IncrementUnit(MilUnitFC unit)
        {
            int blankIndex = selectedSquad.FindUnitIndex(u => u.isBlank);
            if (blankIndex == -1)
            {
                Messages.Message("FCSquadFull".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            selectedSquad.SetUnit(blankIndex, unit);
        }

        private void DecrementUnit(MilUnitFC unit)
        {
            int lastIndex = -1;
            for (int i = selectedSquad.Units.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(selectedSquad.Units[i], unit))
                {
                    lastIndex = i;
                    break;
                }
            }
            if (lastIndex == -1) return;

            selectedSquad.SetUnit(lastIndex, util.blankUnit);
        }

        private void RemoveAllOfUnit(MilUnitFC unit)
        {
            for (int i = 0; i < selectedSquad.Units.Count; i++)
            {
                if (ReferenceEquals(selectedSquad.Units[i], unit))
                {
                    selectedSquad.SetUnit(i, util.blankUnit);
                }
            }
        }

        // --- Deployment Check ---

        private bool IsSquadDeployed(MilSquadFC squad, out string reason)
        {
            reason = "";
            FactionFC factionFC = FactionCache.FactionComp;
            List<WorldSettlementFC> settlementsWithSquad = factionFC?.settlements
                ?.FindAll(settlement => settlement?.MilitaryComp?.militarySquad?.outfit == squad);

            if (settlementsWithSquad == null || settlementsWithSquad.Count == 0) return false;

            if (settlementsWithSquad.Any(s => s.MilitaryComp.militarySquad.IsPhysicallyDeployed()))
            {
                reason = "FCReasonDeployedSquad".Translate();
                return true;
            }

            if (settlementsWithSquad.Any(s => s.MilitaryComp.isUnderAttack
                && settlementsWithSquad.Contains(s.MilitaryComp.defenderForce.homeSettlement)))
            {
                reason = "FCReasonDefendingSquad".Translate();
                return true;
            }

            return false;
        }
    }
}
