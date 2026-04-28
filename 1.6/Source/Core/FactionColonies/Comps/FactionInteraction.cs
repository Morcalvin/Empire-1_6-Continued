using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using Verse;

namespace FactionColonies
{
    public class WorldObjectCompProperties_FactionInteraction : WorldObjectCompProperties
    {
        public WorldObjectCompProperties_FactionInteraction()
        {
            compClass = typeof(WorldObjectComp_FactionInteraction);
        }
    }

    /// <summary>
    /// Adds Empire interaction gizmos (attack, diplomacy) to non-player, non-Empire settlements.
    /// XML-patched onto the vanilla Settlement WorldObjectDef so gizmos flow through
    /// RimWorld's native comp system instead of Harmony patches.
    /// </summary>
    public class WorldObjectComp_FactionInteraction : WorldObjectComp
    {
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;

            if (!HasValidFaction()) yield break;
            FactionFC factionFC = FactionCache.FactionComp;
            if (factionFC is null) yield break;

            Faction faction = parent.Faction;
            PlanetTile tile = parent.Tile;

            if (factionFC.IsActionAllowed(FCActionType.SendDiplomat))
                yield return PeacefulAction(factionFC, faction);

            if (factionFC.IsActionAllowed(FCActionType.DeployMilitary))
                yield return HostileAction(factionFC, faction, tile);
        }

        private bool HasValidFaction() =>
            parent.Faction != FactionCache.PlayerColonyFaction &&
            parent.Faction != Find.FactionManager.OfPlayer;

        private static FloatMenuOption NewOption(FactionFC factionFC, Faction faction, PlanetTile tile, MilitaryJobDef job) =>
            new FloatMenuOption((job.floatMenuLabelKey ?? "FCUnsupportedMilJobError").Translate(), delegate
            {
                // Squad-first refactor: replaced the second-level "settlement picker" float menu
                // with the richer Dialog_SquadSourcePicker. Picker handles travel time, win
                // chance forecast, status filtering, and dispatches the selected squad through
                // the manager on confirm.
                WorldObject target = Find.WorldObjects.WorldObjectAt<WorldObject>(tile);
                if (target is null)
                {
                    Messages.Message("FCNoValidMilitaries".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }
                Find.WindowStack.Add(new Dialog_SquadSourcePicker(target, job, faction));
            });

        private static Command_Action HostileAction(FactionFC factionFC, Faction faction, PlanetTile tile) =>
            new Command_Action
            {
                defaultLabel = "FCAttackSettlement".Translate(
                    faction.HasName ? faction.Name : "FCUnsupportedSettlementFaction".Translate().ToString()),
                defaultDesc = "",
                icon = TexLoad.iconMilitary,
                action = delegate
                {
                    List<FloatMenuOption> list = new List<FloatMenuOption>();

                    foreach (MilitaryJobDef job in FactionCache.HostileMilitaryJobs)
                    {
                        if (!factionFC.IsMilitaryJobAllowed(job)) continue;
                        if (job.Handler != null && !job.Handler.IsValidTarget(faction)) continue;
                        list.Add(NewOption(factionFC, faction, tile, job));
                    }

                    if (list.Count == 0)
                        list.Add(new FloatMenuOption("FCNoValidMilitaries".Translate(), null));

                    Find.WindowStack.Add(new FloatMenu(list));
                }
            };

        private static Command_Action PeacefulAction(FactionFC factionFC, Faction faction) =>
            new Command_Action
            {
                defaultLabel = "FCIncreaseRelations".Translate(),
                defaultDesc = "",
                icon = TexLoad.iconProsperity,
                action = delegate { factionFC.SendDiplomaticEnvoy(faction); }
            };
    }
}
