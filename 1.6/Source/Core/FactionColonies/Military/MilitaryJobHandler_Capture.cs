using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System.Linq;
using Verse;

namespace FactionColonies
{
    public class MilitaryJobHandler_Capture : MilitaryJobHandler
    {
        public override void OnDeployed(WorldObjectComp_SettlementMilitary milComp, PlanetTile location, int timeToFinish, Faction enemy)
        {
            FactionFC factionfc = FactionCache.FactionComp;
            FCEvent evt = FCEventMaker.MakeEvent(FCEventDefOf.captureEnemySettlement);
            evt.customDescription = "FCSettlementMilitaryForcesCapturing".Translate(milComp.WorldSettlement.Name, milComp.ReturnMilitaryTarget().Label);
            Settlement target = Find.WorldObjects.SettlementAt(location);
            Find.LetterStack.ReceiveLetter("FCMilitaryAction".Translate(), "FCMilitarySentCapture".Translate(milComp.WorldSettlement.Name, target?.LabelCap ?? (TaggedString)""), LetterDefOf.NeutralEvent);
            evt.DefineEvent(factionfc, milComp.WorldSettlement.Tile, timeToFinish);
        }

        public override BattleResult OnResolved(WorldObjectComp_SettlementMilitary milComp)
        {
            FactionFC faction = FactionCache.FactionComp;

            Settlement target = Find.WorldObjects.SettlementAt(milComp.militaryLocation);
            if (target == null)
            {
                LogUtil.Warning("Military capture target at tile " + milComp.militaryLocation + " no longer exists");
                return new BattleResult();
            }

            BattleResult result = SimulateBattleFc.FightBattle(
                MilitaryForce.CreateMilitaryForceFromSettlement(milComp.WorldSettlement, true),
                MilitaryForce.CreateMilitaryForceFromFaction(milComp.militaryEnemy, false));

            if (result.AttackerVictory)
            {
                ApplyCaptureSuccess(faction, milComp.WorldSettlement, milComp.militaryLocation, target);
            }
            else if (result.DefenderVictory)
            {
                Find.LetterStack.ReceiveLetter("FCCaptureSettlement".Translate(),
                    "FCCaptureEnemySettlementFailure".Translate(milComp.WorldSettlement.Name, target.Name),
                    LetterDefOf.NegativeEvent, new LookTargets(target));
            }

            return result;
        }

        /* -*-*-*-*- Op-aware path -*-*-*-*- */

        public override void OnOpCreated(MilitaryOperation op)
        {
            WorldSettlementFC home = op.aggressor?.homeSettlement;
            if (home is null) return;

            int travelTicks = System.Math.Max(0, op.nextPhaseTick - Find.TickManager.TicksGame);
            string desc = "FCSettlementMilitaryForcesCapturing".Translate(home.Name, op.targetObject?.Label ?? "").ToString();

            op.ScheduleEvent(FCEventDefOf.captureEnemySettlement, home.Tile, travelTicks, desc);

            Settlement targetSettlement = op.targetObject as Settlement
                ?? Find.WorldObjects.SettlementAt(op.targetTile);
            Find.LetterStack.ReceiveLetter("FCMilitaryAction".Translate(),
                "FCMilitarySentCapture".Translate(home.Name, targetSettlement?.LabelCap ?? (TaggedString)""),
                LetterDefOf.NeutralEvent);
        }

        public override BattleResult OnAutoResolve(MilitaryOperation op)
        {
            MilitaryForce attacker = op.aggressor?.force
                ?? MilitaryForce.CreateMilitaryForceFromSettlement(op.aggressor?.homeSettlement, true);
            MilitaryForce defender = op.defender?.force
                ?? MilitaryForce.CreateMilitaryForceFromFaction(op.defender?.faction, false);
            return SimulateBattleFc.FightBattle(attacker, defender);
        }

        public override void ApplyResult(MilitaryOperation op, BattleResult result)
        {
            if (result is null || op.aggressor?.homeSettlement is null) return;

            Settlement target = op.targetObject as Settlement
                ?? Find.WorldObjects.SettlementAt(op.targetTile);
            if (target is null)
            {
                LogUtil.Warning("Military capture target at tile " + op.targetTile + " no longer exists");
                return;
            }

            if (result.AttackerVictory)
            {
                ApplyCaptureSuccess(FactionCache.FactionComp, op.aggressor.homeSettlement, op.targetTile, target);
            }
            else if (result.DefenderVictory)
            {
                Find.LetterStack.ReceiveLetter("FCCaptureSettlement".Translate(),
                    "FCCaptureEnemySettlementFailure".Translate(op.aggressor.homeSettlement.Name, target.Name),
                    LetterDefOf.NegativeEvent, new LookTargets(target));
            }
        }

        /* Shared capture-victory side effects, used by both legacy and op-aware paths. */
        private static void ApplyCaptureSuccess(FactionFC faction, WorldSettlementFC home,
            PlanetTile capturedTile, Settlement target)
        {
            faction.AddExperienceToFactionLevel(5f);

            string tmpName = target.LabelCap;
            TechLevel tech = target.Faction.def.techLevel;
            Faction tempFactionLink = target.Faction;
            target.Destroy();
            WorldSettlementFC worldsettlement = ColonyUtil.CreatePlayerColonySettlement(
                capturedTile,
                ColonyUtil.DefaultSettlementDefForTile(capturedTile));
            worldsettlement.Name = tmpName;

            int upgradeTimes;

            switch (tech)
            {
                case TechLevel.Archotech:
                case TechLevel.Ultra:
                case TechLevel.Spacer:
                    upgradeTimes = 2;
                    break;
                case TechLevel.Industrial:
                    upgradeTimes = 1;
                    break;
                default:
                    upgradeTimes = 0;
                    break;
            }

            worldsettlement.UpgradeSettlement(upgradeTimes);

            worldsettlement.loyalty = 15;
            worldsettlement.happiness = 25;
            worldsettlement.unrest = 20;
            worldsettlement.prosperity = 70;

            bool defeated = !Find.WorldObjects.Settlements.Any(settlement => settlement.Faction != null
                && settlement.Faction == tempFactionLink);

            if (defeated)
            {
                tempFactionLink.defeated = true;
            }

            Find.LetterStack.ReceiveLetter("FCCaptureSettlement".Translate(),
                "FCCaptureEnemySettlementSuccess".Translate(home.Name, worldsettlement.Name, worldsettlement.settlementLevel),
                LetterDefOf.PositiveEvent, new LookTargets(worldsettlement));
        }
    }
}
