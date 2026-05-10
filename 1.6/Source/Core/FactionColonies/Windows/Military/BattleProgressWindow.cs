using System;
using System.Collections.Generic;
using System.Linq;
using FactionColonies.util;
using RimWorld;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/
    /* BattleProgressWindow                                                        */
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/

    /// <summary>
    /// Shared window for "watch this battle", both during live auto-resolve and
    /// when reviewing an archived <see cref="BattleResult"/>. Two mirrored side panels
    /// (attacker left, defender right) with faction icon + name/label, efficiency, and
    /// a health bar with the force ratio overlaid. Live battles get an inline countdown
    /// progress bar showing time until the next round tick. Below: a scrolling, mirrored
    /// round-roll log — Round | Atk Force/Raw/Final | Def Final/Raw/Force — with the
    /// winning side's block tinted per round.
    /// <para>For manual battles the rounds list is empty; the round-list area shows a
    /// "no per-round detail" placeholder instead of an empty scroll viewport.</para>
    /// </summary>
    public class BattleProgressWindow : Window
    {
        // Live op reference is null when opened from an archived report; the countdown
        // bar is the only feature that requires it.
        private readonly MilitaryOperation op;
        private readonly BattleResult result;
        private readonly BattleViewerSide playerSide;
        // Icon resolution priority: live participant -> archived faction reference -> name string.
        private readonly MilitaryOperationParticipant aggressorParticipant;
        private readonly MilitaryOperationParticipant defenderParticipant;

        private Vector2 scrollPos;

        public BattleProgressWindow(MilitaryOperation op)
        {
            this.op = op;
            this.result = op?.battleResult;
            this.playerSide = MilitaryUtil.ResolvePlayerSide(op);
            this.aggressorParticipant = op?.aggressor;
            this.defenderParticipant = op?.defender;
            InitWindowProps();
        }

        /// <summary>
        /// Constructor for archived-report viewing: the originating op is gone.
        /// Side panels still render the faction icon when the stored faction reference
        /// resolves (defeated factions still resolve); only when the faction has been
        /// removed from the world entirely do they fall back to the bare name string.
        /// </summary>
        public BattleProgressWindow(BattleResult result, BattleViewerSide playerSide)
        {
            this.result = result;
            this.playerSide = playerSide;
            InitWindowProps();
        }

        private void InitWindowProps()
        {
            doCloseButton = true;
            doCloseX = true;
            forcePause = false;
            draggable = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
        }

        public override Vector2 InitialSize => new Vector2(720f, 600f);

        private bool PlayerSideKnown => playerSide != BattleViewerSide.Neither;
        private bool PlayerIsAttacker => playerSide == BattleViewerSide.Attacker;

        public override void DoWindowContents(Rect inRect)
        {
            if (result is null)
            {
                Widgets.Label(inRect, "FCBattleProgressNoBattle".Translate());
                return;
            }

            BattleResult br = result;

            /* -*- Header (title + sub-phase + optional tick countdown) -*- */
            bool showTickBar = TryGetTickProgress(br, out float tickProgress, out int ticksRemaining);
            float headerH = showTickBar ? 72f : 56f;
            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, headerH);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            string targetName = !string.IsNullOrEmpty(br.defenderLabel) ? br.defenderLabel : "?";
            Widgets.Label(new Rect(headerRect.x, headerRect.y, headerRect.width, 28f),
                "FCBattleProgressWindowTitle".Translate(targetName));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(headerRect.x, headerRect.y + 30f, headerRect.width, 22f),
                SubPhaseLabel(br));
            Text.Anchor = TextAnchor.UpperLeft;

            if (showTickBar)
            {
                const float tickBarW = 240f;
                const float tickBarH = 12f;
                Rect tickBarRect = new Rect(
                    headerRect.x + (headerRect.width - tickBarW) / 2f,
                    headerRect.y + 54f,
                    tickBarW, tickBarH);
                DrawTickProgressBar(tickBarRect, tickProgress, ticksRemaining);
            }

            /* -*- Two columns: attacker vs defender -*- */
            float columnsY = inRect.y + headerH + 8f;
            float columnsH = 160f;
            float colGap = 12f;
            float colW = (inRect.width - colGap) * 0.5f;
            Rect attackerCol = new Rect(inRect.x, columnsY, colW, columnsH);
            Rect defenderCol = new Rect(inRect.x + colW + colGap, columnsY, colW, columnsH);

            DrawSideColumn(attackerCol, aggressorParticipant, br.attackerFaction,
                br.attackerLabel, br.attackerFactionName,
                br.attackerInitialForce, br.attackerForceRemaining, br.attackerEfficiency,
                isAttacker: true);
            DrawSideColumn(defenderCol, defenderParticipant, br.defenderFaction,
                br.defenderLabel, br.defenderFactionName,
                br.defenderInitialForce, br.defenderForceRemaining, br.defenderEfficiency,
                isAttacker: false);

            /* -*- Round list (scrollable, latest first) -*- */
            float listY = columnsY + columnsH + 8f;
            float closeBtnReserve = 40f;
            float listH = inRect.height - (listY - inRect.y) - closeBtnReserve;
            Rect listRect = new Rect(inRect.x, listY, inRect.width, listH);

            DrawRoundList(listRect, br);
        }

        private string SubPhaseLabel(BattleResult br)
        {
            switch (br.subPhase)
            {
                case BattleSubPhase.Preparing: return "FCBattlePhasePreparing".Translate();
                case BattleSubPhase.Engaged: return "FCBattlePhaseEngaged".Translate();
                case BattleSubPhase.RollsInProgress: return "FCBattlePhaseRolling".Translate(br.rounds.Count);
                case BattleSubPhase.Resolved: return "FCBattlePhaseResolved".Translate();
                default: return string.Empty;
            }
        }

        /* -*- Tick countdown -*- */

        private bool TryGetTickProgress(BattleResult br, out float progress, out int ticksRemaining)
        {
            progress = 0f;
            ticksRemaining = 0;
            if (op is null) return false;
            if (br.subPhase == BattleSubPhase.Resolved) return false;
            if (br.IsComplete) return false;

            FCEvent evt = op.sourceEvents?.FirstOrDefault(
                e => e is object && e.def == FCEventDefOf.autoResolveBattleRound);
            if (evt is null) return false;

            ticksRemaining = Mathf.Max(0, evt.timeTillTrigger - Find.TickManager.TicksGame);
            int interval = Mathf.Max(1, FCSettings.autoResolveTicksPerRound);
            if (DebugSettings.godMode) interval = 1;
            progress = 1f - Mathf.Clamp01((float)ticksRemaining / interval);
            return true;
        }

        private static void DrawTickProgressBar(Rect rect, float progress, int ticksRemaining)
        {
            Color bg = new Color(0.15f, 0.15f, 0.15f);
            Color fill = new Color(0.35f, 0.65f, 0.75f);
            UIUtil.DrawProgressBarColors(rect, progress, bg, fill);

            int seconds = Mathf.CeilToInt(ticksRemaining / 60f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, "FCBattleNextRoundIn".Translate(seconds));
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /* -*- Side panel (mirrored: attacker = left-anchored, defender = right-anchored) -*- */
        private void DrawSideColumn(Rect rect, MilitaryOperationParticipant participant,
            Faction storedFaction, string label, string fallbackFactionName,
            double initial, double remaining, double efficiency, bool isAttacker)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);

            // Live participant takes priority (faction state is freshest there); the
            // archive-stored Faction reference is the second-tier source so reports
            // remain rendered with full faction info even after the op is gone.
            Faction faction = participant?.faction ?? storedFaction;
            bool isPlayerSide = isAttacker ? PlayerIsAttacker : (playerSide == BattleViewerSide.Defender);
            TextAnchor textAnchor = isAttacker ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;

            /* Title row with side-aware gradient. Full color on the outer edge, fading to
               transparent toward the panel centerline (between the two columns), so the
               two sides read as "facing inward". */
            float titleH = 22f;
            Rect titleRect = new Rect(inner.x, inner.y, inner.width, titleH);
            Color titleTint = ResolveTitleTint(PlayerSideKnown, isPlayerSide);
            TexLoad.DrawHorizontalGradient(titleRect, titleTint, reversed: !isAttacker);

            Text.Font = GameFont.Small;
            Text.Anchor = textAnchor;
            string title = isAttacker
                ? "FCBattleSideAttacker".Translate()
                : "FCBattleSideDefender".Translate();
            GUI.color = new Color(0.95f, 0.95f, 0.95f);
            Widgets.Label(titleRect.ContractedBy(4f, 0f), title);
            GUI.color = Color.white;

            /* Icon block: 40x40 icon flush left/right, two text rows on the other side.
               When the faction can't be resolved at all (live participant gone AND the
               stored reference no longer resolves — i.e., faction removed from world),
               the icon is omitted and the text block expands to use the full inner width. */
            float blockY = inner.y + titleH + 4f;
            const float iconSize = 40f;
            const float blockH = 44f;
            bool drawIcon = faction is object;
            Rect textBlockRect;
            if (drawIcon)
            {
                Rect iconRect;
                if (isAttacker)
                {
                    iconRect = new Rect(inner.x, blockY + (blockH - iconSize) / 2f, iconSize, iconSize);
                    textBlockRect = new Rect(inner.x + iconSize + 8f, blockY,
                        inner.width - iconSize - 8f, blockH);
                }
                else
                {
                    iconRect = new Rect(inner.xMax - iconSize, blockY + (blockH - iconSize) / 2f,
                        iconSize, iconSize);
                    textBlockRect = new Rect(inner.x, blockY,
                        inner.width - iconSize - 8f, blockH);
                }

                Texture2D iconTex = faction.def?.FactionIcon ?? BaseContent.BadTex;
                GUI.color = faction.Color;
                GUI.DrawTexture(iconRect, iconTex);
                GUI.color = Color.white;
            }
            else
            {
                textBlockRect = new Rect(inner.x, blockY, inner.width, blockH);
            }

            // Top text row: faction name (white). Bottom: label (dim grey).
            float textRowH = blockH * 0.5f;
            Rect nameRect = new Rect(textBlockRect.x, textBlockRect.y,
                textBlockRect.width, textRowH);
            Rect labelRect = new Rect(textBlockRect.x, textBlockRect.y + textRowH,
                textBlockRect.width, textRowH);

            Text.Anchor = textAnchor;
            string factionName = faction?.Name ?? fallbackFactionName;
            if (!string.IsNullOrEmpty(factionName))
                Widgets.Label(nameRect, factionName);
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(labelRect, label ?? "?");
            GUI.color = Color.white;

            /* Efficiency line */
            float effY = blockY + blockH + 4f;
            Rect effRect = new Rect(inner.x, effY, inner.width, 20f);
            Text.Anchor = textAnchor;
            Widgets.Label(effRect, "FCBattleEfficiencyLine".Translate(efficiency.ToString("0.00")));

            /* Force bar with overlay */
            float barY = effY + 24f;
            const float barH = 18f;
            Rect bar = new Rect(inner.x, barY, inner.width, barH);
            float fill = initial > 0 ? Mathf.Clamp01((float)(remaining / initial)) : 0f;
            Color barBg = new Color(0.15f, 0.15f, 0.15f);
            Color barFill = isAttacker
                ? new Color(0.75f, 0.35f, 0.30f)
                : new Color(0.30f, 0.55f, 0.75f);
            UIUtil.DrawProgressBarColors(bar, fill, barBg, barFill);

            string forces = "FCBattleForceLine".Translate(remaining.ToString("0.#"),
                initial.ToString("0.#"));
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, forces);

            Text.Anchor = TextAnchor.UpperLeft;
        }

        /* Title row tint: muted player color for player side, soft red for enemy side,
           neutral white-alpha for pure NPC-vs-NPC ops where neither side is the player.
           Alpha is on the higher side here because the gradient fades it to zero by the
           inner edge, so the average density across the band is much lower than a solid fill. */
        private static Color ResolveTitleTint(bool playerSideKnown, bool isPlayerSide)
        {
            if (!playerSideKnown)
                return new Color(1f, 1f, 1f, 0.18f);
            if (isPlayerSide)
            {
                Faction player = FactionCache.PlayerColonyFaction;
                Color baseColor = player is object
                    ? player.Color
                    : new Color(0.30f, 0.55f, 0.75f);
                Color dim = UIUtil.Dim(baseColor, 0.5f);
                return new Color(dim.r, dim.g, dim.b, 0.55f);
            }
            return new Color(0.75f, 0.30f, 0.25f, 0.55f);
        }

        /* -*- Round list -*- */

        private const float RoundHeaderH = 44f; // two 22px tiers
        private const float RoundRowH = 22f;

        private void DrawRoundList(Rect rect, BattleResult br)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(4f);

            // Manual battles have no per-round data — show a placeholder instead of an
            // empty header + empty scroll viewport.
            if (br.rounds is null || br.rounds.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Small;
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(inner, "FCBattleReportNoRoundDetail".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            // The scroll-view content is sized so it would need a scrollbar (rows usually
            // overflow). Reserve the scrollbar width in the header so the leaf columns line
            // up between header and rows.
            int count = br.rounds.Count;
            float contentH = Math.Max(RoundRowH, count * RoundRowH);
            float viewportH = inner.height - RoundHeaderH - 2f;
            bool needsScroll = contentH > viewportH;
            float scrollReserve = needsScroll ? ScrollUtil.ScrollbarWidth + 1f : 0f;

            float tableW = inner.width - scrollReserve;
            Rect headerRect = new Rect(inner.x, inner.y, tableW, RoundHeaderH);
            DrawRoundListHeader(headerRect);

            Rect viewportOuter = new Rect(inner.x, inner.y + RoundHeaderH + 2f, inner.width, viewportH);
            Rect viewRect = ScrollUtil.BeginScrollView(viewportOuter, ref scrollPos, contentH);

            float[] leafW = ComputeRoundLeafWidths(viewRect.width);
            // Latest at top.
            for (int i = count - 1; i >= 0; i--)
            {
                int displayIndex = (count - 1) - i;
                Rect rowRect = new Rect(0f, displayIndex * RoundRowH, viewRect.width, RoundRowH);
                DrawRoundRow(rowRect, br.rounds[i], leafW);
            }

            // Vertical group separator between the two "Final" columns (the mirror axis).
            float sepX = ComputeRollGroupSeparatorX(4f, leafW);
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            Widgets.DrawLineVertical(sepX, 0f, contentH);
            GUI.color = Color.white;

            ScrollUtil.EndScrollView();
        }

        /* Header tiers:
            Top:    | Round | Attacker (3 cols) | Defender (3 cols) |
            Bottom:         |  Force  |  Raw  |  Final  |  Final  |  Raw  |  Force  |    */
        private void DrawRoundListHeader(Rect rect)
        {
            Text.Font = GameFont.Small;
            float[] leafW = ComputeRoundLeafWidths(rect.width);
            float startX = rect.x + 4f;
            float midY = rect.y + RoundHeaderH * 0.5f;
            float topRowH = RoundHeaderH * 0.5f;

            string roundLabel = "FCBattleColRound".Translate().ToString();
            string atkLabel = "FCBattleColAttacker".Translate().ToString();
            string defLabel = "FCBattleColDefender".Translate().ToString();
            string rawLabel = "FCBattleColRollRaw".Translate().ToString();
            string finalLabel = "FCBattleColRollFinal".Translate().ToString();
            string forceLabel = "FCBattleColForce".Translate().ToString();

            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            Text.Anchor = TextAnchor.MiddleCenter;

            float x = startX;

            // Round (full-height)
            Widgets.Label(new Rect(x, rect.y, leafW[0], RoundHeaderH), roundLabel);
            x += leafW[0];

            // Attacker group: top label spans 3 cols; bottom row = Force | Raw | Final
            float atkGroupX = x;
            float atkGroupW = leafW[1] + leafW[2] + leafW[3];
            Widgets.Label(new Rect(atkGroupX, rect.y, atkGroupW, topRowH), atkLabel);
            Widgets.Label(new Rect(x, midY, leafW[1], topRowH), forceLabel);
            x += leafW[1];
            Widgets.Label(new Rect(x, midY, leafW[2], topRowH), rawLabel);
            x += leafW[2];
            Widgets.Label(new Rect(x, midY, leafW[3], topRowH), finalLabel);
            x += leafW[3];

            // Defender group: top label spans 3 cols; bottom row = Final | Raw | Force (mirrored)
            float defGroupX = x;
            float defGroupW = leafW[4] + leafW[5] + leafW[6];
            Widgets.Label(new Rect(defGroupX, rect.y, defGroupW, topRowH), defLabel);
            Widgets.Label(new Rect(x, midY, leafW[4], topRowH), finalLabel);
            x += leafW[4];
            Widgets.Label(new Rect(x, midY, leafW[5], topRowH), rawLabel);
            x += leafW[5];
            Widgets.Label(new Rect(x, midY, leafW[6], topRowH), forceLabel);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            // Horizontal divider between the two header tiers (under the grouped columns only).
            GUI.color = new Color(1f, 1f, 1f, 0.2f);
            Widgets.DrawLineHorizontal(atkGroupX, midY, atkGroupW);
            Widgets.DrawLineHorizontal(defGroupX, midY, defGroupW);
            // Bottom border of the entire header.
            Widgets.DrawLineHorizontal(rect.x, rect.yMax, rect.width);
            GUI.color = Color.white;

            // Vertical group separator: between Atk Final and Def Final (mirror axis).
            float sepX = ComputeRollGroupSeparatorX(startX, leafW);
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            Widgets.DrawLineVertical(sepX, rect.y, RoundHeaderH);
            GUI.color = Color.white;
        }

        private void DrawRoundRow(Rect rect, RoundEntry r, float[] leafW)
        {
            float startX = rect.x + 4f;

            // Tint the winner's 3-column block. Color depends on player perspective:
            // green if player won, red if player lost; neutral when neither side is the player.
            Color winnerTint = ResolveWinnerBlockTint(r.attackerWonRound);
            if (winnerTint.a > 0f)
            {
                float blockX;
                float blockW;
                if (r.attackerWonRound)
                {
                    blockX = startX + leafW[0];
                    blockW = leafW[1] + leafW[2] + leafW[3];
                }
                else
                {
                    blockX = startX + leafW[0] + leafW[1] + leafW[2] + leafW[3];
                    blockW = leafW[4] + leafW[5] + leafW[6];
                }
                Widgets.DrawBoxSolid(new Rect(blockX, rect.y, blockW, rect.height), winnerTint);
            }

            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;

            float x = startX;

            // Round
            Widgets.Label(new Rect(x, rect.y, leafW[0], rect.height), r.roundNumber.ToString());
            x += leafW[0];

            // Attacker: Force | Raw | Final
            Widgets.Label(new Rect(x, rect.y, leafW[1], rect.height), r.attackerForceAfter.ToString("0.#"));
            x += leafW[1];
            DrawRawRollCell(new Rect(x, rect.y, leafW[2], rect.height), r.attackerRawRoll);
            x += leafW[2];
            Widgets.Label(new Rect(x, rect.y, leafW[3], rect.height), r.attackerScore.ToString("0.00"));
            x += leafW[3];

            // Defender: Final | Raw | Force (mirrored)
            Widgets.Label(new Rect(x, rect.y, leafW[4], rect.height), r.defenderScore.ToString("0.00"));
            x += leafW[4];
            DrawRawRollCell(new Rect(x, rect.y, leafW[5], rect.height), r.defenderRawRoll);
            x += leafW[5];
            Widgets.Label(new Rect(x, rect.y, leafW[6], rect.height), r.defenderForceAfter.ToString("0.#"));

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawRawRollCell(Rect rect, int rawRoll)
        {
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(rect, rawRoll.ToString());
            GUI.color = Color.white;
        }

        private Color ResolveWinnerBlockTint(bool attackerWon)
        {
            if (PlayerSideKnown)
            {
                bool playerWonThisRound = (PlayerIsAttacker == attackerWon);
                return playerWonThisRound
                    ? new Color(0.20f, 0.50f, 0.20f, 0.25f)
                    : new Color(0.50f, 0.20f, 0.20f, 0.25f);
            }
            return new Color(1f, 1f, 1f, 0.10f);
        }

        /* Leaf order: Round, AtkForce, AtkRaw, AtkFinal, DefFinal, DefRaw, DefForce.
           Percentages: 8 / 16 / 13 / 17 / 17 / 13 / 16 = 100. */
        private static float[] ComputeRoundLeafWidths(float totalWidth)
        {
            float w = totalWidth - 8f;
            return new[]
            {
                w * 0.08f,
                w * 0.16f,
                w * 0.13f,
                w * 0.17f,
                w * 0.17f,
                w * 0.13f,
                w * 0.16f
            };
        }

        /* X-position of the vertical separator between Atk Final and Def Final (mirror axis). */
        private static float ComputeRollGroupSeparatorX(float startX, float[] leafW)
        {
            return startX + leafW[0] + leafW[1] + leafW[2] + leafW[3];
        }
    }
}
