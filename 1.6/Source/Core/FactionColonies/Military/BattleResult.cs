using System.Collections.Generic;
using Verse;

namespace FactionColonies
{
    public enum BattleWinner
    {
        Attacker = 0,
        Defender = 1,
        Error = -1
    }

    public class BattleResult : IExposable
    {
        public BattleWinner winner;
        public int totalRounds;
        public double attackerInitialForce;
        public double defenderInitialForce;
        public double attackerRemainingForce;
        public double defenderRemainingForce;
        public List<bool> roundLog = new List<bool>(); // true = attacker won round, false = defender won

        public bool AttackerVictory => winner == BattleWinner.Attacker;
        public bool DefenderVictory => winner == BattleWinner.Defender;

        public void ExposeData()
        {
            Scribe_Values.Look(ref winner, "winner");
            Scribe_Values.Look(ref totalRounds, "totalRounds");
            Scribe_Values.Look(ref attackerInitialForce, "attackerInitialForce");
            Scribe_Values.Look(ref defenderInitialForce, "defenderInitialForce");
            Scribe_Values.Look(ref attackerRemainingForce, "attackerRemainingForce");
            Scribe_Values.Look(ref defenderRemainingForce, "defenderRemainingForce");
            Scribe_Collections.Look(ref roundLog, "roundLog", LookMode.Value);
        }
    }
}
