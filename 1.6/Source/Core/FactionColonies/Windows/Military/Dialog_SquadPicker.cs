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
    /// Shared base for squad-picker dialogs (offensive op / defensive swap / generic squad
    /// pick). Holds the card layout, scroll plumbing, sort/filter toolbar, status helpers,
    /// and selection state. Subclasses provide the header content, row computation
    /// (attacker- vs defender-side force / win chance), and the confirm action.
    /// <para><see cref="Dialog_AttackSettlement"/> drives offensive ops; <see cref="Dialog_DefendSettlement"/>
    /// drives the defender-swap flow during the warning window.</para>
    /// </summary>
    public abstract class Dialog_SquadPicker : Window
    {
        public override Vector2 InitialSize => new Vector2(820f, 600f);

        /* Card layout constants — mirror HireSquadsWindow so all squad-listing surfaces share rhythm. */
        protected const float Pad         = 4f;
        protected const float RowGap      = 2f;
        protected const float CardHeaderH = 24f;
        protected const float CardDetailH = 22f;
        protected const float CardH       = CardHeaderH + CardDetailH;
        protected const float AccentW     = 4f;

        /* Header layout constants */
        protected const float TitleH       = 32f;
        protected const float HeaderColGap = 12f;
        protected const float SubHeaderH   = 28f;

        protected const float margin = 5f;
        protected const float smallMargin = 3f;

        protected enum SortMode
        {
            WinChance,
            Travel,
            Power,
            Name
        }

        /* Row data is neutral w.r.t. attacker/defender perspective. ourPower/ourEfficiency/
         * hasOurForce describe the *picker's* side (the squad we'd dispatch). winChanceMin/Max
         * is always from the player's-side perspective. Defend pickers leave min == max since
         * the incoming attacker force is concrete on the op (no variance bounds). */
        protected struct RowData
        {
            public MercenarySquadFC squad;
            public int travelTicks;
            public double winChanceMin;
            public double winChanceMax;
            public double ourPower;
            public double ourEfficiency;
            public bool hasOurForce;
            public string status;
            public Color statusColor;
            public bool available;
            public int deploymentCost;
            public int injuredCount;
        }

        protected MercenarySquadFC selected;
        protected Vector2 scrollPos;
        protected bool availableOnly = true;
        protected SortMode sort = SortMode.WinChance;

        protected List<RowData> rows = new List<RowData>();
        protected bool rowsDirty = true;

        /* Abstract: subclass renders its own header (title + target/incoming-engagement summary)
         * and returns the bottom y so the toolbar can land below it. */
        protected abstract float DrawHeader(Rect inRect);

        /* Abstract: populate `rows` from the right-side data — attacker-side force computation
         * for offense, defender-side for defense. Also re-applies sort. Called whenever
         * rowsDirty flips true. */
        protected abstract void RebuildRows();

        /* Abstract: dispatch the confirmed selection. Called from the Confirm button when
         * CanConfirm() returns true. */
        protected abstract void Confirm();

        /* Virtual seams — defaults are correct for the squad-only attack picker; defend picker
         * overrides to also accept external IAutoDefender selections and to render an extra
         * "External defenders" section under the squad cards. */
        protected virtual bool CanConfirm() => selected is object && selected.IsAvailable;
        protected virtual float ExtraRowsHeight => 0f;
        protected virtual void DrawExtraRows(Rect viewRect, ref float runningY) { }

        /* Column-visibility seams: let subclasses hide travel, force metrics (Pow/Eff), and/or
         * win-chance independently. Defensive swaps skip travel (the warning window already
         * accounts for arrival); the assign-to-slot picker skips win chance (no engagement to
         * predict) but keeps Pow/Eff so the player can compare squad strength. When hidden, the
         * freed horizontal space goes to the squad name / status / Inspect / cost columns and
         * the corresponding sort modes are pruned from the toolbar. */
        protected virtual bool ShowTravel        => true;
        protected virtual bool ShowForceMetrics  => true;
        protected virtual bool ShowWinChance     => true;

        /* Returns a squad that should be flagged as the "current" / "already-assigned" candidate
         * in this picker's context. */
        protected virtual MercenarySquadFC CurrentSquadIndicator => null;

        /* When true, this row's label text renders in the underfunded amber instead of the
         * default white / win-chance color. Used by the assign picker to flag squads whose
         * deploy cost exceeds the destination settlement's max deploy cost. */
        protected virtual bool RowOverBudget(RowData row) => false;

        public override void DoWindowContents(Rect inRect)
        {
            if (rowsDirty) RebuildRows();

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Color colorBefore = GUI.color;

            const float SquadHeaderH = 22f;

            float headerBottom = DrawHeader(inRect);

            // "Select Squad" sub-header
            float subHeaderY = headerBottom + 4f;
            TexLoad.DrawHorizontalPeakGradientLine(0, subHeaderY, inRect.width, Color.gray);
            subHeaderY += 4f;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect squadHeader = new Rect(0, subHeaderY, inRect.width, SquadHeaderH);
            Widgets.DrawHighlight(squadHeader);
            Widgets.Label(squadHeader, "FCSquadPickerSelectSquad".Translate());

            // Filter / sort row
            float toolbarY = subHeaderY + SquadHeaderH + 2f;
            float toolbarHeight = 24f;
            float sortButtonW = 200f;
            float checkboxW = 160f;
            Rect sortButton = new Rect(inRect.xMax - sortButtonW - (margin * 2), toolbarY, sortButtonW, toolbarHeight);
            Rect checkbox = new Rect(sortButton.x - checkboxW - margin, toolbarY, checkboxW, toolbarHeight);
            Text.Anchor = TextAnchor.MiddleLeft;
            bool prevAvailableOnly = availableOnly;
            Widgets.CheckboxLabeled(checkbox, "FCSquadPickerAvailableOnly".Translate(), ref availableOnly);
            if (prevAvailableOnly != availableOnly) rowsDirty = true;
            if (Widgets.ButtonText(sortButton, "FCSquadPickerSort".Translate(SortLabel(sort))))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                if (ShowWinChance)
                    opts.Add(new FloatMenuOption(SortLabel(SortMode.WinChance), () => { sort = SortMode.WinChance; rowsDirty = true; }));
                if (ShowTravel)
                    opts.Add(new FloatMenuOption(SortLabel(SortMode.Travel),    () => { sort = SortMode.Travel;    rowsDirty = true; }));
                opts.Add(new FloatMenuOption(SortLabel(SortMode.Power),         () => { sort = SortMode.Power;     rowsDirty = true; }));
                opts.Add(new FloatMenuOption(SortLabel(SortMode.Name),          () => { sort = SortMode.Name;      rowsDirty = true; }));
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            // Card list (framed)
            float listTop = toolbarY + 32f;
            float buttonsHeight = 36f;
            float listHeight = inRect.height - listTop - buttonsHeight - 6f;
            Rect listRect = new Rect(0, listTop, inRect.width, listHeight);
            Widgets.DrawMenuSection(listRect);
            DrawCardList(listRect);

            // Buttons
            float btnY = inRect.height - buttonsHeight + 2f;
            if (Widgets.ButtonText(new Rect(inRect.width - 320f, btnY, 150f, 32f), "Cancel".Translate()))
            {
                Close();
            }
            bool canConfirm = CanConfirm();
            if (!canConfirm) GUI.color = Color.gray;
            if (Widgets.ButtonText(new Rect(inRect.width - 160f, btnY, 150f, 32f), "Confirm".Translate(), true, true, canConfirm))
            {
                Confirm();
            }
            GUI.color = colorBefore;

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        protected void DrawCardList(Rect listRect)
        {
            if (rows.Count == 0 && ExtraRowsHeight <= 0f)
            {
                Color colorBefore = GUI.color;
                TextAnchor anchorBefore = Text.Anchor;
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(listRect.x, listRect.y + listRect.height * 0.35f,
                    listRect.width, 40f), "FCHireSquadsEmpty".Translate());
                GUI.color = colorBefore;
                Text.Anchor = anchorBefore;
                return;
            }

            float innerX = listRect.x + Pad;
            float innerW = listRect.width - Pad * 2f;
            Rect viewRect = new Rect(innerX, listRect.y + Pad, innerW, listRect.height - Pad * 2f);
            float totalH = rows.Count * (CardH + RowGap) + ExtraRowsHeight;
            Rect scrollRect = ScrollUtil.BeginScrollView(viewRect, ref scrollPos, totalH);

            int now = Find.TickManager.TicksGame;
            float runningY = 0f;
            bool alternate = false;
            for (int i = 0; i < rows.Count; i++)
            {
                Rect cardRect = new Rect(0f, runningY, scrollRect.width, CardH);
                if (alternate) Widgets.DrawHighlight(cardRect);
                DrawSquadCard(cardRect, rows[i], now, alternate);
                runningY += CardH + RowGap;
                alternate = !alternate;
            }

            // Subclass extra rows (e.g. external IAutoDefender entries) render after the squad
            // cards using the same scroll view so they share the scrollbar.
            DrawExtraRows(scrollRect, ref runningY);

            ScrollUtil.EndScrollView();
        }

        /* Win-chance-derived color: drives the accent strip, name, win-chance box, and selected
         * highlight overlay. Midpoint matches the WinChance sort key so visual gradient and
         * sort order stay aligned. Falls back to MilInactive when the row has no force on our
         * side. Virtual so subclasses without a win-chance dimension (e.g. the assign picker)
         * can substitute a neutral hue instead of perpetually-red. */
        protected virtual Color WinChanceColor(RowData row)
        {
            if (!ShowWinChance) return AccentUtil.MilInactive;
            if (!row.hasOurForce) return AccentUtil.MilInactive;
            double midPct = (row.winChanceMin + row.winChanceMax) * 50.0; // *0.5 then *100
            return AccentUtil.GetStatColor((float)midPct, inverted: false);
        }

        /* Per-squad card. Header row: accent strip, squad name, win-chance box (Pow/Eff top,
         * WinChance bottom), and right-side column with status badge over Inspect button.
         * Detail row: Settlement / Travel / Cost cells. The whole card (minus the Inspect
         * button) is the click target for selection — selected card uses the brighter selected
         * highlight tinted by win-chance color, hovered non-selected card uses the standard
         * hover highlight. Card height stays at CardH (46 px). */
        protected void DrawSquadCard(Rect cardRect, RowData row, int now, bool isHighlighted)
        {
            MercenarySquadFC squad = row.squad;
            Color winColor = WinChanceColor(row);

            // Hover / selected highlight (whole card). Selected gets a faint win-chance tint
            // overlay so the selection visual reinforces the box color. The "current" indicator
            // (already-assigned squad in this picker's context) renders as a green 2-px outline
            // so it stays visible whether or not the row is also the user's row-click selection.
            bool isSelected = selected == squad;
            bool isCurrentCard = CurrentSquadIndicator is object && squad == CurrentSquadIndicator;
            if (isSelected)
            {
                Widgets.DrawHighlightSelected(cardRect);
                Widgets.DrawBoxSolid(cardRect, new Color(winColor.r, winColor.g, winColor.b, 0.10f));
            }
            else if (Mouse.IsOver(cardRect))
            {
                Widgets.DrawHighlight(cardRect);
            }
            if (isCurrentCard)
            {
                Color outlineBefore = GUI.color;
                GUI.color = new Color(0.6f, 0.9f, 0.6f);
                Widgets.DrawBox(cardRect, 2);
                GUI.color = outlineBefore;
            }

            // Accent strip — driven by win chance, not settlement military state.
            Widgets.DrawBoxSolid(new Rect(cardRect.x, cardRect.y, AccentW, cardRect.height), winColor);

            float contentX = cardRect.x + AccentW + 6f;

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Color colorBefore = GUI.color;

            // Dim card content when squad is unavailable (busy / cooldown / unassigned).
            // Over-budget rows (squad deploy cost > settlement max) recolor labels amber to
            // match the colony-tab underfunded indicator. Dimmed when unavailable.
            bool overBudget = RowOverBudget(row);
            Color baseTint;
            if (overBudget) baseTint = row.available ? AccentUtil.MilUnderfunded : UIUtil.Dim(AccentUtil.MilUnderfunded);
            else            baseTint = row.available ? Color.white : new Color(0.7f, 0.7f, 0.7f);

            /* Right-side column: status badge (top) + Inspect button (bottom), same width.
             * rightColW (180) is the unconditional bump so longer statuses like
             * "Busy: Defend Settlement" stop wrapping in tiny font. boxW/boxGap collapse to 0
             * when both the Pow/Eff and WinChance halves are hidden; box shrinks to ~half-width
             * when WinChance is hidden but Pow/Eff is still shown. */
            const float btnH = 20f;
            const float rightColW = 180f;
            float boxW = ShowWinChance ? 150f : (ShowForceMetrics ? 90f : 0f);
            float boxGap = (boxW > 0f) ? 15f : 0f;

            float rightColX = cardRect.xMax - rightColW - 4f;
            float headerY = cardRect.y;
            float detailY = cardRect.y + CardHeaderH;

            // Status badge — top of right column, centered.
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = row.statusColor;
            Widgets.Label(new Rect(rightColX, headerY, rightColW, CardHeaderH), row.status);

            // Inspect button — bottom of right column, same width as the status badge above.
            // Drawn *before* the whole-card invisible button so its click is consumed first.
            GUI.color = colorBefore;
            MercenarySquadFC capturedSquad = squad;
            Rect inspectRect = new Rect(rightColX, detailY + 1f, rightColW, btnH);
            if (UIUtil.ButtonFlat(inspectRect, "FCMilitaryTableInspect".Translate(), highlighted: isHighlighted))
            {
                Find.WindowStack.Add(new Dialog_SquadInspection(capturedSquad));
            }
            TooltipHandler.TipRegion(inspectRect, "FCMilBtnInspectTip".Translate());

            /* Pow/Eff/WinChance box — Pow + Eff stacked vertically on the left, Win chance on
             * the right with a horizontal peak-gradient band (dimmed win-chance color) behind
             * it. When WinChance is hidden but ForceMetrics is shown, the box shrinks to a
             * single Pow/Eff column. When both are hidden, the box is skipped entirely. */
            float boxX = rightColX - boxGap - boxW;
            if (ShowForceMetrics || ShowWinChance)
            {
                Rect boxRect = new Rect(boxX, cardRect.y + 4f, boxW, cardRect.height - 8f);
                float powEffW = ShowWinChance ? boxW * 0.5f : boxW;

                if (ShowForceMetrics)
                {
                    string powLbl = (string)"FCSquadColPower".Translate() + ": " + row.ourPower.ToString("0.0");
                    string effLbl = row.hasOurForce
                        ? (string)"FCSquadColEfficiency".Translate() + ": x" + row.ourEfficiency.ToString("0.##")
                        : (string)"FCSquadColEfficiency".Translate() + ": -";

                    float halfBoxH = boxRect.height * 0.5f;

                    // Pow / Eff stacked vertically on the left half (or full width if no win chance).
                    Text.Anchor = TextAnchor.MiddleLeft;
                    GUI.color = baseTint;
                    Widgets.Label(new Rect(boxX, boxRect.y,             powEffW, halfBoxH), powLbl);
                    Widgets.Label(new Rect(boxX, boxRect.y + halfBoxH,  powEffW, halfBoxH), effLbl);
                }

                if (ShowWinChance)
                {
                    string winLbl;
                    if (row.hasOurForce && (row.winChanceMin > 0 || row.winChanceMax > 0))
                    {
                        double minPct = Math.Round(row.winChanceMin * 100);
                        double maxPct = Math.Round(row.winChanceMax * 100);
                        winLbl = (string)"FCSquadColWinChance".Translate() + ": " + TextUtil.FormatRange(minPct, maxPct, "0") + "%";
                    }
                    else
                    {
                        winLbl = (string)"FCSquadColWinChance".Translate() + ": -";
                    }

                    // Win chance on the right half (or full width if no Pow/Eff), vertically
                    // centered, with a peak-gradient band behind it tinted by a dimmed
                    // win-chance color so the full-saturation label remains legible even when
                    // the color is red.
                    float winX = boxX + (ShowForceMetrics ? powEffW : 0f);
                    float winW = ShowForceMetrics ? (boxW - powEffW) : boxW;
                    Rect winRect = new Rect(winX, boxRect.y, winW, boxRect.height);
                    const float gradH = 28f;
                    Rect gradRect = new Rect(winRect.x - 10f, winRect.center.y - gradH * 0.5f, winRect.width + 20f, gradH);
                    Color gradColor = UIUtil.Dim(winColor, 0.3f);
                    TexLoad.DrawHorizontalPeakGradient(gradRect, gradColor);

                    GUI.color = row.available ? winColor : UIUtil.Dim(winColor);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(winRect, winLbl);
                }
            }

            /* Squad name (left, win-chance colored) — header row, left of the box. When the box
             * is hidden, name extends all the way to the right column. Squads matching
             * CurrentSquadIndicator get a "(current)" suffix in green so the player can spot the
             * already-assigned candidate without scanning every billet. */
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            // Over-budget rows use the amber underfunded tint for the name too, mirroring the
            // colony-tab slot-row treatment. Otherwise the name follows win-chance color.
            GUI.color = overBudget
                ? baseTint
                : (row.available ? winColor : UIUtil.Dim(winColor));
            bool boxVisible = ShowForceMetrics || ShowWinChance;
            float nameW = boxVisible ? (boxX - contentX - boxGap) : (rightColX - contentX - 4f);
            if (nameW < 0f) nameW = 0f;
            string nameLbl = squad.DisplayName;
            Widgets.Label(new Rect(contentX, headerY, nameW, CardHeaderH), nameLbl);
            if (isCurrentCard)
            {
                // Append "(current)" right after the name, green-tinted, using the existing
                // FCSetSquadCurrent key.
                Vector2 nameSize = Text.CalcSize(nameLbl);
                float suffixX = contentX + Math.Min(nameSize.x + 6f, nameW - 4f);
                float suffixW = Math.Max(0f, contentX + nameW - suffixX);
                if (suffixW > 0f)
                {
                    Color colorBefore2 = GUI.color;
                    GUI.color = new Color(0.6f, 0.9f, 0.6f);
                    Widgets.Label(new Rect(suffixX, headerY, suffixW, CardHeaderH), "FCSetSquadCurrent".Translate());
                    GUI.color = colorBefore2;
                }
            }

            /* Detail row — Settlement | Travel | Cost, left of the box (or all the way to the
             * right column when the box is hidden). Travel column collapses when ShowTravel
             * is false, folding its space into Cost. */
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = baseTint;

            float labelsW = boxVisible ? (boxX - contentX - boxGap) : (rightColX - contentX - 4f);
            if (labelsW < 0f) labelsW = 0f;
            float colSettlement = Math.Min(220f, labelsW * 0.5f);
            float colTravel     = ShowTravel
                ? Math.Min(90f, Math.Max(0f, (labelsW - colSettlement) * 0.4f))
                : 0f;
            float colCost       = Math.Max(0f, labelsW - colSettlement - colTravel);

            string settlementLbl = "FCSquadColBillet".Translate() + ": "
                + (squad.settlement?.Name ?? "FCMilitaryTableSlotEmpty".Translate());
            string costLbl = (string)"FCSquadColDeploymentCost".Translate() + ": $" + row.deploymentCost;

            float dx = contentX;
            Widgets.Label(new Rect(dx, detailY, colSettlement, CardDetailH), settlementLbl); dx += colSettlement;
            if (ShowTravel)
            {
                string travelLbl = "FCSquadColTravel".Translate() + ": "
                    + (squad.IsAssigned && row.travelTicks > 0
                        ? (row.travelTicks / (float)GenDate.TicksPerDay).ToString("0.0") + " d"
                        : "-");
                Widgets.Label(new Rect(dx, detailY, colTravel, CardDetailH), travelLbl);
                dx += colTravel;
            }
            Widgets.Label(new Rect(dx, detailY, colCost, CardDetailH), costLbl);

            // Whole-card click → select. Drawn last so the Inspect button consumes its click first.
            if (Widgets.ButtonInvisible(cardRect))
            {
                OnRowSelected(squad);
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
            GUI.color = colorBefore;
        }

        /* Selection hook. Defaults to assigning `selected`. Defend picker overrides to also
         * clear the parallel external-defender selection. */
        protected virtual void OnRowSelected(MercenarySquadFC squad) { selected = squad; }

        /* Delegates to SquadStatusUtil so the picker, the squads tab, and the settlement card
         * all derive their status from one place. <paramref name="now"/> kept for source-
         * compatibility with overrides; the helper reads TicksGame internally. */
        protected static void ComputeStatus(MercenarySquadFC squad, int now,
            out string status, out Color color, out bool isReady)
        {
            SquadStatusUtil.Resolve(squad, out status, out color, out isReady);
        }

        protected static string SortLabel(SortMode mode)
        {
            switch (mode)
            {
                case SortMode.WinChance: return "FCSquadColWinChance".Translate();
                case SortMode.Travel:    return "FCSquadColTravel".Translate();
                case SortMode.Power:     return "FCSquadColPower".Translate();
                case SortMode.Name:      return "FCSquadColName".Translate();
            }
            return "?";
        }

        protected void ApplySort()
        {
            switch (sort)
            {
                /* Sort by midpoint so a row with a wider but higher-on-average range still
                 * outranks a narrower lower-average row. */
                case SortMode.WinChance: rows = rows.OrderByDescending(r => (r.winChanceMin + r.winChanceMax) * 0.5).ToList(); break;
                case SortMode.Travel:    rows = rows.OrderBy(r => r.travelTicks).ToList(); break;
                case SortMode.Power:     rows = rows.OrderByDescending(r => r.ourPower).ToList(); break;
                case SortMode.Name:      rows = rows.OrderBy(r => r.squad.DisplayName).ToList(); break;
            }
        }
    }
}
