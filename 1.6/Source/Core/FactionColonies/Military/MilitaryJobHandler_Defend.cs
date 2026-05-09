using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using FactionColonies.util;

namespace FactionColonies
{
    /// <summary>
    /// Handler for defensive ops created by <see cref="MilitaryOperationManager.CreateDefensiveOp"/>.
    /// Promotes defensive ops to first-class handler-driven ops so the unified phase machine in
    /// <see cref="MilitaryOperation.OnEventFired"/> covers them — no special-case branch needed.
    /// <para><see cref="OnAutoResolve"/> runs the simulator (used for external <see cref="IRaidTarget"/>
    /// objects with no map). <see cref="OnManualResolve"/> hands off to
    /// <see cref="BattlefieldContext.StartDefense"/>, which decides auto-vs-manual internally based
    /// on <c>FCSettings.battleMode</c> and the settlement's <c>supportsManualBattle</c>.
    /// <see cref="ApplyResult"/> applies the settlement-side outcome (loyalty / happiness / building
    /// destruction). Because it fires per-op, multi-op battle resolutions (multiple concurrent
    /// attackers on one tile) apply the full settlement-side penalty set once per op — each op
    /// is a distinct logical attack with its own consequences.</para>
    /// </summary>
    public class MilitaryJobHandler_Defend : MilitaryJobHandler
    {
        public override void OnOpCreated(MilitaryOperation op)
        {
            // Warning event + "settlement in danger" letter are scheduled by CreateDefensiveOp.
            // Nothing for the handler to do at op creation.
        }

        public override bool ResolvesManually(MilitaryOperation op)
        {
            // Settlement targets go through BattlefieldContext.StartDefense, which decides auto vs
            // manual internally. External IRaidTarget objects have no map, so always auto-resolve.
            return op?.targetObject is WorldSettlementFC s && s.MilitaryComp is object;
        }

        public override void OnManualResolve(MilitaryOperation op)
        {
            if (op is null) return;

            BattlefieldContext bf = FactionCache.MilitaryManager?.GetOrCreateBattlefield(op.targetTile);
            if (bf is null)
            {
                LogUtil.Error($"MilitaryJobHandler_Defend.OnManualResolve: no battlefield context for op id={op.id} at tile {op.targetTile}; falling back to auto-resolve.");
                op.CompleteBattle(OnAutoResolve(op));
                return;
            }

            // StartDefense's auto sub-path will eventually call EndBattle → comp.EndBattle →
            // op.CompleteBattle. Manual sub-path drives a real battle that resolves the same way.
            // Either way, op.CompleteBattle gets called.
            bf.StartDefense(op);
        }

        public override BattleResult OnAutoResolve(MilitaryOperation op)
        {
            if (op?.aggressor?.force is null || op.defender?.force is null)
            {
                LogUtil.Warning($"MilitaryJobHandler_Defend.OnAutoResolve: missing force(s) on op id={op?.id} " +
                                $"(aggressor={(op?.aggressor?.force is object)}, defender={(op?.defender?.force is object)}).");
                return new BattleResult { winner = BattleWinner.Error };
            }
            return SimulateBattleFc.FightBattle(op.aggressor.force, op.defender.force);
        }

        public override void ApplyResult(MilitaryOperation op, BattleResult result)
        {
            // Per-op settlement-side effects. Concurrent multi-op battles get the penalty set
            // applied once per op — each op is a logically distinct attack on the settlement.
            if (op is null || result is null) return;
            if (result.winner == BattleWinner.Error) return;

            if (!(op.targetObject is WorldSettlementFC target)) return;
            // Guard: don't apply Empire-style settlement effects to a settlement that isn't
            // tracked by Empire (no military comp).
            if (target.MilitaryComp is null) return;

            try
            {
                if (result.DefenderVictory) DefensiveBattleEffects.ApplyWin(target, op);
                else DefensiveBattleEffects.ApplyLoss(target, op);
            }
            catch (Exception e)
            {
                LogUtil.Error($"MilitaryJobHandler_Defend.ApplyResult: settlement-effect application threw on {target.Name}: {e}");
            }
        }
    }

    /// <summary>
    /// Settlement-side effects of a defensive battle outcome (loyalty / happiness / prosperity
    /// changes, building destruction on loss, "settlement leveled down" rolls, result letters).
    /// Invoked from <see cref="MilitaryJobHandler_Defend.ApplyResult"/> so the effects run inside
    /// <see cref="MilitaryOperation.CompleteBattle"/> before lifecycle listeners observe the
    /// resolved op.
    /// </summary>
    internal static class DefensiveBattleEffects
    {
        /// <summary>Set true by <see cref="ApplyWin"/> / <see cref="ApplyLoss"/> after a result
        /// letter is sent. <see cref="WorldObjectComp_SettlementMilitary.EndBattle"/> resets this
        /// at entry and reads it after the per-op dispatch loop to decide whether a fallback
        /// letter is needed.</summary>
        internal static bool letterEmitted;

        /// <summary>Looks up the per-tile <see cref="BattlefieldContext"/> directly from the manager
        /// so this helper doesn't depend on the comp's private <c>Battlefield</c> backdoor.</summary>
        private static BattlefieldContext BattlefieldFor(WorldSettlementFC settlement)
            => FactionCache.MilitaryManager?.GetBattlefield(settlement?.Tile ?? PlanetTile.Invalid);

