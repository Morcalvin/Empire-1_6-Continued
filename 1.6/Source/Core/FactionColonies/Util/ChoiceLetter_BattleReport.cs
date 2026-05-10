using System.Collections.Generic;
using RimWorld;
using Verse;
using FactionColonies.util;

namespace FactionColonies
{
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/
    /* ChoiceLetter_BattleReport                                                   */
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/

    /// <summary>
    /// Letter type for post-battle outcome letters. Carries a <see cref="reportId"/>
    /// pointing into <see cref="WorldComponent_Archive"/>; the "View battle report"
    /// option opens a <see cref="BattleProgressWindow"/> against the archived result.
    /// <para>If the report has been evicted by the archive cap by the time the user
    /// opens the letter, the option toasts a "report no longer available" message
    /// instead of crashing.</para>
    /// </summary>
    public class ChoiceLetter_BattleReport : ChoiceLetter
    {
        public int reportId;
        public BattleViewerSide playerSide = BattleViewerSide.Neither;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if (ArchivedOnly)
                {
                    yield return Option_Close;
                    yield break;
                }
                yield return MakeViewReportOption();
                if (lookTargets.IsValid())
                    yield return Option_JumpToLocation;
                yield return Option_Close;
            }
        }

        private DiaOption MakeViewReportOption()
        {
            DiaOption opt = new DiaOption("FCBattleReportButton".Translate());
            opt.action = delegate
            {
                WorldComponent_Archive archive = WorldComponent_Archive.Get();
                if (archive is object && archive.TryGetBattleReport(reportId, out BattleResult result))
                {
                    Find.WindowStack.Add(new BattleProgressWindow(result, playerSide));
                }
                else
                {
                    Messages.Message("FCBattleReportEvicted".Translate(),
                        MessageTypeDefOf.RejectInput, historical: false);
                }
                Find.LetterStack.RemoveLetter(this);
            };
            opt.resolveTree = true;
            return opt;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref reportId, "reportId", 0);
            Scribe_Values.Look(ref playerSide, "playerSide", BattleViewerSide.Neither);
        }
    }
}
