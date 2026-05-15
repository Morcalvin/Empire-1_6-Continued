using RimWorld;
using System;
using System.Linq;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Computes the Empire Threat Level (ETL), handicap cap, frequency scaling,
    /// and weighted enemy faction selection for threat scaling.
    /// </summary>
    public static class ThreatScalingUtil
    {
        /* Income curve: maps faction income to a 0–0.9 factor */
        private static readonly SimpleCurve IncomeCurve = new SimpleCurve
        {
            new CurvePoint(0f, 0f),
            new CurvePoint(500f, 0.1f),
            new CurvePoint(2000f, 0.3f),
            new CurvePoint(5000f, 0.5f),
            new CurvePoint(10000f, 0.7f),
            new CurvePoint(20000f, 0.9f)
        };

        /* Count curve: maps settlement count to a 0–0.6 factor */
        private static readonly SimpleCurve CountCurve = new SimpleCurve
        {
            new CurvePoint(1f, 0f),
            new CurvePoint(3f, 0.2f),
            new CurvePoint(5f, 0.3f),
            new CurvePoint(10f, 0.5f),
            new CurvePoint(15f, 0.6f)
        };

        /* Time curve: maps seasons elapsed to a handicap cap */
        private static readonly SimpleCurve TimeCurve = new SimpleCurve
        {
            new CurvePoint(0f, 2f),
            new CurvePoint(1f, 3f),
            new CurvePoint(2f, 4.5f),
            new CurvePoint(4f, 8f),
            new CurvePoint(8f, 15f)
        };

        /* Frequency curve: maps settlement count to a frequency multiplier */
        private static readonly SimpleCurve FrequencyCurve = new SimpleCurve
        {
            new CurvePoint(1f, 1.0f),
            new CurvePoint(3f, 1.2f),
            new CurvePoint(5f, 1.4f),
            new CurvePoint(10f, 1.8f),
            new CurvePoint(15f, 2.0f)
        };

        /// <summary>
        /// Computes the Empire Threat Level (ETL) — a composite multiplier
        /// based on average settlement level, max settlement level, income,
        /// settlement count, FCStatDef modifiers, and registry contributions.
        /// Result is clamped between 1.0 and <see cref="FCSettings.maxThreatMultiplier"/>.
        /// </summary>
        public static double ComputeEmpireThreatLevel(FactionFC faction)
        {
            if (!faction.settlements.Any()) return 1.0;
            return Math.Max(1.0, Math.Min(FCSettings.maxThreatMultiplier, ComputeRawEmpireScale(faction)));
        }

        /// <summary>
        /// Same composite formula as <see cref="ComputeEmpireThreatLevel"/>, but without the
        /// settings cap. Floored at 1.0. Used for cost scaling that should keep growing past
        /// the threat cap (e.g., policy re-pick cost).
        /// </summary>
        public static double ComputeEmpireScaleUncapped(FactionFC faction)
        {
            if (!faction.settlements.Any()) return 1.0;
            return Math.Max(1.0, ComputeRawEmpireScale(faction));
        }

        private static double ComputeRawEmpireScale(FactionFC faction)
        {
            double avgLevel = faction.settlements.Average(s => (double)s.settlementLevel);
            double avgFactor = (avgLevel - 1.0) * 0.2; // lvl 1->0, lvl 5->0.8, lvl 10->1.8

            int maxLevel = faction.settlements.Max(s => s.settlementLevel);
            double maxFactor = (maxLevel - 1.0) * 0.1; // lvl 1->0, lvl 5->0.4, lvl 10->0.9

            double incomeFactor = IncomeCurve.Evaluate((float)faction.income);

            double countFactor = CountCurve.Evaluate(faction.settlements.Count);

            double rawScore = (avgFactor * 0.35) + (maxFactor * 0.15)
                            + (incomeFactor * 0.30) + (countFactor * 0.20);

            // Stat-driven modifiers (policies/buildings)
            double statBase = faction.GetStatValue(FCStatDefOf.threatScalingBase);
            double statMult = faction.GetStatValue(FCStatDefOf.threatScalingMultiplier);

            // Registry contributions (submods)
            double registryBase = ThreatScalingRegistry.InvokeGetAdditiveContributions(faction);
            double registryMult = ThreatScalingRegistry.InvokeGetMultiplierContributions(faction);

            return (1.0 + rawScore + statBase + registryBase) * statMult * registryMult;
        }

        /// <summary>
        /// Computes the handicap cap that replaces the old pure time-based cap.
        /// During the first year it's primarily time-based; after that it transitions
        /// to ETL-based scaling.
        /// </summary>
        public static double ComputeHandicapCap(FactionFC faction)
        {
            double seasonsElapsed = (double)(Find.TickManager.TicksGame - faction.timeStart)
                                  / GenDate.TicksPerSeason;
            double timeCap = TimeCurve.Evaluate((float)seasonsElapsed);

            double etlCap = ComputeEmpireThreatLevel(faction) * 5.0;

            double graceFade = Math.Min(1.0, seasonsElapsed / 4.0);
            return Math.Max(2.0, timeCap * (1.0 - graceFade) + etlCap * graceFade);
        }

        /// <summary>
        /// Picks a random enemy faction, weighted by tech level strength at higher ETL.
        /// At ETL 1.0, all factions have roughly equal weight.
        /// At higher ETL, advanced factions are heavily favored.
        /// </summary>
        public static Faction PickWeightedEnemyFaction(double etl)
        {
            var enemies = Find.FactionManager.AllFactionsVisible.Where(f => f.HostileTo(Faction.OfPlayer) && !f.defeated && !f.Hidden);
            if (!enemies.Any()) return null;

            return enemies.RandomElementByWeight(f =>
            {
                double factionStrength;
                MilitaryUtil.GetTechLevelBaseline(
                    f.def.techLevel, out factionStrength, out _);
                // At ETL 1.0: all factions equal weight (~1.0)
                // At ETL 2.0: Spacer(6) weight ~2.5, Neolithic(2) weight ~0.7
                double relevance = 1.0 + (factionStrength * (etl - 1.0) * 0.3);
                return (float)Math.Max(0.1, relevance);
            });
        }

        /// <summary>
        /// Returns a frequency multiplier for attack intervals based on settlement count.
        /// The result is used to bias the random interval toward the lower bound,
        /// but the player's min/max settings are never violated.
        /// </summary>
        public static int ComputeScaledAttackInterval(FactionFC faction)
        {
            double freqMult = FrequencyCurve.Evaluate(faction.settlements.Count);

            IntRange range = FCSettings.minMaxDaysTillMilitaryAction;
            int baseDays = range.RandomInRange;
            int adjustedDays = (int)Math.Round(baseDays / freqMult);
            adjustedDays = Math.Max(range.min, Math.Min(range.max, adjustedDays));

            return adjustedDays;
        }
    }
}
