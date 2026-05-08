using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/
    /* BattleProgressWindow                                                        */
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/

    /// <summary>
    /// Live "watch the battle unfold" window for an auto-resolved battle. Reads the linked
    /// <see cref="MilitaryOperation.battleProgress"/> every frame: data only changes hourly so
    /// per-frame reads are cheap. Two columns (attacker / defender) with health bars,
    /// efficiency / faction labels, and a scrolling list of rolls (latest at top). Player-side
    /// round wins are tinted green; player-side losses are tinted red. Pure NPC-vs-NPC ops
    /// (player aligned with neither side) are not tinted.
    /// </summary>
    public class BattleProgressWindow : Window
    {
        private readonly MilitaryOperation op;
        private Vector2 scrollPos;

        public BattleProgressWindow(MilitaryOperation op)
        {
            this.op = op;
            doCloseButton = true;
            doCloseX = true;
            forcePause = false;
            draggable = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
        }

        public override Vector2 InitialSize => new Vector2(720f, 600f);

        public override void DoWindowContents(Rect inRect)
        {
            if (op is null || op.battleProgress is null)
            {
                Widgets.Label(inRect, "FCBattleProgressNoBattle".Translate());
                return;
            }

            BattleProgress bp = op.battleProgress;

            /* -*- Header -*- */
            float headerH = 56f;
            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, headerH);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            string targetName = !string.IsNullOrEmpty(bp.defenderLabel) ? bp.defenderLabel : "?";
            Widgets.Label(new Rect(headerRect.x, headerRect.y, headerRect.width, 28f),
                "FCBattleProgressWindowTitle".Translate(targetName));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(headerRect.x, headerRect.y + 30f, headerRect.width, 22f),
                SubPhaseLabel(bp));
            Text.Anchor = TextAnchor.UpperLeft;

            /* -*- Two columns: attacker vs defender -*- */
            float columnsY = inRect.y + headerH + 8f;
            float columnsH = 160f;
            float colGap = 12f;
            float colW = (inRect.width - colGap) * 0.5f;
            Rect attackerCol = new Rect(inRect.x, columnsY, colW, columnsH);
            Rect defenderCol = new Rect(inRect.x + colW + colGap, columnsY, colW, columnsH);

            DrawSideColumn(attackerCol,
                bp.attackerLabel,
                bp.attackerFactionName,
                bp.attackerInitialForce,
                bp.attackerForceRemaining,
                bp.attackerEfficiency,
                isAttacker: true);
            DrawSideColumn(defenderCol,
                bp.defenderLabel,
                bp.defenderFactionName,
                bp.defenderInitialForce,
                bp.defenderForceRemaining,
                bp.defenderEfficiency,
                isAttacker: false);

            /* -*- Round list (scrollable, latest first) -*- */
            float listY = columnsY + columnsH + 8f;
            float closeBtnReserve = 40f;
            float listH = inRect.height - (listY - inRect.y) - closeBtnReserve;
            Rect listRect = new Rect(inRect.x, listY, inRect.width, listH);

            DrawRoundList(listRect, bp);
        }

        private string SubPhaseLabel(BattleProgress bp)
        {
            switch (bp.subPhase)
            {
                case BattleSubPhase.Preparing: return "FCBattlePhasePreparing".Translate();
                case BattleSubPhase.Engaged: return "FCBattlePhaseEngaged".Translate();
                case BattleSubPhase.RollsInProgress: return "FCBattlePhaseRolling".Translate(bp.rounds.Count);
                case BattleSubPhase.Resolved: return "FCBattlePhaseResolved".Translate();
                default: return string.Empty;
            }
        }

        private void DrawSideColumn(Rect rect, string label, string factionName,
            double initial, double remaining, double efficiency, bool isAttacker)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            string title = isAttacker
                ? "FCBattleSideAttacker".Translate()
                : "FCBattleSideDefender".Translate();
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 20f), title);
            GUI.color = Color.white;

            float y = inner.y + 22f;
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), label ?? "?");
            y += 22f;
            if (!string.IsNullOrEmpty(factionName))
            {
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(new Rect(inner.x, y, inner.width, 20f), factionName);
                GUI.color = Color.white;
                y += 20f;
            }

            string forces = "FCBattleForceLine".Translate(remaining.ToString("0.#"), initial.ToString("0.#"));
            Widgets.Label(new Rect(inner.x, y, inner.width, 20f), forces);
            y += 20f;
            string eff = "FCBattleEfficiencyLine".Translate(efficiency.ToString("0.00"));
            Widgets.Label(new Rect(inner.x, y, inner.width, 20f), eff);
            y += 24f;

            float fill = initial > 0 ? Mathf.Clamp01((float)(remaining / initial)) : 0f;
            Color barBg = new Color(0.15f, 0.15f, 0.15f);
            Color barFill = isAttacker
                ? new Color(0.75f, 0.35f, 0.30f)   // attacker red-orange
                : new Color(0.30f, 0.55f, 0.75f);  // defender blue
            Rect bar = new Rect(inner.x, y, inner.width, 14f);
            UIUtil.DrawProgressBarColors(bar, fill, barBg, barFill);
        }

        private void DrawRoundList(Rect rect, BattleProgress bp)
        {
            // Frame the list area.
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(4f);

            // Header row above the scroll viewport so it stays put.
            float headerH = 22f;
            Rect headerRect = new Rect(inner.x, inner.y, inner.width, headerH);
            DrawRoundListHeader(headerRect);

            Rect viewportOuter = new Rect(inner.x, inner.y + headerH + 2f, inner.width,
                                          inner.height - headerH - 2f);

            const float rowH = 22f;
            int count = bp.rounds.Count;
            float contentH = Math.Max(rowH, count * rowH);
            Rect viewportInner = new Rect(0f, 0f, viewportOuter.width - 16f, contentH);

            Widgets.BeginScrollView(viewportOuter, ref scrollPos, viewportInner);

            // Latest at top: iterate in reverse.
            bool playerSideKnown = op.IsOffensive || op.IsDefensive;
            for (int i = count - 1; i >= 0; i--)
            {
                int displayIndex = (count - 1) - i;
                Rect rowRect = new Rect(0f, displayIndex * rowH, viewportInner.width, rowH);
                DrawRoundRow(rowRect, bp.rounds[i], playerSideKnown);
            }

            Widgets.EndScrollView();
        }

        private void DrawRoundListHeader(Rect rect)
        {
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            float[] colWidths = ComputeRoundColumnWidths(rect.width);
            float x = rect.x + 4f;
            string[] headers = new[]
            {
                "FCBattleColRound".Translate().ToString(),
                "FCBattleColAttacker".Translate().ToString(),
                "FCBattleColDefender".Translate().ToString(),
                "FCBattleColWinner".Translate().ToString(),
                "FCBattleColForces".Translate().ToString()
            };
            for (int i = 0; i < headers.Length; i++)
            {
                Widgets.Label(new Rect(x, rect.y, colWidths[i], rect.height), headers[i]);
                x += colWidths[i];
            }
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.DrawLineHorizontal(rect.x, rect.yMax, rect.width);
        }

        private void DrawRoundRow(Rect rect, RoundEntry r, bool playerSideKnown)
        {
            // Tint background based on whether the player won this round.
            if (playerSideKnown)
            {
                bool playerWonThisRound = (op.IsOffensive == r.attackerWonRound);
                Color tint = playerWonThisRound
                    ? new Color(0.20f, 0.50f, 0.20f, 0.25f)   // soft green
                    : new Color(0.50f, 0.20f, 0.20f, 0.25f);  // soft red
                Widgets.DrawBoxSolid(rect, tint);
            }

            float[] colWidths = ComputeRoundColumnWidths(rect.width);
            float x = rect.x + 4f;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;

            // Round
            Widgets.Label(new Rect(x, rect.y, colWidths[0], rect.height), r.roundNumber.ToString());
            x += colWidths[0];

            // Attacker raw -> final
            string atk = $"{r.attackerRawRoll} -> {r.attackerScore:0.00}";
            Widgets.Label(new Rect(x, rect.y, colWidths[1], rect.height), atk);
            x += colWidths[1];

            // Defender raw -> final
            string def = $"{r.defenderRawRoll} -> {r.defenderScore:0.00}";
            Widgets.Label(new Rect(x, rect.y, colWidths[2], rect.height), def);
            x += colWidths[2];

            // Winner
            string winner = r.attackerWonRound
                ? "FCBattleSideAttacker".Translate().ToString()
                : "FCBattleSideDefender".Translate().ToString();
            Widgets.Label(new Rect(x, rect.y, colWidths[3], rect.height), winner);
            x += colWidths[3];

            // Forces (A / D)
            string forces = $"{r.attackerForceAfter:0.#} / {r.defenderForceAfter:0.#}";
            Widgets.Label(new Rect(x, rect.y, colWidths[4], rect.height), forces);

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static float[] ComputeRoundColumnWidths(float totalWidth)
        {
            // Round (8%), Attacker (28%), Defender (28%), Winner (16%), Forces (20%)
            float w = totalWidth - 8f;
            return new[]
            {
                w * 0.08f,
                w * 0.28f,
                w * 0.28f,
                w * 0.16f,
                w * 0.20f
            };
        }
    }
}
