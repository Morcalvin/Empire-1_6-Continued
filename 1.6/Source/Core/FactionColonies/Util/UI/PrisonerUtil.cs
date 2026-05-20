using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /* Shared renderer + action helpers for FCPrisoner displays.
       Used by FCPrisonerMenu (single-settlement window, rich row) and
       MainTabWindow_Colony.DrawPrisonersTab (faction-wide tab, compact row). */
    public static class PrisonerUtil
    {
        public const float RowHeight = 95f;
        public const float CompactRowHeight = 46f;
        public const float AccentWidth = 4f;

        private const float portraitW = 70f;
        private const float compactPortraitSz = 38f;
        private const float infoBtnSz = 18f;
        private const float gap = 4f;
        private const float rightColW = 128f;
        private const float pad = 4f;

        private static readonly Color healthBarBg = new Color(0.15f, 0.15f, 0.15f);

        public static int CullNullPrisoners(FactionFC faction)
        {
            if (faction?.settlements is null) return 0;
            int total = 0;
            for (int i = 0; i < faction.settlements.Count; i++)
            {
                total += faction.settlements[i]?.PrisonerComp?.CullNullPrisoners() ?? 0;
            }
            return total;
        }

        public static bool HasPrisonersOfColony(Caravan caravan)
        {
            if (caravan?.PawnsListForReading is null) return false;
            List<Pawn> pawns = caravan.PawnsListForReading;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i].IsPrisonerOfColony) return true;
            }
            return false;
        }

        private static void GetWorkloadPresentation(FCWorkLoad workload, out string label, out string trend, out Color trendColor)
        {
            switch (workload)
            {
                case FCWorkLoad.Heavy:
                    label = "FCHeavy".Translate().CapitalizeFirst();
                    trend = "FCPrisonerHealthChangePerDay".Translate("-4");
                    trendColor = AccentUtil.StatBad;
                    return;
                case FCWorkLoad.Medium:
                    label = "FCMedium".Translate().CapitalizeFirst();
                    trend = "FCPrisonerHealthChangePerDay".Translate("-2");
                    trendColor = AccentUtil.StatMedGood;
                    return;
                case FCWorkLoad.Light:
                    label = "FCLight".Translate().CapitalizeFirst();
                    trend = "FCPrisonerHealthChangePerDay".Translate("+1");
                    trendColor = AccentUtil.StatGood;
                    return;
                default:
                    label = "null";
                    trend = "";
                    trendColor = Color.white;
                    return;
            }
        }

        /* "Jonathan, Novelist" — title segment colorized via SubtleGrayColor.
           Falls back to just the name when the pawn has no backstory title. */
        private static string BuildNameWithTitle(Pawn pawn)
        {
            if (pawn is null) return "unknown";
            string name = pawn.Name?.ToStringShort ?? "unknown";
            string title = pawn.story?.TitleShortCap;
            if (string.IsNullOrEmpty(title)) return name;
            return name + (", " + title).Colorize(ColoredText.SubtleGrayColor);
        }

        /* "Male, age 63 (115) of New Arrivals" — vanilla pawn descriptor. */
        private static string BuildSubtitle(Pawn pawn)
        {
            if (pawn is null) return "";
            try { return pawn.MainDesc(writeFaction: true); }
            catch { return ""; }
        }

        /* Just the prisoner's home faction name (e.g. "New Arrivals"). Used by the
         * compact row where the full MainDesc string is too wide for a 2-column card. */
        private static string BuildFactionLabel(Pawn pawn)
        {
            Faction f = pawn?.Faction;
            if (f is null || f.Hidden) return "";
            return f.Name;
        }

        /* Rich 95px prisoner row (settlement window).
           Three content rows next to the portrait:
             Row 1: Name, TitleShort (gray) ............... [info]      | $value
             Row 2: Male, age 63 (115) of New Arrivals                  | [Actions]
             Row 3: [== health 100 ===== +4/tick ====]                  | [Workload] */
        public static void DrawPrisonerRow(Rect box, FCPrisoner prisoner, WorldSettlementFC settlement, int altIndex, Action onRemoved)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Color origColor = GUI.color;

            Widgets.DrawMenuSection(box);
            if (altIndex % 2 == 0)
            {
                Widgets.DrawHighlight(box);
            }

            // Faction-of-origin accent strip (left edge)
            Color accentColor = prisoner.prisoner?.Faction?.Color ?? Color.gray;
            Widgets.DrawBoxSolid(new Rect(box.x, box.y, AccentWidth, box.height), accentColor);

            float contentStartX = box.x + AccentWidth;

            // Portrait
            Rect portraitRect = new Rect(contentStartX + pad, box.y + 6f, portraitW, 78f);
            if (prisoner.prisoner is object)
            {
                UIUtil.DrawPawnPortrait(portraitRect, prisoner.prisoner, 1.2f);
            }

            // Center + right column geometry
            float centerX = portraitRect.xMax + gap + pad;
            float rightX = box.xMax - rightColW - pad;
            float centerW = rightX - centerX - gap;

            const float row1H = 24f;
            const float row2H = 22f;
            const float row3H = 22f;
            const float rowGap = 2f;

            float row1Y = box.y + pad;
            float row2Y = row1Y + row1H + rowGap;
            float row3Y = row2Y + row2H + rowGap;

            /* ROW 1: name + title + info button (center), value (right) */
            Rect infoRect = new Rect(centerX + centerW - infoBtnSz, row1Y + (row1H - infoBtnSz) / 2f, infoBtnSz, infoBtnSz);

            float nameW = centerW - infoBtnSz - 4f;
            Rect nameRect = new Rect(centerX, row1Y, nameW, row1H);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, BuildNameWithTitle(prisoner.prisoner));

            if (prisoner.prisoner is object)
            {
                UIUtil.InfoCardThing(infoRect, prisoner.prisoner);
            }

            Rect valueRect = new Rect(rightX, row1Y, rightColW, row1H);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(valueRect, "$" + (int)(prisoner.prisoner?.MarketValue ?? 0));

            /* ROW 2: subtitle (gender, age, faction) | Actions */
            Rect subtitleRect = new Rect(centerX, row2Y, centerW, row2H);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(subtitleRect, BuildSubtitle(prisoner.prisoner));
            GUI.color = origColor;

            Rect actionsRect = new Rect(rightX, row2Y, rightColW, row2H);
            if (UIUtil.ButtonFlat(actionsRect, "FCActions".Translate()))
            {
                settlement.PrisonerComp?.DoActionsMenu(prisoner, onRemoved);
            }

            /* ROW 3: health bar with embedded trend | Workload */
            float healthBarH = 16f;
            float healthBarY = row3Y + (row3H - healthBarH) / 2f;
            const float trendW = 60f;
            Rect healthBarRect = new Rect(centerX, healthBarY, centerW - trendW - 5f, healthBarH);
            float healthFrac = prisoner.health / 100f;
            Color healthColor = AccentUtil.GetStatColor(prisoner.health, false);
            UIUtil.DrawProgressBarColors(healthBarRect, healthFrac, healthBarBg, healthColor);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(healthBarRect, "Health".Translate().CapitalizeFirst() + ": " + (int)prisoner.health);

            GetWorkloadPresentation(prisoner.workload, out string wlLabel, out string wlTrend, out Color wlTrendColor);

            // Trend indicator anchored to the right end of the bar
            Rect trendRect = new Rect(healthBarRect.xMax + 4f, healthBarRect.y, trendW, healthBarRect.height);
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = wlTrendColor;
            Widgets.Label(trendRect, wlTrend);
            GUI.color = origColor;

            Rect workloadRect = new Rect(rightX, row3Y, rightColW, row3H);
            if (UIUtil.ButtonFlat(workloadRect, "FCWorkload".Translate().CapitalizeFirst() + ": " + wlLabel))
            {
                settlement.PrisonerComp?.OpenWorkloadFloatMenu(prisoner);
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
            GUI.color = origColor;
        }

        /* Compact ~46px prisoner card (faction-wide tab, 2 cards per row).
           Two rows next to a 38×38 portrait:
             Top:    [i] Name, Title ... ... ... ... New Arrivals   $value
             Bottom: Health: 100/100 ... +1/day  [Light]  [Actions] */
        public static void DrawPrisonerRowCompact(Rect box, FCPrisoner prisoner, WorldSettlementFC settlement, int altIndex, Action onRemoved)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Color origColor = GUI.color;

            bool isHighlighted = altIndex % 2 == 0;
            if (isHighlighted)
            {
                Widgets.DrawHighlight(box);
            }

            Color accentColor = prisoner.prisoner?.Faction?.Color ?? Color.gray;
            Widgets.DrawBoxSolid(new Rect(box.x, box.y, AccentWidth, box.height), accentColor);

            float contentStartX = box.x + AccentWidth;

            // Portrait — vertically centered in the row
            float portraitY = box.y + (box.height - compactPortraitSz) / 2f;
            Rect portraitRect = new Rect(contentStartX + pad, portraitY, compactPortraitSz, compactPortraitSz);
            if (prisoner.prisoner is object)
            {
                UIUtil.DrawPawnPortrait(portraitRect, prisoner.prisoner, 1.2f);
            }

            const float topRowH = 22f;
            const float botRowH = 20f;
            const float rowGap = 0f;

            float topY = box.y + pad;
            float botY = topY + topRowH + rowGap;

            // Center column starts right of portrait
            float centerX = portraitRect.xMax + (pad * 2);
            float rightEdge = box.xMax - pad;

            /* Button geometry (computed up front so the top-row faction label can
               right-align to the trend's right edge on the row below). Narrower than
               the pre-2-column layout: workload label drops its "Workload: " prefix. */
            const float actionsW = 70f;
            const float workloadW = 100f;
            const float btnGap = 4f;
            const float trendW = 50f;

            float actionsX = rightEdge - actionsW;
            float workloadX = actionsX - btnGap - workloadW;
            float trendRightEdge = workloadX - btnGap;
            float trendLeftEdge = trendRightEdge - trendW;

            /* TOP ROW */
            // Far right: $value
            const float valueW = 60f;
            Rect valueRect = new Rect(rightEdge - valueW, topY, valueW, topRowH);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(valueRect, "$" + (int)(prisoner.prisoner?.MarketValue ?? 0));

            // Faction label right-aligned to the trend's right edge below
            const float subtitleW = 120f;
            Rect subtitleRect = new Rect(valueRect.x - subtitleW - 4f, topY, subtitleW, topRowH);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(subtitleRect, BuildFactionLabel(prisoner.prisoner));
            GUI.color = origColor;

            // Left of center: info button + name+title
            Rect infoRect = new Rect(centerX, topY + (topRowH - infoBtnSz) / 2f, infoBtnSz, infoBtnSz);
            if (prisoner.prisoner is object)
            {
                UIUtil.InfoCardThing(infoRect, prisoner.prisoner);
            }

            float nameX = centerX + infoBtnSz + 4f;
            float nameW = subtitleRect.x - nameX - 4f;
            Rect nameRect = new Rect(nameX, topY, nameW, topRowH);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, BuildNameWithTitle(prisoner.prisoner));

            /* BOTTOM ROW */
            // Buttons render in Tiny font, with row-alt highlight tracking
            Text.Font = GameFont.Tiny;

            Rect actionsRect = new Rect(actionsX, botY, actionsW, botRowH);
            if (UIUtil.ButtonFlat(actionsRect, "FCActions".Translate(), highlighted: isHighlighted))
            {
                settlement.PrisonerComp?.DoActionsMenu(prisoner, onRemoved);
            }

            GetWorkloadPresentation(prisoner.workload, out string wlLabel, out string wlTrend, out Color wlTrendColor);
            Rect workloadRect = new Rect(workloadX, botY, workloadW, botRowH);
            if (UIUtil.ButtonFlat(workloadRect, wlLabel, highlighted: isHighlighted))
            {
                settlement.PrisonerComp?.OpenWorkloadFloatMenu(prisoner);
            }

            // Left of buttons: trend label (right-aligned, in trend color)
            Rect trendRect = new Rect(trendLeftEdge, botY, trendW, botRowH);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = wlTrendColor;
            Widgets.Label(trendRect, wlTrend);
            GUI.color = origColor;

            // Health text fills the remaining left side, color-graded by health value
            Rect healthRect = new Rect(centerX, botY, trendRect.x - centerX - 4f, botRowH);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            UIUtil.DrawColoredLabel(healthRect, "Health: " + (int)prisoner.health + "/100", AccentUtil.GetStatColor(prisoner.health, false));

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
            GUI.color = origColor;
        }
    }
}
