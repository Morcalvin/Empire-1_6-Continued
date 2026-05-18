using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public class CreateColonyWindowFc : Window
    {
        public sealed override Vector2 InitialSize => new Vector2(300f, 650f);

        public PlanetTile currentTileSelected = PlanetTile.Invalid;
        public PlanetTile oldTileSelected = PlanetTile.Invalid;
        public BiomeResourceDef currentBiomeSelected;
        public bool settlementCostModified;
        public int timeToTravel = -1;

        public WorldSettlementDef currentSettlementType;
        public WorldSettlementDef oldSettlementType;

        private int settlementCreationCost = 0;
        private readonly FactionFC faction = null;

        private int SettlementCreationBaseCost => (int)(faction.GetStatValue(FCStatDefOf.createSettlementMultiplier) *
                                                        (currentSettlementType.GetSettlementTypeExtension().GetCreationCost() + faction.GetStatValue(FCStatDefOf.createSettlementBaseCost)));

        /* UI math stuff! Yaaaay!
         * what a pain
         */
        public const int verticalMargins = 5;
        public const int newColonyHeader_height = 40;
        public const int upperBox_height = 50;
        public const int costConstructionBox_height = 50;
        public const int productionLabel_height = 40;
        public int prodBoxHeight = 220;
        public const int productionHeaders_height = 25;
        public const int button_height = 32;

        public CreateColonyWindowFc()
        {
            forcePause = false;
            draggable = true;
            preventCameraMotion = false;
            doCloseX = true;
            faction = FindFC.FactionComp;
            if (faction == null)
            {
                LogUtil.Error("FactionFC WorldComponent is null in CreateColonyWindowFC constructor!");
                return;
            }
            prodBoxHeight = faction.FactionResources.Count * 22 + 10;
            windowRect = new Rect(UI.screenWidth - InitialSize.x - 5, (UI.screenHeight - InitialSize.y) / 2f - (UI.screenHeight / 8f), InitialSize.x, InitialSize.y);
            currentSettlementType = GetDefaultSettlementType();
            oldSettlementType = null;
        }



        //Pre-Opening
        public override void PreOpen()
        {
            FactionFC faction = FindFC.FactionComp;
            if (faction is null)
            {
                //panic!
                // the faction worldcomp should never be null. If it is, something majorly bad has happened. Can't hurt to check, though
                LogUtil.Error("Attempted to open CreateColonyWindowFC when FactionFC WorldComponent does not exist! Bailing out!");
                return;
            }

            faction.layersForTilePicker = currentSettlementType.planetLayers;
            faction.roadBuilder.shouldDrawPaths = true;

            Find.TilePicker.StartTargeting_NewTemp(delegate (PlanetTile tile)
            {
                if (CanCreateSettlementHere())
                {
                    return true;
                }
                return false;
            }, delegate (PlanetTile tile)
            {
                Find.World.renderer.wantedMode = WorldRenderMode.None;
                GetTileData();
            }, allowEscape: true, showRandomButton: false, showNextButton: false, canCancel: true);
        }

        //Drawing
        public override void DoWindowContents(Rect inRect)
        {
            if (faction == null || !Find.TilePicker.Active)
            {
                Close();
                return;
            }
            GetTileData();

            //grab before anchor/font
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            CalculateSettlementCreationCost();

            //Draw Label
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect newColonyHeader = new Rect(0, 0, 260, newColonyHeader_height);
            Widgets.Label(newColonyHeader, "FCSettleANewColony".Translate());

            //hori line
            Widgets.DrawLineHorizontal(0, newColonyHeader_height, 300);


            //Upper menu
            Rect upperBox = new Rect(5, newColonyHeader.yMax + verticalMargins, 258, upperBox_height);
            Widgets.DrawMenuSection(upperBox); //height was originally 220

            DrawLabelBox(new Rect(10, newColonyHeader.yMax + verticalMargins, 100, costConstructionBox_height), (currentSettlementType.isConstructed ? "FCConstructionTime".Translate() : "FCTravelTime".Translate()), timeToTravel.ToTimeString());
            DrawLabelBox(new Rect(153, newColonyHeader.yMax + verticalMargins, 100, costConstructionBox_height), "FCInitialCost".Translate(), settlementCreationCost + " " + "FCSilver".Translate());

            // Additional founding costs from registered validators
            float additionalCostHeight = 0f;
            if (currentTileSelected.Valid)
            {
                List<string> additionalCosts = FoundingValidatorRegistry.GetCostDescriptions(currentTileSelected, currentSettlementType);
                if (additionalCosts.Count > 0)
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.UpperCenter;
                    float costY = upperBox.yMax + verticalMargins;
                    foreach (string costDesc in additionalCosts)
                    {
                        float lineHeight = Text.CalcHeight(costDesc, 258f);
                        Widgets.Label(new Rect(5, costY, 258, lineHeight), costDesc);
                        costY += lineHeight;
                    }
                    additionalCostHeight = costY - (upperBox.yMax + verticalMargins);
                }
            }

            //Lower Menu label
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect productionLabelBox = new Rect(0, upperBox.yMax + verticalMargins + additionalCostHeight, 268, productionLabel_height); //0, 270, 268, 40
            Widgets.Label(productionLabelBox, "FCBaseProductionStats".Translate());


            //Lower menu
            Rect prodBox = new Rect(5, productionLabelBox.yMax + verticalMargins, 258, prodBoxHeight); //5, 210, 258, 220
            Widgets.DrawMenuSection(prodBox);

            //Draw production
            DrawProduction(prodBox);

            float curHeight = prodBox.yMax;
            curHeight = DrawChooseSettlementTypeButton(curHeight);
            curHeight = DrawCreateSettlementButton(curHeight);

            windowRect.height = curHeight + (verticalMargins * 7);

            //reset anchor/font
            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }


        private void GetTileData()
        {
            PlanetTile selectedTile = Find.WorldSelector.SelectedTile;
            if (selectedTile.Valid && selectedTile != currentTileSelected)
            {
                currentTileSelected = selectedTile;
            }
            else /* If a WorldObject is selected, then get the tile underneath it. */
            {
                WorldObject obj = Find.WorldSelector.SingleSelectedObject;
                if (obj != null && obj.Tile != null && obj.Tile.Valid)
                {
                    currentTileSelected = obj.Tile;
                }
            }
            if (currentTileSelected == PlanetTile.Invalid)
            {
                return;
            }
            /* No need to keep redoing all of the below calculations if the selected tile or settlement type hasn't changed */
            if (currentTileSelected == oldTileSelected &&
                currentSettlementType == oldSettlementType)
            {
                return;
            }
            oldTileSelected = currentTileSelected;
            oldSettlementType = currentSettlementType;
            LogUtil.Message($"Called GetTileData on tile {selectedTile}. Valid: {selectedTile.Valid} layer: {selectedTile.Layer} tileid: {selectedTile.tileId}");

            if (currentSettlementType.biomeResourceOverride != null)
            {
                currentBiomeSelected = currentSettlementType.biomeResourceOverride;
                //default biome
                if (!DefDatabase<BiomeResourceDef>.AllDefs.Contains(currentBiomeSelected))
                {
                    LogUtil.Error($"Settlement type {currentSettlementType.LabelCap} has an invalid override biome. Using default biome.");
                    currentBiomeSelected = BiomeResourceDefOf.defaultBiome;
                }
            }
            else
            {
                currentBiomeSelected = DefDatabase<BiomeResourceDef>.GetNamed(currentTileSelected.Tile.PrimaryBiome.defName, false);
                //default biome
                if (currentBiomeSelected == default(BiomeResourceDef))
                {
                    LogUtil.Warning($"Selected tile has biome {currentTileSelected.Tile.PrimaryBiome.LabelCap}, which is not defined for Empire settlements. Using default biome.");
                    currentBiomeSelected = BiomeResourceDefOf.defaultBiome;
                }
            }

            FCWindow_CreateColonyStatModifiers.RefreshForTile(currentTileSelected, currentBiomeSelected);

            if (IsTileValidForSettlement())
            {
                currentTileSelected = currentSettlementType.GetTileForSettlement(currentTileSelected);
                timeToTravel = currentSettlementType.GetCreationTime(currentTileSelected);
            }
            else
            {
                timeToTravel = 0;
            }
        }

        private static WorldSettlementDef GetDefaultSettlementType()
        {
            foreach (WorldSettlementDef def in DefDatabase<WorldSettlementDef>.AllDefs)
            {
                if (def.IsUnlocked()) return def;
            }
            return WorldSettlementDefOf.WorldSettlementDef_Surface;
        }

        private void CalculateSettlementCreationCost()
        {
            double baseCost = SettlementCreationBaseCost;
            settlementCreationCost = (int)(baseCost * faction.GetStatValue(FCStatDefOf.settlementCostMultiplier));

            settlementCostModified = settlementCreationCost != (int)baseCost;
        }

        private void DrawProduction(Rect prodBox)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            //Production headers
            Widgets.Label(new Rect(40, prodBox.y, 60, productionHeaders_height), "FCBase".Translate()); // 40, 190, 60, 25
            Widgets.Label(new Rect(110, prodBox.y, 60, productionHeaders_height), "FCModifier".Translate());
            Widgets.Label(new Rect(180, prodBox.y, 60, productionHeaders_height), "FCFinal".Translate());

            if (currentTileSelected != PlanetTile.Invalid)
            {
                List<ResourceDisplay> resTypes = faction.FactionResources;
                List<ResourceTypeDef> settlementResourceTypes = currentSettlementType.GetResourceDefs();
                int startHeight = (int)prodBox.y + productionHeaders_height + verticalMargins;

                for (int i = 0; i < resTypes.Count; i++)
                {
                    ResourceTypeDef titheType = resTypes[i].resourceDef;
                    int baseHeight = 15;
                    string resLabel = resTypes[i].label;
                    if (Widgets.ButtonImage(new Rect(20, startHeight + i * (5 + baseHeight), baseHeight, baseHeight), resTypes[i].Icon, true, resLabel.CapitalizeFirst()))
                    {
                        Find.WindowStack.Add(new DescWindowFc("FCSettlementProductionOf".Translate() + ": " + resLabel, resLabel.CapitalizeFirst()));
                    }
                    /* currentBiomeSelected already accounted for the settlement type's biome resource override. So if we grab resources from it now,
                     * it should accurately represent the resources that the settlement would produce */
                    ResourceAvailability biomeRes = currentBiomeSelected.GetBiomeResource(titheType);
                    ResourceAvailability settleRes = currentSettlementType.GetSettlementResource(titheType);

                    float xMod = 70f;
                    Rect baseRect = new Rect(40, startHeight + i * (5 + baseHeight), 60, baseHeight + 2);

                    if (biomeRes == null || settleRes == null || !titheType.ResourceTypeAllowedByTech(faction.techLevel))
                    {
                        /* One of the following is true:
                         *  1. The biome does not support this resource type
                         *  2. The settlement type does not support this resource type
                         *  3. The resource type's research requirements have not been met
                         * So show it as producing nothing.
                         */
                        TaggedString na = "N/A".ApplyTag(TagType.Gray);
                        Widgets.Label(baseRect, na);
                        Widgets.Label(baseRect.CopyAndShift(xMod, 0f), na);
                        Widgets.Label(baseRect.CopyAndShift(xMod * 2f, 0f), na);
                    }
                    else
                    {
                        double baseProduction = biomeRes.additive + settleRes.additive
                            + titheType.GetExtensionAdditives(currentTileSelected)
                            + titheType.GetMutatorAdditives(currentTileSelected)
                            + titheType.GetLandmarkAdditives(currentTileSelected);
                        double baseMultiplier = biomeRes.multiplier * settleRes.multiplier
                            * titheType.GetExtensionMultipliers(currentTileSelected)
                            * titheType.GetMutatorMultipliers(currentTileSelected)
                            * titheType.GetLandmarkMultipliers(currentTileSelected);
                        double total = baseProduction * baseMultiplier;

                        Rect multRect = baseRect.CopyAndShift(xMod, 0f);
                        Widgets.Label(baseRect, Math.Round(baseProduction, 2).ToString());
                        Widgets.Label(multRect, Math.Round(baseMultiplier, 2).ToString());
                        Widgets.Label(baseRect.CopyAndShift(xMod * 2f, 0f), Math.Round(total, 2).ToString());

                        // Breakdown tooltips mirroring SettlementWindowFC.cs.
                        StringBuilder addSb = new StringBuilder();
                        if (biomeRes.additive != 0)
                            addSb.Append(TextUtil.ColorizeAdditiveBonus(biomeRes.additive)).Append(" - ").Append(currentBiomeSelected.LabelCap).Append('\n');
                        if (settleRes.additive != 0)
                            addSb.Append(TextUtil.ColorizeAdditiveBonus(settleRes.additive)).Append(" - ").Append(currentSettlementType.LabelCap).Append('\n');
                        titheType.ForEachExtensionAdditive(currentTileSelected, (label, value) =>
                            addSb.Append(TextUtil.ColorizeAdditiveBonus(value)).Append(" - ").Append(label).Append('\n'));
                        titheType.ForEachMutatorAdditive(currentTileSelected, (label, value) =>
                            addSb.Append(TextUtil.ColorizeAdditiveBonus(value)).Append(" - ").Append(label).Append('\n'));
                        titheType.ForEachLandmarkAdditive(currentTileSelected, (label, value) =>
                            addSb.Append(TextUtil.ColorizeAdditiveBonus(value)).Append(" - ").Append(label).Append('\n'));

                        StringBuilder multSb = new StringBuilder();
                        if (biomeRes.multiplier != 1)
                            multSb.Append(TextUtil.ColorizeMultiplierBonus(biomeRes.multiplier)).Append(" - ").Append(currentBiomeSelected.LabelCap).Append('\n');
                        if (settleRes.multiplier != 1)
                            multSb.Append(TextUtil.ColorizeMultiplierBonus(settleRes.multiplier)).Append(" - ").Append(currentSettlementType.LabelCap).Append('\n');
                        titheType.ForEachExtensionMultiplier(currentTileSelected, (label, value) =>
                            multSb.Append(TextUtil.ColorizeMultiplierBonus(value)).Append(" - ").Append(label).Append('\n'));
                        titheType.ForEachMutatorMultiplier(currentTileSelected, (label, value) =>
                            multSb.Append(TextUtil.ColorizeMultiplierBonus(value)).Append(" - ").Append(label).Append('\n'));
                        titheType.ForEachLandmarkMultiplier(currentTileSelected, (label, value) =>
                            multSb.Append(TextUtil.ColorizeMultiplierBonus(value)).Append(" - ").Append(label).Append('\n'));

                        if (addSb.Length > 0)
                            TooltipHandler.TipRegion(baseRect, addSb.ToString().TrimEnd());
                        if (multSb.Length > 0)
                            TooltipHandler.TipRegion(multRect, multSb.ToString().TrimEnd());
                    }
                }
                /* Highlight the total value */
                Widgets.DrawHighlight(new Rect(180, startHeight - verticalMargins, 60, (resTypes.Count * 20f) + verticalMargins));
            }
        }
        private float DrawChooseSettlementTypeButton(float curHeight)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            int buttonLength = 200;
            Rect button = new Rect((InitialSize.x - 32 - buttonLength) / 2f, curHeight + verticalMargins, buttonLength, button_height);
            if (Widgets.ButtonText(button, currentSettlementType.LabelCap))
            {
                Find.WindowStack.Add(new FCWindow_SettlementTypePicker(delegate (WorldSettlementDef selected)
                {
                    currentSettlementType = selected;
                    FindFC.FactionComp.layersForTilePicker = selected.planetLayers;
                }));
            }
            return button.yMax;
        }

        private float DrawCreateSettlementButton(float curHeight)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            int buttonLength = 200;
            Rect button = new Rect((InitialSize.x - 32 - buttonLength) / 2f, curHeight + verticalMargins, buttonLength, button_height);
            if (Widgets.ButtonText(button, "FCSettle".Translate() + ": (" + settlementCreationCost + ")")) //add inital cost
            {
                if (!CanCreateSettlementHere()) return button.yMax;

                if (FCSettings.showSettleConfirm)
                {
                    Find.WindowStack.Add(new FCWindow_ConfirmSettle(
                        currentSettlementType,
                        settlementCreationCost,
                        () =>
                        {
                            DoFoundSettlement();
                            Close();
                        },
                        dontShow => FCSettings.showSettleConfirm = !dontShow));
                }
                else
                {
                    DoFoundSettlement();
                }
            }
            return button.yMax;
        }

        private void DoFoundSettlement()
        {
            LogUtil.Message($"DrawCreateSettlementButton: creating settleNewColony event");

            PaymentUtil.PaySilver(settlementCreationCost, PaymentUtil.Reason_SettlementCreation);
            FoundingValidatorRegistry.NotifyFounded(currentTileSelected, currentSettlementType);

            //create settle event
            FCEvent evt = FCEventMaker.MakeEvent(FCEventDefOf.settleNewColony);
            evt.location = currentTileSelected;
            evt.timeTillTrigger = Find.TickManager.TicksGame + timeToTravel;
            evt.source = faction.capitalLocation;
            evt.settlementToCreate = currentSettlementType;
            if (currentSettlementType.isConstructed)
            {
                evt.customDescription = "FCColonyConstruction".Translate(currentSettlementType.LabelCap);
            }
            else
            {
                evt.customDescription = "FCSettleEventDesc".Translate(
                    currentSettlementType.LabelCap,
                    currentTileSelected.Tile.PrimaryBiome.LabelCap,
                    (evt.timeTillTrigger - Find.TickManager.TicksGame).ToTimeString());
            }
            evt.hasCustomDescription = true;
            faction.eventManager.AddEvent(evt);

            faction.settlementCaravansList.Add(evt.location);
            Messages.Message((currentSettlementType.isConstructed ? "FCConstructionToLocation".Translate() : "FCCaravanSentToLocation".Translate()) + " " +
                             (evt.timeTillTrigger - Find.TickManager.TicksGame).ToTimeString() + "!", MessageTypeDefOf.PositiveEvent);

            DoPostEventCreationTraitThings();
        }

        /// <summary>
        /// Checks only tile validity (location, adjacency, caravan list). Used for travel time
        /// and production preview — does NOT check silver or founding validator resource costs.
        /// </summary>
        private bool IsTileValidForSettlement()
        {
            return WorldTileChecker.IsValidTileForNewSettlement(currentTileSelected, currentSettlementType, null)
                && !faction.CheckSettlementCaravansList(currentTileSelected);
        }

        private bool CanCreateSettlementHere(bool silent = false)
        {
            StringBuilder reason = new StringBuilder();
            if (!WorldTileChecker.IsValidTileForNewSettlement(currentTileSelected, currentSettlementType, reason)
                || faction.CheckSettlementCaravansList(currentTileSelected)
                || !PlayerHasEnoughSilver(reason)
                || !FoundingValidatorRegistry.CanFound(currentTileSelected, currentSettlementType, reason))
            {
                if (!silent)
                {
                    Messages.Message(reason.ToString(), MessageTypeDefOf.RejectInput);
                }
                LogUtil.Message($"Rejected settlement founding due to reason: {reason}");
                return false;
            }

            return true;
        }

        private void DoPostEventCreationTraitThings()
        {
            if (settlementCostModified)
            {
                faction.ForEachBehavior(b => b.OnSettlementCostPaid(faction));
            }
        }

        private bool PlayerHasEnoughSilver(StringBuilder reason)
        {
            if (PaymentUtil.GetSilver() >= settlementCreationCost) return true;

            reason?.Append("FCNotEnoughSilverToSettle".Translate() + "!");
            return false;
        }

        public void DrawLabelBox(Rect rect, string text1, string text2)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            //Draw highlight
            Widgets.DrawHighlight(new Rect(rect.x, rect.y + rect.height / 8, rect.width, rect.height * 3f / 8f));
            Widgets.Label(new Rect(rect.x, rect.y + rect.height / 16, rect.width, rect.height / 2f), text1);

            //Bottom Text
            Widgets.Label(new Rect(rect.x, rect.y + rect.height / 2, rect.width, rect.height / 2f), text2);
        }

        public override void PreClose()
        {
            base.PreClose();
            FactionFC comp = FindFC.FactionComp;
            if (comp != null)
            {
                comp.layersForTilePicker = null;
                comp.roadBuilder.shouldDrawPaths = false;
            }
            Find.TilePicker.StopTargeting();
            FCWindow_CreateColonyStatModifiers.TryClose();
        }

        /// <summary>
        /// A stub for harmony patch targeting
        /// </summary>
        public override void PostOpen()
        {
            base.PostOpen();
        }
    }
}