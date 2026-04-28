using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System.Linq;
using Verse;

namespace FactionColonies
{
    public class MilitaryJobHandler_Capture : MilitaryJobHandler
    {
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
            string tmpName = target.LabelCap;
            TechLevel tech = target.Faction.def.techLevel;
            Faction tempFactionLink = target.Faction;
            target.Destroy();

            // Mod-protected settlements (Empire's own WorldSettlementFC, or third-party
            // protected settlements) might Harmony-patch Destroy to no-op. Detect both common shapes:
            // a prefix-return-false leaves Destroyed=false; a postfix that re-adds the object
            // leaves Destroyed=true but the same instance still resolves at the tile.
            bool destructionSucceeded = target.Destroyed
                && Find.WorldObjects.SettlementAt(capturedTile) != target;
            if (!destructionSucceeded)
            {
                LogUtil.Warning($"Capture: target settlement at {capturedTile} survived Destroy(); " +
                                "likely destruction-protected. Falling back to raid rewards.");
                ApplyCaptureFallbackToRaid(faction, home, tempFactionLink, target);
                return;
            }

            // XP grant moved past the destruction check so the fallback path doesn't double up
            // (ApplyVictoryToTarget grants its own +5f).
            faction.AddExperienceToFactionLevel(5f);

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

        /* Failed-capture fallback: target's Destroy() was blocked by another mod, but the squad
         * won the battle. Send a "couldn't permanently neutralize, raided supplies instead" letter
         * and route through Raid's victory side effects (loot + optional prisoner + delivery). */
        private static void ApplyCaptureFallbackToRaid(FactionFC faction, WorldSettlementFC home,
            Faction enemyFaction, Settlement target)
        {
            Find.LetterStack.ReceiveLetter(
                "FCCaptureSettlement".Translate(),
                "FCCaptureBlockedFallbackToRaid".Translate(home.Name, target.LabelCap),
                LetterDefOf.NeutralEvent, new LookTargets(target));
            MilitaryJobHandler_Raid.ApplyVictoryToTarget(faction, home, enemyFaction, target);
        }
    }
}
