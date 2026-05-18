using System;
using System.Collections.Generic;
using System.Linq;

namespace FactionColonies
{
    public static class TitheIncomeTests
    {
        // ============================
        // Helpers
        // ============================

        private static WorldSettlementFC GetFirstSettlement()
        {
            var settlements = FindFC.FactionComp?.settlements;
            if (settlements == null || settlements.Count == 0)
                return null;
            return settlements[0];
        }

        private static ResourceFC GetFirstNonPoolResource(WorldSettlementFC settlement)
        {
            return settlement.Resources.FirstOrDefault(r => r.canTithe);
        }

        private static void WithFactionModifier(FCStatDef stat, double value, Action action)
        {
            FactionFC faction = FindFC.FactionComp;
            var settlement = faction.settlements[0];
            var mods = new List<FCStatModifier> { new FCStatModifier { stat = stat, value = value } };
            settlement.AddStatModifiers(mods, "titheTest");
            try
            {
                action();
            }
            finally
            {
                settlement.RemoveStatModifiersBySource("titheTest");
            }
        }

        // ============================
        // Tests
        // ============================

        [EmpireTest("TitheIncome")]
        public static void TitheModifierPerWorker_MatchesStatPlusSetting()
        {
            var settlement = GetFirstSettlement();
            if (settlement == null) TestAssert.Skip("No settlement");

            ResourceFC resource = GetFirstNonPoolResource(settlement);
            if (resource == null) TestAssert.Skip("No non-pool resource");

            double expected = settlement.GetStatValue(FCStatDefOf.taxBaseRandomModifier)
                            + FCSettings.productionTitheMod;
            double actual = resource.GetTitheModifierPerWorker();

            TestAssert.AreEqual(expected, actual,
                message: "GetTitheModifierPerWorker should equal taxBaseRandomModifier stat + productionTitheMod setting");
        }

        [EmpireTest("TitheIncome")]
        public static void TitheIncome_ZeroWorkers_EqualsBaseTimesMultiplier()
        {
            var settlement = GetFirstSettlement();
            if (settlement == null) TestAssert.Skip("No settlement");

            ResourceFC resource = GetFirstNonPoolResource(settlement);
            if (resource == null) TestAssert.Skip("No non-pool resource");

            int savedWorkers = resource.assignedWorkers;
            try
            {
                resource.assignedWorkers = 0;
                resource.SetDirtyCache();

                double multForTotal = FindFC.FactionComp.GetStatValue(FCStatDefOf.titheValueMultiplier, settlement);
                double expected = resource.taxableProductionMarketValue * multForTotal + resource.externalTitheBudget;
                double actual = resource.GetTitheIncome();

                TestAssert.AreEqual(expected, actual,
                    message: "With 0 workers, tithe income should equal taxableProductionMarketValue * titheValueMultiplier + externalTitheBudget");
            }
            finally
            {
                resource.assignedWorkers = savedWorkers;
                resource.SetDirtyCache();
            }
        }

        [EmpireTest("TitheIncome")]
        public static void TitheIncome_FormulaConsistency()
        {
            var settlement = GetFirstSettlement();
            if (settlement == null) TestAssert.Skip("No settlement");

            ResourceFC resource = GetFirstNonPoolResource(settlement);
            if (resource == null) TestAssert.Skip("No non-pool resource");

            // Manually compute using the same formula that GetTitheIncome should use
            double workerMod = resource.GetTitheModifierPerWorker() * resource.assignedWorkers;
            double multForTotal = FindFC.FactionComp.GetStatValue(FCStatDefOf.titheValueMultiplier, settlement);
            double expected = (resource.taxableProductionMarketValue + workerMod) * multForTotal + resource.externalTitheBudget;
            double actual = resource.GetTitheIncome();

            TestAssert.AreEqual(expected, actual,
                message: "GetTitheIncome should equal (taxableMarketValue + workerMod) * titheValueMultiplier + externalTitheBudget");
        }

        [EmpireTest("TitheIncome")]
        public static void TitheIncome_MultForTotal_ChangesWithStat()
        {
            var settlement = GetFirstSettlement();
            if (settlement == null) TestAssert.Skip("No settlement");

            ResourceFC resource = GetFirstNonPoolResource(settlement);
            if (resource == null) TestAssert.Skip("No non-pool resource");
            if (resource.assignedWorkers == 0) TestAssert.Skip("Resource has 0 workers");

            double incomeBefore = resource.GetTitheIncome();

            // titheValueMultiplier is multiplicative, so adding 1.5 means multiplying by 1.5
            WithFactionModifier(FCStatDefOf.titheValueMultiplier, 1.5, () =>
            {
                resource.SetDirtyCache();
                double incomeAfter = resource.GetTitheIncome();

                if (incomeBefore != 0)
                {
                    TestAssert.GreaterThan(incomeAfter, incomeBefore,
                        $"Tithe income should increase with higher titheValueMultiplier (before={incomeBefore:F2}, after={incomeAfter:F2})");
                }
            });

            resource.SetDirtyCache();
            double incomeRestored = resource.GetTitheIncome();
            TestAssert.AreEqual(incomeBefore, incomeRestored,
                message: "Tithe income should restore after removing modifier");
        }

        [EmpireTest("TitheIncome")]
        public static void TitheIncome_AllResources_NonNegative()
        {
            var faction = FindFC.FactionComp;
            if (faction == null || faction.settlements.Count == 0)
                TestAssert.Skip("No faction/settlements");

            foreach (var settlement in faction.settlements)
            {
                foreach (var resource in settlement.Resources)
                {
                    double tithe = resource.GetTitheIncome();
                    TestAssert.IsTrue(tithe >= 0,
                        $"{settlement.Name}/{resource.def?.defName}: tithe income should be >= 0, got {tithe:F2}");
                    TestAssert.IsFalse(double.IsNaN(tithe),
                        $"{settlement.Name}/{resource.def?.defName}: tithe income should not be NaN");
                }
            }
        }
    }
}
