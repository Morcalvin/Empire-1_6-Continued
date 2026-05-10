using System;
using System.Collections.Generic;
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
    /// Shared window for "watch this battle" — both during live auto-resolve and
    /// when reviewing an archived <see cref="BattleResult"/>. Two mirrored side panels
    /// (attacker left, defender right) with faction icon + name/label, efficiency, and
    /// a health bar with the force ratio overlaid. Below: a scrolling round-roll log
    /// with a two-tier header (Attacker Roll / Defender Roll each spanning Raw | Final).
    /// Player-side round wins are tinted green; player-side losses red. Pure NPC-vs-NPC
    /// (player aligned with neither side) is not tinted.
    /// <para>For manual battles the rounds list is empty; the round-list area shows a
    /// "no per-round detail" placeholder instead of an empty scroll viewport.</para>
    /// </summary>
    public class BattleProgressWindow : Window
    {
        private readonly BattleResult result;
        private readonly BattleViewerSide playerSide;
        // Optional: present only when opened from a live MilitaryOperation. The faction
        // icon resolves from these first, then falls through to result.attackerFaction /
        // result.defenderFaction (the faction references stored on the archived report),
        // and only as a last resort to the cached faction-name string with no icon.
        private readonly MilitaryOperationParticipant aggressorParticipant;
        private readonly MilitaryOperationParticipant defenderParticipant;

        private Vector2 scrollPos;

        public BattleProgressWindow(MilitaryOperation op)
        {
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

            /* -*- Header -*- */
            float headerH = 56f;
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

            /* Title row with side-aware tint */
            float titleH = 22f;
            Rect titleRect = new Rect(inner.x, inner.y, inner.width, titleH);
            Color titleTint = ResolveTitleTint(PlayerSideKnown, isPlayerSide);
            Widgets.DrawBoxSolid(titleRect, titleTint);

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
           neutral white-alpha for pure NPC-vs-NPC ops where neither side is the player. */
        private static Color ResolveTitleTint(bool playerSideKnown, bool isPlayerSide)
        {
            if (!playerSideKnown)
                return new Color(1f, 1f, 1f, 0.08f);
            if (isPlayerSide)
            {
                Faction player = FactionCache.PlayerColonyFaction;
                Color baseColor = player is object
                    ? player.Color
                    : new Color(0.30f, 0.55f, 0.75f);
                Color dim = UIUtil.Dim(baseColor, 0.5f);
                return new Color(dim.r, dim.g, dim.b, 0.25f);
            }
            return new Color(0.75f, 0.30f, 0.25f, 0.25f);
        }

        /* -*- Round list -*- */

        private const float RoundHeaderH = 44f; // two 22px tiers
        private const float RoundRowH = 22f;
        private const float ScrollbarReserve = 16f;

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

            // Reserve scrollbar space in the header so columns line up with the rows.
            float tableW = inner.width - ScrollbarReserve;
            Rect headerRect = new Rect(inner.x, inner.y, tableW, RoundHeaderH);
            DrawRoundListHeader(headerRect);

            Rect viewportOuter = new Rect(inner.x, inner.y + RoundHeaderH + 2f, inner.width,
                                          inner.height - RoundHeaderH - 2f);

            int count = br.rounds.Count;
            float contentH = Math.Max(RoundRowH, count * RoundRowH);
            Rect viewportInner = new Rect(0f, 0f, viewportOuter.width - ScrollbarReserve, contentH);

            Widgets.BeginScrollView(viewportOuter, ref scrollPos, viewportInner);

            float[] leafW = ComputeRoundLeafWidths(viewportInner.width);
            // Latest at top.
            for (int i = count - 1; i >= 0; i--)
            {
                int displayIndex = (count - 1) - i;
                Rect rowRect = new Rect(0f, displayIndex * RoundRowH, viewportInner.width, RoundRowH);
                DrawRoundRow(rowRect, br.rounds[i], leafW);
            }

            // Group separator: vertical line between Atk Final and Def Raw, drawn inside
            // the scroll view so it scrolls with the rows but stays in the column position.
            float sepX = ComputeRollGroupSeparatorX(0f + 4f, leafW);
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            Widgets.DrawLineVertical(sepX, 0f, contentH);
            GUI.color = Color.white;

            Widgets.EndScrollView();
        }

        private void DrawRoundListHeader(Rect rect)
        {
            Text.Font = GameFont.Small;
            float[] leafW = ComputeRoundLeafWidths(rect.width);
            float startX = rect.x + 4f;
            float midY = rect.y + RoundHeaderH * 0.5f;
            float topRowH = RoundHeaderH * 0.5f;

            string roundLabel = "FCBattleColRound".Translate().ToString();
            string atkRollLabel = "FCBattleColAttackerRoll".Translate().ToString();
            string defRollLabel = "FCBattleColDefenderRoll".Translate().ToString();
            string winnerLabel = "FCBattleColWinner".Translate().ToString();
            string forcesLabel = "FCBattleColForces".Translate().ToString();
            string rawLabel = "FCBattleColRollRaw".Translate().ToString();
            string finalLabel = "FCBattleColRollFinal".Translate().ToString();

            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            Text.Anchor = TextAnchor.MiddleCenter;

            float x = startX;

            // Round (full-height)
            Widgets.Label(new Rect(x, rect.y, leafW[0], RoundHeaderH), roundLabel);
            x += leafW[0];

            // Attacker Roll group
            float atkGroupX = x;
            float atkGroupW = leafW[1] + leafW[2];
            Widgets.Label(new Rect(atkGroupX, rect.y, atkGroupW, topRowH), atkRollLabel);
            Widgets.Label(new Rect(x, midY, leafW[1], topRowH), rawLabel);
            x += leafW[1];
            Widgets.Label(new Rect(x, midY, leafW[2], topRowH), finalLabel);
            x += leafW[2];

            // Defender Roll group
            float defGroupX = x;
            float defGroupW = leafW[3] + leafW[4];
            Widgets.Label(new Rect(defGroupX, rect.y, defGroupW, topRowH), defRollLabel);
            Widgets.Label(new Rect(x, midY, leafW[3], topRowH), rawLabel);
            x += leafW[3];
            Widgets.Label(new Rect(x, midY, leafW[4], topRowH), finalLabel);
            x += leafW[4];

            // Winner (full-height)
            Widgets.Label(new Rect(x, rect.y, leafW[5], RoundHeaderH), winnerLabel);
            x += leafW[5];

            // Forces (full-height)
            Widgets.Label(new Rect(x, rect.y, leafW[6], RoundHeaderH), forcesLabel);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            // Horizontal divider between the two tiers (only under the grouped columns).
            GUI.color = new Color(1f, 1f, 1f, 0.2f);
            Widgets.DrawLineHorizontal(atkGroupX, midY, atkGroupW);
            Widgets.DrawLineHorizontal(defGroupX, midY, defGroupW);
            // Bottom border of the entire header.
            Widgets.DrawLineHorizontal(rect.x, rect.yMax, rect.width);
            GUI.color = Color.white;

            // Vertical group separator between the two roll groups.
            float sepX = ComputeRollGroupSeparatorX(startX, leafW);
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            Widgets.DrawLineVertical(sepX, rect.y, RoundHeaderH);
            GUI.color = Color.white;
        }

        private void DrawRoundRow(Rect rect, RoundEntry r, float[] leafW)
        {
            // Player-side row tint (preserved from previous version).
            if (PlayerSideKnown)
            {
                bool playerWonThisRound = (PlayerIsAttacker == r.attackerWonRound);
                Color tint = playerWonThisRound
                    ? new Color(0.20f, 0.50f, 0.20f, 0.25f)
                    : new Color(0.50f, 0.20f, 0.20f, 0.25f);
                Widgets.DrawBoxSolid(rect, tint);
            }

            float x = rect.x + 4f;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;

            // Round
            Widgets.Label(new Rect(x, rect.y, leafW[0], rect.height), r.roundNumber.ToString());
            x += leafW[0];

            // Attacker Raw / Final
            Widgets.Label(new Rect(x, rect.y, leafW[1], rect.height), r.attackerRawRoll.ToString());
            x += leafW[1];
            Widgets.Label(new Rect(x, rect.y, leafW[2], rect.height), r.attackerScore.ToString("0.00"));
            x += leafW[2];

            // Defender Raw / Final
            Widgets.Label(new Rect(x, rect.y, leafW[3], rect.height), r.defenderRawRoll.ToString());
            x += leafW[3];
            Widgets.Label(new Rect(x, rect.y, leafW[4], rect.height), r.defenderScore.ToString("0.00"));
            x += leafW[4];

            // Winner
            string winner = r.attackerWonRound
                ? "FCBattleSideAttacker".Translate().ToString()
                : "FCBattleSideDefender".Translate().ToString();
            Widgets.Label(new Rect(x, rect.y, leafW[5], rect.height), winner);
            x += leafW[5];

            // Forces (A / D)
            string forces = $"{r.attackerForceAfter:0.#} / {r.defenderForceAfter:0.#}";
            Widgets.Label(new Rect(x, rect.y, leafW[6], rect.height), forces);

            Text.Anchor = TextAnchor.UpperLeft;
        }

        /* Leaf order: Round, AtkRaw, AtkFinal, DefRaw, DefFinal, Winner, Forces.
           Percentages: 8 / 11 / 14 / 11 / 14 / 16 / 26 = 100. */
        private static float[] ComputeRoundLeafWidths(float totalWidth)
        {
            float w = totalWidth - 8f;
            return new[]
            {
                w * 0.08f,
                w * 0.11f,
                w * 0.14f,
                w * 0.11f,
                w * 0.14f,
                w * 0.16f,
                w * 0.26f
            };
        }

        /* X-position of the vertical separator between Atk Final and Def Raw. */
        private static float ComputeRollGroupSeparatorX(float startX, float[] leafW)
        {
            return startX + leafW[0] + leafW[1] + leafW[2];
        }
    }
}
