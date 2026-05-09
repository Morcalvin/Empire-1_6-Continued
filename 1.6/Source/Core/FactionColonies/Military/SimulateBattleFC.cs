using FactionColonies.util;
using System;
using System.Collections.Generic;

namespace FactionColonies
{
    public class SimulateBattleFc
    {
        public static BattleResult FightBattle(MilitaryForce MFA, MilitaryForce MFB, IRandProvider rand = null)
        {
            var result = new BattleResult();
            try
            {
                // Battle modifiers are applied by op.BeginEngagement before this point in the
                // op-driven flow; callers that invoke FightBattle directly (tests, win-chance
                // probes) get the unmodified baseline.

                // Defender advantage: defenders are inherently harder to dislodge
                MFB.forceRemaining = Math.Round(MFB.forceRemaining * FCSettings.defenderAdvantage);

                result.attackerInitialForce = MFA.forceRemaining;
                result.defenderInitialForce = MFB.forceRemaining;
                result.roundLog = new List<bool>();

                LogUtil.Message("SimulateBattleFc.FightBattle: Starting battle");
                while (MFA.forceRemaining > 0 && MFB.forceRemaining > 0)
                {
                    double prevDefender = MFB.forceRemaining;
                    FightRound(MFA, MFB, rand);
                    // If defender lost force this round, attacker won the round
                    result.roundLog.Add(MFB.forceRemaining < prevDefender);
                }

                result.attackerRemainingForce = MFA.forceRemaining;
                result.defenderRemainingForce = MFB.forceRemaining;
                result.totalRounds = result.roundLog.Count;

                if (MFA.forceRemaining <= 0)
                {
                    LogUtil.Message("SimulateBattleFc.FightBattle: Defending Force has won.");
                    result.winner = BattleWinner.Defender;
                }
                else
                {
                    LogUtil.Message("SimulateBattleFc.FightBattle: Attacking Force has won.");
                    result.winner = BattleWinner.Attacker;
                }
            }
            catch (Exception e)
            {
                LogUtil.Error($"An exception occurred while resolving combat in Empire {Environment.NewLine}[{e}]");
                result.winner = BattleWinner.Error;
            }

            return result;
        }

        /// <summary>
        /// Per-round outcome detail. Returned by <see cref="SimulateRound"/> so callers can
        /// record both the raw d20 roll (1..20) and the dampening-applied final score for
        /// both sides. The round winner is determined by score comparison; force decrement
        /// is the caller's responsibility (see <see cref="FightRound"/> or
        /// <c>MilitaryOperation.AdvanceBattleProgress</c>).
        /// </summary>
        public struct RoundOutcome
        {
            public int attackerRawRoll;
            public int defenderRawRoll;
            public double attackerDampenedEfficiency;
            public double defenderDampenedEfficiency;
            public double attackerScore;
            public double defenderScore;
            public bool attackerWonRound;
        }

        /// <summary>
        /// Roll one round without mutating either force. True d20 (1..20 inclusive) rolled for
        /// each side, multiplied by their dampened efficiency. The higher score wins the round.
        /// On ties the defender wins.
        /// </summary>
        public static RoundOutcome SimulateRound(MilitaryForce MFA, MilitaryForce MFB, IRandProvider rand = null)
        {
            rand = rand ?? new RimWorldRandProvider();
            // True d20: 1..20 inclusive. rand.Range(int, int) follows Verse.Rand semantics
            // (max-exclusive), so pass (1, 21).
            int rawA = rand.Range(1, 21);
            int rawB = rand.Range(1, 21);
            double effA = DampenEfficiency(MFA.militaryEfficiency);
            double effB = DampenEfficiency(MFB.militaryEfficiency);
            double scoreA = rawA * effA;
            double scoreB = rawB * effB;
            return new RoundOutcome
            {
                attackerRawRoll = rawA,
                defenderRawRoll = rawB,
                attackerDampenedEfficiency = effA,
                defenderDampenedEfficiency = effB,
                attackerScore = scoreA,
                defenderScore = scoreB,
                attackerWonRound = scoreA > scoreB
            };
        }

        public static void FightRound(MilitaryForce MFA, MilitaryForce MFB, IRandProvider rand = null)
        {
            RoundOutcome outcome = SimulateRound(MFA, MFB, rand);
            if (outcome.attackerWonRound)
            {
                MFB.forceRemaining -= 1;
            }
            else
            {
                MFA.forceRemaining -= 1;
            }
        }

        private static double DampenEfficiency(double efficiency)
        {
            return 1.0 + (efficiency - 1.0) * FCSettings.efficiencyDamping;
        }

        /// <summary>
        /// Calculates the probability that the defender wins using the binomial tail sum for a
        /// Bernoulli race (attrition model). The attacker needs <c>defenderHP</c> round-wins to deplete
        /// the defender; the defender needs <c>attackerHP</c> round-wins to deplete the attacker.
        /// <c>P(attacker wins) = P(X &gt;= defenderHP)</c> where <c>X ~ Binomial(attackerHP+defenderHP-1, p)</c>.
        /// Does not account for <see cref="BattleModifierRegistry"/> modifications.
        /// </summary>
        /// <returns>Defender win probability in [0, 1].</returns>
        /// <summary>Mirror of <see cref="CalculateDefenderWinChance"/> from the attacker's side.
        /// Returns the probability that the attacker depletes the defender's HP before being
        /// depleted itself. Stable across calls (deterministic given the two force snapshots).</summary>
        public static double CalculateAttackerWinChance(MilitaryForce attacker, MilitaryForce defender)
        {
            if (attacker is null || defender is null) return 0.0;
            return 1.0 - CalculateDefenderWinChance(attacker, defender);
        }

        public static double CalculateDefenderWinChance(MilitaryForce attacker, MilitaryForce defender)
        {
            if (attacker.forceRemaining <= 0) return 1.0;
            if (defender.forceRemaining <= 0) return 0.0;

            int attackerHP = (int)Math.Max(1, Math.Round(attacker.forceRemaining));
            int defenderHP = (int)Math.Max(1, Math.Round(defender.forceRemaining * FCSettings.defenderAdvantage));

            double attackerEfficiency = DampenEfficiency(attacker.militaryEfficiency);
            double defenderEfficiency = DampenEfficiency(defender.militaryEfficiency);

            // P(attacker wins a single round) for Uniform(0, 20*attackerEfficiency) vs Uniform(0, 20*defenderEfficiency)
            double p;
            if (attackerEfficiency <= defenderEfficiency)
                p = attackerEfficiency / (2.0 * defenderEfficiency);
            else
                p = 1.0 - defenderEfficiency / (2.0 * attackerEfficiency);

            if (p <= 0.0) return 1.0;
            if (p >= 1.0) return 0.0;

            double q = 1.0 - p;
            // n = the maximum possible number of rounds
            int n = attackerHP + defenderHP - 1;

            // Sum P(X >= defenderHP) where X ~ Binomial(n, p), iterating from k=n down to k=defenderHP.
            // term(n) = p^n, then term(k) = term(k+1) * (k+1)/(n-k) * q/p
            double term = Math.Pow(p, n);
            double sum = term;
            for (int k = n - 1; k >= defenderHP; k--)
            {
                term *= (k + 1.0) / (n - k) * (q / p);
                sum += term;
            }

            return Math.Max(0.0, Math.Min(1.0, 1.0 - sum));
        }
    }
}