using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
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

    /* Emits a "Transfer prisoner to settlement" gizmo whenever a player caravan
       sits on the settlement's tile with at least one pawn flagged
       IsPrisonerOfColony. Visible from both sides: selecting the caravan
       (GetCaravanGizmos) and selecting the settlement (GetGizmos). */
    public class WorldObjectComp_SettlementPrisoners : WorldObjectComp
    {
        public override IEnumerable<Gizmo> GetCaravanGizmos(Caravan caravan)
        {
            foreach (Gizmo gizmo in base.GetCaravanGizmos(caravan))
            {
                yield return gizmo;
            }

            WorldSettlementFC settlement = parent as WorldSettlementFC;
            if (settlement is null) yield break;
            if (caravan is null || caravan.Tile != parent.Tile) yield break;
            if (FactionCache.FactionComp is null) yield break;
            if (!FactionCache.FactionComp.IsActionAllowed(FCActionType.SendPrisoner)) yield break;
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
            if (FactionCache.FactionComp is null) yield break;
            if (!FactionCache.FactionComp.IsActionAllowed(FCActionType.SendPrisoner)) yield break;

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
