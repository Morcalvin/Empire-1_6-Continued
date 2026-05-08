using Verse;

namespace FactionColonies.util
{
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/
    /* MilitaryLetterUtil                                                          */
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/

    /// <summary>
    /// Helpers shared across military handlers for assembling outcome letters.
    /// </summary>
    internal static class MilitaryLetterUtil
    {
        /// <summary>
        /// Append the per-round table from <paramref name="op"/>'s <see cref="BattleProgress"/>
        /// to a letter <paramref name="body"/>. Returns the body unchanged when there is no
        /// progress object (manual battles, error fallbacks) or when no rounds were rolled
        /// (overwhelming victory shortcut, etc).
        /// </summary>
        public static string AppendBattleRoundLog(string body, MilitaryOperation op)
        {
            if (op?.battleProgress is null) return body;
            if (op.battleProgress.rounds is null || op.battleProgress.rounds.Count == 0) return body;
            return body + "\n\n" + "FCAutoResolveRoundLogHeader".Translate() + "\n"
                        + op.battleProgress.BuildRoundLogText();
        }
    }
}
