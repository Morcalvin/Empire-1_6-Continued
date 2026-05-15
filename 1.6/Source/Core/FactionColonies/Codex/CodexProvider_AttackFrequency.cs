using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Dynamic provider that shows attack frequency scaling
    /// based on current settlement count.
    /// </summary>
    public class CodexProvider_AttackFrequency : ICodexDynamicProvider
    {
        public string GetDynamicContent(FactionFC faction)
        {
            int count = faction.settlements.Count;
            IntRange range = FCSettings.minMaxDaysTillMilitaryAction;

            string result = "FCCodexFreqParams".Translate() + "\n\n";
            result += "FCCodexFreqRange".Translate(range.min, range.max) + "\n";
            result += "FCCodexFreqCount".Translate(count) + "\n\n";

            // Show frequency curve points
            result += "FCCodexFreqCurve".Translate() + "\n";
            int[] points = { 1, 3, 5, 10, 15 };
            foreach (int p in points)
            {
                string marker = (p == count) ? " <--" : "";
                double freq;
                switch (p)
                {
                    case 1: freq = 1.0; break;
                    case 3: freq = 1.2; break;
                    case 5: freq = 1.4; break;
                    case 10: freq = 1.8; break;
                    case 15: freq = 2.0; break;
                    default: freq = 1.0; break;
                }
                result += "  " + "FCCodexFreqPoint".Translate(p, freq) + marker + "\n";
            }

            return result;
        }
    }
}
