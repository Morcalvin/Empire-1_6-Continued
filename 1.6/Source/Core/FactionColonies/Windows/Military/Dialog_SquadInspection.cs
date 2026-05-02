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
    /// Per-squad inspection window. Title band + two-column context band (settlement / template)
    /// + action bar across the top, then a scrolling list of pawn cards (one per slot) accented
    /// by pawn health, then any submod-registered <see cref="ISquadInspectionSection"/>s below.
    ///
    /// Submods can extend the window by registering <see cref="ISquadInspectionSection"/>s
    /// via <see cref="SquadInspectionRegistry"/>; their content renders below the card list
    /// in declared <see cref="ISquadInspectionSection.Order"/>.
    ///
    /// Strict-manual outfit policy: every gear-altering action requires an explicit click.
    /// Template swap and clear are free. Fill, Upgrade All, and per-pawn Upgrade charge silver
    /// at the moment the player commits.
    /// </summary>
    public class Dialog_SquadInspection : Window
    {
        public override Vector2 InitialSize => new Vector2(620f, 680f);

        private readonly MercenarySquadFC squad;
        private Vector2 scroll;

        /* Layout constants */
        private const float TitleBandHeight = 38f;
        private const float ContextBandHeight = 72f;
        private const float ActionBarHeight = 32f;
        private const float BandGap = 6f;
        private const float SmallGap = 4f;

        private const float CardHeight = 72f;
        private const float CardGap = 4f;
        private const float AccentBarWidth = 3f;
        private const float PortraitSize = 60f;
        private const float CardOuterPad = 5f;

        private const float IconButtonSize = 22f;
        private const float InfoCardSize = 24f;
        private const float ActionButtonWidth = 110f;
        private const float ActionButtonHeight = 26f;

        private static readonly Color CaptionTextColor = new Color(0.7f, 0.7f, 0.7f);
        private static readonly Color DimValueColor = new Color(0.65f, 0.65f, 0.65f);

        public Dialog_SquadInspection(MercenarySquadFC squad)
        {
            this.squad = squad;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
            preventCameraMotion = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (squad is null)
            {
                Widgets.Label(inRect, "FCSquadInspectionNoSquad".Translate());
                return;
            }

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            float y = inRect.y;

            /* Title band: full-width highlighted header with squad name + rename pencil */
            Rect titleRect = new Rect(inRect.x, y, inRect.width, TitleBandHeight);
            DrawTitleBand(titleRect);
            y = titleRect.yMax + BandGap;

            /* Two-column context band: Settlement | Template (caption / value / buttons) */
            Rect contextRect = new Rect(inRect.x, y, inRect.width, ContextBandHeight);
            DrawContextBand(contextRect);
            y = contextRect.yMax + (BandGap / 2f);

            Rect barAboveActions = new Rect(inRect.x + 4f, y, inRect.width - 8f, 1f);
            TexLoad.DrawHorizontalPeakGradient(barAboveActions, Color.gray);
            y = barAboveActions.yMax + (BandGap / 2f);

            /* Action bar: Fill empty / Upgrade all */
            Rect actionsRect = new Rect(inRect.x, y, inRect.width, ActionBarHeight);
            DrawActionBar(actionsRect);
            y = actionsRect.yMax + (BandGap / 2f);
            
            Rect barBelowActions = new Rect(inRect.x + 4f, y, inRect.width - 8f, 1f);
            TexLoad.DrawHorizontalPeakGradient(barBelowActions, Color.gray);
            y = barBelowActions.yMax + (BandGap / 2f);

            /* Card list (fills remaining height after subtracting submod sections) */
            float sectionsHeight = ComputeSectionsHeight(inRect.width);
            float listH = inRect.yMax - y - sectionsHeight - (sectionsHeight > 0f ? BandGap : 0f);
            if (listH < 80f) listH = 80f;
            Rect listRect = new Rect(inRect.x, y, inRect.width, listH);
            DrawCardList(listRect);
            y = listRect.yMax + BandGap;

            /* Submod sections */
            if (sectionsHeight > 0f)
            {
                Rect sectionsRect = new Rect(inRect.x, y, inRect.width, sectionsHeight);
                DrawSubmodSections(sectionsRect);
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        /*-*-*-*-* Header bands *-*-*-*-*/

        private void DrawTitleBand(Rect rect)
        {
            UIUtil.DrawColoredHighlight(rect, AccentUtil.MilInactive);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            string label = "FCSquadInspectionTitle".Translate(squad.name ?? "(?)");
            float labelW = Text.CalcSize(label).x;
            float labelX = rect.x + 12f;
            Widgets.Label(new Rect(labelX, rect.y, labelW + 4f, rect.height), label);

            /* Pencil rename icon to the right of the squad name */
            float iconY = rect.y + (rect.height - IconButtonSize) / 2f;
            Rect pencilRect = new Rect(labelX + labelW + 8f, iconY, IconButtonSize, IconButtonSize);
            if (Widgets.ButtonImage(pencilRect, TexButton.Rename))
            {
                Find.WindowStack.Add(new FCWindow_Rename(squad.name ?? "", "FCRenameSquad",
                    n => { squad.name = n; }));
            }
            TooltipHandler.TipRegion(pencilRect, "FCSquadInspectionRenameSquadTip".Translate());
        }

        private void DrawContextBand(Rect rect)
        {
            /* Two columns split 50/50, separated by a vertical gray line.
               Each column lays out as: caption (Tiny dim) / value (Small) / two ButtonFlat. */
            float midX = rect.x + rect.width / 2f;
            UIUtil.DrawColoredVerticalLine(midX, rect.y + 4f, rect.height - 8f, Color.gray);

            Rect leftCol = new Rect(rect.x + 6f, rect.y, rect.width / 2f - 12f, rect.height);
            Rect rightCol = new Rect(midX + 6f, rect.y, rect.width / 2f - 12f, rect.height);

            DrawSettlementColumn(leftCol);
            DrawTemplateColumn(rightCol);
        }

        private void DrawSettlementColumn(Rect rect)
        {
            float captionH = 18f;
            float valueH = 24f;
            float buttonsH = ActionButtonHeight;
            float topPad = (rect.height - (captionH + valueH + buttonsH)) / 2f;
            float y = rect.y + Mathf.Max(4f, topPad);

            /* Caption */
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerLeft;
            UIUtil.DrawColoredLabel(new Rect(rect.x, y, rect.width, captionH),
                "FCSquadInspectionContextSettlementCaption".Translate(), CaptionTextColor);
            y += captionH;

            /* Value — promoted to Small. Dimmed when unassigned. */
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            bool hasSettlement = squad.settlement != null;
            string settlementName = hasSettlement
                ? squad.settlement.Name
                : (string)"FCSquadInspectionContextSettlementUnassigned".Translate();
            if (hasSettlement)
                Widgets.Label(new Rect(rect.x, y, rect.width, valueH), settlementName);
            else
                UIUtil.DrawColoredLabel(new Rect(rect.x, y, rect.width, valueH), settlementName, DimValueColor);
            y += valueH;

            /* Buttons: [Reassign] [Dismiss squad] */
            bool canReassign = !squad.IsBusy;
            bool canDismiss = !squad.IsBusy;
            Rect reassignRect = new Rect(rect.x, y, ActionButtonWidth, buttonsH);
            if (UIUtil.ButtonFlat(reassignRect, "FCSquadActReassign".Translate(), disabled: !canReassign))
            {
                Find.WindowStack.Add(new Dialog_SquadAssignment(squad));
            }

            Rect dismissRect = new Rect(rect.x + ActionButtonWidth + SmallGap, y, ActionButtonWidth, buttonsH);
            if (UIUtil.ButtonFlat(dismissRect, "FCSquadActDismissSquad".Translate(), disabled: !canDismiss))
            {
                int refund = (int)Math.Round(squad.hireCostPaid * FCSettings.squadDismissalRefundFraction);
                MercenarySquadFC captured = squad;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "FCSquadActDismissConfirm".Translate(captured.name, refund),
                    delegate
                    {
                        FactionCache.FactionComp?.militaryCustomizationUtil?.DismissSquad(captured);
                        Close();
                    }));
            }
        }

        private void DrawTemplateColumn(Rect rect)
        {
            float captionH = 18f;
            float valueH = 24f;
            float buttonsH = ActionButtonHeight;
            float topPad = (rect.height - (captionH + valueH + buttonsH)) / 2f;
            float y = rect.y + Mathf.Max(4f, topPad);

            /* Caption */
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerLeft;
            UIUtil.DrawColoredLabel(new Rect(rect.x, y, rect.width, captionH),
                "FCSquadInspectionContextTemplateCaption".Translate(), CaptionTextColor);
            y += captionH;

            /* Value — promoted to Small. Dimmed when no template. */
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            bool hasTemplate = squad.outfit != null;
            string templateName = hasTemplate
                ? (squad.outfit.name ?? "(?)")
                : (string)"FCSquadInspectionContextTemplateNone".Translate();
            if (hasTemplate)
                Widgets.Label(new Rect(rect.x, y, rect.width, valueH), templateName);
            else
                UIUtil.DrawColoredLabel(new Rect(rect.x, y, rect.width, valueH), templateName, DimValueColor);
            y += valueH;

            /* Buttons: [Pick template] [Clear template] */
            Rect pickRect = new Rect(rect.x, y, ActionButtonWidth, buttonsH);
            if (UIUtil.ButtonFlat(pickRect, "FCSquadInspectionPickTemplate".Translate()))
            {
                OpenTemplateMenu();
            }

            Rect clearRect = new Rect(rect.x + ActionButtonWidth + SmallGap, y, ActionButtonWidth, buttonsH);
            if (UIUtil.ButtonFlat(clearRect, "FCSquadInspectionClearTemplate".Translate(), disabled: !hasTemplate))
            {
                squad.SwapTemplate(null);
            }
        }

        private void DrawActionBar(Rect rect)
        {
            int fillCost = squad.FillEmptySlotsCost;
            int emptyCount = squad.EmptySlotCount;
            bool canFill = emptyCount > 0;

            int upgradeNet = squad.UpgradeCost;
            (int upgrade, int hire, int refund) = squad.UpgradeCostBreakdown;
            /* Allow Upgrade All any time the breakdown has nonzero components — there might
               be reassignments to apply even if the net is zero or negative. */
            bool hasUpgradeWork = upgrade != 0 || hire != 0 || refund != 0;
            bool canUpgradeAll = squad.outfit != null && !squad.IsBusy && hasUpgradeWork;

            float gap = 8f;
            float btnW = (rect.width - gap) / 2;
            float bx = rect.x;

            Rect fillRect = new Rect(bx, rect.y, btnW, rect.height);
            string fillLabel = "FCSquadInspectionFillEmptySlots".Translate(emptyCount, fillCost);
            if (UIUtil.ButtonFlat(fillRect, fillLabel, disabled: !canFill))
            {
                squad.FillEmptySlots();
            }
            bx += btnW + gap;

            Rect upgradeRect = new Rect(bx, rect.y, btnW, rect.height);
            string upgradeLabel = squad.outfit is null
                ? (string)"FCSquadInspectionUpgradeAllNoTemplate".Translate()
                : (hasUpgradeWork
                    ? (string)"FCSquadInspectionUpgradeAll".Translate(upgradeNet)
                    : (string)"FCSquadInspectionUpgradeAllUpToDate".Translate());
            if (UIUtil.ButtonFlat(upgradeRect, upgradeLabel, disabled: !canUpgradeAll))
            {
                squad.UpgradeToTemplate();
            }

            /* Cost breakdown tooltip on Upgrade All — explains where the net total came from. */
            if (squad.outfit != null && hasUpgradeWork)
            {
                string tooltip = "FCSquadInspectionUpgradeAllTooltip".Translate(upgrade, hire, refund, upgradeNet);
                TooltipHandler.TipRegion(upgradeRect, tooltip);
            }
        }

        /*-*-*-*-* Card list *-*-*-*-*/

        private void DrawCardList(Rect rect)
        {
            /* Filter out the blank-loadout placeholder slots created by InitiateSquad
               when the template has fewer real units than MilSquadFC.MaxSquadSize. */
            List<Mercenary> mercs = (squad.mercenaries ?? new List<Mercenary>())
                .Where(m => m != null && m.EffectiveLoadout != null && !m.EffectiveLoadout.isBlank)
                .ToList();

            float contentH = mercs.Count * (CardHeight + CardGap);
            Rect viewRect = ScrollUtil.BeginScrollView(rect, ref scroll, contentH);
            for (int i = 0; i < mercs.Count; i++)
            {
                Rect cardRect = new Rect(0f, i * (CardHeight + CardGap), viewRect.width, CardHeight);
                DrawPawnCard(cardRect, i, mercs[i]);
            }
            ScrollUtil.EndScrollView();
        }

        private void DrawPawnCard(Rect cardRect, int slotIndex, Mercenary merc)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            /* Background — highlight even rows for readability */
            if (slotIndex % 2 == 0) Widgets.DrawHighlight(cardRect);
            
            float leftx = cardRect.x;

            /* Accent bar (3px on the left edge) — health-driven */
            Color accent = GetSlotAccent(merc);
            Rect accentRect = new Rect(leftx, cardRect.y, AccentBarWidth, cardRect.height);
            Widgets.DrawBoxSolid(accentRect, accent);

            /* Portrait */
            float portraitX = leftx + AccentBarWidth + CardOuterPad;
            float portraitY = cardRect.y + (cardRect.height - PortraitSize) / 2f;
            Rect portraitRect = new Rect(portraitX, portraitY, PortraitSize, PortraitSize);
            if (merc?.pawn != null)
            {
                UIUtil.DrawPawnPortrait(portraitRect, merc.pawn);
            }
            else
            {
                Widgets.DrawMenuSection(portraitRect);
            }

            /* Content area to the right of the portrait, leaving room for action buttons */
            float contentX = portraitRect.xMax + CardOuterPad;
            float contentW = cardRect.xMax - contentX;
            Rect contentRect = new Rect(contentX, cardRect.y + 4f, contentW, cardRect.height - 8f);
            DrawCardContent(contentRect, slotIndex, merc);

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        private void DrawCardContent(Rect rect, int slotIndex, Mercenary merc)
        {
            float lineH = 18f;
            float y = rect.y;

            /* Header line: "Slot N - Pawn Name"  + info-card + rename-pencil icons */
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            string slotLabel = "FCSquadInspectionSlotLabel".Translate(slotIndex + 1);
            string pawnName = merc?.pawn != null
                ? merc.pawn.LabelShortCap
                : (string)"FCSquadInspectionEmptyPawn".Translate();
            // Don't include "Slot #" in the header, not here. The slot number might still be useful to show somewhere, though...
            string headerText = pawnName; //slotLabel + " - " + pawnName;
            float iconAreaW = (merc?.pawn != null) ? (InfoCardSize + IconButtonSize + 8f) : 0f;
            float headerH = 22f;
            Rect headerRect = new Rect(rect.x, y, rect.width - iconAreaW, headerH);
            Widgets.Label(headerRect, headerText);

            if (merc?.pawn != null)
            {
                float iconY = y + (headerH - InfoCardSize) / 2f;
                float ix = rect.xMax - iconAreaW + 4f;
                Widgets.InfoCardButton(ix, iconY, merc.pawn);
                ix += InfoCardSize + 4f;
                Rect pencilRect = new Rect(ix, y + (headerH - IconButtonSize) / 2f, IconButtonSize, IconButtonSize);
                if (Widgets.ButtonImage(pencilRect, TexButton.Rename))
                {
                    Find.WindowStack.Add(merc.pawn.NamePawnDialog());
                }
                TooltipHandler.TipRegion(pencilRect, "FCSquadInspectionRenamePawnTip".Translate());
            }

            y += headerH;

            /* Loadout line */
            Text.Font = GameFont.Tiny;
            string loadoutName = merc?.EffectiveLoadout?.name ?? (string)"FCNone".Translate();
            if (merc?.ownedLoadout != null) loadoutName = "* " + loadoutName;
            string loadoutText = "FCSquadInspectionLoadoutLabel".Translate(loadoutName);
            Rect loadoutRect = new Rect(rect.x, y, rect.width, lineH);
            Widgets.Label(loadoutRect, loadoutText);
            if (merc?.ownedLoadout != null)
            {
                TooltipHandler.TipRegion(loadoutRect, "FCSquadInspectionDivergedTip".Translate());
            }
            y += lineH;

            /* Status line — colored to reinforce the accent */
            string statusText = "FCSquadInspectionStatusLabel".Translate(ComputeMercStatus(merc));
            Rect statusRect = new Rect(rect.x, y, rect.width, lineH);
            UIUtil.DrawColoredLabel(statusRect, statusText, GetSlotAccent(merc));
            

            /* Action buttons (right-aligned, vertically centered) */
            float actionsW = ActionButtonWidth * 3 + SmallGap * 2 + CardOuterPad;
            Rect actionsRect = new Rect(rect.xMax - actionsW,
                rect.yMax - ActionButtonHeight - 5f,
                actionsW, ActionButtonHeight);
            DrawCardActions(actionsRect, slotIndex, merc);
        }

        private void DrawCardActions(Rect rect, int slotIndex, Mercenary merc)
        {
            Text.Font = GameFont.Small;
            float btnW = ActionButtonWidth;
            float btnH = rect.height;
            float bx = rect.x;

            if (merc != null && merc.IsEmptySlot)
            {
                MilUnitFC blueprint = merc.BlueprintLoadout;
                bool canFill = blueprint != null && !blueprint.isBlank;
                int slotFillCost = canFill
                    ? (int)Math.Round(blueprint.getTotalCost * FCSettings.squadHireCostMultiplier)
                    : 0;
                /* For empty slots we use a single wide Fill button taking the full action area
                   (the full width spanned by Edit Loadout + Upgrade + Dismiss when filled). */
                float fillW = btnW * 3f + SmallGap * 2f;
                Rect fillRect = new Rect(bx, rect.y, fillW, btnH);
                if (UIUtil.ButtonFlat(fillRect,
                    "FCSquadInspectionPerSlotFill".Translate(slotFillCost), disabled: !canFill))
                {
                    FillSingleSlot(merc, slotFillCost, blueprint);
                }
            }
            else if (merc?.pawn != null)
            {
                /* Edit Loadout */
                Rect editRect = new Rect(bx, rect.y, btnW, btnH);
                if (UIUtil.ButtonFlat(editRect, "FCSquadInspectionEditLoadout".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_PawnLoadout(squad, merc));
                }
                bx += btnW + SmallGap;

                /* Upgrade — target the template's slot at this merc's own index. */
                int slotUpgradeCost = ComputePerPawnUpgradeCost(slotIndex, merc);
                bool canUpgrade = squad.outfit != null && slotUpgradeCost > 0 && !squad.IsBusy;
                Rect upgRect = new Rect(bx, rect.y, btnW, btnH);
                if (UIUtil.ButtonFlat(upgRect,
                    "FCSquadInspectionPerSlotUpgrade".Translate(slotUpgradeCost), disabled: !canUpgrade))
                {
                    PerPawnUpgrade(slotIndex, merc, slotUpgradeCost);
                }
                bx += btnW + SmallGap;

                /* Dismiss this merc — refunds proportional silver and clears the slot for refill. */
                bool canDismiss = !squad.IsBusy;
                Rect dismissRect = new Rect(bx, rect.y, btnW, btnH);
                if (UIUtil.ButtonFlat(dismissRect, "FCMercDismiss".Translate(), disabled: !canDismiss))
                {
                    Mercenary captured = merc;
                    double cost = captured.EffectiveLoadout?.getTotalCost ?? 0;
                    int refund = (int)Math.Round(cost * FCSettings.squadDismissalRefundFraction);
                    string pawnLabel = captured.pawn?.LabelShortCap ?? "?";
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "FCMercDismissConfirm".Translate(pawnLabel, refund),
                        delegate { squad.DismissMercenary(captured); }));
                }
                TooltipHandler.TipRegion(dismissRect, "FCMercDismissTip".Translate());
            }
        }

        /*-*-*-*-* Status / accent helpers *-*-*-*-*/

        private string ComputeMercStatus(Mercenary merc)
        {
            if (merc is null || merc.IsEmptySlot) return "FCSquadInspectionStatusEmpty".Translate();
            if (merc.pawn.Dead) return "FCSquadInspectionStatusDead".Translate();
            if (merc.pawn.Downed) return "FCSquadInspectionStatusDowned".Translate();
            int injuries = CountActiveInjuries(merc.pawn);
            if (injuries > 0) return "FCSquadInspectionStatusInjured".Translate(injuries);
            return "FCSquadInspectionStatusOk".Translate();
        }

        private static int CountActiveInjuries(Pawn pawn)
        {
            int n = 0;
            List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs is null) return 0;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_Injury inj && !inj.IsPermanent()) n++;
            }
            return n;
        }

        /// <summary>Health-driven accent for the left edge of a card.
        /// Empty / dead → gray, downed → red, heavy injuries → orange,
        /// light injuries → yellow, healthy → green.</summary>
        private static Color GetSlotAccent(Mercenary merc)
        {
            if (merc is null || merc.IsEmptySlot) return AccentUtil.MilInactive;
            if (merc.pawn.Dead) return AccentUtil.MilInactive;
            if (merc.pawn.Downed) return AccentUtil.MilUnderAttack;
            int injuries = CountActiveInjuries(merc.pawn);
            if (injuries >= 3) return AccentUtil.MilActiveMission;
            if (injuries >= 1) return AccentUtil.MilCooldown;
            return AccentUtil.MilReady;
        }

        /*-*-*-*-* Per-pawn upgrade *-*-*-*-*/

        /// <summary>Cost to upgrade the merc at <paramref name="slotIndex"/> to the template's
        /// slot at the same index. Reads <see cref="Mercenary.EffectiveLoadout"/> for the
        /// "what's equipped now" baseline. Returns 0 when no template, slot is null/missing,
        /// or there's no positive diff.</summary>
        private int ComputePerPawnUpgradeCost(int slotIndex, Mercenary merc)
        {
            MilUnitFC target = GetTemplateSlot(slotIndex);
            if (target is null || merc is null) return 0;
            double oldCost = merc.EffectiveLoadout?.getTotalCost ?? 0;
            double newCost = target.getTotalCost;
            if (newCost <= oldCost) return 0;
            return (int)Math.Round((newCost - oldCost) * FCSettings.squadUpgradeCostMultiplier);
        }

        private MilUnitFC GetTemplateSlot(int slotIndex)
        {
            if (squad.outfit?.Units is null) return null;
            if (slotIndex < 0 || slotIndex >= squad.outfit.Units.Count) return null;
            return squad.outfit.Units[slotIndex];
        }

        private void PerPawnUpgrade(int slotIndex, Mercenary merc, int cost)
        {
            MilUnitFC target = GetTemplateSlot(slotIndex);
            if (target is null) return;
            if (cost > 0 && PaymentUtil.GetSilver() < cost)
            {
                Messages.Message("FCSquadUpgradeInsufficientSilver".Translate(cost),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (cost > 0) PaymentUtil.PaySilver(cost, PaymentUtil.Reason_SquadUpgrade, squad.settlement);
            merc.loadout = target;
            merc.ownedLoadout = null;
            merc.currentLoadout = target.Clone();
            squad.StripPawn(merc);
            squad.EquipPawn(merc, target);
        }

        private void FillSingleSlot(Mercenary merc, int cost, MilUnitFC blueprint)
        {
            if (blueprint is null || blueprint.isBlank) return;
            if (cost > 0 && PaymentUtil.GetSilver() < cost)
            {
                Messages.Message("FCSquadFillSlotsInsufficient".Translate(cost),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (cost > 0) PaymentUtil.PaySilver(cost, PaymentUtil.Reason_SquadFillSlot, squad.settlement);
            Mercenary slot = merc;
            squad.CreateNewPawn(ref slot, blueprint.pawnKind, blueprint.xenotype, blueprint.customXenotypeName);
            if (slot.pawn != null) squad.EquipPawn(slot, blueprint);
            slot.currentLoadout = blueprint.Clone();
            FactionCache.FactionComp?.militaryCustomizationUtil?.RebuildMercenaryPawnSet();
        }

        /*-*-*-*-* Template menu *-*-*-*-*/

        private void OpenTemplateMenu()
        {
            MilitaryCustomizationUtil util = FactionCache.FactionComp?.militaryCustomizationUtil;
            if (util?.squads is null) return;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("FCNone".Translate(), delegate { squad.SwapTemplate(null); }));
            foreach (MilSquadFC template in util.squads)
            {
                MilSquadFC captured = template;
                options.Add(new FloatMenuOption(template.name ?? "(?)", delegate { squad.SwapTemplate(captured); }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /*-*-*-*-* Submod sections *-*-*-*-*/

        private float ComputeSectionsHeight(float width)
        {
            IReadOnlyList<ISquadInspectionSection> sections = SquadInspectionRegistry.Sections;
            if (sections.Count == 0) return 0f;
            float total = 0f;
            float headerH = 24f;
            for (int i = 0; i < sections.Count; i++)
            {
                float sh = sections[i].GetSectionHeight(squad, width);
                if (sh <= 0f) continue;
                total += headerH + sh + 6f;
            }
            return total;
        }

        private void DrawSubmodSections(Rect rect)
        {
            IReadOnlyList<ISquadInspectionSection> sections = SquadInspectionRegistry.Sections;
            float headerH = 24f;
            float y = rect.y;
            for (int i = 0; i < sections.Count; i++)
            {
                ISquadInspectionSection section = sections[i];
                float sh = section.GetSectionHeight(squad, rect.width);
                if (sh <= 0f) continue;

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(rect.x, y, rect.width, headerH), section.SectionLabel);
                y += headerH;

                Rect contentRect = new Rect(rect.x, y, rect.width, sh);
                try { section.DrawSection(squad, contentRect); }
                catch (Exception ex)
                {
                    LogUtil.Error($"ISquadInspectionSection {section.GetType().FullName} threw: {ex}");
                }
                y += sh + 6f;
            }
        }
    }
}
