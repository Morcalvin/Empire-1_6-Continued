using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Faction-wide squad pool overview. Lists every <see cref="MercenarySquadFC"/> with billet,
    /// status, hire cost, and upgrade cost. Per-row actions: Rename, Reassign, Upgrade, Dismiss.
    /// Reused as the "By Squad" subtab body of the main military tab.
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

        /// <summary>Draws the table directly into <paramref name="rect"/>. Used both standalone
        /// (this Window's DoWindowContents) and embedded inside the main military tab's
        /// "By Squad" subtab.</summary>
        public void Draw(Rect rect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 30f), "FCHireSquadsHeader".Translate());

            Text.Font = GameFont.Small;
            float headerH = 30f;
            Rect tableRect = new Rect(rect.x, rect.y + headerH + 4f, rect.width, rect.height - headerH - 4f);
            DrawTable(tableRect);

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        private void DrawTable(Rect rect)
        {
            // Column header
            float colHeaderH = 22f;
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, colHeaderH);
            Widgets.DrawHighlight(headerRect);
            DrawColumns(headerRect, isHeader: true,
                name: "FCSquadColName".Translate(),
                template: "FCSquadColTemplate".Translate(),
                billet: "FCSquadColBillet".Translate(),
                status: "FCSquadColStatus".Translate(),
                cost: "FCSquadColCost".Translate(),
                upgrade: "FCSquadColUpgrade".Translate(),
                actions: "FCSquadColActions".Translate());

            FactionFC fc = FactionCache.FactionComp;
            MilitaryCustomizationUtil util = fc?.militaryCustomizationUtil;
            List<MercenarySquadFC> pool = util?.mercenarySquads ?? new List<MercenarySquadFC>();

            float rowH = 30f;
            float listTop = rect.y + colHeaderH;
            Rect listRect = new Rect(rect.x, listTop, rect.width, rect.yMax - listTop);
            float viewH = pool.Count * rowH;
            Rect viewRect = new Rect(0, 0, listRect.width - 16f, viewH);
            Widgets.BeginScrollView(listRect, ref scroll, viewRect);
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < pool.Count; i++)
            {
                MercenarySquadFC squad = pool[i];
                if (squad is null) continue;
                Rect rowRect = new Rect(0, i * rowH, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawHighlight(rowRect);

                string status;
                if (!squad.IsAssigned) status = "FCSquadStatusUnassigned".Translate();
                else if (squad.IsBusy)
                {
                    MilitaryOperation op = squad.Operation;
                    int ticksLeft = Math.Max(0, op.nextPhaseTick - now);
                    string opLabel = op?.kind?.label ?? "?";
                    status = "FCSquadStatusBusyOp".Translate(opLabel, (ticksLeft / (float)GenDate.TicksPerDay).ToString("0.0"));
                }
                else if (squad.nextAvailableTick > now)
                {
                    int ticksLeft = squad.nextAvailableTick - now;
                    status = "FCSquadStatusCooldown".Translate((ticksLeft / (float)GenDate.TicksPerDay).ToString("0.0"));
                }
                else status = "FCSquadStatusReady".Translate();

                int upgrade = squad.UpgradeCost;
                string upgradeText = upgrade > 0 ? "$" + upgrade : "-";

                DrawColumns(rowRect, isHeader: false,
                    name: squad.name ?? "(?)",
                    template: squad.outfit?.name ?? "(stripped)",
                    billet: squad.settlement?.Name ?? "(unassigned)",
                    status: status,
                    cost: "$" + squad.hireCostPaid,
                    upgrade: upgradeText,
                    actions: "");

                // Action buttons in the rightmost column
                float actionsX = rect.x + ColumnOffset(6);
                MercenarySquadFC capturedSquad = squad;
                if (Widgets.ButtonText(new Rect(actionsX, rowRect.y + 2f, 90f, rowH - 4f), "FCSquadActReassign".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_SquadAssignment(capturedSquad));
                }
                bool canUpgrade = upgrade > 0 && !squad.IsBusy;
                Color colorBefore = GUI.color;
                if (!canUpgrade) GUI.color = Color.gray;
                if (Widgets.ButtonText(new Rect(actionsX + 95f, rowRect.y + 2f, 90f, rowH - 4f), "FCSquadActUpgrade".Translate(), true, true, canUpgrade))
                {
                    capturedSquad.UpgradeToTemplate();
                }
                GUI.color = colorBefore;

                bool canDismiss = !squad.IsBusy;
                Color cb = GUI.color;
                if (!canDismiss) GUI.color = Color.gray;
                if (Widgets.ButtonText(new Rect(actionsX + 190f, rowRect.y + 2f, 80f, rowH - 4f), "FCSquadActDismiss".Translate(), true, true, canDismiss))
                {
                    int refund = (int)Math.Round(capturedSquad.hireCostPaid * FCSettings.squadDismissalRefundFraction);
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "FCSquadActDismissConfirm".Translate(capturedSquad.name, refund),
                        delegate { util.DismissSquad(capturedSquad); }));
                }
                GUI.color = cb;

                // Click on Name to rename
                Rect nameClickRect = new Rect(rowRect.x + ColumnOffset(0), rowRect.y, ColumnWidth(0), rowH);
                if (Widgets.ButtonInvisible(nameClickRect))
                {
                    Find.WindowStack.Add(new FCWindow_Rename(squad.name ?? "", "FCRenameSquad",
                        n => { capturedSquad.name = n; }));
                }
            }
            Widgets.EndScrollView();
        }

        private static readonly float[] ColWidths = { 140f, 130f, 130f, 160f, 70f, 70f, 280f };

        private static float ColumnOffset(int idx)
        {
            float x = 6f;
            for (int i = 0; i < idx; i++) x += ColWidths[i];
            return x;
        }
        private static float ColumnWidth(int idx) => ColWidths[idx];

        private static void DrawColumns(Rect rect, bool isHeader,
            string name, string template, string billet, string status, string cost, string upgrade, string actions)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Text.Font = isHeader ? GameFont.Tiny : GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            float x = rect.x + 6f;
            Widgets.Label(new Rect(x, rect.y, ColWidths[0], rect.height), name); x += ColWidths[0];
            Widgets.Label(new Rect(x, rect.y, ColWidths[1], rect.height), template); x += ColWidths[1];
            Widgets.Label(new Rect(x, rect.y, ColWidths[2], rect.height), billet); x += ColWidths[2];
            Widgets.Label(new Rect(x, rect.y, ColWidths[3], rect.height), status); x += ColWidths[3];
            Widgets.Label(new Rect(x, rect.y, ColWidths[4], rect.height), cost); x += ColWidths[4];
            Widgets.Label(new Rect(x, rect.y, ColWidths[5], rect.height), upgrade); x += ColWidths[5];
            // Actions column drawn separately by caller (interactive buttons).

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }
    }
}
