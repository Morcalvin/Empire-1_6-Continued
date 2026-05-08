using System;
using System.Collections.Generic;
using System.Text;
using RimWorld.Planet;
using Verse;

namespace FactionColonies
{
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/
    /* BattleProgress                                                              */
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/

    public enum BattleSubPhase
    {
        Preparing = 0,
        Engaged = 1,
        RollsInProgress = 2,
        Resolved = 3
    }

    /// <summary>
    /// Per-op live state for a multi-hour auto-resolved battle. Rolls happen one round
    /// per hour, and this object accumulates the per-round detail as the battle unfolds.
    /// </summary>
    public class BattleProgress : IExposable
    {
        /* Snapshots taken at battle start, displayed for the duration of the battle. */
        public double attackerInitialForce;
        public double defenderInitialForce; // already includes the defenderAdvantage multiplier
        public double attackerEfficiency;
        public double defenderEfficiency;
        public string attackerLabel;
        public string defenderLabel;
        public string attackerFactionName;
        public string defenderFactionName;
        public PlanetTile targetTile = PlanetTile.Invalid;

        /* Live state - mutates each round as rolls resolve. */
        public double attackerForceRemaining;
        public double defenderForceRemaining;
        public BattleSubPhase subPhase = BattleSubPhase.Preparing;
        public List<RoundEntry> rounds = new List<RoundEntry>();
        public BattleWinner winner = BattleWinner.Error; // set on completion

        public bool IsComplete => attackerForceRemaining <= 0 || defenderForceRemaining <= 0;

        /// <summary>
        /// Build a <see cref="BattleResult"/> from this progress object for downstream consumers
        /// (handler ApplyResult, lifecycle hooks, overwhelming-victory shortcut).
        /// </summary>
        public BattleResult ToBattleResult()
        {
            BattleResult r = new BattleResult
            {
                winner = winner,
                totalRounds = rounds.Count,
                attackerInitialForce = attackerInitialForce,
                defenderInitialForce = defenderInitialForce,
                attackerRemainingForce = attackerForceRemaining,
                defenderRemainingForce = defenderForceRemaining,
                roundLog = new List<bool>()
            };
            for (int i = 0; i < rounds.Count; i++)
                r.roundLog.Add(rounds[i].attackerWonRound);
            return r;
        }

        /// <summary>
        /// ASCII table of every roll, suitable for appending to a letter body. Alignment is
        /// space-padded so it renders consistently in the vanilla letter dialog.
        /// </summary>
        public string BuildRoundLogText()
        {
            if (rounds is null || rounds.Count == 0) return string.Empty;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Round  Attacker (raw -> final)   Defender (raw -> final)   Winner    Forces (A / D)");
            for (int i = 0; i < rounds.Count; i++)
            {
                RoundEntry r = rounds[i];
                string atk = $"{r.attackerRawRoll,2} -> {r.attackerScore,6:0.00}";
                string def = $"{r.defenderRawRoll,2} -> {r.defenderScore,6:0.00}";
                string winnerCol = r.attackerWonRound ? "Attacker" : "Defender";
                string forces = $"{r.attackerForceAfter,5:0.0} / {r.defenderForceAfter,5:0.0}";
                sb.AppendLine($"{r.roundNumber,5}  {atk,-25} {def,-25} {winnerCol,-9} {forces}");
            }
            return sb.ToString();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref attackerInitialForce, "attackerInitialForce");
            Scribe_Values.Look(ref defenderInitialForce, "defenderInitialForce");
            Scribe_Values.Look(ref attackerEfficiency, "attackerEfficiency");
            Scribe_Values.Look(ref defenderEfficiency, "defenderEfficiency");
            Scribe_Values.Look(ref attackerLabel, "attackerLabel");
            Scribe_Values.Look(ref defenderLabel, "defenderLabel");
            Scribe_Values.Look(ref attackerFactionName, "attackerFactionName");
            Scribe_Values.Look(ref defenderFactionName, "defenderFactionName");
            Scribe_Values.Look(ref targetTile, "targetTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref attackerForceRemaining, "attackerForceRemaining");
            Scribe_Values.Look(ref defenderForceRemaining, "defenderForceRemaining");
            Scribe_Values.Look(ref subPhase, "subPhase", BattleSubPhase.Preparing);
            Scribe_Collections.Look(ref rounds, "rounds", LookMode.Deep);
            Scribe_Values.Look(ref winner, "winner", BattleWinner.Error);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && rounds is null)
                rounds = new List<RoundEntry>();
        }
    }

    /// <summary>
    /// One round of an auto-resolved battle. Captures both the raw d20 roll (1..20) and the
    /// post-dampening final score for both sides, the round winner, and the resulting force
    /// remaining on each side. Pre- and post-efficiency values are both stored so the player
    /// can audit upset victories without inferring the dampening formula.
    /// </summary>
    public class RoundEntry : IExposable
    {
        public int roundNumber;
        public int attackerRawRoll;       // 1..20
        public int defenderRawRoll;       // 1..20
        public double attackerScore;       // raw * dampened efficiency
        public double defenderScore;
        public bool attackerWonRound;
        public double attackerForceAfter;  // force remaining on attacker after this round
        public double defenderForceAfter;

        public void ExposeData()
        {
            Scribe_Values.Look(ref roundNumber, "roundNumber");
            Scribe_Values.Look(ref attackerRawRoll, "attackerRawRoll");
            Scribe_Values.Look(ref defenderRawRoll, "defenderRawRoll");
            Scribe_Values.Look(ref attackerScore, "attackerScore");
            Scribe_Values.Look(ref defenderScore, "defenderScore");
            Scribe_Values.Look(ref attackerWonRound, "attackerWonRound");
            Scribe_Values.Look(ref attackerForceAfter, "attackerForceAfter");
            Scribe_Values.Look(ref defenderForceAfter, "defenderForceAfter");
        }
    }
}
