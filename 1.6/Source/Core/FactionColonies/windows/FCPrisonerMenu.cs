using FactionColonies.util;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    class FCPrisonerMenu : Window
    {
        public List<FCPrisoner> prisoners;
        public WorldSettlementFC settlement;
        public FactionFC faction;

        public Vector2 scrollPosition = Vector2.zero;

        private const float titleHeight = 30f;
        private const float dividerGap = 8f;

        public override Vector2 InitialSize => new Vector2(538f, 518f);

        public FCPrisonerMenu(WorldSettlementFC settlement)
        {
            this.faction = FindFC.FactionComp;
            this.settlement = settlement;
            settlement.PrisonerComp?.CullNullPrisoners();
            this.prisoners = settlement.PrisonerComp?.prisonerList ?? new List<FCPrisoner>();

            this.forcePause = false;
            this.draggable = true;
            this.doCloseX = true;
            this.preventCameraMotion = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Title bar — title (truncated) | count badge | Default workload | Bulk set
            const float countBadgeW = 40f;
            const float btnW = 135f;
            const float btnGap = 4f;
            WorldObjectComp_SettlementPrisoners comp = settlement.PrisonerComp;

            float bxRight = inRect.xMax;
            Rect bulkRect = new Rect(bxRight - btnW, inRect.y, btnW, titleHeight);
            Rect defaultRect = new Rect(bulkRect.x - btnGap - btnW, inRect.y, btnW, titleHeight);
            Rect countRect = new Rect(defaultRect.x - btnGap - countBadgeW, inRect.y, countBadgeW, titleHeight);
            Rect titleRect = new Rect(inRect.x, inRect.y, countRect.x - inRect.x, titleHeight);

            bool wordWrapBefore = Text.WordWrap;
            Text.WordWrap = false;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(titleRect, "FCPrisonerWindowTitle".Translate(settlement.Name));
            Text.WordWrap = wordWrapBefore;

            if (prisoners.Count > 0)
            {
                Text.Anchor = TextAnchor.MiddleRight;
                UIUtil.DrawColoredLabel(countRect, "(" + prisoners.Count + ")", Color.gray);
            }

            if (comp is object)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                string defaultBtnLabel = comp.hasDefaultWorkloadOverride
                    ? "FCSetDefaultWorkloadShort".Translate() + ": " + PrisonerUtil.WorkloadLabel(comp.defaultWorkloadOverride)
                    : "FCSetDefaultWorkloadShort".Translate() + ": " + "FCFromFaction".Translate();
                if (UIUtil.ButtonFlat(defaultRect, defaultBtnLabel))
                    comp.OpenDefaultWorkloadFloatMenu();

                if (prisoners.Count > 0)
                {
                    if (UIUtil.ButtonFlat(bulkRect, "FCBulkSetWorkloadShort".Translate()))
                        comp.OpenBulkSetWorkloadFloatMenu();
                }
            }

            // Divider
            float contentY = titleRect.yMax + dividerGap;
            UIUtil.DrawColoredHorizontalLine(inRect.x, titleRect.yMax + (dividerGap / 2f), inRect.width, Color.gray);

            float contentHeight = inRect.height - contentY;

            if (prisoners.Count == 0)
            {
                // Empty state
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                UIUtil.DrawColoredLabel(
                    new Rect(inRect.x, contentY + contentHeight * 0.35f, inRect.width, 40f),
                    "FCNoPrisoners".Translate(),
                    Color.gray);
            }
            else
            {
                // Prisoner list
                Text.Anchor = TextAnchor.MiddleLeft;
                var outRect = new Rect(0, contentY, inRect.width, contentHeight);
                var viewRect = ScrollUtil.BeginScrollView(outRect, ref scrollPosition, prisoners.Count * PrisonerUtil.RowHeight);
                var ls = new Listing_Standard();
                ls.Begin(viewRect);
                for (int i = 0; i < prisoners.Count; i++)
                {
                    Rect rowBox = ls.GetRect(PrisonerUtil.RowHeight);
                    PrisonerUtil.DrawPrisonerRow(rowBox, prisoners[i], settlement, i, WindowUpdate);
                }
                ls.End();
                ScrollUtil.EndScrollView();
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }
    }
}
