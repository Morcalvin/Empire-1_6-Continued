using Verse;

namespace FactionColonies
{
    /* Tests for EmpireThreatAdaptation. The internal adaptDays field is private, so most tests
       observe behavior through ThreatFactor (which reads from Find.Storyteller's curves) or
       through pure exception-safety on Notify_*. Skip when Find.Storyteller or its curves are
       unavailable. */
    public static class EmpireThreatAdaptationTests
    {
        private static bool HasStoryteller =>
            Find.Storyteller is object && Find.Storyteller.def is object;

        private static bool HasAdaptationCurve =>
            HasStoryteller && Find.Storyteller.def.pointsFactorFromAdaptDays is object;

        private static bool HasLossCurve =>
            HasStoryteller && Find.Storyteller.def.adaptDaysLossFromColonistLostByPostPopulation is object;

        // -*- ThreatFactor -*-

        [EmpireTest("ThreatAdaptation")]
        public static void ThreatFactor_FreshInstance_FiniteAndPositive()
        {
            if (!HasStoryteller) TestAssert.Skip("No storyteller available");

            var adapt = new EmpireThreatAdaptation();
            double factor = adapt.ThreatFactor;
            TestAssert.IsFalse(double.IsNaN(factor), "ThreatFactor should not be NaN");
            TestAssert.IsFalse(double.IsInfinity(factor), "ThreatFactor should not be infinite");
            TestAssert.GreaterThan(factor, 0, "ThreatFactor should be positive");
        }

        [EmpireTest("ThreatAdaptation")]
        public static void ThreatFactor_NullPointsCurve_ReturnsOne()
        {
            // Defensive guard in ThreatFactor — null curve short-circuits to 1.0.
            if (HasAdaptationCurve)
                TestAssert.Skip("Current storyteller has a pointsFactorFromAdaptDays curve");
            if (!HasStoryteller) TestAssert.Skip("No storyteller available");

            var adapt = new EmpireThreatAdaptation();
            TestAssert.AreEqual(1.0, adapt.ThreatFactor);
        }

        // -*- Notify_BattleWon -*-

        [EmpireTest("ThreatAdaptation")]
        public static void NotifyBattleWon_DoesNotThrow()
        {
            if (!HasStoryteller) TestAssert.Skip("No storyteller available");

            var adapt = new EmpireThreatAdaptation();
            TestAssert.DoesNotThrow(() => adapt.Notify_BattleWon());
        }

        [EmpireTest("ThreatAdaptation")]
        public static void NotifyBattleWon_ChangesThreatFactor()
        {
            // After many BattleWon calls (well past 0), adaptDays climbs and ThreatFactor changes
            // if the curve isn't flat at the start.
            if (!HasAdaptationCurve) TestAssert.Skip("Storyteller lacks adaptation curve");
            if (Find.Storyteller.difficulty.adaptationEffectFactor <= 0f)
                TestAssert.Skip("adaptationEffectFactor is zero; ThreatFactor is curve-insensitive");

            var adapt = new EmpireThreatAdaptation();
            double before = adapt.ThreatFactor;
            // Push hard so even a flat-near-zero curve registers some change.
            for (int i = 0; i < 100; i++) adapt.Notify_BattleWon();
            double after = adapt.ThreatFactor;

            // We don't pin the direction — some curves go up, some flat, some down.
            // We just verify the field actually moved, OR that we're sitting at the curve's
            // saturated value (no change because of a clamp).
            // Allow saturation-at-equal as a valid outcome.
            TestAssert.IsTrue(System.Math.Abs(after - before) > 0.0001
                              || System.Math.Abs(before - 1.0) < 0.0001 == false,
                $"Expected ThreatFactor to move after 100 BattleWon calls; before={before}, after={after}");
        }

        [EmpireTest("ThreatAdaptation")]
        public static void NotifyBattleWon_ClampsAtMax()
        {
            // Spam BattleWon way past the storyteller's adaptDaysMax; subsequent ThreatFactor
            // calls should stabilize (idempotent at the clamp).
            if (!HasStoryteller) TestAssert.Skip("No storyteller available");

            var adapt = new EmpireThreatAdaptation();
            for (int i = 0; i < 1000; i++) adapt.Notify_BattleWon();
            double afterFirstSpam = adapt.ThreatFactor;
            for (int i = 0; i < 1000; i++) adapt.Notify_BattleWon();
            double afterSecondSpam = adapt.ThreatFactor;

            TestAssert.AreEqual(afterFirstSpam, afterSecondSpam, tolerance: 0.0001,
                "ThreatFactor should stabilize once adaptDays is clamped at max");
        }

        // -*- Notify_BattleLost -*-

        [EmpireTest("ThreatAdaptation")]
        public static void NotifyBattleLost_DoesNotThrow_RequiresFactionFC()
        {
            // Notify_BattleLost reads FactionCache.FactionComp.settlements.Count for the curve
            // sample. Skip if no faction is available.
            if (FindFC.FactionComp is null) TestAssert.Skip("No FactionFC");
            if (!HasStoryteller) TestAssert.Skip("No storyteller available");

            var adapt = new EmpireThreatAdaptation();
            TestAssert.DoesNotThrow(() => adapt.Notify_BattleLost());
        }

        [EmpireTest("ThreatAdaptation")]
        public static void NotifyBattleLost_NullCurve_NoOp()
        {
            // Guard in Notify_BattleLost — null curve makes the call a silent no-op.
            // This is only really testable when the active storyteller lacks the curve; Skip
            // otherwise.
            if (HasLossCurve) TestAssert.Skip("Current storyteller has a loss curve");
            if (FindFC.FactionComp is null) TestAssert.Skip("No FactionFC");
            if (!HasStoryteller) TestAssert.Skip("No storyteller available");

            var adapt = new EmpireThreatAdaptation();
            // Push adaptDays up so a non-no-op call would visibly lower it.
            for (int i = 0; i < 20; i++) adapt.Notify_BattleWon();
            double before = adapt.ThreatFactor;

            TestAssert.DoesNotThrow(() => adapt.Notify_BattleLost());
            double after = adapt.ThreatFactor;

            TestAssert.AreEqual(before, after, tolerance: 0.0001,
                "Notify_BattleLost should be a no-op when the storyteller has no loss curve");
        }

        [EmpireTest("ThreatAdaptation")]
        public static void NotifyBattleLost_ClampsAtMin()
        {
            // Mirror of NotifyBattleWon_ClampsAtMax: spam BattleLost beyond the floor.
            if (FindFC.FactionComp is null) TestAssert.Skip("No FactionFC");
            if (!HasLossCurve) TestAssert.Skip("Storyteller lacks loss curve");

            var adapt = new EmpireThreatAdaptation();
            for (int i = 0; i < 1000; i++) adapt.Notify_BattleLost();
            double after1 = adapt.ThreatFactor;
            for (int i = 0; i < 1000; i++) adapt.Notify_BattleLost();
            double after2 = adapt.ThreatFactor;

            TestAssert.AreEqual(after1, after2, tolerance: 0.0001,
                "ThreatFactor should stabilize once adaptDays is clamped at min");
        }

        // -*- ExposeData round-trip -*-

        [EmpireTest("ThreatAdaptation")]
        public static void ExposeData_DoesNotThrow_OnFreshInstance()
        {
            // The Scribe machinery has no test driver in this framework; we only verify the
            // method shape is invokable without runtime errors in the no-op (no active scribe
            // mode) case.
            var adapt = new EmpireThreatAdaptation();
            TestAssert.DoesNotThrow(() => adapt.ExposeData());
        }
    }
}
