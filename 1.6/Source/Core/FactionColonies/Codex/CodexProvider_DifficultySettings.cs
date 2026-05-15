using FactionColonies.util;
using RimWorld;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Dynamic provider that shows the full difficulty parameter table
    /// with the current difficulty highlighted.
    /// </summary>
    public class CodexProvider_DifficultySettings : ICodexDynamicProvider
    {
        public string GetDynamicContent(FactionFC faction)
        {
            EmpireDifficultyLevel current = FCSettings.difficultyLevel;

            string result = "FCCodexDiffCurrent".Translate(current.ToString()) + "\n\n";
            result += "FCCodexDiffHeader".Translate() + "\n";

            result += FormatRow(EmpireDifficultyLevel.Peaceful, 200, 2, 50, 75, current);
            result += FormatRow(EmpireDifficultyLevel.CommunityBuilder, 150, 5, 25, 100, current);
            result += FormatRow(EmpireDifficultyLevel.AdventureStory, 100, 5, 25, 100, current);
            result += FormatRow(EmpireDifficultyLevel.StriveToSurvive, 100, 10, 20, 125, current);
            result += FormatRow(EmpireDifficultyLevel.BloodAndDust, 80, 15, 15, 125, current);
            result += FormatRow(EmpireDifficultyLevel.LosingIsFun, 70, 30, 10, 150, current);

            if (current == EmpireDifficultyLevel.Custom)
            {
                result += "\n" + "FCCodexDiffCustomLabel".Translate() + "\n";
                result += "FCCodexDiffCustomRow".Translate("Silver/Res", FCSettings.silverPerResource) + "\n";
                result += "FCCodexDiffCustomRow".Translate("Tax Days", FCSettings.timeBetweenTaxes / GenDate.TicksPerDay) + "\n";
                result += "FCCodexDiffCustomRow".Translate("Tithe Mod", FCSettings.productionTitheMod) + "\n";
                result += "FCCodexDiffCustomRow".Translate("Worker Cost", FCSettings.workerCost);
            }

            return result;
        }

        private static string FormatRow(EmpireDifficultyLevel level, int silver, int taxDays, int tithe, int workerCost, EmpireDifficultyLevel current)
        {
            string marker = (level == current) ? " <--" : "";
            return "  " + "FCCodexDiffRow".Translate(
                level.ToString(),
                silver,
                taxDays,
                tithe,
                workerCost) + marker + "\n";
        }
    }
}
