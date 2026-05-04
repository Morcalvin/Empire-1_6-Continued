using RimWorld;

namespace FactionColonies
{
    public static class MilitaryTests
    {
        // --- MilitaryUtil.GetTechLevelBaseline (XML-driven via EnemyPowerTechDef) ---

        private static void AssertTechLevel(TechLevel techLevel, double expectedLevel, double expectedEfficiency)
        {
            MilitaryUtil.GetTechLevelBaseline(techLevel, out double level, out double efficiency);
            TestAssert.AreEqual(expectedLevel, level, message: $"Military level for {techLevel}");
            TestAssert.AreEqual(expectedEfficiency, efficiency, message: $"Efficiency for {techLevel}");
        }

        [EmpireTest("Military")]
        public static void TechLevel_Undefined_Returns1_05()
        {
            AssertTechLevel(TechLevel.Undefined, 1, 0.5);
        }

        [EmpireTest("Military")]
        public static void TechLevel_Animal_Returns1_05()
        {
            AssertTechLevel(TechLevel.Animal, 1, 0.5);
        }

        [EmpireTest("Military")]
        public static void TechLevel_Neolithic_Returns1_1()
        {
            AssertTechLevel(TechLevel.Neolithic, 2, 0.9);
        }

        [EmpireTest("Military")]
        public static void TechLevel_Medieval_Returns2_12()
        {
            AssertTechLevel(TechLevel.Medieval, 3, 1.0);
        }

        [EmpireTest("Military")]
        public static void TechLevel_Industrial_Returns3_12()
        {
            AssertTechLevel(TechLevel.Industrial, 5, 1.1);
        }

        [EmpireTest("Military")]
        public static void TechLevel_Spacer_Returns3_13()
        {
            AssertTechLevel(TechLevel.Spacer, 6, 1.2);
        }

        [EmpireTest("Military")]
        public static void TechLevel_Ultra_Returns3_13()
        {
            AssertTechLevel(TechLevel.Ultra, 7, 1.3);
        }

        [EmpireTest("Military")]
        public static void TechLevel_Archotech_Returns4_15()
        {
            AssertTechLevel(TechLevel.Archotech, 9, 1.5);
        }

        // --- CalculateAccuracyCostPercentage ---

        [EmpireTest("Military")]
        public static void AccuracyCost_MaxAccuracy_ZeroSurcharge()
        {
            TestAssert.AreEqual(0f, MilitaryFireSupport.CalculateAccuracyCostPercentage(15f), 0.01f);
        }

        [EmpireTest("Military")]
        public static void AccuracyCost_ZeroAccuracy_FullSurcharge()
        {
            TestAssert.AreEqual(100f, MilitaryFireSupport.CalculateAccuracyCostPercentage(0f), 0.01f);
        }

        [EmpireTest("Military")]
        public static void AccuracyCost_Accuracy10_33Percent()
        {
            TestAssert.AreEqual(33f, MilitaryFireSupport.CalculateAccuracyCostPercentage(10f), 0.01f);
        }

        [EmpireTest("Military")]
        public static void AccuracyCost_Accuracy75_50Percent()
        {
            TestAssert.AreEqual(50f, MilitaryFireSupport.CalculateAccuracyCostPercentage(7.5f), 0.01f);
        }

        // --- CalculateTotalCost ---

        [EmpireTest("Military")]
        public static void TotalCost_SingleProjectile_PerfectAccuracy()
        {
            // marketValue * 1.5 * (1 + 0/100) = 100 * 1.5 = 150
            float cost = MilitaryFireSupport.CalculateTotalCost(15f, new[] { 100f });
            TestAssert.AreEqual(150f, cost, 0.01f);
        }

        [EmpireTest("Military")]
        public static void TotalCost_MultipleProjectiles_ImperfectAccuracy()
        {
            // accuracy=10 → surcharge=33%
            // each: 100 * 1.5 * 1.33 = 199.5, two = 399, rounded = 399
            float cost = MilitaryFireSupport.CalculateTotalCost(10f, new[] { 100f, 100f });
            TestAssert.AreEqual(399f, cost, 0.01f);
        }

        [EmpireTest("Military")]
        public static void TotalCost_EmptyList_ReturnsZero()
        {
            float cost = MilitaryFireSupport.CalculateTotalCost(15f, new float[] { });
            TestAssert.AreEqual(0f, cost, 0.01f);
        }
    }
}
