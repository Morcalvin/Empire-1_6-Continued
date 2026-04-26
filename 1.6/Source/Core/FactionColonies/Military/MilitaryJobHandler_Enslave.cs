using RimWorld;
using RimWorld.Planet;
using System;
using Verse;

namespace FactionColonies
{
    public class MilitaryJobHandler_Enslave : MilitaryJobHandler
    {
        public override bool IsValidTarget(Faction targetFaction) => targetFaction?.def?.defName != "Insect";

        [Obsolete("Use OnOpCreated(MilitaryOperation) instead. Will be removed in a future version.")]
        public override void OnDeployed(WorldObjectComp_SettlementMilitary milComp, PlanetTile location, int timeToFinish, Faction enemy)
        {
            FactionFC factionfc = FactionCache.FactionComp;
            FCEvent evt = FCEventMaker.MakeEvent(FCEventDefOf.enslaveEnemySettlement);
            evt.customDescription = "FCSettlementMilitaryForcesEnslave".Translate(milComp.WorldSettlement.Name, milComp.ReturnMilitaryTarget().Label);
            Settlement target = Find.WorldObjects.SettlementAt(location);
            Find.LetterStack.ReceiveLetter("FCMilitaryAction".Translate(), "FCMilitarySentEnslave".Translate(milComp.WorldSettlement.Name, target?.LabelCap ?? (TaggedString)""), LetterDefOf.NeutralEvent);
            evt.DefineEvent(factionfc, milComp.WorldSettlement.Tile, timeToFinish);
        }

        [Obsolete("Use OnAutoResolve(MilitaryOperation) and ApplyResult(MilitaryOperation, BattleResult) instead. Will be removed in a future version.")]
        public override BattleResult OnResolved(WorldObjectComp_SettlementMilitary milComp)
        {
            FactionFC faction = FactionCache.FactionComp;

            Settlement target = Find.WorldObjects.SettlementAt(milComp.militaryLocation);
            if (target == null)
            {
                LogUtil.Warning("Military enslave target at tile " + milComp.militaryLocation + " no longer exists");
                return new BattleResult();
            }

            BattleResult result = SimulateBattleFc.FightBattle(
                MilitaryForce.CreateMilitaryForceFromSettlement(milComp.WorldSettlement, true),
                MilitaryForce.CreateMilitaryForceFromFaction(milComp.militaryEnemy, false));

            if (result.AttackerVictory)
            {
                ApplyEnslaveSuccess(faction, milComp.WorldSettlement, milComp.militaryEnemy, target);
            }
            else if (result.DefenderVictory)
            {
                Find.LetterStack.ReceiveLetter("FCRaidFailure".Translate(),
                    "FCRaidEnemySettlementFailure".Translate(target.LabelCap),
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
            string desc = "FCSettlementMilitaryForcesEnslave".Translate(home.Name, op.targetObject?.Label ?? "").ToString();

            op.ScheduleEvent(FCEventDefOf.enslaveEnemySettlement, home.Tile, travelTicks, desc);

            Settlement targetSettlement = op.targetObject as Settlement
                ?? Find.WorldObjects.SettlementAt(op.targetTile);
            Find.LetterStack.ReceiveLetter("FCMilitaryAction".Translate(),
                "FCMilitarySentEnslave".Translate(home.Name, targetSettlement?.LabelCap ?? (TaggedString)""),
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
                LogUtil.Warning("Military enslave target at tile " + op.targetTile + " no longer exists");
                return;
            }

            if (result.AttackerVictory)
            {
                ApplyEnslaveSuccess(FactionCache.FactionComp, op.aggressor.homeSettlement,
                    op.defender?.faction, target);
            }
            else if (result.DefenderVictory)
            {
                Find.LetterStack.ReceiveLetter("FCRaidFailure".Translate(),
                    "FCRaidEnemySettlementFailure".Translate(target.LabelCap),
                    LetterDefOf.NegativeEvent, new LookTargets(target));
            }
        }

        /* Shared enslave-victory side effects: 1-3 prisoners. */
        private static void ApplyEnslaveSuccess(FactionFC faction, WorldSettlementFC home, Faction enemyFaction, Settlement target)
        {
            faction.AddExperienceToFactionLevel(5f);

            string text = "";

            int num = new IntRange(1, 3).RandomInRange;
            for (int i = 0; i <= num; i++)
            {
                Pawn prisoner = PaymentUtil.GeneratePrisoner(enemyFaction);
                text += "FCPrisonerCaptureInfo".Translate(prisoner.Name.ToString(), home.Name) + "\n";
                home.AddPrisoner(prisoner);
            }

            Find.LetterStack.ReceiveLetter("FCRaidLoot".Translate(),
                "FCRaidEnemySettlementSuccess".Translate(target.LabelCap) + "\n" + text,
                LetterDefOf.PositiveEvent, new LookTargets(target));
        }
    }
}
