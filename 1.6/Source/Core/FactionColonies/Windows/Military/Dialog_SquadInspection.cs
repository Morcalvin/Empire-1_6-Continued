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
    /// Per-squad inspection window. Lists each mercenary slot with portrait, name, loadout,
    /// status, and per-pawn actions (Edit / Upgrade / Fill). Also surfaces squad-level actions:
    /// rename, template swap/clear, dismiss, reassign, fill all empty slots, upgrade all.
    ///
    /// Submods can extend the window by registering <see cref="ISquadInspectionSection"/>s
    /// via <see cref="SquadInspectionRegistry"/>; their content renders below the per-pawn
    /// rows in declared <see cref="ISquadInspectionSection.Order"/>.
    ///
    /// Strict-manual outfit policy: every gear-altering action requires an explicit click.
    /// Template swap and clear are free. Fill, Upgrade All, and per-pawn Upgrade charge silver
    /// at the moment the player commits.
    /// </summary>
    public class Dialog_SquadInspection : Window
    {
        public override Vector2 InitialSize => new Vector2(900f, 640f);

        private readonly MercenarySquadFC squad;
        private Vector2 scroll;

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

            float headerH = 30f;
            float subHeaderH = 28f;
            float actionsBarH = 32f;
            float colHeaderH = 22f;
            const float pad = 4f;

            // === Title row: name + template ===
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, headerH);
            DrawTitle(titleRect);

            // === Sub-header row: settlement + reassign + dismiss + clear template ===
            Rect subRect = new Rect(inRect.x, titleRect.yMax + 2f, inRect.width, subHeaderH);
            DrawSubHeader(subRect);

            // === Action bar: fill / upgrade ===
            Rect actionsRect = new Rect(inRect.x, subRect.yMax + 4f, inRect.width, actionsBarH);
            DrawActionBar(actionsRect);

            // === Per-pawn table ===
            float listTop = actionsRect.yMax + 4f;
            float sectionsHeight = ComputeSectionsHeight(inRect.width);
            float listH = inRect.height - (listTop - inRect.y) - sectionsHeight - pad;
            if (listH < 80f) listH = 80f;
            Rect tableRect = new Rect(inRect.x, listTop, inRect.width, listH);
            DrawTable(tableRect, colHeaderH);

            // === Submod sections ===
            if (sectionsHeight > 0f)
            {
                Rect sectionsRect = new Rect(inRect.x, tableRect.yMax + 4f, inRect.width, sectionsHeight);
                DrawSubmodSections(sectionsRect);
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Header rows ---

        private void DrawTitle(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            string label = "FCSquadInspectionTitle".Translate(squad.name ?? "(?)");
            float labelW = Text.CalcSize(label).x + 16f;
            Widgets.Label(new Rect(rect.x, rect.y, labelW, rect.height), label);

            // Rename button
            Text.Font = GameFont.Small;
            float renameW = 90f;
            Rect renameRect = new Rect(rect.x + labelW, rect.y + 2f, renameW, rect.height - 4f);
            if (Widgets.ButtonText(renameRect, "FCSquadInspectionRename".Translate()))
            {
                Find.WindowStack.Add(new FCWindow_Rename(squad.name ?? "", "FCRenameSquad",
                    n => { squad.name = n; }));
            }

            // Template selector (right side)
            float templateW = 280f;
            Rect templateRect = new Rect(rect.xMax - templateW, rect.y + 2f, templateW, rect.height - 4f);
            string templateLabel = "FCSquadInspectionTemplate".Translate(squad.outfit?.name ?? (string)"FCNone".Translate());
            if (Widgets.ButtonText(templateRect, templateLabel))
            {
                OpenTemplateMenu();
            }
        }

        private void DrawSubHeader(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            string settlementLabel = "FCSquadInspectionSettlement".Translate(
                squad.settlement?.Name ?? (string)"FCSquadStatusUnassigned".Translate());
            float labelW = 280f;
            Widgets.Label(new Rect(rect.x, rect.y, labelW, rect.height), settlementLabel);

            float btnW = 110f;
            float btnH = rect.height - 2f;
            float bx = rect.xMax - btnW;

            // Clear Template
            bool hasTemplate = squad.outfit != null;
            Color colorBefore = GUI.color;
            if (!hasTemplate) GUI.color = Color.gray;
            Rect clearRect = new Rect(bx, rect.y, btnW, btnH);
            if (Widgets.ButtonText(clearRect, "FCSquadInspectionClearTemplate".Translate(), true, true, hasTemplate))
            {
                squad.SwapTemplate(null);
            }
            GUI.color = colorBefore;
            bx -= btnW + 4f;

            // Dismiss
            bool canDismiss = !squad.IsBusy;
            colorBefore = GUI.color;
            if (!canDismiss) GUI.color = Color.gray;
            Rect dismissRect = new Rect(bx, rect.y, btnW, btnH);
            if (Widgets.ButtonText(dismissRect, "FCSquadActDismiss".Translate(), true, true, canDismiss))
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
            GUI.color = colorBefore;
            bx -= btnW + 4f;

            // Reassign
            bool canReassign = !squad.IsBusy;
            colorBefore = GUI.color;
            if (!canReassign) GUI.color = Color.gray;
            Rect reassignRect = new Rect(bx, rect.y, btnW, btnH);
            if (Widgets.ButtonText(reassignRect, "FCSquadActReassign".Translate(), true, true, canReassign))
            {
                Find.WindowStack.Add(new Dialog_SquadAssignment(squad));
            }
            GUI.color = colorBefore;
        }

        private void DrawActionBar(Rect rect)
        {
            int fillCost = squad.FillEmptySlotsCost;
            int emptyCount = squad.EmptySlotCount;
            bool canFill = emptyCount > 0;

            int upgradeNet = squad.UpgradeCost;
            (int upgrade, int hire, int refund) = squad.UpgradeCostBreakdown;
            // Allow Upgrade All any time the breakdown has nonzero components — there might
            // be reassignments to apply even if the net is zero or negative.
            bool hasUpgradeWork = upgrade != 0 || hire != 0 || refund != 0;
            bool canUpgradeAll = squad.outfit != null && !squad.IsBusy && hasUpgradeWork;

            float btnW = 200f;
            float btnH = rect.height;
            float gap = 6f;
            float bx = rect.x;

            Color colorBefore = GUI.color;
            if (!canFill) GUI.color = Color.gray;
            Rect fillRect = new Rect(bx, rect.y, btnW, btnH);
            string fillLabel = canFill
                ? (string)"FCSquadInspectionFillEmptySlots".Translate(emptyCount, fillCost)
                : (string)"FCSquadInspectionFillEmptyNone".Translate();
            if (Widgets.ButtonText(fillRect, fillLabel, true, true, canFill))
            {
                squad.FillEmptySlots();
            }
            GUI.color = colorBefore;
            bx += btnW + gap;

            colorBefore = GUI.color;
            if (!canUpgradeAll) GUI.color = Color.gray;
            Rect upgradeRect = new Rect(bx, rect.y, btnW, btnH);
            string upgradeLabel = squad.outfit is null
                ? (string)"FCSquadInspectionUpgradeAllNoTemplate".Translate()
                : (hasUpgradeWork
                    ? (string)"FCSquadInspectionUpgradeAll".Translate(upgradeNet)
                    : (string)"FCSquadInspectionUpgradeAllUpToDate".Translate());
            if (Widgets.ButtonText(upgradeRect, upgradeLabel, true, true, canUpgradeAll))
            {
                squad.UpgradeToTemplate();
            }
            GUI.color = colorBefore;

            // Cost breakdown tooltip on Upgrade All — explains where the net total came from.
            if (squad.outfit != null && hasUpgradeWork)
            {
                string tooltip = "FCSquadInspectionUpgradeAllTooltip".Translate(upgrade, hire, refund, upgradeNet);
                TooltipHandler.TipRegion(upgradeRect, tooltip);
            }
        }

        // --- Per-pawn table ---

        private void DrawTable(Rect rect, float colHeaderH)
        {
            // Column header
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, colHeaderH);
            Widgets.DrawHighlight(headerRect);

            float[] colWidths = ColumnWidths();
            string[] colLabels = {
                "FCSquadInspectionColSlot".Translate(),
                "FCSquadInspectionColPawn".Translate(),
                "FCSquadInspectionColLoadout".Translate(),
                "FCSquadInspectionColStatus".Translate(),
                "FCSquadInspectionColActions".Translate(),
            };

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float hx = rect.x + 6f;
            for (int i = 0; i < colWidths.Length; i++)
            {
                Widgets.Label(new Rect(hx, headerRect.y, colWidths[i], headerRect.height), colLabels[i]);
                hx += colWidths[i];
            }

            float rowH = 50f;
            List<Mercenary> mercs = squad.mercenaries ?? new List<Mercenary>();
            float listTop = headerRect.yMax;
            float listH = rect.yMax - listTop;
            Rect listRect = new Rect(rect.x, listTop, rect.width, listH);
            float viewH = mercs.Count * rowH;
            Rect viewRect = new Rect(0, 0, listRect.width - 16f, viewH);
            Widgets.BeginScrollView(listRect, ref scroll, viewRect);
            for (int i = 0; i < mercs.Count; i++)
            {
                Mercenary merc = mercs[i];
                Rect rowRect = new Rect(0, i * rowH, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawHighlight(rowRect);
                DrawMercRow(rowRect, i, merc, colWidths);
            }
            Widgets.EndScrollView();
        }

        private static float[] ColumnWidths() => new float[] { 50f, 200f, 180f, 130f, 280f };

        private void DrawMercRow(Rect rowRect, int slotIndex, Mercenary merc, float[] colWidths)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            float x = rowRect.x + 6f;

            // Slot index
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(x, rowRect.y, colWidths[0], rowRect.height), (slotIndex + 1).ToString());
            x += colWidths[0];

            // Pawn portrait + name
            Text.Anchor = TextAnchor.MiddleLeft;
            float portraitSize = rowRect.height - 6f;
            Rect portraitRect = new Rect(x + 2f, rowRect.y + 3f, portraitSize, portraitSize);
            if (merc?.pawn != null)
            {
                UIUtil.DrawPawnPortrait(portraitRect, merc.pawn);
            }
            else
            {
                Widgets.DrawMenuSection(portraitRect);
            }
            string pawnName = merc?.pawn != null
                ? merc.pawn.LabelShortCap
                : (string)"FCSquadInspectionEmptyPawn".Translate();
            Widgets.Label(new Rect(portraitRect.xMax + 4f, rowRect.y, colWidths[1] - portraitSize - 6f, rowRect.height), pawnName);
            x += colWidths[1];

            // Loadout name + diverged marker. Display reads the pool reference (loadout)
            // for the human-readable template tag; the marker reflects ownedLoadout.
            string loadoutLabel = merc?.loadout?.name ?? (string)"FCNone".Translate();
            if (merc?.ownedLoadout != null) loadoutLabel = "* " + loadoutLabel;
            Widgets.Label(new Rect(x, rowRect.y, colWidths[2], rowRect.height), loadoutLabel);
            if (merc?.ownedLoadout != null)
            {
                TooltipHandler.TipRegion(new Rect(x, rowRect.y, colWidths[2], rowRect.height),
                    "FCSquadInspectionDivergedTip".Translate());
            }
            x += colWidths[2];

            // Status
            string status = ComputeMercStatus(merc);
            Widgets.Label(new Rect(x, rowRect.y, colWidths[3], rowRect.height), status);
            x += colWidths[3];

            // Actions: Edit / Upgrade / Fill (Fill replaces Edit+Upgrade for empty slots)
            Text.Font = GameFont.Tiny;
            float btnW = 86f;
            float btnH = rowRect.height - 8f;
            float btnY = rowRect.y + 4f;
            float bx = x;
            if (merc != null && merc.IsEmptySlot)
            {
                MilUnitFC blueprint = merc.currentLoadout ?? merc.loadout;
                int slotFillCost = blueprint != null
                    ? (int)Math.Round(blueprint.getTotalCost * FCSettings.squadHireCostMultiplier)
                    : 0;
                bool canFill = blueprint != null;
                Color cb = GUI.color;
                if (!canFill) GUI.color = Color.gray;
                Rect fillRect = new Rect(bx, btnY, btnW, btnH);
                if (Widgets.ButtonText(fillRect, "FCSquadInspectionPerSlotFill".Translate(slotFillCost), true, true, canFill))
                {
                    FillSingleSlot(merc, slotFillCost, blueprint);
                }
                GUI.color = cb;
            }
            else if (merc?.pawn != null)
            {
                // Edit
                Rect editRect = new Rect(bx, btnY, btnW, btnH);
                if (Widgets.ButtonText(editRect, "FCSquadInspectionEdit".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_PawnLoadout(squad, merc));
                }
                bx += btnW + 4f;

                // Upgrade — target the template's slot at this merc's own index.
                int slotUpgradeCost = ComputePerPawnUpgradeCost(slotIndex, merc);
                bool canUpgrade = squad.outfit != null && slotUpgradeCost > 0 && !squad.IsBusy;
                Color cb = GUI.color;
                if (!canUpgrade) GUI.color = Color.gray;
                Rect upgRect = new Rect(bx, btnY, btnW, btnH);
                if (Widgets.ButtonText(upgRect,
                    "FCSquadInspectionPerSlotUpgrade".Translate(slotUpgradeCost), true, true, canUpgrade))
                {
                    PerPawnUpgrade(slotIndex, merc, slotUpgradeCost);
                }
                GUI.color = cb;
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        private string ComputeMercStatus(Mercenary merc)
        {
            if (merc is null || merc.IsEmptySlot) return "FCSquadInspectionStatusEmpty".Translate();
            if (merc.pawn.Dead) return "FCSquadInspectionStatusDead".Translate();
            int injuries = 0;
            List<Hediff> hediffs = merc.pawn.health?.hediffSet?.hediffs;
            if (hediffs != null)
            {
                for (int i = 0; i < hediffs.Count; i++)
                {
                    if (hediffs[i] is Hediff_Injury inj && !inj.IsPermanent()) injuries++;
                }
            }
            if (injuries > 0) return "FCSquadInspectionStatusInjured".Translate(injuries);
            return "FCSquadInspectionStatusOk".Translate();
        }

        // --- Per-pawn upgrade ---

        /// <summary>Cost to upgrade the merc at <paramref name="slotIndex"/> to the template's
        /// slot at the same index. Reads <see cref="Mercenary.currentLoadout"/> for the
        /// "what's equipped now" baseline. Returns 0 when no template, slot is null/missing,
        /// or there's no positive diff.</summary>
        private int ComputePerPawnUpgradeCost(int slotIndex, Mercenary merc)
        {
            MilUnitFC target = GetTemplateSlot(slotIndex);
            if (target is null || merc is null) return 0;
            double oldCost = merc.currentLoadout?.getTotalCost ?? 0;
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
            if (blueprint is null) return;
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

        // --- Template menu ---

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

        // --- Submod sections ---

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
