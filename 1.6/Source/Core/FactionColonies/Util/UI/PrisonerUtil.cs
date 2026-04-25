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
        public const float CompactRowHeight = 48f;
        public const float AccentWidth = 4f;

        private const float portraitW = 70f;
        private const float compactPortraitSz = 40f;
        private const float infoBtnSz = 18f;
        private const float gap = 4f;
        private const float rightColW = 128f;
        private const float pad = 4f;

        private static readonly Color healthBarBg = new Color(0.15f, 0.15f, 0.15f);

        private static readonly FieldInfo hostFactionField =
            typeof(Pawn_GuestTracker).GetField("hostFactionInt", BindingFlags.NonPublic | BindingFlags.Instance);

        /* Removes any FCPrisoner entries whose backing Pawn has gone null
           (e.g., a mod interaction destroyed the pawn without removing the
           wrapper). Call on window open to defend against save corruption. */
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

        /* Moves a single prisoner from a caravan into the settlement's prisonerList.
           AddPrisoner wraps the pawn in FCPrisoner, which in turn sets guest status
           to Prisoner of PlayerColonyFaction and dirties the settlement stats cache. */
        public static void TransferFromCaravan(Pawn pawn, Caravan caravan, WorldSettlementFC settlement)
        {
            if (pawn is null || caravan is null || settlement is null) return;
            caravan.RemovePawn(pawn);
            caravan.Notify_PawnRemoved(pawn);
            settlement.AddPrisoner(pawn);
        }

        /* Opens the FloatMenu listing every prisoner currently in the caravan,
           with "transfer to settlement" as the only action per-prisoner. */
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

            if (FactionCache.FactionComp.IsActionAllowed(FCActionType.SellPrisoner))
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

        /* Rich 95px prisoner row: portrait + name/info + health + workload + trend,
           and a right column with market value, origin faction, and Actions. */
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

            // Origin-faction accent strip (left edge)
            Color accentColor = prisoner.prisoner?.Faction?.Color ?? Color.gray;
            Widgets.DrawBoxSolid(new Rect(box.x, box.y, AccentWidth, box.height), accentColor);

            float contentStartX = box.x + AccentWidth;

            // Portrait
            Rect portraitRect = new Rect(contentStartX + pad, box.y + 6f, portraitW, 78f);
            if (prisoner.prisoner is object)
            {
                UIUtil.DrawPawnPortrait(portraitRect, prisoner.prisoner, 1.2f);
            }

            // Center column layout
            float cx = contentStartX + portraitW + gap + pad;
            float cw = box.xMax - cx - rightColW - pad;

            // Info card button (18x18), centered vertically in the name row
            float nameRowY = box.y + pad;
            float nameH = 24f;
            Rect infoRect = new Rect(cx, nameRowY + (nameH - infoBtnSz) / 2f, infoBtnSz, infoBtnSz);
            if (prisoner.prisoner is object)
            {
                UIUtil.InfoCardThing(infoRect, prisoner.prisoner);
            }

            // Name (right of info button)
            float nameX = cx + infoBtnSz + 4f;
            float nameW = cw - infoBtnSz - 4f;
            Rect nameRect = new Rect(nameX, nameRowY, nameW, nameH);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, prisoner.prisoner?.Name.ToStringShort ?? "unknown");

            // Health bar
            Rect healthBarRect = new Rect(cx, nameRect.yMax + 2f, cw, 14f);
            float healthFrac = prisoner.health / 100f;
            Color healthColor = AccentUtil.GetStatColor(prisoner.health, false);
            UIUtil.DrawProgressBarColors(healthBarRect, healthFrac, healthBarBg, healthColor);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(healthBarRect, "Health".Translate().CapitalizeFirst() + ": " + (int)prisoner.health);

            // Workload button + trend indicator
            Rect workloadRect = new Rect(cx, healthBarRect.yMax + 4f, 150f, 22f);
            GetWorkloadPresentation(prisoner.workload, out string wlLabel, out string wlTrend, out Color wlTrendColor);

            if (Widgets.ButtonText(workloadRect, "FCWorkload".Translate().CapitalizeFirst() + ": " + wlLabel))
            {
                OpenWorkloadFloatMenu(prisoner, settlement);
            }

            Rect trendRect = new Rect(workloadRect.xMax + 4f, workloadRect.y, 60f, 22f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = wlTrendColor;
            Widgets.Label(trendRect, wlTrend);
            GUI.color = origColor;

            // Right column: market value + origin faction + actions (fixed 22f)
            float rx = box.xMax - rightColW - pad;

            Rect valueRect = new Rect(rx, box.y + pad, rightColW, 20f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(valueRect, "$" + (int)(prisoner.prisoner?.MarketValue ?? 0));

            Rect factionRect = new Rect(rx, valueRect.yMax + 2f, rightColW, 14f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            string factionName = prisoner.prisoner?.Faction is object ? (string)prisoner.prisoner.Faction.NameColored : "";
            Widgets.Label(factionRect, factionName);

            Rect actionsBtn = new Rect(rx, factionRect.yMax + 4f, rightColW, 22f);
            if (UIUtil.ButtonFlat(actionsBtn, "FCActions".Translate()))
            {
                DoActionsMenu(prisoner, settlement, onRemoved);
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
            GUI.color = origColor;
        }

        /* Compact 48px row used in the faction-wide Prisoners tab.
           Layout (left→right, top→bottom):
             accent(4) | portrait(40) | [info]+name+health  | workload | actions
           Drops the trend indicator and origin-faction label. */
        public static void DrawPrisonerRowCompact(Rect box, FCPrisoner prisoner, WorldSettlementFC settlement, int altIndex, Action onRemoved)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Color origColor = GUI.color;

            Widgets.DrawMenuSection(box);
            if (altIndex % 2 == 0)
            {
                Widgets.DrawHighlight(box);
            }

            // Faction accent
            Color accentColor = prisoner.prisoner?.Faction?.Color ?? Color.gray;
            Widgets.DrawBoxSolid(new Rect(box.x, box.y, AccentWidth, box.height), accentColor);

            float contentStartX = box.x + AccentWidth;

            // Portrait (40×40 centered vertically)
            float portraitY = box.y + (box.height - compactPortraitSz) / 2f;
            Rect portraitRect = new Rect(contentStartX + pad, portraitY, compactPortraitSz, compactPortraitSz);
            if (prisoner.prisoner is object)
            {
                UIUtil.DrawPawnPortrait(portraitRect, prisoner.prisoner, 1.2f);
            }

            // Right-side button stack: workload (left), actions (right)
            const float actionsW = 90f;
            const float workloadW = 120f;
            const float btnH = 22f;
            const float btnGap = 4f;

            float actionsX = box.xMax - pad - actionsW;
            float workloadX = actionsX - btnGap - workloadW;
            float topY = box.y + pad;

            Rect workloadRect = new Rect(workloadX, topY, workloadW, btnH);
            GetWorkloadPresentation(prisoner.workload, out string wlLabel, out string wlTrend, out Color wlTrendColor);
            if (Widgets.ButtonText(workloadRect, "FCWorkload".Translate().CapitalizeFirst() + ": " + wlLabel))
            {
                OpenWorkloadFloatMenu(prisoner, settlement);
            }

            Rect actionsRect = new Rect(actionsX, topY, actionsW, btnH);
            if (UIUtil.ButtonFlat(actionsRect, "FCActions".Translate()))
            {
                DoActionsMenu(prisoner, settlement, onRemoved);
            }

            // Market value: right-aligned on the bottom row beneath the buttons
            float valueY = topY + btnH + 2f;
            Rect valueRect = new Rect(workloadX, valueY, workloadW + btnGap + actionsW, 14f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(valueRect, "$" + (int)(prisoner.prisoner?.MarketValue ?? 0));

            // Center block: info + name on top, health bar below
            float centerX = portraitRect.xMax + (pad * 2);
            float centerW = workloadRect.x - centerX - btnGap;
            float nameH = 24f;
            Rect infoRect = new Rect(centerX, topY + (nameH - infoBtnSz) / 2f, infoBtnSz, infoBtnSz);
            if (prisoner.prisoner is object)
            {
                UIUtil.InfoCardThing(infoRect, prisoner.prisoner);
            }

            float nameX = centerX + infoBtnSz + 4f;
            float nameW = centerW - infoBtnSz - 4f;
            Rect nameRect = new Rect(nameX, topY, nameW, nameH);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, prisoner.prisoner?.Name.ToStringShort ?? "unknown");

            Rect healthBarRect = new Rect(centerX, nameRect.yMax + 2f, centerW, 14f);
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