        public static void ApplyWin(WorldSettlementFC settlement, MilitaryOperation op = null)
        {
            FactionFC faction = FactionCache.FactionComp;
            if (faction is null) return;

            faction.AddExperienceToFactionLevel(5f);
            // Threat adaptation runs in MilitaryOperation.CompleteBattle for every Empire battle.

            string text = "FCDefenseSuccessfulFull".Translate(settlement.Name);
            string deliveryMsg = BattlefieldFor(settlement)?.pendingDeliveryMessage;
            if (!string.IsNullOrEmpty(deliveryMsg))
                text += "\n\n" + deliveryMsg;
            if (settlement.Map != null)
                text += "\n\n" + "FCDefenseBattleOverLeaveMap".Translate();
            text = MilitaryLetterUtil.AppendBattleRoundLog(text, op);

            Find.LetterStack.ReceiveLetter("FCDefenseSuccessful".Translate(),
                text,
                LetterDefOf.PositiveEvent, new LookTargets(settlement));
            letterEmitted = true;
        }

        public static void ApplyLoss(WorldSettlementFC settlement, MilitaryOperation op = null)
        {
            FactionFC faction = FactionCache.FactionComp;
            if (faction is null) return;

            double happinessLostMultiplier = settlement.GetStatValue(FCStatDefOf.happinessLostMultiplier);
            double loyaltyLostMultiplier = settlement.GetStatValue(FCStatDefOf.loyaltyLostMultiplier);

            var (prosperityLoss, happinessLoss, loyaltyLoss) = SettlementFormulas.CalculateBattleLossPenalties(happinessLostMultiplier, loyaltyLostMultiplier);
            prosperityLoss *= faction.GetStatValue(FCStatDefOf.battleProsperityLossMultiplier);
            happinessLoss *= faction.GetStatValue(FCStatDefOf.battleHappinessLossMultiplier);
            loyaltyLoss *= faction.GetStatValue(FCStatDefOf.battleLoyaltyLossMultiplier);
            bool canDestroyBuildings = !faction.AnyPolicyPreventsBuildingDestruction();

            // buildingDestructionChance stat scales the survival threshold:
            //  stat=1.0 -> threshold 7 (36% destruction, default)
            //  stat<1.0 -> higher threshold (less destruction)
            //  stat>1.0 -> lower threshold (more destruction)
            double destructionStat = faction.GetStatValue(FCStatDefOf.buildingDestructionChance);
            int deconstructChance = Math.Max(0, Math.Min(11, (int)Math.Round(11 - 4 * destructionStat)));

            settlement.prosperity -= prosperityLoss;
            settlement.happiness -= happinessLoss;
            settlement.loyalty -= loyaltyLoss;

            string str = "FCDefenseFailureFull".Translate(settlement.Name);
            str += "\n\n" + "FCDefenseFailurePenaltiesHeader".Translate();

            int displayProsperity = (int)Math.Round(prosperityLoss);
            int displayHappiness = (int)Math.Round(happinessLoss);
            int displayLoyalty = (int)Math.Round(loyaltyLoss);

            if (displayProsperity > 0) str += "\n  - " + "FCDefenseFailureProsperityLoss".Translate(displayProsperity);
            if (displayHappiness > 0) str += "\n  - " + "FCDefenseFailureHappinessLoss".Translate(displayHappiness);
            if (displayLoyalty > 0) str += "\n  - " + "FCDefenseFailureLoyaltyLoss".Translate(displayLoyalty);

            if (canDestroyBuildings && settlement?.BuildingsComp != null)
            {
                List<int> candidates = new List<int>();
                for (int k = 0; k < 4; k++)
                {
                    int deconstructRoll = new IntRange(0, 10).RandomInRange;
                    if (deconstructRoll < deconstructChance
                        || !settlement.BuildingsComp.BuildingSlotIsBuilding(k)) continue;
                    candidates.Add(k);
                }

                // Sort so buildings that depend on other buildings are demolished first.
                candidates.Sort((a, b) =>
                {
                    BuildingFCDef defA = settlement.BuildingsComp.GetBuildingInSlot(a);
                    BuildingFCDef defB = settlement.BuildingsComp.GetBuildingInSlot(b);
                    bool aRequiresB = FactionCache.SatisfiesAnyRequirement(defB, defA.requiredBuildings);
                    bool bRequiresA = FactionCache.SatisfiesAnyRequirement(defA, defB.requiredBuildings);
                    if (aRequiresB) return -1;
                    if (bRequiresA) return 1;
                    int aReqCount = defA.requiredBuildings?.Count ?? 0;
                    int bReqCount = defB.requiredBuildings?.Count ?? 0;
                    return bReqCount.CompareTo(aReqCount);
                });

                foreach (int k in candidates)
                {
                    str += "\n  - " + "FCBuildingDestroyedInRaid".Translate(settlement.BuildingsComp.BuildingLabel(k));
                    settlement.DeconstructBuilding(k);
                }
            }

            if (!canDestroyBuildings)
                str += "\n  - " + "FCDefenseFailureBuildingsProtected".Translate();

            // Level remover roll — uses the same destruction stat scaling.
            if (settlement?.settlementLevel > 1 && canDestroyBuildings)
            {
                int num = new IntRange(0, 10).RandomInRange;
                if (num >= deconstructChance)
                {
                    str += "\n  - " + "FCSettlementDeleveledRaid".Translate();
                    settlement.DelevelSettlement();
                }
            }

            string deliveryMsg = BattlefieldFor(settlement)?.pendingDeliveryMessage;
            if (!string.IsNullOrEmpty(deliveryMsg))
                str += "\n\n" + deliveryMsg;
            if (settlement.Map != null)
                str += "\n\n" + "FCDefenseBattleOverLeaveMap".Translate();
            str = MilitaryLetterUtil.AppendBattleRoundLog(str, op);

            Find.LetterStack.ReceiveLetter("FCDefenseFailure".Translate(), str, LetterDefOf.Death,
                new LookTargets(settlement));
            letterEmitted = true;
        }
    }
}
