using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
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

            if (dead is null) return;
            foreach (FCPrisoner d in dead) HandlePrisonerDeath(d);
        }

        public bool RemovePrisoner(FCPrisoner p)
        {
            if (prisonerList is null || p is null) return false;
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

        public int ReturnMaxWorkersFromPrisoners()
        {
            int num = 0;
            foreach (FCPrisoner prisoner in prisonerList)
            {
                switch (prisoner.workload)
                {
                    case FCWorkLoad.Medium:
                        num++;
                        break;
                    case FCWorkLoad.Heavy:
                        num += 2;
                        break;
                }
            }
            return num;
        }

        public int ReturnOverMaxWorkersFromPrisoners()
        {
            return prisonerList.Count(prisoner => prisoner.workload == FCWorkLoad.Light);
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

            yield return BuildTransferGizmo(caravan, settlement);
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

            yield return BuildTransferGizmo(caravan, settlement);
        }

        private static Command_Action BuildTransferGizmo(Caravan caravan, WorldSettlementFC settlement)
        {
            return new Command_Action
            {
                defaultLabel = "FCTransferPrisonerToSettlement".Translate(),
                defaultDesc = "FCTransferPrisonerGizmoDesc".Translate(),
                icon = TexLoad.iconMilitary,
                action = delegate
                {
                    PrisonerUtil.DoTransferMenu(caravan, settlement);
                }
            };
        }
    }
}
