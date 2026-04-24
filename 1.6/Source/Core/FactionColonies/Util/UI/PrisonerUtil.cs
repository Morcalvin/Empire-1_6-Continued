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
       Used by FCPrisonerMenu (single-settlement window) and
       MainTabWindow_Colony.DrawPrisonersTab (faction-wide tab). */
    public static class PrisonerUtil
    {
        public const float RowHeight = 95f;
        public const float AccentWidth = 4f;

        private const float portraitW = 70f;
        private const float gap = 4f;
        private const float rightColW = 128f;
        private const float pad = 4f;

        private static readonly Color healthBarBg = new Color(0.15f, 0.15f, 0.15f);

        private static readonly FieldInfo hostFactionField =
            typeof(Pawn_GuestTracker).GetField("hostFactionInt", BindingFlags.NonPublic | BindingFlags.Instance);

        /* Re-attaches the detached health tracker onto the pawn so InfoCardButton
           and Dialog_InfoCard render the prisoner's real health state. */
        public static void EnsureHealthTracker(FCPrisoner p)
        {
            if (p?.prisoner is null) return;
            if (p.healthTracker is null)
            {
                p.healthTracker = new Pawn_HealthTracker(p.prisoner);
            }
            if (p.prisoner.health != p.healthTracker)
            {
                p.prisoner.health = p.healthTracker;
            }
        }

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
            EnsureHealthTracker(p);

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

        /* Draws a single 95px-tall prisoner row. altIndex drives alternating-row
           highlighting. onRemoved is invoked after Sell/Return; pass null from
           windows that re-enumerate every frame (main-tab view). */
        public static void DrawPrisonerRow(Rect box, FCPrisoner prisoner, WorldSettlementFC settlement, int altIndex, Action onRemoved)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Color origColor = GUI.color;

            // Background
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

            // Info card button (standard "i" widget), to the left of the name
            EnsureHealthTracker(prisoner);
            float infoSz = Widgets.InfoCardButtonSize;
            float nameRowY = box.y + pad;
            float infoBtnY = nameRowY + (20f - infoSz) / 2f;
            Widgets.InfoCardButton(cx, infoBtnY, prisoner.prisoner);

            // Name (shifted right by the info button)
            float nameX = cx + infoSz + 4f;
            float nameW = cw - infoSz - 4f;
            Rect nameRect = new Rect(nameX, nameRowY, nameW, 20f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, prisoner.prisoner?.Name.ToStringShort ?? "unknown");

            // Health bar (full center-column width)
            Rect healthBarRect = new Rect(cx, nameRect.yMax + 2f, cw, 14f);
            float healthFrac = prisoner.health / 100f;
            Color healthColor = AccentUtil.GetStatColor(prisoner.health, false);
            UIUtil.DrawProgressBarColors(healthBarRect, healthFrac, healthBarBg, healthColor);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(healthBarRect, "Health".Translate().CapitalizeFirst() + ": " + (int)prisoner.health);

            // Workload button + trend indicator
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
                List<FloatMenuOption> wlList = new List<FloatMenuOption>
                {
                    new FloatMenuOption("FCHeavy".Translate().CapitalizeFirst() + " - " + "FCHeavyExplanation".Translate(), delegate
                    {
                        SetWorkload(prisoner, settlement, FCWorkLoad.Heavy);
                    }),
                    new FloatMenuOption("FCMedium".Translate().CapitalizeFirst() + " - " + "FCMediumExplanation".Translate(), delegate
                    {
                        SetWorkload(prisoner, settlement, FCWorkLoad.Medium);
                    }),
                    new FloatMenuOption("FCLight".Translate().CapitalizeFirst() + " - " + "FCLightExplanation".Translate(), delegate
                    {
                        SetWorkload(prisoner, settlement, FCWorkLoad.Light);
                    })
                };
                Find.WindowStack.Add(new FloatMenu(wlList));
            }

            Rect trendRect = new Rect(workloadRect.xMax + 4f, workloadRect.y, 60f, 22f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = trendColor;
            Widgets.Label(trendRect, trendText);
            GUI.color = origColor;

            // Right column
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

            // Actions fills the freed-up vertical space (old layout had View Info above it)
            float actionsY = factionRect.yMax + 4f;
            float actionsH = box.yMax - pad - actionsY;
            if (actionsH < 22f) actionsH = 22f;
            Rect actionsBtn = new Rect(rx, actionsY, rightColW, actionsH);
            if (UIUtil.ButtonFlat(actionsBtn, "FCActions".Translate()))
            {
                DoActionsMenu(prisoner, settlement, onRemoved);
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
            GUI.color = origColor;
        }
    }
}
