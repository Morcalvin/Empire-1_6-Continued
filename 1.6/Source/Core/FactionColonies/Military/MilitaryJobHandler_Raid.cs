using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace FactionColonies
{
    public class MilitaryJobHandler_Raid : MilitaryJobHandler
    {
        public override void OnOpCreated(MilitaryOperation op)
        {
            WorldSettlementFC home = op.aggressor?.homeSettlement;
            if (home is null) return;

            int travelTicks = System.Math.Max(0, op.nextPhaseTick - Find.TickManager.TicksGame);
            string desc = "FCSettlementMilitaryForcesRaiding".Translate(home.Name, op.targetObject?.Label ?? "").ToString();

            op.ScheduleEvent(FCEventDefOf.raidEnemySettlement, home.Tile, travelTicks, desc);

            Settlement targetSettlement = op.targetObject as Settlement
                ?? Find.WorldObjects.SettlementAt(op.targetTile);
            Find.LetterStack.ReceiveLetter("FCMilitaryAction".Translate(),
                "FCMilitarySentRaid".Translate(home.Name, targetSettlement?.LabelCap ?? (TaggedString)""),
                LetterDefOf.NeutralEvent);
        }

        public override BattleResult OnAutoResolve(MilitaryOperation op)
        {
            // Forces are populated in CreateOffensiveOp + BeginEngagement. Use them directly so
            // BattleModifierRegistry / op-aware modifiers see the same instances.
            // Defensive fallback: CreateOffensiveOp populates aggressor.force eagerly via the
            // squad-derived path, so this should never be reached on modern saves. Kept for
            // resilience against legacy paths that never set the force.
            MilitaryForce attacker = op.aggressor?.force
                ?? MilitaryForce.CreateMilitaryForceFromSquad(op.aggressor?.squad, isAttacking: true)
                ?? MilitaryForce.CreateMilitaryForceFromUnstaffedBillet(op.aggressor?.homeSettlement, isAttacking: true);
            MilitaryForce defender = op.defender?.force
                ?? FactionCache.EnemyPower?.ResolveDefenderForceForOp(op, op.BuildBattleContext());
            return SimulateBattleFc.FightBattle(attacker, defender);
        }

        public override void ApplyResult(MilitaryOperation op, BattleResult result)
        {
            if (result is null || op.aggressor?.homeSettlement is null) return;

            Settlement target = op.targetObject as Settlement
                ?? Find.WorldObjects.SettlementAt(op.targetTile);
            if (target is null)
            {
                LogUtil.Warning("Military raid target at tile " + op.targetTile + " no longer exists");
                return;
            }

            if (result.AttackerVictory)
            {
                ApplyVictoryToTarget(FactionCache.FactionComp, op.aggressor.homeSettlement,
                    op.defender?.faction, target);
            }
            else
            {
                Find.LetterStack.ReceiveLetter("FCRaidFailure".Translate(),
                    "FCRaidEnemySettlementFailure".Translate(target.LabelCap),
                    LetterDefOf.NegativeEvent, new LookTargets(target));
            }
        }

        /* Shared victory side effects: loot, prisoners, XP, delivery event. Called from ApplyResult,
         * and from MilitaryJobHandler_Capture's failed-destruction fallback. */
        internal static void ApplyVictoryToTarget(FactionFC faction, WorldSettlementFC home, Faction enemyFaction, Settlement target)
        {
            faction.AddExperienceToFactionLevel(5f);

            TechLevel tech = target.Faction.def.techLevel;
            int lootLevel;
            bool getSlaves = true;

            switch (tech)
            {
                case TechLevel.Archotech:
                case TechLevel.Ultra:
                case TechLevel.Spacer:
                    lootLevel = 4;
                    break;
                case TechLevel.Industrial:
                    lootLevel = 3;
                    break;
                case TechLevel.Medieval:
                case TechLevel.Neolithic:
                    lootLevel = 2;
                    break;
                default:
                    lootLevel = 1;
                    break;
            }

            if (target.Faction.def.defName == "Insect")
            {
                lootLevel = 3;
                getSlaves = false;
            }

            List<Thing> loot = PaymentUtil.GenerateRaidLoot(lootLevel, tech);

            string text = "FCSettlementDeliveringLoot".Translate();
            text = loot.Aggregate(text, (current, thing) => current + thing.LabelCap + " " + thing.stackCount + "x\n ");

            int num = new IntRange(0, 10).RandomInRange;
            if (num <= 4 && getSlaves && enemyFaction is object)
            {
                Pawn prisoner = PaymentUtil.GeneratePrisoner(enemyFaction);
                text += "FCPrisonerCaptureInfo".Translate(prisoner.Name.ToString(), home.Name);
                home.AddPrisoner(prisoner);
            }

            Find.LetterStack.ReceiveLetter("FCRaidLoot".Translate(),
                "FCRaidEnemySettlementSuccess".Translate(target.LabelCap) + "\n" + text,
                LetterDefOf.PositiveEvent, new LookTargets(target));

            FCEvent eventParams = new FCEvent()
            {
                location = Find.AnyPlayerHomeMap.Tile,
                source = home.Tile,
                goods = loot,
                customDescription = text,
                timeTillTrigger = Find.TickManager.TicksGame + TravelUtil.ReturnTicksToArrive(home.Tile, Find.AnyPlayerHomeMap.Tile)
            };
            DeliveryEvent.CreateDeliveryEvent(eventParams);
        }
    }
}
