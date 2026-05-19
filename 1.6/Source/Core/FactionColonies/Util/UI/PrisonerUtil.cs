using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
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

        private static readonly FieldInfo hostFactionField =
            typeof(Pawn_GuestTracker).GetField("hostFactionInt", BindingFlags.NonPublic | BindingFlags.Instance);

        public static int CullNullPrisoners(WorldSettlementFC settlement)
        {
            if (settlement?.prisonerList is null) return 0;

            int removed = 0;
            for (int i = settlement.prisonerList.Count - 1; i >= 0; i--)
            {
                FCPrisoner p = settlement.prisonerList[i];
                if (p is null || p.prisoner is null)
                {
                    settlement.prisonerList.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
            {
                settlement.DirtyStatsCache();
                LogUtil.Warning("Culled " + removed + " null prisoner(s) from " + settlement.Name);
            }
            return removed;
        }

        public static int CullNullPrisoners(FactionFC faction)
        {
            if (faction?.settlements is null) return 0;
            int total = 0;
            for (int i = 0; i < faction.settlements.Count; i++)
            {
                total += CullNullPrisoners(faction.settlements[i]);
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

        public static void TransferFromCaravan(Pawn pawn, Caravan caravan, WorldSettlementFC settlement)
        {
            if (pawn is null || caravan is null || settlement is null) return;
            caravan.RemovePawn(pawn);
            caravan.Notify_PawnRemoved(pawn);
            settlement.AddPrisoner(pawn);
        }

        public static void DoTransferMenu(Caravan caravan, WorldSettlementFC settlement)
        {
            if (caravan is null || settlement is null) return;

            List<FloatMenuOption> list = new List<FloatMenuOption>();
            List<Pawn> pawns = caravan.PawnsListForReading;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn captured = pawns[i];
                if (!captured.IsPrisonerOfColony) continue;
                list.Add(new FloatMenuOption(
                    "FCTransferPrisonerOption".Translate(captured.Name.ToStringShort),
                    delegate { TransferFromCaravan(captured, caravan, settlement); }));
            }

            if (list.Count == 0)
            {
                Messages.Message("FCNoPrisonersInCaravan".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WindowStack.Add(new FloatMenu(list));
        }

        public static void SetWorkload(FCPrisoner p, WorldSettlementFC settlement, FCWorkLoad workload)
        {
            p.workload = workload;
            settlement.DirtyStatsCache();
        }

        public static void SellPrisoner(FCPrisoner p, WorldSettlementFC settlement)
        {
            settlement.AddOneTimeSilverIncome(p.prisoner.MarketValue);
            settlement.prisonerList.Remove(p);
            settlement.DirtyStatsCache();
        }

        public static void ReturnPrisonerToPlayer(FCPrisoner p, WorldSettlementFC settlement)
        {
            if (!HealthUtility.TryAnesthetize(p.prisoner))
                HealthUtility.DamageUntilDowned(p.prisoner, false);

            if (p.prisoner.guest is null)
                p.prisoner.guest = new Pawn_GuestTracker();
            p.prisoner.guest.guestStatusInt = GuestStatus.Prisoner;
            hostFactionField?.SetValue(p.prisoner.guest, Find.FactionManager.OfPlayer);

            DeliveryEvent.CreateDeliveryEvent(new FCEvent
            {
                location = Find.AnyPlayerHomeMap.Tile,
                source = settlement.Tile,
                goods = new List<Thing> { p.prisoner },
                customDescription = "FCAPrisonerIsBeingDeliveredToYou".Translate(),
                timeTillTrigger = Find.TickManager.TicksGame + TravelUtil.ReturnTicksToArrive(settlement.Tile, Find.AnyPlayerHomeMap.Tile)
            });

            settlement.prisonerList.Remove(p);
            settlement.DirtyStatsCache();
        }

        public static void DoActionsMenu(FCPrisoner p, WorldSettlementFC settlement, Action onRemoved)
        {
            List<FloatMenuOption> list = new List<FloatMenuOption>();

            if (FindFC.FactionComp.IsActionAllowed(FCActionType.SellPrisoner))
            {
                list.Add(new FloatMenuOption(
                    "FCSellPawn".Translate() + " $" + p.prisoner.MarketValue + " " + "FCSellPawnInfo".Translate(),
                    delegate
                    {
                        SellPrisoner(p, settlement);
                        onRemoved?.Invoke();
                    }));
            }

            list.Add(new FloatMenuOption("FCReturnToPlayer".Translate(), delegate
            {
                ReturnPrisonerToPlayer(p, settlement);
                onRemoved?.Invoke();
            }));

            Find.WindowStack.Add(new FloatMenu(list));
        }

        private static void GetWorkloadPresentation(FCWorkLoad workload, out string label, out string trend, out Color trendColor)
        {
            switch (workload)
            {
                case FCWorkLoad.Heavy:
                    label = "FCHeavy".Translate().CapitalizeFirst();
                    trend = "-20/tick";
                    trendColor = AccentUtil.StatBad;
                    return;
                case FCWorkLoad.Medium:
                    label = "FCMedium".Translate().CapitalizeFirst();
                    trend = "-10/tick";
                    trendColor = AccentUtil.StatMedGood;
                    return;
                case FCWorkLoad.Light:
                    label = "FCLight".Translate().CapitalizeFirst();
                    trend = "+4/tick";
                    trendColor = AccentUtil.StatGood;
                    return;
                default:
                    label = "null";
                    trend = "";
                    trendColor = Color.white;
                    return;
            }
        }

        private static void OpenWorkloadFloatMenu(FCPrisoner prisoner, WorldSettlementFC settlement)
        {
            List<FloatMenuOption> wlList = new List<FloatMenuOption>
            {
                new FloatMenuOption("FCHeavy".Translate().CapitalizeFirst() + " - " + "FCHeavyExplanation".Translate(),
                    delegate { SetWorkload(prisoner, settlement, FCWorkLoad.Heavy); }),
                new FloatMenuOption("FCMedium".Translate().CapitalizeFirst() + " - " + "FCMediumExplanation".Translate(),
                    delegate { SetWorkload(prisoner, settlement, FCWorkLoad.Medium); }),
                new FloatMenuOption("FCLight".Translate().CapitalizeFirst() + " - " + "FCLightExplanation".Translate(),
                    delegate { SetWorkload(prisoner, settlement, FCWorkLoad.Light); })
            };
            Find.WindowStack.Add(new FloatMenu(wlList));
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
                DoActionsMenu(prisoner, settlement, onRemoved);
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
                OpenWorkloadFloatMenu(prisoner, settlement);
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
            GUI.color = origColor;
        }

        /* Compact 56px prisoner row (faction-wide tab).
           Two rows next to a 40×40 portrait:
             Top:    [i] Name, Title ... ... ... ... Male, 63 of NewArr.   $value
             Bottom: [== bar 100 ==] +4/tick                  [Workload] [Actions] */
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

            /* Button geometry (computed up front so the top-row subtitle can right-align
               to the trend's right edge on the row below). */
            const float actionsW = 90f;
            const float workloadW = 130f;
            const float btnGap = 4f;
            const float trendW = 50f;

            float actionsX = rightEdge - actionsW;
            float workloadX = actionsX - btnGap - workloadW;
            float trendRightEdge = workloadX - btnGap;
            float trendLeftEdge = trendRightEdge - trendW;

            /* TOP ROW */
            // Far right: $value
            const float valueW = 70f;
            Rect valueRect = new Rect(rightEdge - valueW, topY, valueW, topRowH);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(valueRect, "$" + (int)(prisoner.prisoner?.MarketValue ?? 0));

            // Subtitle (gender, age, faction) right-aligned to the trend's right edge below
            const float subtitleW = 220f;
            Rect subtitleRect = new Rect(trendRightEdge - subtitleW, topY, subtitleW, topRowH);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(subtitleRect, BuildSubtitle(prisoner.prisoner));
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
                DoActionsMenu(prisoner, settlement, onRemoved);
            }

            GetWorkloadPresentation(prisoner.workload, out string wlLabel, out string wlTrend, out Color wlTrendColor);
            Rect workloadRect = new Rect(workloadX, botY, workloadW, botRowH);
            if (UIUtil.ButtonFlat(workloadRect, "FCWorkload".Translate().CapitalizeFirst() + ": " + wlLabel, highlighted: isHighlighted))
            {
                OpenWorkloadFloatMenu(prisoner, settlement);
            }

            // Left of buttons: trend label (right-aligned, in trend color)
            Rect trendRect = new Rect(trendLeftEdge, botY, trendW, botRowH);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = wlTrendColor;
            Widgets.Label(trendRect, wlTrend);
            GUI.color = origColor;

            // Health bar fills the remaining left side, vertically centered
            float healthBarH = 14f;
            float healthBarY = botY + (botRowH - healthBarH) / 2f;
            float healthBarX = centerX;
            float healthBarW = trendRect.x - healthBarX - 4f;
            Rect healthBarRect = new Rect(healthBarX, healthBarY, healthBarW, healthBarH);
            float healthFrac = prisoner.health / 100f;
            Color healthColor = AccentUtil.GetStatColor(prisoner.health, false);
            UIUtil.DrawProgressBarColors(healthBarRect, healthFrac, healthBarBg, healthColor);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(healthBarRect, "Health".Translate().CapitalizeFirst() + ": " + (int)prisoner.health);

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
            GUI.color = origColor;
        }
    }
}
