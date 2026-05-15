using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public enum SettlementStatType
    {
        MilitaryLevel,
        Happiness,
        Loyalty,
        Unrest,
        Prosperity
    }

    public enum SettlementButtonType
    {
        Upgrade,
        SpecialActions,
        Prisoners,
        Military,
        Delete
    }

    public sealed class SettlementWindowFc : Window
    {
        public override Vector2 InitialSize
        {
            get { return new Vector2(1250f, 645f); }
        }


        //UI STUFF
        public const int ScrollSpacing = 45;
        public const int ScrollHeight = 315;

        private const int margin = 5;
        private const int smallMargin = 3;

        private int maxScroll;
        private FactionFC factionfc;

        // Building UI values
        private const int buildingSpacing = margin;//15;
        private const int buildingBoxSide = 72;

        private const int constructionListItemLabelHeight = 15;
        private const int constructionListProgressBarHeight = 10;
        private const int constructionListItemHeight = (smallMargin * 4) + (constructionListItemLabelHeight * 2) + constructionListProgressBarHeight; // 4 * smallMargin + 2 * label height + progress bar height
        private const int constructionListIconHeight = constructionListItemLabelHeight * 2 + smallMargin;

        private const int buildingSpacingFromSide = margin; //15; // (494 - (spacing + boxSide) * elementsPerRow) / 2;

        private const int scrollSpacing = (int)ScrollUtil.ScrollbarWidth + 1;

        // UI State
        private int overviewTab = 0;
        private int titheTab = 0;

        // Tithe buffers
        private List<string> titheBuffers = new List<string>();
        private int currentDictSize = 0;

        // Comps with overview tabs
        private List<ISettlementWindowOverview> overviews = new List<ISettlementWindowOverview>();
        private Color accentColor;
        private Color highlightColor;

        public override void PreOpen()
        {
            base.PreOpen();
            maxScroll = (settlement.Resources.Count * ScrollSpacing) - ScrollHeight;
            factionfc = FactionCache.FactionComp;
            Color baseColor = settlement.settlementDef.accentColor ?? Color.white;
            accentColor = baseColor * Color.gray;
            highlightColor = baseColor * Color.white;

            foreach (WorldObjectComp comp in settlement.AllComps)
            {
                ISettlementWindowOverview overview = comp as ISettlementWindowOverview;
                if (!(overview is null))
                {
                    overview.PreOpenWindow(settlement);
                    overviews.Add(overview);
                    overviewTabs.Add(overview.OverviewTabName());
                }
            }
        }
        public override void PostClose()
        {
            base.PostClose();

            foreach (ISettlementWindowOverview overview in overviews)
            {
                overview.PostCloseWindow();
            }
        }

        private List<string> overviewTabs = new List<string>
        {
            "FCOverview".Translate(),
            "FCTithing".Translate()
        };

        private static readonly SettlementStatType[] stats =
        {
            SettlementStatType.MilitaryLevel,
            SettlementStatType.Happiness,
            SettlementStatType.Loyalty,
            SettlementStatType.Unrest,
            SettlementStatType.Prosperity
        };

        private static readonly SettlementButtonType[] mainButtons =
        {
            SettlementButtonType.Upgrade,
            SettlementButtonType.SpecialActions,
            SettlementButtonType.Prisoners,
            SettlementButtonType.Military
        };

        private WorldSettlementFC settlement; //Don't expose

        public SettlementWindowFc(WorldSettlementFC settlement)
        {
            if (settlement == null)
            {
                Close();
                return;
            }

            this.settlement = settlement;
            forcePause = false;
            draggable = true;
            doCloseX = true;
            preventCameraMotion = false;
        }


        public override void DoWindowContents(Rect inRect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            float validWidth = InitialSize.x - (Margin * 2);
            float validHeight = InitialSize.y - (Margin * 2);

            float leftWidth = 150f;
            float rightWidth = 415f;
            float centerWidth = validWidth - leftWidth - rightWidth - (margin * 2);

            Rect headerBox = new Rect(inRect.x, inRect.y, leftWidth + centerWidth + margin, 30 + (margin * 2) + 60);
            Rect leftBox = new Rect(inRect.x, headerBox.yMax + (margin * 2), leftWidth, validHeight - headerBox.height - (margin * 2));
            Rect centerBox = new Rect(leftBox.xMax + margin, headerBox.yMax + (margin * 2), centerWidth, validHeight - headerBox.height - (margin * 2));
            Rect rightBox = new Rect(centerBox.xMax + margin, inRect.y, rightWidth, validHeight);

            DrawCenterHeader(headerBox);
            DrawLeftInfo(leftBox);
            DrawCenterInfo(centerBox);
            DrawRightInfo(rightBox);

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        /* Left side overview */
        private void DrawLeftInfo(Rect boundingBox)
        {
            Rect topBox = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, boundingBox.height / 2f);
            Rect topBoxInner = new Rect(topBox.x + margin, topBox.y + margin, topBox.width - (margin * 2), topBox.height - (margin * 2));
            Rect botBox = new Rect(topBox.x, topBox.yMax, topBox.width, topBox.height);
            Rect botBoxInner = new Rect(botBox.x + margin, botBox.y + margin, botBox.width - (margin * 2), botBox.height - (margin * 2));

            UIUtil.DrawColoredBox(topBox, accentColor);
            UIUtil.DrawColoredBox(botBox, accentColor);
            DrawSettlementStats(topBoxInner);
            DrawMainButtons(botBoxInner);
        }

        private void DrawCenterInfo(Rect boundingBox)
        {
            Color? tabBaseColor = accentColor != Color.gray ? (Color?)accentColor : null;
            int newTab = UIUtil.DrawTabRow(boundingBox, overviewTabs, overviewTab, out Rect contentRect,
                baseColor: tabBaseColor, borderColor: accentColor);
            if (newTab != overviewTab)
            {
                overviewTab = newTab;
                if (overviewTab >= 2 && (overviewTab - 2) < overviews.Count)
                {
                    overviews[overviewTab - 2].OnTabSwitch();
                }
            }
            Rect infobox = contentRect.ContractedBy(margin);
            DrawOverview(infobox);
        }
        private void DrawOverview(Rect boundingBox)
        {
            if (overviewTab == 0)
            {
                DrawBasicOverview(boundingBox);
            }
            else if (overviewTab == 1)
            {
                DrawTitheOverview(boundingBox);
            }
            else if (overviews.Count > 0 && overviewTab >= 2 && (overviewTab - 2) < overviews.Count)
            {
                ISettlementWindowOverview overview = overviews[overviewTab - 2];
                overview.DrawOverviewTab(boundingBox);
            }
        }
        private void DrawCenterHeader(Rect boundingBox)
        {
            /* Settlement name on top */
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect nameRect = new Rect(boundingBox.x + margin, boundingBox.y + margin, boundingBox.width - (margin * 2 + 44), 30);
            Widgets.Label(nameRect, settlement.Name);
            //Draw codex button
            Rect codexBtnRect = new Rect(nameRect.xMax + margin, boundingBox.y + margin, 20, 20);
            CodexTooltips.DrawCodexButton(codexBtnRect);
            //Draw name settings button
            Rect configRect = new Rect(codexBtnRect.xMax + margin, boundingBox.y + margin, 20, 20);
            if (Widgets.ButtonImage(configRect, TexLoad.iconCustomize))
            {
                //if click faction customize button
                Find.WindowStack.Add(new SettlementCustomizeWindowFc(settlement));
            }
            // Just used for alignment. Can maybe use this box to draw some background art based on the settlement's biome. Kinda like stellaris, maybe
            Rect infoBox = new Rect(boundingBox.x, nameRect.yMax, boundingBox.width, boundingBox.height - (nameRect.height + margin * 2));

            /* Town level */
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect levelBoundingBox = new Rect(infoBox.x, infoBox.y + margin, 60, 60);
            //gotta love aligning text
            Rect levelBox = new Rect(levelBoundingBox.x + 14, levelBoundingBox.y + 14, 30, 30);
            Widgets.DrawShadowAround(levelBox);
            UIUtil.DrawColoredHighlight(levelBoundingBox, highlightColor);
            UIUtil.DrawColoredBox(levelBoundingBox, accentColor);
            Widgets.Label(levelBox, settlement.settlementLevel.ToString());

            // Draw settlement type, basic description (from def), and location text
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            float rightSideWidth = boundingBox.width - (margin + levelBoundingBox.width);
            Rect typeBox = new Rect(levelBoundingBox.xMax + margin, levelBoundingBox.y, rightSideWidth, levelBoundingBox.height / 2);
            Rect typeTextBox = new Rect(typeBox.x + margin, typeBox.y, (typeBox.width - (margin * 2)) / 2f, typeBox.height);
            Rect foundTextBox = new Rect(typeTextBox.xMax, typeTextBox.y, typeTextBox.width, typeTextBox.height);
            Rect basicDescBox = new Rect(typeBox.x, typeBox.yMax, rightSideWidth * 0.4f, levelBoundingBox.height / 2);
            Rect basicDescTextBox = new Rect(basicDescBox.x + margin, basicDescBox.y, basicDescBox.width - (margin * 2), basicDescBox.height);
            Rect locBox = new Rect(basicDescBox.xMax + margin, typeBox.yMax, (rightSideWidth * 0.6f) - margin, levelBoundingBox.height / 2);
            Rect locTextBox = new Rect(locBox.x + margin, locBox.y, locBox.width - (margin * 2), locBox.height);
            UIUtil.DrawColoredHighlight(typeBox, highlightColor);
            Widgets.Label(typeTextBox, settlement.settlementDef.LabelCap);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(foundTextBox, "FCFoundedOn".Translate(settlement.GetFoundingDate()));
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            Widgets.Label(basicDescTextBox, TextUtil.GetTownTitle(settlement));
            UIUtil.DrawColoredVerticalLine(basicDescBox.xMax, basicDescBox.y + margin, basicDescBox.height - (margin * 2), accentColor);
            //TODO: localize this. LabelCap and description can be localized through def injection, but locationText is derived differently
            Widgets.Label(locTextBox, settlement.locationText);
        }
        private void DrawBasicOverview(Rect boundingBox)
        {
            float bottomHeight = (buildingBoxSide * 3) + (buildingSpacing * 4) + 30f;
            Rect topRect = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, (boundingBox.height - margin - bottomHeight));
            Rect botRect = new Rect(boundingBox.x, topRect.yMax + margin, boundingBox.width, bottomHeight);

            DrawBasicOverviewTop(topRect);
            DrawBasicOverviewBottom(botRect);
        }

        private void DrawBasicOverviewTop(Rect boundingBox)
        {
            // Maybe we'll put more than just the description here. Who knows?
            // A biome-fitting image might be cool, kind of like Stellaris
            DrawDescription(boundingBox);
        }

        private void DrawBasicOverviewBottom(Rect boundingBox)
        {
            float scrollMargin = ((settlement.BuildingsComp?.Buildings.Count ?? 0) > 12) ? scrollSpacing : 0;
            float buildingBoxWidth = Math.Max(boundingBox.x * 0.7f, (buildingSpacingFromSide * 2) + (buildingBoxSide * 4) + (buildingSpacing * 3) + scrollMargin);
            float constructionBoxWidth = boundingBox.width - buildingBoxWidth;

            Rect leftBox = new Rect(boundingBox.x, boundingBox.y, constructionBoxWidth, boundingBox.height);
            Rect rightBox = new Rect(leftBox.xMax, leftBox.y, buildingBoxWidth, boundingBox.height);

            if (settlement.BuildingsComp == null)
            {
                DrawFacilities(rightBox);
                return;
            }

            int numUnderConstruction = settlement.BuildingsComp.GetUnderConstructionBuildings().Count + (settlement.IsUpgrading ? 1 : 0);
            DrawConstructionBox(leftBox, numUnderConstruction, settlement.BuildingsComp.GetUnderConstructionBuildings());
            DrawFacilities(rightBox);
        }
        private void DrawTitheOverview(Rect boundingBox)
        {
            Color origColor = GUI.color;
            List<ResourceFC> resources = settlement.GetTitheableResources();
            int numResources = resources.Count;
            if (numResources == 0) return;
            if (titheTab >= numResources) titheTab = 0;
            /* Draw resource tabs on the left */
            float tabWidth = 25f;
            float tabHeight = boundingBox.height / numResources;
            Rect chosenRect = new Rect();
            for (int i = 0; i < numResources; i++)
            {
                Rect tabBox = new Rect(boundingBox.x, boundingBox.y + (tabHeight * i), tabWidth, tabHeight);
                float imgSize = Math.Min(tabWidth, tabHeight);
                Rect iconBox = new Rect(tabBox.x + 2f + (tabWidth - imgSize) / 2f, tabBox.y + (tabHeight - imgSize) / 2f, imgSize, imgSize);
                if (UIUtil.ButtonFlat(tabBox, "", highlighted: titheTab == i))
                {
                    titheTab = i;
                    UpdateTitheDictBuffers(resources[i]);
                }
                Text.Font = GameFont.Small;
                Widgets.Label(iconBox, new GUIContent(resources[i].def.Icon));
                // Resource color accent
                Widgets.DrawBoxSolid(new Rect(tabBox.x, tabBox.y, 3f, tabBox.height), resources[i].def.color);
                TooltipHandler.TipRegion(tabBox, resources[i].def.LabelCap);
                // Tithe budget status indicator on right edge
                double titheIncome = resources[i].GetTitheIncome();
                if (titheIncome > 0)
                {
                    Color alertColor;
                    if (resources[i].tithesPaused)
                    {
                        alertColor = Color.grey;
                    }
                    else
                    {
                        float ratio = (float)(resources[i].titheTotalValue / titheIncome);
                        if (ratio >= 1f)
                            alertColor = Color.red;
                        else if (ratio >= 0.5f)
                            alertColor = Color.yellow;
                        else
                            alertColor = Color.green;
                    }

                    float alertSize = 3f;
                    Widgets.DrawBoxSolid(new Rect(tabBox.xMax - alertSize - 2f, tabBox.y + 5f, alertSize, alertSize), alertColor);
                }
                if (titheTab == i)
                {
                    chosenRect = tabBox;
                }
            }
            UIUtil.DrawTabDecoratorVerticalLeft(chosenRect, boundingBox, resources[titheTab].def.color);

            ResourceFC titheRes = resources[titheTab];

            /* Calculate heights */
            float bodyX = boundingBox.x + tabWidth + margin;
            float bodyWidth = boundingBox.width - tabWidth - margin;
            float headerHeight = 30 + margin + (23f * 3);//60f;
            float footerHeight = 23f;
            float bodyHeight = boundingBox.height - headerHeight - footerHeight - (margin * 2);
            float randomboxHeight = 0f;
            if (titheRes.hasRandomTithe)
            {
                randomboxHeight = bodyHeight / 2f;
            }
            else
            {
                randomboxHeight = 23f * 2;
            }
            float scrollboxHeight = bodyHeight - randomboxHeight;

            /* Header box (contains pause button — always interactive) */
            Rect headerBox = new Rect(bodyX, boundingBox.y, bodyWidth, headerHeight);
            DrawTitheHeaderBox(headerBox, titheRes);

            /* Dim content below header when paused */
            if (titheRes.tithesPaused)
            {
                GUI.enabled = false;
            }

            /* Scrollbox */
            Rect scrollBox = new Rect(bodyX, headerBox.yMax + margin, bodyWidth, scrollboxHeight);
            DrawTitheScrollBox(scrollBox, titheRes);

            /* Random tithe box */
            Rect titheBox = new Rect(bodyX, scrollBox.yMax, bodyWidth, randomboxHeight);
            DrawTitheRandomBox(titheBox, titheRes);

            /* Footer box */
            Rect footerBox = new Rect(bodyX, titheBox.yMax + margin, bodyWidth, footerHeight);
            DrawTitheFooterBox(footerBox, titheRes);
            GUI.enabled = true;
            GUI.color = origColor;

            /* Blocking overlay when paused — covers content below header */
            if (titheRes.tithesPaused)
            {
                float overlayY = boundingBox.y + 30f + margin;
                Rect contentArea = new Rect(bodyX, overlayY, bodyWidth, boundingBox.yMax - overlayY);
                Widgets.DrawBoxSolid(contentArea, new Color(0f, 0f, 0f, 0.75f));
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(contentArea, "FCTithesPaused".Translate());
            }
        }
        private void DrawTitheHeaderBox(Rect boundingBox, ResourceFC res)
        {
            float pauseBtnWidth = 90f;
            Rect iconBox = new Rect(boundingBox.x, boundingBox.y, 30f, 30f);
            Rect labelHighlight = new Rect(iconBox.xMax + margin, boundingBox.y, boundingBox.width - margin - iconBox.width - pauseBtnWidth - margin, 30f);
            Rect labelText = new Rect(labelHighlight.x + smallMargin, labelHighlight.y + smallMargin, labelHighlight.width - (smallMargin * 2), labelHighlight.height - (smallMargin * 2));
            Rect pauseBtn = new Rect(labelHighlight.xMax + margin, boundingBox.y + 3f, pauseBtnWidth, 27f);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.DrawHighlight(iconBox);
            Widgets.Label(iconBox, new GUIContent(res.def.Icon));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.DrawHighlight(labelHighlight);
            Widgets.Label(labelText, res.def.LabelCap);

            /* Pause button */
            Text.Font = GameFont.Tiny;
            string buttonText = res.tithesPaused ? "FCUnPauseTithes".Translate() : "FCPauseTithes".Translate();
            if (UIUtil.ButtonFlat(pauseBtn, buttonText, highlighted: res.tithesPaused))
            {
                res.tithesPaused = !res.tithesPaused;
                res.settlement.DirtyProfitCache();
            }
            TooltipHandler.TipRegion(pauseBtn, "FCPauseTithesDesc".Translate());

            Rect iconAccent = new Rect(iconBox.x, iconBox.y, iconBox.width, 3f);
            Rect labelAccent = new Rect(labelHighlight.x, labelHighlight.y, labelHighlight.width, 3f);
            Rect btnAccent = new Rect(pauseBtn.x, pauseBtn.y - 3f, pauseBtn.width, 3f);
            Widgets.DrawBoxSolid(iconAccent, res.def.color);
            Widgets.DrawBoxSolid(labelAccent, res.def.color);
            Widgets.DrawBoxSolid(btnAccent, res.def.color);

            /* Info boxes */
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float rowHeight = 23f;
            float labelHeight = rowHeight - (smallMargin * 2);

            double extBudget = res.externalTitheBudget;
            bool hasInjection = extBudget > 0.01;

            Rect titheModBox = new Rect(boundingBox.x, iconBox.yMax + margin, (boundingBox.width - margin) / 2f, rowHeight * 3f);
            Rect prodBox = new Rect(titheModBox.xMax + margin, iconBox.yMax + margin, (boundingBox.width - margin) / 2f, rowHeight);
            float budgetY = hasInjection ? prodBox.yMax + rowHeight : prodBox.yMax;
            Rect budgetBox = new Rect(titheModBox.xMax + margin, budgetY, (boundingBox.width - margin) / 2f, rowHeight);

            /* Tithe modifier info */
            Rect titheRow1 = new Rect(titheModBox.x, titheModBox.y, titheModBox.width, rowHeight);
            Rect trow1label = new Rect(titheRow1.x + smallMargin, titheRow1.y + smallMargin, (titheRow1.width - (smallMargin * 2)), labelHeight);
            Rect titheRow2 = new Rect(titheModBox.x + (margin * 2), titheRow1.yMax, titheModBox.width - (margin * 2), rowHeight);
            Rect trow2label = new Rect(titheRow2.x + smallMargin, titheRow2.y + smallMargin, (titheRow2.width - (smallMargin * 2)) * 0.75f, labelHeight);
            Rect trow2num = new Rect(trow2label.xMax, trow2label.y, (titheRow2.width - (smallMargin * 2)) * 0.25f, labelHeight);
            Rect titheRow3 = new Rect(titheRow2.x, titheRow2.yMax, titheRow2.width, rowHeight);
            Rect trow3label = new Rect(titheRow3.x + smallMargin, titheRow3.y + smallMargin, (titheRow3.width - (smallMargin * 2)) * 0.75f, labelHeight);
            Rect trow3num = new Rect(trow3label.xMax, trow3label.y, (titheRow3.width - (smallMargin * 2)) * 0.25f, labelHeight);
            Widgets.DrawHighlight(titheModBox);
            Widgets.DrawHighlight(titheRow1);
            Widgets.Label(trow1label, "FCTitheModifier".Translate());
            Widgets.Label(trow2label, "FCPerWorker".Translate());
            Widgets.DrawHighlight(titheRow3);
            Widgets.Label(trow3label, "FCTotal".Translate());
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(trow2num, Math.Round(res.GetTitheModifierPerWorker()).ToString());
            Widgets.Label(trow3num, Math.Round(res.GetTotalTitheModifierForWorkers()).ToString());

            /* Production */
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect prodLabel = new Rect(prodBox.x + smallMargin, prodBox.y + smallMargin, (prodBox.width - (margin * 2)) * 0.75f, labelHeight);
            Rect prodnum = new Rect(prodLabel.xMax, prodLabel.y, (prodBox.width - (margin * 2)) * 0.25f, labelHeight);
            Widgets.DrawHighlight(prodBox);
            Widgets.Label(prodLabel, "FCTotalProd".Translate());
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(prodnum, Math.Round(res.taxableProductionMarketValue).ToString());

            /* External Tithe Injection */
            if (hasInjection)
            {
                Rect injBox = new Rect(prodBox.x, prodBox.yMax, prodBox.width, rowHeight);
                Rect injLabel = new Rect(injBox.x + smallMargin, injBox.y + smallMargin, (injBox.width - (margin * 2)) * 0.75f, labelHeight);
                Rect injNum = new Rect(injLabel.xMax, injLabel.y, (injBox.width - (margin * 2)) * 0.25f, labelHeight);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(injLabel, "FCExternalTitheBudget".Translate());
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(injNum, Math.Round(extBudget).ToString());

                StringBuilder injTip = new StringBuilder();
                foreach (WorldObjectComp comp in res.settlement.AllComps)
                {
                    if (comp is ITitheBudgetModifier modifier)
                    {
                        string desc = modifier.GetExternalTitheBudgetDesc(res);
                        if (!string.IsNullOrEmpty(desc))
                            injTip.AppendLine(desc);
                    }
                }
                if (injTip.Length > 0)
                    TooltipHandler.TipRegion(injBox, injTip.ToString().TrimEnd());
            }

            /* Tithe Budget */
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect budgetLabel = new Rect(budgetBox.x + smallMargin, budgetBox.y + smallMargin, (budgetBox.width - (margin * 2)) * 0.75f, labelHeight);
            Rect budgetnum = new Rect(budgetLabel.xMax, budgetLabel.y, (budgetBox.width - (margin * 2)) * 0.25f, labelHeight);
            Widgets.DrawMenuSection(budgetBox);
            Widgets.Label(budgetLabel, "FCTotalTitheBudget".Translate());
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(budgetnum, Math.Round(res.GetTitheIncome()).ToString());
        }
        private Vector2 titheScrollBar = new Vector2();
        private void UpdateTitheDictBuffers(ResourceFC res)
        {
            titheBuffers.Clear();
            if (res != null)
            {
                List<ThingQualityTuple> items = res.GetTitheListKeys();
                for (int i = 0; i < items.Count; i++)
                {
                    titheBuffers.Add(res.GetTitheListValue(items[i]).ToString());
                }
                res.storedRandomTitheBudgetBuffer = res.storedRandomTitheBudget.ToString();
            }
            currentDictSize = titheBuffers.Count;
        }
        private void KeepTitheDictBuffersUpdated(ResourceFC res)
        {
            if (res != null && currentDictSize != res.GetTitheListCount())
            {
                UpdateTitheDictBuffers(res);
            }
        }
        private void DrawTitheScrollBox(Rect boundingBox, ResourceFC res)
        {
            UIUtil.DrawColoredBox(boundingBox, accentColor);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            float rowHeight = 23f;
            Rect header = new Rect(boundingBox.x, boundingBox.y, boundingBox.width * 0.8f, rowHeight);
            Rect headerText = new Rect(header.x + margin, header.y, header.width - (margin * 2), header.height);
            Widgets.DrawHighlight(header);
            Widgets.Label(headerText, "FCTitheSelection".Translate());

            Rect addItemButton = new Rect(header.xMax, header.y, (boundingBox.width * 0.2f) - smallMargin, header.height);
            if (Widgets.ButtonText(addItemButton, "FCAddItem".Translate()))
            {
                Find.WindowStack.Add(new SettlementWindowFC_AddTithe(settlement, res));
            }

            KeepTitheDictBuffersUpdated(res);

            List<ThingQualityTuple> titheItems = res.GetTitheListKeys();

            /* Doing weird box-in-a-box to try and fix some UI drawing issues */
            Rect drawBox = new Rect(boundingBox.x + margin, header.yMax, boundingBox.width - (margin * 2), boundingBox.yMax - header.yMax - margin);
            Rect selectedListBox = new Rect(drawBox.x + 2, drawBox.y + 2, drawBox.width - 4, drawBox.height - 4);
            float listHeight = titheItems.Count * rowHeight;
            Widgets.DrawMenuSection(selectedListBox);
            Rect innerScrollBox = ScrollUtil.BeginScrollView(selectedListBox, ref titheScrollBar, listHeight);
            for (int i = 0; i < titheItems.Count; i++)
            {
                ThingQualityTuple thingTuple = titheItems[i];
                ThingDef iThing = thingTuple.thingDef;
                QualityCategory iQuality = thingTuple.quality;
                ThingDef iStuff = thingTuple.stuffDef;
                bool labelExtended = false;

                Rect row = new Rect(innerScrollBox.x, innerScrollBox.y + (i * rowHeight), innerScrollBox.width, rowHeight);
                Rect icon = new Rect(row.x + margin, row.y, rowHeight, rowHeight);
                Rect info = new Rect(icon.xMax, row.y + 2, rowHeight - 4, rowHeight - 4);
                Rect xBox = new Rect(row.xMax - margin - 20f, row.y + 2, rowHeight - 4, rowHeight - 4);
                Rect fieldBox = new Rect(xBox.x - margin - 180f, row.y + 2, 180f, rowHeight - 4);
                Rect valueLabel = new Rect(fieldBox.x - margin - 60f, row.y, 60f, rowHeight);
                Rect stuffBox = new Rect(valueLabel.x - margin - 80f, row.y + 2, 80f, rowHeight - 4);
                Rect qualityBox = new Rect(stuffBox.x - 80f, row.y + 2, 80f, rowHeight - 4);
                Rect label = new Rect(info.xMax + margin, row.y, qualityBox.x - info.xMax - margin, rowHeight);
                if (i % 2 == 0)
                {
                    Widgets.DrawHighlight(row);
                }


                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(icon, new GUIContent(iThing.uiIcon));
                Widgets.InfoCardButton(info, iThing);
                if (Widgets.ButtonText(xBox, "X"))
                {
                    res.RemoveFromTitheList(thingTuple);
                    break;
                }
                TooltipHandler.TipRegion(xBox, "FCTitheXDesc".Translate());
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(valueLabel, $"${Math.Round(res.TitheThingValue(thingTuple), 2)}");

                QualityCategory maxQuality = QualityCategory.Legendary;
                if (CraftUtil.ThingHasQuality(iThing) && res.CanSetTitheQuality(out maxQuality))
                {
                    List<QualityCategory> categoryList = res.GetValidTitheQualities(maxQuality);
                    if (Widgets.ButtonText(qualityBox, TextUtil.GetQualityLabelCap(iQuality)))
                    {
                        List<FloatMenuOption> options = new List<FloatMenuOption>();
                        foreach (QualityCategory cat in categoryList)
                        {
                            options.Add(new FloatMenuOption(TextUtil.GetQualityLabelCap(cat), delegate
                            {
                                int qty = res.GetTitheListValue(thingTuple);
                                ThingQualityTuple newTuple = new ThingQualityTuple
                                {
                                    thingDef = iThing,
                                    quality = cat,
                                    stuffDef = iStuff
                                };
                                if (res.HasTitheListKey(newTuple))
                                {
                                    Messages.Message(newTuple.ListRejectionMessage(), MessageTypeDefOf.RejectInput);
                                }
                                else
                                {
                                    res.RemoveFromTitheList(thingTuple);
                                    res.AddToTitheList(newTuple, qty);
                                    UpdateTitheDictBuffers(res);
                                }
                            }));
                        }
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                }
                else
                {
                    label.width += qualityBox.width;
                    labelExtended = true;
                }

                if (CraftUtil.ThingIsStuffable(iThing))
                {
                    List<ThingDef> stuffList = res.GetStuffListForThingDef(iThing);
                    if (Widgets.ButtonText(stuffBox, iStuff?.LabelCap ?? "None"))
                    {
                        List<FloatMenuOption> options = new List<FloatMenuOption>();
                        foreach (ThingDef stuff in stuffList)
                        {
                            options.Add(new FloatMenuOption(stuff.LabelCap, delegate
                            {
                                int qty = res.GetTitheListValue(thingTuple);
                                ThingQualityTuple newTuple = new ThingQualityTuple
                                {
                                    thingDef = iThing,
                                    quality = iQuality,
                                    stuffDef = stuff
                                };
                                if (res.HasTitheListKey(newTuple))
                                {
                                    Messages.Message(newTuple.ListRejectionMessage(), MessageTypeDefOf.RejectInput);
                                }
                                else
                                {
                                    res.RemoveFromTitheList(thingTuple);
                                    res.AddToTitheList(newTuple, qty);
                                    UpdateTitheDictBuffers(res);
                                }
                            }));
                        }
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                }
                else if (labelExtended)
                {
                    label.width += stuffBox.width;
                }
                string nulabel = Text.ClampTextWithEllipsis(label, iThing.LabelCap);
                Widgets.Label(label, nulabel);
                if (nulabel != iThing.LabelCap)
                {
                    TooltipHandler.TipRegion(label, iThing.LabelCap);
                }
                // This seems like a *really* hacky way to handle these buffers. Seems like it'd be prone to UI jitteryness, or just general bad feel
                //   keep this in mind when testing...
                int quantity = res.GetTitheListValue(thingTuple);
                int oldQuantity = quantity;
                int max = Math.Max(0, quantity + res.MaxThingCanAfford(thingTuple));
                string buf = titheBuffers[i];
                Widgets.IntEntry(fieldBox, ref quantity, ref buf);
                int unclamped = quantity;
                quantity = Math.Clamp(quantity, 0, max);
                buf = quantity.ToString();
                if (unclamped > max)
                {
                    if (res.GetTitheIncome() <= 0)
                        Messages.Message("FCTitheBudgetNoWorkers".Translate(), MessageTypeDefOf.RejectInput);
                    else
                        Messages.Message("FCTitheBudgetInsufficient".Translate(), MessageTypeDefOf.RejectInput);
                }
                if (oldQuantity != quantity)
                {
                    res.AddToTitheList(thingTuple, quantity, true);
                }
                titheBuffers[i] = buf;
            }

            ScrollUtil.EndScrollView();
        }
        private Vector2 randomTitheScrollBar = new Vector2();
        private void DrawTitheRandomBox(Rect boundingBox, ResourceFC res)
        {
            Color origColor = GUI.color;
            UIUtil.DrawColoredBox(boundingBox, accentColor);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            float rowHeight = 23f;
            Rect header = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, rowHeight);
            Rect headerText = new Rect(header.x + margin, header.y, header.width - (margin * 2), header.height);
            Widgets.DrawHighlight(header);
            Widgets.CheckboxLabeled(headerText, "FCRandomTithesEnabled".Translate(), ref res.hasRandomTithe, disabled: res.tithesPaused);
            TooltipHandler.TipRegion(header, "FCRandomTithesDesc".Translate());

            Rect accruedBox = new Rect(boundingBox.x, header.yMax, boundingBox.width * 0.6f, rowHeight);
            Rect accruedTextBox = new Rect(accruedBox.x + smallMargin, accruedBox.y, accruedBox.width - (smallMargin * 2), accruedBox.height);
            Rect disburseBox = new Rect(accruedBox.xMax, header.yMax, boundingBox.width - accruedBox.width, rowHeight);
            Rect disbursedTextBox = new Rect(disburseBox.x + smallMargin, disburseBox.y, disburseBox.width - (smallMargin * 2), disburseBox.height);
            Widgets.Label(accruedTextBox, "FCRandomTitheAccrued".Translate(res.randomTitheStock));
            Widgets.CheckboxLabeled(disbursedTextBox, "FCDisburseAccruedRandomTithe".Translate(), ref res.disburseTitheStock, disabled: res.tithesPaused);
            TooltipHandler.TipRegion(accruedBox, "FCRandomTitheAccruedDesc".Translate());
            TooltipHandler.TipRegion(disburseBox, "FCDisburseAccruedRandomTitheDesc".Translate());

            if (res.hasRandomTithe)
            {
                float budgetRowWidth = boundingBox.width * 0.6f;
                float checkboxWidth = 55f;
                Rect budgetBox = new Rect(boundingBox.x, disburseBox.yMax, budgetRowWidth - checkboxWidth, 23f);
                Rect budgetTextBox = new Rect(budgetBox.x + margin, budgetBox.y, budgetBox.width - (margin * 2), budgetBox.height);
                if (res.autoMaxRandomTithe)
                {
                    Widgets.Label(budgetTextBox, "FCRandomTitheBudget".Translate() + ": " + res.randomTitheBudget);
                }
                else
                {
                    Widgets.TextFieldNumericLabeled(budgetTextBox, "FCRandomTitheBudget".Translate() + ": ", ref res.storedRandomTitheBudget, ref res.storedRandomTitheBudgetBuffer, 0, (float)(res.GetTitheIncome() - res.titheTotalValueNoRandom));
                    res.RefreshOnRandomTitheBudgetChange();
                }
                Rect maxCheckBox = new Rect(budgetBox.xMax, budgetBox.y, checkboxWidth, budgetBox.height);
                bool prevAutoMax = res.autoMaxRandomTithe;
                Widgets.CheckboxLabeled(maxCheckBox, "FCAutoMaxRandomTithe".Translate(), ref res.autoMaxRandomTithe, disabled: res.tithesPaused);
                TooltipHandler.TipRegion(maxCheckBox, "FCAutoMaxRandomTitheDesc".Translate());
                if (!res.autoMaxRandomTithe && prevAutoMax)
                {
                    res.storedRandomTitheBudget = Math.Max(0, (int)(res.GetTitheIncome() - res.titheTotalValueNoRandom));
                    res.storedRandomTitheBudgetBuffer = res.storedRandomTitheBudget.ToString();
                    res.settlement.DirtyProfitCache();
                }
                Rect selectBox = new Rect(boundingBox.x + budgetRowWidth, budgetBox.y, boundingBox.width * 0.4f - margin, budgetBox.height);
                if (Widgets.ButtonText(selectBox, "FCItemSelection".Translate()))
                {
                    Find.WindowStack.Add(new SettlementWindowFC_RandomTithe(settlement, res));
                }

                List<ThingDef> selectedThings = res.GetRandomTitheFilterThings();

                /* Doing weird box-in-a-box to try and fix some UI drawing issues */
                Rect drawBox = new Rect(boundingBox.x + margin, budgetBox.yMax, boundingBox.width - (margin * 2), boundingBox.yMax - budgetBox.yMax - margin);
                Rect selectedListBox = new Rect(drawBox.x + 2, drawBox.y + 2, drawBox.width - 4, drawBox.height - 4);
                float listHeight = selectedThings.Count * rowHeight;

                Widgets.DrawMenuSection(selectedListBox);
                Rect innerScrollBox = ScrollUtil.BeginScrollView(selectedListBox, ref randomTitheScrollBar, listHeight);

                for (int i = 0; i < selectedThings.Count; i++)
                {
                    ThingDef iThing = selectedThings[i];
                    Rect row = new Rect(innerScrollBox.x, innerScrollBox.y + (i * rowHeight), innerScrollBox.width, rowHeight);
                    Rect icon = new Rect(row.x + margin, row.y, rowHeight, rowHeight);
                    Rect info = new Rect(icon.xMax, row.y + 2, rowHeight - 4, rowHeight - 4);
                    Rect xBox = new Rect(row.xMax - margin - 20f, row.y + 2, 19f, 19f);
                    Rect valueLabel = new Rect(xBox.x - margin - 60f, xBox.y, 60f, rowHeight);
                    Rect label = new Rect(info.xMax + margin, row.y, valueLabel.x - info.xMax - margin, rowHeight);

                    if (i % 2 == 0)
                    {
                        Widgets.DrawHighlight(row);
                    }
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(icon, new GUIContent(iThing.uiIcon));
                    if (Widgets.ButtonText(xBox, "X"))
                    {
                        res.SetRandomTitheFilterAllow(iThing, false);
                    }
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(label, iThing.LabelCap);
                    Widgets.Label(valueLabel, $"${Math.Round(iThing.BaseMarketValue, 2)}");
                    Widgets.InfoCardButton(info, iThing);
                }

                ScrollUtil.EndScrollView();
            }
        }
        private void DrawTitheFooterBox(Rect boundingBox, ResourceFC res)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            /* Available Budget */
            float budgetWidth = (boundingBox.width * 0.5f);
            Rect availBudgetBox = new Rect(boundingBox.xMax - budgetWidth, boundingBox.y, budgetWidth, boundingBox.height);
            Rect availLabel = new Rect(availBudgetBox.x + smallMargin, availBudgetBox.y + smallMargin, (availBudgetBox.width - smallMargin * 2) / 2f, availBudgetBox.height - smallMargin * 2);
            Rect availNum = new Rect(availLabel.xMax, availLabel.y, availLabel.width, availLabel.height);
            double totalTithe = Math.Round(res.GetTitheIncome(), 2);
            double usedTithe = Math.Round(res.titheTotalValue, 2);
            TaggedString usedTitheStr = usedTithe.ToString();
            if (usedTithe > totalTithe)
            {
                usedTitheStr = usedTitheStr.Colorize(Color.red);
                TooltipHandler.TipRegion(availNum, "FCTitheOverBudget".Translate());
            }
            else if (res.actualIncome < 0)
            {
                usedTitheStr = usedTitheStr.Colorize(Color.yellow);
            }
            else
            {
                usedTitheStr = usedTitheStr.Colorize(Color.green);
            }
            Widgets.DrawHighlight(availBudgetBox);
            Widgets.Label(availLabel, "FCUsedTitheBudget".Translate() + ":");
            Widgets.Label(availNum, "FCNumRatio".Translate(usedTitheStr, totalTithe));
        }

        //Original: 125 wide, 215ish tall
        private void DrawSettlementStats(Rect boundingBox)
        {
            float statBoxHeight = (boundingBox.height - (4 * margin)) / 5;
            float statGainBoxHeight = 30;
            float statGainBoxWidth = 35;
            float statSize = Math.Min(30f, statBoxHeight);
            for (int i = 0; i < stats.Length; i++)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Medium;
                Rect statBox = new Rect(boundingBox.x, boundingBox.y + (statBoxHeight + margin) * i, boundingBox.width, statBoxHeight);
                Widgets.DrawMenuSection(statBox);
                Rect buttonBox = new Rect(statBox.x + margin, statBox.y + margin, statSize + 4, statSize + 4);
                Rect labelBox = new Rect(buttonBox.xMax, buttonBox.y, statBox.width - (buttonBox.width + margin * 2), buttonBox.height);
                Rect statGainBox = new Rect(statBox.xMax - statGainBoxWidth - margin, statBox.y + (statBox.height - statGainBoxHeight) / 2, statGainBoxWidth, statGainBoxHeight);
                Rect mainToolTipBox = new Rect(statBox.x, statBox.y, statGainBox.x - statBox.x, statBox.height);
                string tooltip;

                switch (stats[i])
                {
                    case SettlementStatType.MilitaryLevel:
                        tooltip = DrawStatMilitaryLevel(buttonBox, labelBox);
                        break;
                    case SettlementStatType.Happiness:
                        tooltip = DrawStatWithGainBox(buttonBox, labelBox, statGainBox,
                            TexLoad.iconHappiness, settlement.happiness + "%",
                            "FCSettlementHappiness", "FCSettlementHappinessDesc",
                            settlement.GetTotalHappinessGain(), settlement.GetHappinessDesc());
                        break;
                    case SettlementStatType.Loyalty:
                        tooltip = DrawStatWithGainBox(buttonBox, labelBox, statGainBox,
                            TexLoad.iconLoyalty, settlement.loyalty + "%",
                            "FCSettlementLoyalty", "FCSettlementLoyaltyDesc",
                            settlement.GetTotalLoyaltyGain(), settlement.GetLoyaltyDesc());
                        break;
                    case SettlementStatType.Unrest:
                        tooltip = DrawStatWithGainBox(buttonBox, labelBox, statGainBox,
                            TexLoad.iconUnrest, settlement.unrest + "%",
                            "FCSettlementUnrest", "FCSettlementUnrestDesc",
                            settlement.GetTotalUnrestGain(), settlement.GetUnrestDesc(), invertColor: true);
                        break;
                    case SettlementStatType.Prosperity:
                        tooltip = DrawStatWithGainBox(buttonBox, labelBox, statGainBox,
                            TexLoad.iconProsperity, settlement.prosperity + "%",
                            "FCSettlementProsperity", "FCSettlementProsperityDesc",
                            settlement.GetProsperityGain(), settlement.GetProsperityDesc());
                        break;
                    default:
                        tooltip = "";
                        break;
                }

                TooltipHandler.TipRegion(mainToolTipBox, tooltip);
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Medium;
        }

        private string DrawStatMilitaryLevel(Rect buttonBox, Rect labelBox)
        {
            Widgets.Label(buttonBox, new GUIContent(TexLoad.iconMilitary));
            // Squad-derived display: shows the strongest available stationed squad's level
            // (white), strongest stationed if all busy (yellow), half-power ghost (yellow),
            // or "—" greyed when SquadCap == 0. Red overrides everything when under attack.
            (double powLevel, double powEff, SettlementPowerStatus powStatus) = settlement.GetDisplayedPower();

            // Headline number is the full defensive total — base x efficiency x defender advantage —
            // so the value the player sees matches what the settlement actually brings to a defense.
            double defAdv = FCSettings.defenderAdvantage;
            double totalLevel = powLevel * powEff * defAdv;
            string label = powStatus == SettlementPowerStatus.NoMilitary
                ? "—"
                : ((int)Math.Round(totalLevel)).ToString();
            UIUtil.DrawColoredLabel(labelBox, label, ColorForPowerStatus(powStatus));

            string statusLine = StatusLineForPower(powStatus);
            string tooltip = "FCSettlementMilitaryLevel".Translate() + "\n-----\n"
                + "FCSettlementMilitaryLevelDesc".Translate() + "\n\n"
                + statusLine + "\n\n"
                + "Base Military Level: " + powLevel.ToString("0.#") + "\n"
                + "  Military Efficiency: " + powEff.ToString("0.0#") + "x\n"
                + "  Defender Advantage: " + defAdv.ToString("0.0#") + "x\n"
                + "Total Military Level: " + ((int)Math.Round(totalLevel));
            return tooltip;
        }

        private static Color ColorForPowerStatus(SettlementPowerStatus status)
        {
            switch (status)
            {
                case SettlementPowerStatus.UnderAttack: return AccentUtil.MilUnderAttack;
                case SettlementPowerStatus.AllBusy: return new Color(1f, 0.85f, 0.4f);
                case SettlementPowerStatus.Ghost: return new Color(1f, 0.85f, 0.4f);
                case SettlementPowerStatus.NoMilitary: return Color.gray;
                default: return Color.white;
            }
        }

        private static string StatusLineForPower(SettlementPowerStatus status)
        {
            switch (status)
            {
                case SettlementPowerStatus.UnderAttack: return "FCMilPowerTipUnderAttack".Translate();
                case SettlementPowerStatus.AllBusy: return "FCMilPowerTipAllBusy".Translate();
                case SettlementPowerStatus.Ghost: return "FCMilPowerTipGhost".Translate();
                case SettlementPowerStatus.NoMilitary: return "FCMilPowerTipNoMilitary".Translate();
                default: return "FCMilPowerTipSquadShort".Translate();
            }
        }

        private string DrawStatWithGainBox(Rect buttonBox, Rect labelBox, Rect statGainBox,
            Texture2D icon, string valueText,
            string tooltipTitleKey, string tooltipDescKey,
            double gainValue, string gainTooltip, bool invertColor = false)
        {
            Widgets.Label(buttonBox, new GUIContent(icon));
            Widgets.Label(labelBox, valueText);
            string tooltip = tooltipTitleKey.Translate() + "\n-----\n" + tooltipDescKey.Translate();

            Widgets.DrawHighlight(statGainBox);
            double rounded = Math.Round(gainValue, 1);
            TaggedString statGain = TextUtil.ColorizeAdditiveBonus(rounded, invertColor);

            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            Widgets.Label(statGainBox, statGain);
            TooltipHandler.TipRegion(statGainBox, gainTooltip);

            return tooltip;
        }

        private void DrawDescription(Rect boundingBox)
        {
            Widgets.DrawMenuSection(boundingBox);

            Rect textBox = new Rect(boundingBox.x + margin, boundingBox.y + margin, boundingBox.width - (margin * 2), boundingBox.height - (margin * 2));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(textBox, settlement.description);
        }
        private void RemoveSettlement()
        {
            LogUtil.Message($"Removing settlement {settlement.Name}...");
            Find.WindowStack.TryRemove(this);
            ColonyUtil.RemovePlayerSettlement(settlement);
        }
        private void DrawMainButtons(Rect boundingBox)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;

            IReadOnlyList<ISettlementWindowButton> registered = SettlementButtonRegistry.Entries;
            int visibleRegistered = 0;
            foreach (var t in registered)
            {
                if (t.IsVisible(settlement)) visibleRegistered++;
            }
            // 4 main built-ins + registered + 1 delete
            int totalCount = mainButtons.Length + visibleRegistered + 1;
            float size = (boundingBox.height - ((totalCount - 1) * margin)) / totalCount;
            int drawn = 0;

            // Main built-in buttons (Upgrade, SpecialActions, Prisoners, Military)
            for (int i = 0; i < mainButtons.Length; i++)
            {
                DrawBuiltInButton(boundingBox, mainButtons[i], size, ref drawn);
            }

            // Registered submod buttons
            for (int i = 0; i < registered.Count; i++)
            {
                ISettlementWindowButton button = registered[i];
                if (!button.IsVisible(settlement)) continue;

                Rect buttonRect = new Rect(boundingBox.x, boundingBox.y + ((size + margin) * drawn), boundingBox.width, size);
                drawn++;

                bool enabled = button.IsEnabled(settlement);
                if (!enabled) GUI.color = Color.gray;

                if (Widgets.ButtonText(buttonRect, button.Label(settlement), active: enabled))
                {
                    button.OnClick(settlement);
                }

                if (!enabled) GUI.color = Color.white;
            }

            // Delete button always last
            DrawBuiltInButton(boundingBox, SettlementButtonType.Delete, size, ref drawn);
        }

        private void DrawBuiltInButton(Rect boundingBox, SettlementButtonType type, float size, ref int drawn)
        {
            Rect buttonRect = new Rect(boundingBox.x, boundingBox.y + ((size + margin) * drawn), boundingBox.width, size);
            drawn++;

            string label = GetButtonLabel(type);
            bool enabled = true;

            if (type == SettlementButtonType.Upgrade && settlement.IsUpgrading)
            {
                GUI.color = Color.gray;
                enabled = false;
            }

            if (Widgets.ButtonText(buttonRect, label, active: enabled))
            {
                HandleBuiltInButtonClick(type);
            }

            if (type == SettlementButtonType.Upgrade && settlement.IsUpgrading)
            {
                GUI.color = Color.white;
            }
        }

        private string GetButtonLabel(SettlementButtonType type)
        {
            switch (type)
            {
                case SettlementButtonType.Upgrade:
                    return settlement.IsUpgrading
                        ? (string)"FCSettlementUpgradeInProgress".Translate()
                        : (string)"FCUpgradeSettlement".Translate();
                case SettlementButtonType.SpecialActions:
                    return "FCSpecialActions".Translate();
                case SettlementButtonType.Prisoners:
                    string label = "FCPrisonersMenu".Translate();
                    int count = settlement.prisonerList.Count;
                    return count > 0 ? label + " (" + count + ")" : label;
                case SettlementButtonType.Military:
                    return "FCMilitary".Translate();
                case SettlementButtonType.Delete:
                    return "FCDeleteSettlement".Translate();
                default:
                    return "";
            }
        }

        private void HandleBuiltInButtonClick(SettlementButtonType type)
        {
            switch (type)
            {
                case SettlementButtonType.Upgrade:
                    if (!settlement.IsUpgrading)
                    {
                        Find.WindowStack.Add(new SettlementUpgradeWindowFc(settlement));
                    }
                    break;
                case SettlementButtonType.Delete:
                    Find.WindowStack.Add(new Dialog_Confirm("FCDeleteSettlementConfirm".Translate(settlement.Name), RemoveSettlement));
                    break;
                case SettlementButtonType.SpecialActions:
                    HandleSpecialActionsClick();
                    break;
                case SettlementButtonType.Prisoners:
                    Find.WindowStack.Add(new FCPrisonerMenu(settlement));
                    break;
                case SettlementButtonType.Military:
                    HandleMilitaryClick();
                    break;
            }
        }

        private void HandleSpecialActionsClick()
        {
            List<FloatMenuOption> list = new List<FloatMenuOption>
            {
                new FloatMenuOption("FCGoToLocation".Translate(), delegate
                {
                    Find.WindowStack.TryRemove(this);
                    settlement.GoTo();
                })
            };

            factionfc.ForEachBehavior(b =>
            {
                var actions = b.GetSettlementActions(factionfc, settlement);
                if (actions != null)
                    list.AddRange(actions);
            });

            if (list.Count == 0)
                list.Add(new FloatMenuOption("FCNoSpecialActions".Translate(), delegate { }));
            Find.WindowStack.Add(new FloatMenu(list));
        }

        private void HandleMilitaryClick()
        {
            if (settlement.MilitaryComp is null) return;

            List<MercenarySquadFC> stationed = settlement.StationedSquads;
            int cap = settlement.SquadCap;

            List<FloatMenuOption> list = new List<FloatMenuOption>();

            // Header: Squads N / M  •  Max squad size K (informational, no action)
            list.Add(new FloatMenuOption(
                "FCSettlementMilHeader".Translate(stationed.Count, cap, settlement.MaxSquadSize),
                null, MenuOptionPriority.High));

            // Per-stationed-squad submenu — Auto-defend toggle, recall (unassign), reassign, reset pawns.
            foreach (MercenarySquadFC mercSquad in stationed)
            {
                MercenarySquadFC capturedSquad = mercSquad;
                list.Add(new FloatMenuOption(
                    "FCSettlementMilSquadEntry".Translate(capturedSquad.DisplayName,
                        capturedSquad.autoDefend ? (string)"FCYes".Translate() : (string)"FCNo".Translate()),
                    delegate
                    {
                        BuildPerSquadMenu(capturedSquad);
                    }));
            }

            // Hire & assign here.
            bool roomForHire = stationed.Count < cap;
            list.Add(new FloatMenuOption("FCSettlementMilHireAndAssign".Translate(),
                roomForHire ? (Action)delegate
                {
                    Find.WindowStack.Add(new Dialog_HireSquad(settlement));
                }
            : (Action)null));

            if (settlement.MilitaryComp.isUnderAttack)
            {
                FCEvent evt = MilitaryUtilFC.ReturnMilitaryEventByLocation(settlement.Tile);
                MilitaryOperation op = evt?.linkedOperation;
                MilitaryForce attackerForce = op?.aggressor?.force;
                MilitaryForce defenderForce = op?.defender?.force;
                if (attackerForce is object && defenderForce is object)
                {
                    double winChance = SimulateBattleFc.CalculateDefenderWinChance(attackerForce, defenderForce);
                    list.Add(new FloatMenuOption(
                        "FCSettlementDefendingInformation".Translate(
                            defenderForce.homeSettlement?.Name ?? "",
                            defenderForce.DefensivePower,
                            (winChance * 100).ToString("F0")), null, MenuOptionPriority.High));
                }

                FCEvent capturedEvt = evt;
                list.Add(new FloatMenuOption("FCChangeDefendingForce".Translate(),
                    capturedEvt is object
                        ? (Action)delegate { Find.WindowStack.Add(new Dialog_DefendSettlement(capturedEvt)); }
                : null));
            }

            Find.WindowStack.Add(new FloatMenu(list));
        }

        /// <summary>Per-squad submenu opened from the settlement's Military button. Toggles
        /// per-squad auto-defend, opens reassignment, dismisses, or resets pawns. Operates on
        /// the squad regardless of which settlement opened the menu — squad-first refactor.</summary>
        private void BuildPerSquadMenu(MercenarySquadFC squad)
        {
            if (squad is null) return;
            MilitaryCustomizationUtil util = FactionCache.FactionComp?.militaryCustomizationUtil;
            List<FloatMenuOption> list = new List<FloatMenuOption>();

            list.Add(new FloatMenuOption("FCSquadMenuInspect".Translate(),
                delegate { Find.WindowStack.Add(new Dialog_SquadInspection(squad)); }));

            list.Add(new FloatMenuOption(
                "FCSquadMenuToggleAutoDefend".Translate(squad.autoDefend ? (string)"FCOn".Translate() : (string)"FCOff".Translate()),
                delegate { squad.autoDefend = !squad.autoDefend; }));

            list.Add(new FloatMenuOption("FCSquadMenuReassign".Translate(),
                squad.IsBusy ? (Action)null : (Action)delegate
                {
                    Find.WindowStack.Add(new Dialog_SquadAssignment(squad));
                }));

            int upgrade = SquadUpgradeUtil.UpgradeCost(squad);
            if (SquadUpgradeUtil.HasUpgradeWork(squad))
            {
                list.Add(new FloatMenuOption("FCSquadMenuUpgrade".Translate(upgrade),
                    squad.IsBusy ? (Action)null : (Action)delegate { MilitaryUtil.ConfirmAndUpgradeAll(squad); }));
            }

            list.Add(new FloatMenuOption("FCSquadMenuDismiss".Translate(),
                squad.IsBusy ? (Action)null : (Action)delegate
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "FCSquadActDismissConfirm".Translate(squad.DisplayName),
                        delegate { util?.DismissSquad(squad); }));
                }));

            if (!squad.Deployment.IsPhysicallyDeployed())
            {
                list.Add(new FloatMenuOption("fcResetSquadPawns".Translate(), delegate
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "fcResetSquadPawnsConfirm".Translate((NamedArgument)squad.DisplayName),
                        delegate
                        {
                            // Bypass InitiateSquad's empty-slot guard — explicit player reset.
                            squad.mercenaries = null;
                            squad.InitiateSquad();
                            Messages.Message("FCResetSquadPawns".Translate(), MessageTypeDefOf.NeutralEvent);
                        }));
                }));
            }

            Find.WindowStack.Add(new FloatMenu(list));
        }

        private Vector2 scrollVectorBuildings = new Vector2();
        public void DrawFacilities(Rect boundingBox)
        {
            Widgets.DrawMenuSection(boundingBox);

            if (settlement?.BuildingsComp is null)
            {
                // can't draw what doesn't exist
                return;
            }

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;

            Rect labelHighlight = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, 30);
            Rect labelTextBox = new Rect(labelHighlight.x + smallMargin, labelHighlight.y + smallMargin, labelHighlight.width - (smallMargin * 2), labelHighlight.height - (smallMargin * 2));
            Widgets.DrawHighlight(labelHighlight);
            Widgets.Label(labelTextBox, "FCFacilities".Translate());

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerCenter;

            float buildingBoxHeight = boundingBox.height - (labelHighlight.height + margin);
            Rect buildingBox = new Rect(boundingBox.x, labelHighlight.yMax + margin, boundingBox.width, buildingBoxHeight);

            // For a row of n buildings, there will only be n-1 spaces between them. So to offset the denominator, we add one buildingSpacing to the numerator.
            int elementsPerRow = (int)((boundingBox.width - (buildingSpacingFromSide * 2) + buildingSpacing) / (buildingBoxSide + buildingSpacing));
            float totalHeight = Mathf.Ceil(((float)settlement.BuildingsComp.Buildings.Count / (float)elementsPerRow)) * (buildingBoxSide + buildingSpacing);

            int row;
            int column;

            Rect box = new Rect(0 + buildingSpacingFromSide, 0, buildingBoxSide, buildingBoxSide);
            Rect buildingIcon = new Rect(4 + box.x, 4 + box.y, buildingBoxSide - 8, buildingBoxSide - 8);

            Rect nBox;
            Rect nBuilding;

            Rect viewRect = ScrollUtil.BeginScrollView(buildingBox, ref scrollVectorBuildings, totalHeight);


            int i = 0;

            foreach (BuildingFC buildingfc in settlement.BuildingsComp.Buildings)
            {
                BuildingFCDef building = buildingfc.def;
                //Update Variables for List
                row = (int)Math.Floor(i / (double)elementsPerRow);
                column = i % elementsPerRow;

                nBox = new Rect(
                    new Vector2(box.x + ((box.width + buildingSpacing) * column),
                                box.y + viewRect.y + ((box.height + buildingSpacing) * row)),
                    box.size);
                nBuilding = new Rect(
                    new Vector2(buildingIcon.x + ((box.width + buildingSpacing) * column),
                                buildingIcon.y + viewRect.y + ((box.height + buildingSpacing) * row)),
                    buildingIcon.size);

                //Actual UI Code
                Widgets.DrawMenuSection(nBox);
                if (i < settlement.BuildingsComp.NumBuildingSlots)
                {
                    TooltipHandler.TipRegion(nBuilding, settlement.BuildingsComp.GetBuildingDescFull(building));
                    if (Widgets.ButtonImage(nBuilding, building.Icon))
                    {
                        Find.WindowStack.Add(new FCBuildingWindow(settlement, i));
                    }
                }
                else
                {
                    WorldSettlementDef sDef = settlement.settlementDef;
                    int requiredLevel = sDef.GetSettlementTypeExtension().GetRequiredLevelForSlot(i, sDef.maxBuildingCount);
                    bool isCapLocked = requiredLevel < 0 ||
                                       settlement.BuildingsComp.NumBuildingSlots >= sDef.maxBuildingCount;
                    string lockTooltip = isCapLocked
                        ? "FCBuildingLockedMax".Translate()
                        : "FCBuildingLockedLevel".Translate(requiredLevel);
                    TooltipHandler.TipRegion(nBox, lockTooltip);
                    if (Widgets.ButtonImage(nBuilding, TexLoad.buildingLocked))
                    {
                        Messages.Message("FCBuildingLocked".Translate(), MessageTypeDefOf.RejectInput);
                    }
                }

                i++;
            }
            ScrollUtil.EndScrollView();
        }
        private Vector2 scrollVectorConstruction = new Vector2();
        private void DrawConstructionBox(Rect boundingBox, int numConstruction, List<BuildingFC> construction)
        {
            Widgets.DrawMenuSection(boundingBox);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            /* Draw the construction header */
            Rect conHeader = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, 30);
            Rect conHeaderText = new Rect(conHeader.x, conHeader.y + smallMargin, conHeader.width, conHeader.height - (smallMargin * 2));
            Widgets.DrawHighlight(conHeader);
            Widgets.Label(conHeaderText, "FCActiveConstruction".Translate());

            Text.Font = GameFont.Small;
            if (numConstruction > 0)
            {
                /* Scroll view time, baby */
                float listHeight = boundingBox.height - (conHeader.height + margin);
                float totalHeight = (constructionListItemHeight * numConstruction) + (margin * (numConstruction - 1));
                Rect listBox = new Rect(boundingBox.x, conHeader.yMax + margin, boundingBox.width, listHeight);
                Rect viewRect = ScrollUtil.BeginScrollView(listBox, ref scrollVectorConstruction, totalHeight);

                float initialY = viewRect.y;
                Text.Anchor = TextAnchor.MiddleLeft;

                if (settlement.IsUpgrading)
                {
                    float progress = (float)(Find.TickManager.TicksGame - settlement.StartUpgradeTick) / (float)(settlement.FinishUpgradeTick - settlement.StartUpgradeTick);
                    Rect upgradeRect = new Rect(viewRect.x + margin,
                                                viewRect.y,
                                                viewRect.width - (margin * 2),
                                                constructionListItemHeight);
                    DrawConstructionInfoBox(upgradeRect, null, "FCSettlementupgrading".Translate(),
                                            "FCCompletiontimer".Translate((settlement.FinishUpgradeTick - Find.TickManager.TicksGame).ToTimeString()),
                                            progress);

                    initialY = upgradeRect.yMax + margin;
                }

                for (int i = 0; i < construction.Count; i++)
                {
                    float progress = (float)(Find.TickManager.TicksGame - construction[i].startedTick) / (float)(construction[i].completionTick - construction[i].startedTick);
                    Rect upgradeRect = new Rect(viewRect.x + margin,
                                                initialY + (i * (constructionListItemHeight + margin)),
                                                viewRect.width - (margin * 2),
                                                constructionListItemHeight);
                    DrawConstructionInfoBox(upgradeRect, construction[i].underConstructionDef.Icon, construction[i].underConstructionDef.LabelCap,
                                            "FCCompletiontimer".Translate(Math.Max(construction[i].completionTick - Find.TickManager.TicksGame, 0).ToTimeString()),
                                            progress);

                    TooltipHandler.TipRegion(upgradeRect, settlement.BuildingsComp?.GetBuildingDescFull(construction[i].underConstructionDef) ?? TaggedString.Empty);
                }

                ScrollUtil.EndScrollView();
            }
        }
        private void DrawConstructionInfoBox(Rect boundingBox, Texture2D icon, string label, string time, float progress)
        {
            Text.Font = GameFont.Tiny;
            float elementHeight = (boundingBox.height - (smallMargin * 4f)) / 3f;
            float iconHeight = (boundingBox.height - (smallMargin * 3f)) * (2f / 3f);
            float labelX = icon == null ? boundingBox.x : boundingBox.x + constructionListIconHeight + smallMargin;
            Rect iconBox = new Rect(boundingBox.x + smallMargin,
                                    boundingBox.y + smallMargin,
                                    constructionListIconHeight,
                                    constructionListIconHeight);
            Rect labelBox = new Rect(labelX + smallMargin * 2,
                                     boundingBox.y + smallMargin,
                                     boundingBox.xMax - (labelX + smallMargin * 3),
                                     constructionListItemLabelHeight);
            Rect labelHighlight = new Rect(labelX + smallMargin,
                                           boundingBox.y + smallMargin,
                                           boundingBox.xMax - (labelX + smallMargin * 2),
                                           constructionListItemLabelHeight);
            Rect timeBox = new Rect(labelBox.x,
                                    labelBox.yMax + smallMargin,
                                    labelBox.width,
                                    constructionListItemLabelHeight);
            Rect progressRect = new Rect(boundingBox.x + smallMargin,
                                         timeBox.yMax + smallMargin,
                                         boundingBox.width - (smallMargin * 2),
                                         constructionListProgressBarHeight);

            string nulabel = Text.ClampTextWithEllipsis(labelBox, label);

            Widgets.DrawMenuSection(boundingBox);
            if (icon != null)
            {
                Widgets.ButtonImage(iconBox, icon);
            }
            Widgets.DrawHighlight(labelHighlight);
            Widgets.Label(labelBox, nulabel);
            Widgets.Label(timeBox, time);
            UIUtil.DrawProgressBar(progressRect, progress);
        }

        private void DrawRightInfo(Rect boundingBox)
        {
            Color origColor = GUI.color;
            UIUtil.DrawColoredBox(boundingBox, accentColor);
            Rect prodBox = new Rect(boundingBox.x + margin, boundingBox.y + margin, boundingBox.width - (margin * 2), boundingBox.height - (margin * 2));
            DrawProduction(prodBox);
        }
        public void DrawProduction(Rect boundingBox)
        {
            Rect header = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, 30f);
            Rect costs = new Rect(boundingBox.x, header.yMax, boundingBox.width, 73f);
            Rect workers = new Rect(boundingBox.x, costs.yMax + margin, boundingBox.width, 69f);

            DrawProductionHeader(header);
            DrawCostBreakdown(costs);
            DrawWorkerBreakdown(workers);

            Rect prodOverview = new Rect(boundingBox.x, workers.yMax + margin, boundingBox.width, boundingBox.yMax - (workers.yMax + margin));
            DrawProductionOverview(prodOverview);
        }
        private void DrawProductionHeader(Rect boundingBox)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(boundingBox.x, boundingBox.y, boundingBox.width, 30), "FCProduction".Translate());
        }

        private void DrawCostBreakdown(Rect boundingBox)
        {
            Text.Font = GameFont.Small;
            float rowHeight = 20f;
            float labelHeight = rowHeight - (smallMargin * 2);
            float labelWidth = (boundingBox.width - margin) / 2f;

            Rect profitBox = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, 28f);
            Rect profitLabel = new Rect(profitBox.x, profitBox.y, labelWidth, profitBox.height);
            Rect profitNum = new Rect(profitLabel.xMax + margin, profitLabel.y, labelWidth, profitBox.height);
            UIUtil.DrawColoredHighlight(profitBox, highlightColor);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(profitLabel, "FCTotal".Translate() + " " + "FCProfit".Translate() + ":");
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(profitNum, new GUIContent(Math.Round(settlement.totalProfit).ToString(), ThingDefOf.Silver.uiIcon));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerCenter;
            labelWidth = (boundingBox.width - (margin * 12f)) / 3f;
            Rect incomeBox = new Rect(boundingBox.x + (margin * 3), profitBox.yMax + margin, labelWidth, rowHeight * 2);
            Rect costsBox = new Rect(incomeBox.xMax + (margin * 3), incomeBox.y, labelWidth, rowHeight * 2);
            Rect taxBonusBox = new Rect(costsBox.xMax + (margin * 3), incomeBox.y, labelWidth, rowHeight * 2);

            Rect incomeLabel = new Rect(incomeBox.x, incomeBox.y, incomeBox.width, incomeBox.height / 2f);
            Rect costLabel = new Rect(costsBox.x, costsBox.y, costsBox.width, costsBox.height / 2f);
            Rect taxBonusLabel = new Rect(taxBonusBox.x, taxBonusBox.y, taxBonusBox.width, taxBonusBox.height / 2f);

            Rect incomeNum = new Rect(incomeLabel.x, incomeLabel.yMax + smallMargin, incomeBox.width, incomeBox.height / 2f);
            Rect costsNum = new Rect(costLabel.x, costLabel.yMax + smallMargin, costsBox.width, costsBox.height / 2f);
            Rect taxBonusNum = new Rect(taxBonusLabel.x, taxBonusLabel.yMax + smallMargin, taxBonusBox.width, taxBonusBox.height / 2f);

            UIUtil.DrawColoredHighlight(incomeBox, highlightColor);
            UIUtil.DrawColoredHighlight(costsBox, highlightColor);
            UIUtil.DrawColoredHighlight(taxBonusBox, highlightColor);

            Widgets.Label(incomeLabel, "FCTotal".Translate() + " " + "FCIncome".Translate());
            Widgets.Label(costLabel, "FCUpkeep".Translate());
            Widgets.Label(taxBonusLabel, "FCTaxBase".Translate());

            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(incomeNum, Math.Round(settlement.totalIncome, 2).ToString());
            Widgets.Label(costsNum, Math.Round(settlement.totalUpkeep, 2).ToString());
            Widgets.Label(taxBonusNum, (settlement.GetSettlementTaxBonus() * 100d).ToString() + "%");

            TooltipHandler.TipRegion(incomeBox, settlement.incomeExp);
            TooltipHandler.TipRegion(costsBox, settlement.upkeepExp);
            TooltipHandler.TipRegion(taxBonusBox, settlement.GetTaxBaseDesc());
        }

        private void DrawWorkerBreakdown(Rect boundingBox)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float rowHeight = 23f;
            float labelHeight = rowHeight - (smallMargin * 2);
            float labelWidth = (boundingBox.width - (margin * 2));

            Rect workerBox = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, rowHeight);
            Rect overMaxBox = new Rect(boundingBox.x, workerBox.yMax, boundingBox.width, rowHeight);
            Rect upkeepBox = new Rect(boundingBox.x, overMaxBox.yMax, boundingBox.width, rowHeight);

            Rect workerLabel = new Rect(workerBox.x + margin, workerBox.y + smallMargin, labelWidth * 0.75f, labelHeight);
            Rect overMaxLabel = new Rect(overMaxBox.x + margin, overMaxBox.y + smallMargin, labelWidth * 0.75f, labelHeight);
            Rect upkeepLabel = new Rect(upkeepBox.x + margin, upkeepBox.y + smallMargin, labelWidth * 0.75f, labelHeight);

            Rect workerNum = new Rect(workerLabel.xMax, workerLabel.y, labelWidth * 0.25f, labelHeight);
            Rect overMaxNum = new Rect(overMaxLabel.xMax, overMaxLabel.y, labelWidth * 0.25f, labelHeight);
            Rect upkeepNum = new Rect(upkeepLabel.xMax, upkeepLabel.y, labelWidth * 0.25f, labelHeight);

            UIUtil.DrawColoredHighlight(workerBox, highlightColor);
            TooltipHandler.TipRegion(overMaxBox, "FCAssignedOvermaxWorkersTooltip".Translate());
            UIUtil.DrawColoredHighlight(upkeepBox, highlightColor);

            Widgets.Label(workerLabel, "FCAssignedWorkers".Translate());
            Widgets.Label(overMaxLabel, "FCAssignedOvermaxWorkers".Translate());
            Widgets.Label(upkeepLabel, "FCCostPerWorker".Translate());

            Text.Anchor = TextAnchor.MiddleRight;
            int numWorkers = (int)Math.Min(settlement.workers, settlement.workersMax);
            int numOvermaxWorkers = (int)Math.Max(0, settlement.workers - settlement.workersMax);
            Widgets.Label(workerNum, "FCAssignedWorkersValue".Translate(numWorkers, settlement.workersMax));
            Widgets.Label(overMaxNum, "FCAssignedOvermaxWorkersValue".Translate(numOvermaxWorkers, settlement.workersUltraMax - settlement.workersMax));
            Widgets.Label(upkeepNum, settlement.workerCost.ToString());
        }

        private void DrawProductionOverview(Rect boundingBox)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            /* Header */
            float colWidth = (boundingBox.width - (margin * 7)) / 8f;
            float headerHeight = 44;

            Rect workersBox = new Rect(boundingBox.x + colWidth + margin, boundingBox.y, colWidth, headerHeight);
            Rect prodHeaderBox = new Rect(workersBox.xMax + margin, boundingBox.y, colWidth * 3 + margin * 2, headerHeight / 2f);
            Rect prodBaseBox = new Rect(workersBox.xMax + margin, prodHeaderBox.yMax, colWidth, headerHeight / 2f);
            Rect prodMultBox = new Rect(prodBaseBox.xMax + margin, prodBaseBox.y, colWidth, prodBaseBox.height);
            Rect prodFinalBox = new Rect(prodMultBox.xMax + margin, prodMultBox.y, colWidth, prodMultBox.height);
            Rect prodTotalBox = new Rect(prodHeaderBox.xMax + margin, boundingBox.y, colWidth, headerHeight);

            Rect incomeBox = new Rect(prodTotalBox.xMax + margin, boundingBox.y, colWidth * 2 + margin, headerHeight / 2f);
            Rect incomeRawBox = new Rect(incomeBox.x, incomeBox.yMax, colWidth, headerHeight / 2f);
            Rect incomeNetBox = new Rect(incomeRawBox.xMax + margin, incomeRawBox.y, colWidth, headerHeight / 2f);
            UIUtil.DrawColoredHighlight(workersBox, highlightColor);
            Widgets.Label(workersBox, "FCWorkers".Translate());

            UIUtil.DrawColoredHighlight(prodHeaderBox, highlightColor);
            Widgets.Label(prodHeaderBox, "FCPerWorkerProduction".Translate());
            UIUtil.DrawColoredHorizontalLine(prodHeaderBox.x, prodHeaderBox.yMax, prodHeaderBox.width, accentColor);
            UIUtil.DrawColoredHighlight(prodBaseBox, highlightColor);
            Widgets.Label(prodBaseBox, "FCBase".Translate());
            UIUtil.DrawColoredHighlight(prodMultBox, highlightColor);
            Widgets.Label(prodMultBox, "FCMult".Translate());
            UIUtil.DrawColoredHighlight(prodFinalBox, highlightColor);
            Widgets.Label(prodFinalBox, "FCFinal".Translate());

            UIUtil.DrawColoredHighlight(prodTotalBox, highlightColor);
            Widgets.Label(prodTotalBox, "FCTotal".Translate());

            UIUtil.DrawColoredHighlight(incomeBox, highlightColor);
            Widgets.Label(incomeBox, "FCIncome".Translate());
            UIUtil.DrawColoredHorizontalLine(incomeBox.x, incomeBox.yMax, incomeBox.width, accentColor);
            UIUtil.DrawColoredHighlight(incomeRawBox, highlightColor);
            Widgets.Label(incomeRawBox, "FCRaw".Translate());
            TooltipHandler.TipRegion(incomeRawBox, "FCRawIncomeDesc".Translate());
            UIUtil.DrawColoredHighlight(incomeNetBox, highlightColor);
            Widgets.Label(incomeNetBox, "FCNet".Translate());
            TooltipHandler.TipRegion(incomeNetBox, "FCNetIncomeDesc".Translate());

            Rect resourceArea = new Rect(boundingBox.x, workersBox.yMax + margin, boundingBox.width, boundingBox.yMax - (workersBox.yMax + margin));
            DrawResources(resourceArea, colWidth);
        }
        private Vector2 scrollVectorResources = new Vector2();
        private void DrawResources(Rect boundingBox, float colWidth)
        {
            float rowHeight = 25f;
            List<ResourceFC> availableResources = settlement.Resources;
            float totalHeight = (availableResources.Count * rowHeight) + (availableResources.Count * margin);
            Rect viewRect = ScrollUtil.BeginScrollView(boundingBox, ref scrollVectorResources, totalHeight);

            Rect totalProdCol = new Rect(5f * (colWidth + margin), 0f, colWidth, viewRect.height - (margin / 2f));
            Rect incomeRawCol = new Rect(6f * (colWidth + margin), 0f, colWidth, viewRect.height - (margin / 2f));
            Rect incomeNetCol = new Rect(7f * (colWidth + margin), 0f, colWidth, viewRect.height - (margin / 2f));
            UIUtil.DrawColoredHighlight(totalProdCol, highlightColor);
            UIUtil.DrawColoredHighlight(incomeRawCol, highlightColor);
            Widgets.DrawMenuSection(incomeNetCol);
            TooltipHandler.TipRegion(incomeRawCol, "FCRawIncomeDesc".Translate());

            for (int i = 0; i < availableResources.Count; i++)
            {
                ResourceFC resource = availableResources[i];
                if (resource == null) continue;

                float rectY = i * (rowHeight + margin);
                /* Alternating highlights, to make rows easier to read/track */
                if (i % 2 == 0)
                {
                    Rect rowHighlight = new Rect(0f, rectY - (margin / 2f), viewRect.width, rowHeight + margin);
                    UIUtil.DrawColoredHighlight(rowHighlight, highlightColor);
                }

                // Resource color accent
                Widgets.DrawBoxSolid(new Rect(0f, rectY, 3f, rowHeight), resource.def.color);

                float resourceImgSize = Math.Min(colWidth, rowHeight);
                float resourceImxgX = (colWidth - resourceImgSize) / 2f;
                Rect resourceImgRect = new Rect(resourceImxgX, rectY, resourceImgSize, resourceImgSize);
                Widgets.ButtonImage(resourceImgRect, resource.def.Icon);
                TooltipHandler.TipRegion(resourceImgRect, resource.def.LabelCap);

                //Production Efficiency
                float arrowButtonHeight = Math.Min(rowHeight, 20f);
                float arrowButtonY = rectY + ((rowHeight - arrowButtonHeight) / 2f);
                Rect workersDecArrow = new Rect(colWidth + margin, arrowButtonY, colWidth / 3f, arrowButtonHeight);
                Rect workersNum = new Rect(workersDecArrow.xMax, rectY, workersDecArrow.width, rowHeight);
                Rect workersIncArrow = new Rect(workersNum.xMax, arrowButtonY, workersDecArrow.width, arrowButtonHeight);
                Widgets.Label(workersNum, resource.assignedWorkers.ToString());
                if (Widgets.ButtonText(workersDecArrow, "<")) IncreaseWorkers(resource, true);
                if (Widgets.ButtonText(workersIncArrow, ">")) IncreaseWorkers(resource);

                //Base Production
                Rect baseProd = new Rect(workersIncArrow.xMax + margin, rectY, colWidth, rowHeight);
                Widgets.Label(baseProd, TextUtil.FloorStat(resource.productionBase));
                TooltipHandler.TipRegion(baseProd, resource.GetProductionAdditivesDesc());

                //Modifier
                Rect multProd = new Rect(baseProd.xMax + margin, rectY, colWidth, rowHeight);
                Widgets.Label(multProd, TextUtil.FloorStat(resource.productionMult));
                TooltipHandler.TipRegion(multProd, resource.GetProductionMultipliersDesc());

                //Final Base
                Rect finalProd = new Rect(multProd.xMax + margin, rectY, colWidth, rowHeight);
                Widgets.Label(finalProd, (TextUtil.FloorStat(resource.production)));

                //Total Production
                Rect totalProd = new Rect(finalProd.xMax + margin, rectY, colWidth, rowHeight);
                Widgets.Label(totalProd, (TextUtil.FloorStat(resource.rawTotalProduction)));
                if (resource.AccumulationDays > 0)
                {
                    int totalPeriodDays = FCSettings.timeBetweenTaxes / GenDate.TicksPerDay;
                    string tooltip = "FCTotalProdTooltip".Translate(
                        TextUtil.FloorStat(resource.InstantaneousProduction),
                        TextUtil.FloorStat(resource.AccumulatedAverageProduction),
                        resource.AccumulationDays.ToString(),
                        totalPeriodDays.ToString());
                    TooltipHandler.TipRegion(totalProd, tooltip);
                }

                //Raw Income (total production as silver, before stockpile diversions and tithes)
                Rect incomeRawBox = new Rect(totalProd.xMax + margin, rectY, colWidth, rowHeight);
                Widgets.Label(incomeRawBox, (TextUtil.FloorStat(resource.grossMarketValue)));

                //Net Income, after stockpile diversions and tithes
                Rect incomeNetBox = new Rect(incomeRawBox.xMax + margin, rectY, colWidth, rowHeight);
                Widgets.Label(incomeNetBox, (TextUtil.FloorStat(resource.actualIncome)));

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("FCNetIncomeBreakdownGross".Translate(TextUtil.FloorStat(resource.grossMarketValue)));
                if (resource.stockpileMarketValue > 0)
                    sb.AppendLine("FCNetIncomeBreakdownStockpile".Translate(TextUtil.FloorStat(resource.stockpileMarketValue)));
                if (resource.titheTotalValue > 0)
                    sb.AppendLine("FCNetIncomeBreakdownTithes".Translate(TextUtil.FloorStat(resource.titheTotalValue)));
                double titheOffset = Math.Min(resource.titheTotalValue, resource.externalTitheBudget);
                if (titheOffset > 0)
                    sb.AppendLine("FCNetIncomeBreakdownTitheInjection".Translate(TextUtil.FloorStat(titheOffset)));
                sb.AppendLine("FCNetIncomeBreakdownNet".Translate(TextUtil.FloorStat(resource.actualIncome)));
                if (resource.AccumulationDays > 0)
                {
                    int totalPeriodDays = FCSettings.timeBetweenTaxes / GenDate.TicksPerDay;
                    sb.AppendLine("-----");
                    sb.Append("FCNetIncomeBreakdownAvg".Translate(
                        TextUtil.FloorStat(resource.averageActualIncome),
                        resource.AccumulationDays.ToString(),
                        totalPeriodDays.ToString()));
                }
                TooltipHandler.TipRegion(incomeNetBox, sb.ToString());
            }

            ScrollUtil.EndScrollView();
        }

        /// <summary>
        /// Increases the amount of workers in a settlement. Decreases if <paramref name="negative"/> is true. Modifies the amount based on if shift/ctrl are held
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="negative"></param>
        private void IncreaseWorkers(ResourceFC resource, bool negative = false)
        {
            if (settlement.MilitaryComp?.isUnderAttack == true)
            {
                Messages.Message("FCSettlementUnderAttack".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            //if clicked to lower amount of workers
            settlement.IncreaseWorkers(resource, (negative ? -1 : 1) * UIUtil.GetModifier);
        }
    }
}