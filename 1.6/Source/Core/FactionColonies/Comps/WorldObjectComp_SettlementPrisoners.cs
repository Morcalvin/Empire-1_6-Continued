using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public class WorldObjectCompProperties_SettlementPrisoners : WorldObjectCompProperties
    {
        public WorldObjectCompProperties_SettlementPrisoners()
        {
            compClass = typeof(WorldObjectComp_SettlementPrisoners);
        }

        public override IEnumerable<string> ConfigErrors(WorldObjectDef parentDef)
        {
            foreach (string item in base.ConfigErrors(parentDef))
            {
                yield return item;
            }
            if (!typeof(MapParent).IsAssignableFrom(parentDef.worldObjectClass))
            {
                yield return parentDef.defName + " has WorldObjectCompProperties_SettlementPrisoners but it's not MapParent.";
            }
        }
    }

    /* Owns this settlement's FCPrisoner list and its daily health tick.
       Also emits the "Transfer prisoner to settlement" gizmo when a player
       caravan sits on the settlement's tile with at least one pawn flagged
       IsPrisonerOfColony. The gizmo is visible from both sides: selecting the
       caravan (GetCaravanGizmos) and selecting the settlement (GetGizmos). */
    public class WorldObjectComp_SettlementPrisoners : WorldObjectComp
    {
        public List<FCPrisoner> prisonerList = new List<FCPrisoner>();

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref prisonerList, "prisoners", LookMode.Deep);
            if (prisonerList is null) prisonerList = new List<FCPrisoner>();
        }

        public override void CompTick()
        {
            base.CompTick();
            if (Find.TickManager.TicksGame % GenDate.TicksPerDay == 0)
            {
                AdvanceDailyHealth();
            }
        }

        /* Daily health update. Heavy workload damages, Light heals. Two-phase to keep
         * the iteration over prisonerList safe: first pass applies deltas and gathers any
         * dead prisoners into a scratch list; second pass processes the deaths. */
        public void AdvanceDailyHealth()
        {
            if (prisonerList is null) return;

            List<FCPrisoner> dead = null;
            foreach (FCPrisoner p in prisonerList)
            {
                switch (p.workload)
                {
                    case FCWorkLoad.Heavy:  p.AdjustHealth(-4); break;
                    case FCWorkLoad.Medium: p.AdjustHealth(-2); break;
                    case FCWorkLoad.Light:  p.AdjustHealth(1);  break;
                }
                if (p.IsDead)
                {
                    if (dead is null) dead = new List<FCPrisoner>();
                    dead.Add(p);
                }
            }

            if (dead != null)
                foreach (FCPrisoner d in dead) HandlePrisonerDeath(d);

            // A Light-workload heal may have un-downed a prisoner, so refresh the worker cap.
            (parent as WorldSettlementFC)?.NotifyWorkforceChanged();
        }

        /* The single removal entry point — no other code should call prisonerList.Remove
         * directly. Null `p` is allowed (and removes the first null entry) so CullNullPrisoners
         * can route degenerate null FCPrisoner entries through this method too. */
        public bool RemovePrisoner(FCPrisoner p)
        {
            if (prisonerList is null) return false;
            bool removed = prisonerList.Remove(p);
            if (removed) (parent as WorldSettlementFC)?.NotifyWorkforceChanged();
            return removed;
        }

        private void HandlePrisonerDeath(FCPrisoner p)
        {
            WorldSettlementFC s = parent as WorldSettlementFC;
            string pawnName = p.prisoner?.Name?.ToString() ?? "";
            string sName = s?.Name ?? "";
            RemovePrisoner(p);
            Find.LetterStack.ReceiveLetter(
                "FCPrisonerHasDiedLetter".Translate(),
                "FCPrisonerHasDied".Translate(pawnName, sName),
                LetterDefOf.NeutralEvent);
        }

        public void AddPrisoner(Pawn pawn)
        {
            WorldSettlementFC settlement = parent as WorldSettlementFC;
            if (settlement is null || pawn is null) return;

            // FCPrisoner is the canonical deep owner of the held pawn. If WorldPawns
            // already has it (e.g., redressed by PawnGenerator, passed via LeaveMap,
            // dropped from a caravan), pull it out so save doesn't double-scribe.
            // The conditional Scribe in FCPrisoner.ExposeData defends on-map cases.
            if (Find.WorldPawns is object && Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.RemovePawn(pawn);
            }
            prisonerList.Add(new FCPrisoner(pawn, settlement));
            settlement.DirtyStatsCache();
        }

        /* Removes any FCPrisoner entries that are themselves null or wrap a null pawn. */
        public int CullNullPrisoners()
        {
            if (prisonerList is null) return 0;

            List<FCPrisoner> toRemove = null;
            foreach (FCPrisoner p in prisonerList)
            {
                if (p?.prisoner is null)
                {
                    if (toRemove is null) toRemove = new List<FCPrisoner>();
                    toRemove.Add(p);
                }
            }

            if (toRemove is null) return 0;
            foreach (FCPrisoner p in toRemove) RemovePrisoner(p);

            WorldSettlementFC s = parent as WorldSettlementFC;
            LogUtil.Warning("Culled " + toRemove.Count + " null prisoner(s) from " + (s?.Name ?? "<unknown>"));
            return toRemove.Count;
        }

        public void TransferFromCaravan(Pawn pawn, Caravan caravan)
        {
            if (pawn is null || caravan is null) return;
            caravan.RemovePawn(pawn);
            caravan.Notify_PawnRemoved(pawn);
            AddPrisoner(pawn);
        }

        public void DoTransferMenu(Caravan caravan)
        {
            if (caravan is null) return;

            List<FloatMenuOption> list = new List<FloatMenuOption>();
            List<Pawn> pawns = caravan.PawnsListForReading;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn captured = pawns[i];
                if (!captured.IsPrisonerOfColony) continue;
                list.Add(new FloatMenuOption(
                    "FCTransferPrisonerOption".Translate(captured.Name.ToStringShort),
                    delegate { TransferFromCaravan(captured, caravan); }));
            }

            if (list.Count == 0)
            {
                Messages.Message("FCNoPrisonersInCaravan".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WindowStack.Add(new FloatMenu(list));
        }

        public void SetWorkload(FCPrisoner p, FCWorkLoad workload)
        {
            if (p is null) return;
            p.workload = workload;
            (parent as WorldSettlementFC)?.DirtyStatsCache();
        }

        public void SellPrisoner(FCPrisoner p)
        {
            if (p?.prisoner is null) return;
            WorldSettlementFC s = parent as WorldSettlementFC;
            s?.AddOneTimeSilverIncome(p.prisoner.MarketValue);
            RemovePrisoner(p);
        }

        public void ReturnPrisonerToPlayer(FCPrisoner p)
        {
            if (p?.prisoner is null) return;
            WorldSettlementFC s = parent as WorldSettlementFC;
            if (s is null) return;

            if (!HealthUtility.TryAnesthetize(p.prisoner))
                HealthUtility.DamageUntilDowned(p.prisoner, false);

            if (p.prisoner.guest is null)
                p.prisoner.guest = new Pawn_GuestTracker(p.prisoner);
            p.prisoner.guest.SetGuestStatus(Find.FactionManager.OfPlayer, GuestStatus.Prisoner);

            DeliveryEvent.CreateDeliveryEvent(new FCEvent
            {
                location = Find.AnyPlayerHomeMap.Tile,
                source = s.Tile,
                goods = new List<Thing> { p.prisoner },
                customDescription = "FCAPrisonerIsBeingDeliveredToYou".Translate(),
                timeTillTrigger = Find.TickManager.TicksGame + TravelUtil.ReturnTicksToArrive(s.Tile, Find.AnyPlayerHomeMap.Tile)
            });

            RemovePrisoner(p);
        }

        public void DoActionsMenu(FCPrisoner p, Action onRemoved)
        {
            if (p is null) return;
            List<FloatMenuOption> list = new List<FloatMenuOption>();

            if (FindFC.FactionComp.IsActionAllowed(FCActionType.SellPrisoner))
            {
                list.Add(new FloatMenuOption(
                    "FCSellPawn".Translate() + " $" + p.prisoner.MarketValue + " " + "FCSellPawnInfo".Translate(),
                    delegate
                    {
                        SellPrisoner(p);
                        onRemoved?.Invoke();
                    }));
            }

            list.Add(new FloatMenuOption("FCReturnToPlayer".Translate(), delegate
            {
                ReturnPrisonerToPlayer(p);
                onRemoved?.Invoke();
            }));

            Find.WindowStack.Add(new FloatMenu(list));
        }

        public void OpenWorkloadFloatMenu(FCPrisoner p)
        {
            if (p is null) return;
            List<FloatMenuOption> wlList = new List<FloatMenuOption>
            {
                new FloatMenuOption("FCHeavy".Translate().CapitalizeFirst() + " - " + "FCHeavyExplanation".Translate(),
                    delegate { SetWorkload(p, FCWorkLoad.Heavy); }),
                new FloatMenuOption("FCMedium".Translate().CapitalizeFirst() + " - " + "FCMediumExplanation".Translate(),
                    delegate { SetWorkload(p, FCWorkLoad.Medium); }),
                new FloatMenuOption("FCLight".Translate().CapitalizeFirst() + " - " + "FCLightExplanation".Translate(),
                    delegate { SetWorkload(p, FCWorkLoad.Light); })
            };
            Find.WindowStack.Add(new FloatMenu(wlList));
        }

        /* Downed prisoners can't perform any work, so they're excluded from worker contributions. */
        public int ReturnMaxWorkersFromPrisoners()
        {
            int num = 0;
            foreach (FCPrisoner p in prisonerList)
            {
                if (p?.prisoner is null || p.prisoner.Downed) continue;
                switch (p.workload)
                {
                    case FCWorkLoad.Medium: num++; break;
                    case FCWorkLoad.Heavy:  num += 2; break;
                }
            }
            return num;
        }

        public int ReturnOverMaxWorkersFromPrisoners()
        {
            return prisonerList.Count(p =>
                p?.prisoner is object && !p.prisoner.Downed && p.workload == FCWorkLoad.Light);
        }

        public override IEnumerable<Gizmo> GetCaravanGizmos(Caravan caravan)
        {
            foreach (Gizmo gizmo in base.GetCaravanGizmos(caravan))
            {
                yield return gizmo;
            }

            WorldSettlementFC settlement = parent as WorldSettlementFC;
            if (settlement is null) yield break;
            if (caravan is null || caravan.Tile != parent.Tile) yield break;
            if (FindFC.FactionComp is null) yield break;
            if (!FindFC.FactionComp.IsActionAllowed(FCActionType.SendPrisoner)) yield break;
            if (!PrisonerUtil.HasPrisonersOfColony(caravan)) yield break;

            yield return BuildTransferGizmo(caravan);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            WorldSettlementFC settlement = parent as WorldSettlementFC;
            if (settlement is null) yield break;
            if (FindFC.FactionComp is null) yield break;
            if (!FindFC.FactionComp.IsActionAllowed(FCActionType.SendPrisoner)) yield break;

            Caravan caravan = Find.WorldObjects.PlayerControlledCaravanAt(parent.Tile);
            if (caravan is null) yield break;
            if (!PrisonerUtil.HasPrisonersOfColony(caravan)) yield break;

            yield return BuildTransferGizmo(caravan);
        }

        private Command_Action BuildTransferGizmo(Caravan caravan)
        {
            return new Command_Action
            {
                defaultLabel = "FCTransferPrisonerToSettlement".Translate(),
                defaultDesc = "FCTransferPrisonerGizmoDesc".Translate(),
                icon = TexLoad.iconMilitary,
                action = delegate
                {
                    DoTransferMenu(caravan);
                }
            };
        }
    }
}
