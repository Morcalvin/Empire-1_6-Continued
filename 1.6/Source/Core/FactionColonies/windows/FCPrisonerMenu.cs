using FactionColonies.util;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
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

        private const int optionHeight = 95;
        private const float titleHeight = 30f;
        private const float dividerGap = 8f;
        private const float portraitW = 70f;
        private const float gap = 4f;
        private const float rightColW = 128f;
        private const float pad = 4f;

        private static readonly Color healthBarBg = new Color(0.15f, 0.15f, 0.15f);

        public override Vector2 InitialSize => new Vector2(538f, 518f);

        public FCPrisonerMenu(WorldSettlementFC settlement)
        {
            this.faction = FindFC.FactionComp;
            this.settlement = settlement;
            this.prisoners = settlement.prisonerList;

            this.forcePause = false;
            this.draggable = true;
            this.doCloseX = true;
            this.preventCameraMotion = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Title bar
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, titleHeight);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(titleRect, "FCPrisonerWindowTitle".Translate(settlement.Name));

            if (prisoners.Count > 0)
            {
                Text.Anchor = TextAnchor.MiddleRight;
                UIUtil.DrawColoredLabel(titleRect, "(" + prisoners.Count + ")", Color.gray);
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
                var viewRect = ScrollUtil.BeginScrollView(outRect, ref scrollPosition, prisoners.Count * optionHeight);
                var ls = new Listing_Standard();
                ls.Begin(viewRect);
                int i = 0;
                foreach (FCPrisoner prisoner in prisoners)
                {
                    DrawPrisonerRow(ls, prisoner, i);
                    i++;
                }
                ls.End();
                ScrollUtil.EndScrollView();
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        private void DrawPrisonerRow(Listing_Standard ls, FCPrisoner prisoner, int index)
        {
            Rect box = ls.GetRect(optionHeight);

            // Background
            Widgets.DrawMenuSection(box);
            if (index % 2 == 0)
            {
                Widgets.DrawHighlight(box);
            }

            // Portrait
            Rect portraitRect = new Rect(box.x + pad, box.y + 6f, portraitW, 78f);
            if (prisoner.prisoner != null)
            {
                UIUtil.DrawPawnPortrait(portraitRect, prisoner.prisoner, 1.2f);
            }

            // Center column
            float cx = box.x + portraitW + gap + pad;
            float cw = box.width - portraitW - rightColW - (gap * 2) - (pad * 2);

            // Name
            Rect nameRect = new Rect(cx, box.y + pad, cw, 20f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, prisoner.prisoner.Name.ToStringShort);

            // Health bar
            Rect healthBarRect = new Rect(cx, nameRect.yMax + 2f, cw, 14f);
            float healthFrac = prisoner.health / 100f;
            Color healthColor = AccentUtil.GetStatColor(prisoner.health, false);
            UIUtil.DrawProgressBarColors(healthBarRect, healthFrac, healthBarBg, healthColor);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(healthBarRect, "Health".Translate().CapitalizeFirst() + ": " + (int)prisoner.health);

            // Workload button
            Rect workloadRect = new Rect(cx, healthBarRect.yMax + 4f, 150f, 22f);
            string workloadLabel;
            string trendText;
            Color trendColor;
            switch (prisoner.workload)
            {
                case FCWorkLoad.Heavy:
                    workloadLabel = "FCHeavy".Translate().CapitalizeFirst();
                    trendText = "-20/tick";
                    trendColor = AccentUtil.StatBad;
                    break;
                case FCWorkLoad.Medium:
                    workloadLabel = "FCMedium".Translate().CapitalizeFirst();
                    trendText = "-10/tick";
                    trendColor = AccentUtil.StatMedGood;
                    break;
                case FCWorkLoad.Light:
                    workloadLabel = "FCLight".Translate().CapitalizeFirst();
                    trendText = "+4/tick";
                    trendColor = AccentUtil.StatGood;
                    break;
                default:
                    workloadLabel = "null";
                    trendText = "";
                    trendColor = Color.white;
                    break;
            }

            if (Widgets.ButtonText(workloadRect, "FCWorkload".Translate().CapitalizeFirst() + ": " + workloadLabel))
            {
                List<FloatMenuOption> list = new List<FloatMenuOption>();
                list.Add(new FloatMenuOption("FCHeavy".Translate().CapitalizeFirst() + " - " + "FCHeavyExplanation".Translate(), delegate
                {
                    prisoner.workload = FCWorkLoad.Heavy;
                    settlement.NotifyWorkforceChanged();
                }));
                list.Add(new FloatMenuOption("FCMedium".Translate().CapitalizeFirst() + " - " + "FCMediumExplanation".Translate(), delegate
                {
                    prisoner.workload = FCWorkLoad.Medium;
                    settlement.NotifyWorkforceChanged();
                }));
                list.Add(new FloatMenuOption("FCLight".Translate().CapitalizeFirst() + " - " + "FCLightExplanation".Translate(), delegate
                {
                    prisoner.workload = FCWorkLoad.Light;
                    settlement.NotifyWorkforceChanged();
                }));
                Find.WindowStack.Add(new FloatMenu(list));
            }

            // Health trend indicator
            Rect trendRect = new Rect(workloadRect.xMax + 4f, workloadRect.y, 60f, 22f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            UIUtil.DrawColoredLabel(trendRect, trendText, trendColor);

            // Right column
            float rx = box.xMax - rightColW - pad;

            // Market value
            Rect valueRect = new Rect(rx, box.y + pad, rightColW, 20f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(valueRect, "$" + (int)prisoner.prisoner.MarketValue);

            // Faction of origin
            Rect factionRect = new Rect(rx, valueRect.yMax + 2f, rightColW, 14f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            string factionName = prisoner.prisoner.Faction != null ? (string)prisoner.prisoner.Faction.NameColored : "";
            Widgets.Label(factionRect, factionName);

            // View Info button
            Rect infoBtn = new Rect(rx, factionRect.yMax + 4f, rightColW, 22f);
            if (UIUtil.ButtonFlat(infoBtn, "FCViewInfo".Translate()))
            {
                Find.WindowStack.Add(new Dialog_InfoCard(prisoner.prisoner));
            }

            // Actions button
            Rect actionsBtn = new Rect(rx, infoBtn.yMax + 2f, rightColW, 22f);
            if (UIUtil.ButtonFlat(actionsBtn, "FCActions".Translate()))
            {
                List<FloatMenuOption> list = new List<FloatMenuOption>();

                if (FindFC.FactionComp.IsActionAllowed(FCActionType.SellPrisoner))
                {
                    list.Add(new FloatMenuOption("FCSellPawn".Translate() + " $" + prisoner.prisoner.MarketValue + " " + "FCSellPawnInfo".Translate(), delegate
                    {
                        settlement.AddOneTimeSilverIncome(prisoner.prisoner.MarketValue);

                        prisoners.Remove(prisoner);
                        settlement.NotifyWorkforceChanged();
                        WindowUpdate();
                    }));
                }

                list.Add(new FloatMenuOption("FCReturnToPlayer".Translate(), delegate
                {
                    if (!HealthUtility.TryAnesthetize(prisoner.prisoner)) HealthUtility.DamageUntilDowned(prisoner.prisoner, false);

                    if (prisoner.prisoner.guest == null)
                    {
                        prisoner.prisoner.guest = new Pawn_GuestTracker();
                    }
                    prisoner.prisoner.guest.guestStatusInt = GuestStatus.Prisoner;
                    FieldInfo hostFaction = typeof(Pawn_GuestTracker).GetField("hostFactionInt", BindingFlags.NonPublic | BindingFlags.Instance);
                    hostFaction?.SetValue(prisoner.prisoner.guest, Find.FactionManager.OfPlayer);

                    DeliveryEvent.CreateDeliveryEvent(new FCEvent
                    {
                        location = Find.AnyPlayerHomeMap.Tile,
                        source = settlement.Tile,
                        goods = new List<Thing> { prisoner.prisoner },
                        customDescription = "FCAPrisonerIsBeingDeliveredToYou".Translate(),
                        timeTillTrigger = Find.TickManager.TicksGame + TravelUtil.ReturnTicksToArrive(settlement.Tile, Find.AnyPlayerHomeMap.Tile)
                    });

                    prisoners.Remove(prisoner);
                    settlement.NotifyWorkforceChanged();
                    WindowUpdate();
                }));

                Find.WindowStack.Add(new FloatMenu(list));
            }
        }
    }
}
