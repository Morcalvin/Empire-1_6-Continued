using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Faction-wide squad pool overview. Lists every <see cref="MercenarySquadFC"/> as a
    /// header+detail card with accent strip, billet, status, hire/upgrade costs, and per-squad
    /// actions: Inspect, Reassign, Upgrade, Dismiss. Reused as the "By Squad" subtab body of
    /// the main military tab.
    /// </summary>
    public class HireSquadsWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(900f, 640f);

        private Vector2 scroll;

        public HireSquadsWindow()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
            preventCameraMotion = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Draw(inRect);
        }

        /* Draw layout constants. cardH = cardHeaderH + cardDetailH; cards stack with rowGap.
           SummaryH matches DrawMilitarySettlementCards' "# settlements" readout so the two
           subtabs share the same top-of-content rhythm. */
        private const float SummaryH     = 24f;
        private const float Pad          = 4f;
        private const float RowGap       = 2f;
        private const float CardHeaderH  = 24f;
        private const float CardDetailH  = 22f;
        private const float CardH        = CardHeaderH + CardDetailH;
        private const float AccentW      = 4f;

        /// <summary>Draws the squad pool list directly into <paramref name="rect"/>. Used both
        /// standalone (this Window's DoWindowContents) and embedded inside the main military
        /// tab's "By Squad" subtab.</summary>
        public void Draw(Rect rect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            FactionFC fc = FactionCache.FactionComp;
            MilitaryCustomizationUtil util = fc?.militaryCustomizationUtil;
            List<MercenarySquadFC> pool = util?.mercenarySquads ?? new List<MercenarySquadFC>();

            float innerX = rect.x + Pad;
            float innerW = rect.width - Pad * 2f;

            // Count readout — small/grey, matches DrawMilitarySettlementCards' "# settlements".
            Color origColor = GUI.color;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(innerX, rect.y + Pad, innerW * 0.5f, SummaryH),
                "FCHireSquadsCount".Translate(pool.Count));
            GUI.color = origColor;

            Rect tableRect = new Rect(rect.x, rect.y + SummaryH + 4f,
                rect.width, rect.height - SummaryH - 4f);

            if (pool.Count == 0)
            {
                Color savedColor = GUI.color;
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(tableRect.x, tableRect.y + tableRect.height * 0.35f,
                    tableRect.width, 40f), "FCHireSquadsEmpty".Translate());
                GUI.color = savedColor;
                Text.Font = fontBefore;
                Text.Anchor = anchorBefore;
                return;
            }

            float listY  = tableRect.y + Pad;
            float viewH  = tableRect.yMax - listY - Pad;
            float totalH = pool.Count * (CardH + RowGap);

            Rect viewRect = new Rect(innerX, listY, innerW, viewH);
            Rect scrollRect = ScrollUtil.BeginScrollView(viewRect, ref scroll, totalH);

            int now = Find.TickManager.TicksGame;
            float runningY = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                MercenarySquadFC squad = pool[i];
                if (squad is null) continue;
                Rect cardRect = new Rect(0f, runningY, scrollRect.width, CardH);
                DrawSquadCard(cardRect, squad, i, now, util);
                runningY += CardH + RowGap;
            }
            ScrollUtil.EndScrollView();

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        /* Per-squad card. Header row: accent strip, squad name (clickable), right-aligned
           status badge. Detail row: Template / Billet / Cost / Upgrade columns followed by
           four right-aligned action buttons (Inspect, Reassign, Upgrade, Dismiss). */
        private void DrawSquadCard(Rect cardRect, MercenarySquadFC squad, int index, int now,
            MilitaryCustomizationUtil util)
        {
            // Alternating row background to match settlement-card list style
            if (index % 2 == 0)
                Widgets.DrawHighlight(cardRect);

            // Accent strip
            Color accent = squad.settlement?.MilitaryComp != null
                ? AccentUtil.GetMilitaryAccent(squad.settlement.MilitaryComp)
                : AccentUtil.MilInactive;
            Widgets.DrawBoxSolid(new Rect(cardRect.x, cardRect.y, AccentW, cardRect.height), accent);

            float contentX = cardRect.x + AccentW + 6f;
            float contentW = cardRect.xMax - contentX - 4f;

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Color colorBefore = GUI.color;

            /* === HEADER ROW === */
            float headerY = cardRect.y;
            string statusText = ComputeStatus(squad, now);
            Color statusColor = ColorForStatus(squad, now);

            // Status badge — right-aligned
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            float statusW = 180f;
            GUI.color = statusColor;
            Widgets.Label(new Rect(cardRect.xMax - statusW - 4f, headerY, statusW, CardHeaderH), statusText);
            GUI.color = colorBefore;

            // Squad name (clickable to open inspection — kept as a fallback alongside the
            // explicit Inspect button below).
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = accent;
            float nameW = contentW - statusW - 6f;
            Rect nameRect = new Rect(contentX, headerY, nameW, CardHeaderH);
            Widgets.Label(nameRect, squad.DisplayName);
            GUI.color = colorBefore;
            if (Mouse.IsOver(nameRect)) Widgets.DrawHighlight(nameRect);
            if (Widgets.ButtonInvisible(nameRect))
                Find.WindowStack.Add(new Dialog_SquadInspection(squad));

            /* === DETAIL ROW === */
            float detailY = cardRect.y + CardHeaderH;
            const float btnW = 78f;
            const float btnGap = 2f;
            float btnH = CardDetailH - 2f;
            float btnY = detailY + 1f;
            const int btnCount = 4;
            float buttonAreaW = btnW * btnCount + btnGap * (btnCount - 1);

            // Detail labels — fixed-width columns left of the button block
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float dx = contentX;
            float labelsW = contentW - buttonAreaW - 6f;
            if (labelsW < 0f) labelsW = 0f;

            float colTemplate = Math.Min(190f, labelsW * 0.28f);
            float colBillet   = Math.Min(220f, labelsW * 0.30f);
            float colPower    = Math.Min(90f,  labelsW * 0.14f);
            float colCost     = Math.Min(110f, labelsW * 0.16f);
            float colUpgrade  = Math.Max(0f, labelsW - colTemplate - colBillet - colPower - colCost);

            double powerLevel  = SquadPowerRegistry.Resolve(squad).militaryLevel;
            string templateLbl = (string)"FCSquadColTemplate".Translate() + ": " + (squad.outfit?.name ?? "(stripped)");
            string billetLbl   = (string)"FCSquadColBillet".Translate() + ": " + (squad.settlement?.Name ?? (string)"FCMilitaryTableSlotEmpty".Translate());
            string powerLbl    = (string)"FCSquadColPower".Translate() + ": " + powerLevel.ToString("0.0");
            string costLbl     = (string)"FCSquadColCost".Translate() + ": $" + squad.hireCostPaid;
            int upgrade        = squad.UpgradeCost;
            string upgradeLbl  = (string)"FCSquadColUpgrade".Translate() + ": " + (upgrade > 0 ? "$" + upgrade : "-");

            Widgets.Label(new Rect(dx, detailY, colTemplate, CardDetailH), templateLbl); dx += colTemplate;
            Widgets.Label(new Rect(dx, detailY, colBillet,   CardDetailH), billetLbl);   dx += colBillet;
            Widgets.Label(new Rect(dx, detailY, colPower,    CardDetailH), powerLbl);    dx += colPower;
            Widgets.Label(new Rect(dx, detailY, colCost,     CardDetailH), costLbl);     dx += colCost;
            Widgets.Label(new Rect(dx, detailY, colUpgrade,  CardDetailH), upgradeLbl);

            // Action buttons (right-aligned)
            MercenarySquadFC capturedSquad = squad;
            float bx = cardRect.xMax - buttonAreaW - 4f;

            // Inspect
            Rect inspectRect = new Rect(bx, btnY, btnW, btnH);
            if (UIUtil.ButtonFlat(inspectRect, "FCMilitaryTableInspect".Translate()))
            {
                Find.WindowStack.Add(new Dialog_SquadInspection(capturedSquad));
            }
            TooltipHandler.TipRegion(inspectRect, "FCMilBtnInspectTip".Translate());
            bx += btnW + btnGap;

            // Reassign
            Rect reassignRect = new Rect(bx, btnY, btnW, btnH);
            if (UIUtil.ButtonFlat(reassignRect, "FCSquadActReassign".Translate()))
            {
                Find.WindowStack.Add(new Dialog_SquadAssignment(capturedSquad));
            }
            bx += btnW + btnGap;

            // Upgrade
            bool canUpgrade = upgrade > 0 && !squad.IsBusy;
            Rect upgradeRect = new Rect(bx, btnY, btnW, btnH);
            if (UIUtil.ButtonFlat(upgradeRect, "FCSquadActUpgrade".Translate(), disabled: !canUpgrade))
            {
                capturedSquad.UpgradeToTemplate();
            }
            bx += btnW + btnGap;

            // Dismiss
            bool canDismiss = !squad.IsBusy;
            Rect dismissRect = new Rect(bx, btnY, btnW, btnH);
            if (UIUtil.ButtonFlat(dismissRect, "FCSquadActDismiss".Translate(), disabled: !canDismiss))
            {
                int refund = (int)Math.Round(capturedSquad.hireCostPaid * FCSettings.squadDismissalRefundFraction);
                MilitaryCustomizationUtil utilCaptured = util;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "FCSquadActDismissConfirm".Translate(capturedSquad.DisplayName, refund),
                    delegate { utilCaptured.DismissSquad(capturedSquad); }));
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
            GUI.color = colorBefore;
        }

        /* Status string for one squad. Lifted out of the old DrawTable so the card draw
           stays readable. */
        private static string ComputeStatus(MercenarySquadFC squad, int now)
        {
            if (!squad.IsAssigned) return "FCSquadStatusUnassigned".Translate();
            if (squad.IsBusy)
            {
                MilitaryOperation op = squad.Operation;
                int ticksLeft = Math.Max(0, op.nextPhaseTick - now);
                string opLabel = op?.kind?.label ?? "?";
                return "FCSquadStatusBusyOp".Translate(opLabel,
                    (ticksLeft / (float)GenDate.TicksPerDay).ToString("0.0"));
            }
            if (squad.nextAvailableTick > now)
            {
                int ticksLeft = squad.nextAvailableTick - now;
                return "FCSquadStatusCooldown".Translate(
                    (ticksLeft / (float)GenDate.TicksPerDay).ToString("0.0"));
            }
            return "FCSquadStatusReady".Translate();
        }

        /* Status label color: ready = green, cooldown/busy = yellow/orange, unassigned = grey. */
        private static Color ColorForStatus(MercenarySquadFC squad, int now)
        {
            if (!squad.IsAssigned) return AccentUtil.MilInactive;
            if (squad.IsBusy) return AccentUtil.MilActiveMission;
            if (squad.nextAvailableTick > now) return AccentUtil.MilCooldown;
            return AccentUtil.MilReady;
        }
    }
}
