using RimWorld;
using Verse;

namespace FactionColonies.util
{
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/
    /* MilitaryLetterUtil                                                          */
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/

    /// <summary>
    /// Helpers shared across military handlers for delivering outcome letters.
    /// </summary>
    internal static class MilitaryLetterUtil
    {
        /// <summary>
        /// Send a post-battle outcome letter carrying a "View battle report" button that
        /// opens the archived <see cref="BattleResult"/> identified by <paramref name="reportId"/>.
        /// Mirrors <see cref="LetterStack.ReceiveLetter(TaggedString, TaggedString, LetterDef, LookTargets, Faction, Quest, System.Collections.Generic.List{ThingDef}, string, int, bool, bool)"/>
        /// but uses the custom <see cref="ChoiceLetter_BattleReport"/> letter type so the
        /// dialog shows the button.
        /// </summary>
        /// <param name="op">Originating op (used to derive which side the player is on for tinting).
        /// May be null when the letter is being sent post-completion with no live op reference.</param>
        public static void SendBattleReportLetter(string label, string text, LetterDef def,
            LookTargets lookTargets, int reportId, MilitaryOperation op = null)
        {
            ChoiceLetter_BattleReport letter = (ChoiceLetter_BattleReport)
                LetterMaker.MakeLetter(label, text, def, lookTargets);
            letter.reportId = reportId;
            letter.playerSide = MilitaryUtil.ResolvePlayerSide(op);
            Find.LetterStack.ReceiveLetter(letter);
        }
    }
}
