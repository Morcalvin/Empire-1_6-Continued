using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public static class RegistryTests
    {
        // ============================
        // Helpers
        // ============================

        private static WorldSettlementFC GetFirstSettlement()
        {
            var settlements = FactionCache.FactionComp?.settlements;
            if (settlements == null || settlements.Count == 0) return null;
            return settlements[0];
        }

        // ============================
        // Test Doubles
        // ============================

        private class TestLifecycleParticipant : LifecycleParticipantBase
        {
            public int SettlementCreatedCount;
            public int SettlementRemovedCount;
            public int BuildingConstructedCount;
            public int BattleResolvedCount;
            public int ResearchCompletedCount;
            public override void OnSettlementCreated(WorldSettlementFC s) => SettlementCreatedCount++;
            public override void OnSettlementRemoved(WorldSettlementFC s) => SettlementRemovedCount++;
            public override void OnBuildingConstructed(WorldSettlementFC s, BuildingFCDef b, int slot) => BuildingConstructedCount++;
            public override void OnBattleResolved(MilitaryOperation op, bool victory, BattleResult result) => BattleResolvedCount++;
            public override void OnResearchCompleted(ResearchProjectDef p) => ResearchCompletedCount++;
        }

        private class ThrowingLifecycleParticipant : LifecycleParticipantBase
        {
            public override void OnSettlementCreated(WorldSettlementFC s) => throw new InvalidOperationException("test");
            public override void OnSettlementRemoved(WorldSettlementFC s) => throw new InvalidOperationException("test");
            public override void OnBuildingConstructed(WorldSettlementFC s, BuildingFCDef b, int slot) => throw new InvalidOperationException("test");
            public override void OnBattleResolved(MilitaryOperation op, bool victory, BattleResult result) => throw new InvalidOperationException("test");
        }

        private class TestBattleModifier : IBattleModifier
        {
            public double LevelBonus;
            public void ModifyForce(BattleForceContext ctx, MilitaryForce force, bool isAttacker) => force.militaryLevel += LevelBonus;
        }

        private class ThrowingBattleModifier : IBattleModifier
        {
            public void ModifyForce(BattleForceContext ctx, MilitaryForce force, bool isAttacker) => throw new InvalidOperationException("test");
        }

        private class TestPaymentModifier : ISilverPaymentModifier
        {
            public int Discount;
            public void ModifyPayment(SilverPaymentContext context) => context.Amount -= Discount;
        }

        private class ThrowingPaymentModifier : ISilverPaymentModifier
        {
            public void ModifyPayment(SilverPaymentContext context) => throw new InvalidOperationException("test");
        }

        private class TestDefenseValidator : IDefenseValidator
        {
            public bool Allow = true;
            public bool CanDefend(WorldSettlementFC defender, WorldSettlementFC target) => Allow;
        }

        private class ThrowingDefenseValidator : IDefenseValidator
        {
            public bool CanDefend(WorldSettlementFC defender, WorldSettlementFC target) => throw new InvalidOperationException("test");
        }

        private class TestTaxTicker : ITaxTickParticipant
        {
            public int PreTaxCount;
            public int PostTaxCount;
            public void PreTaxResolution(FactionFC f) => PreTaxCount++;
            public void PostTaxResolution(FactionFC f) => PostTaxCount++;
            public void PreSettlementCreateTax(WorldSettlementFC s) { }
            public void PostSettlementCreateTax(WorldSettlementFC s, ref int a, List<Thing> t) { }
        }

        private class ThrowingTaxTicker : ITaxTickParticipant
        {
            public void PreTaxResolution(FactionFC f) => throw new InvalidOperationException("test");
            public void PostTaxResolution(FactionFC f) => throw new InvalidOperationException("test");
            public void PreSettlementCreateTax(WorldSettlementFC s) => throw new InvalidOperationException("test");
            public void PostSettlementCreateTax(WorldSettlementFC s, ref int a, List<Thing> t) => throw new InvalidOperationException("test");
        }

        private class TestSquadValidator : ISquadAssignmentValidator
        {
            public bool Allow = true;
            public string RejectReason = "test reject";
            public bool CanAssign(WorldSettlementFC s, MercenarySquadFC sq, out string reason)
            {
                reason = Allow ? null : RejectReason;
                return Allow;
            }
        }

        private class ThrowingSquadValidator : ISquadAssignmentValidator
        {
            public bool CanAssign(WorldSettlementFC s, MercenarySquadFC sq, out string reason)
            {
                reason = null;
                throw new InvalidOperationException("test");
            }
        }

        private class TestMainTab : IMainTabWindowOverview
        {
            public int PostCloseCount;
            public void PreOpenWindow(FactionFC f) { }
            public void OnTabSwitch() { }
            public void DrawOverviewTab(Rect b) { }
            public void PostCloseWindow() => PostCloseCount++;
            public string TabName() => "TestTab";
        }

        private class ThrowingMainTab : IMainTabWindowOverview
        {
            public void PreOpenWindow(FactionFC f) { }
            public void OnTabSwitch() { }
            public void DrawOverviewTab(Rect b) { }
            public void PostCloseWindow() => throw new InvalidOperationException("test");
            public string TabName() => "ThrowingTab";
        }

        // ============================
        // LifecycleRegistry
        // ============================

        [EmpireTest("Registry")]
        public static void Lifecycle_Register_InvokesSettlementCreated()
        {
            var settlement = GetFirstSettlement();
            if (settlement == null) TestAssert.Skip("No settlements");

            var p = new TestLifecycleParticipant();
            LifecycleRegistry.Register(p);
            try
            {
                LifecycleRegistry.InvokeOnSettlementCreated(settlement);
                TestAssert.AreEqual(1, p.SettlementCreatedCount);
            }
            finally { LifecycleRegistry.Unregister(p); }
        }

        [EmpireTest("Registry")]
        public static void Lifecycle_Register_InvokesSettlementRemoved()
        {
            var settlement = GetFirstSettlement();
            if (settlement == null) TestAssert.Skip("No settlements");

            var p = new TestLifecycleParticipant();
            LifecycleRegistry.Register(p);
            try
            {
                LifecycleRegistry.InvokeOnSettlementRemoved(settlement);
                TestAssert.AreEqual(1, p.SettlementRemovedCount);
            }
            finally { LifecycleRegistry.Unregister(p); }
        }

        [EmpireTest("Registry")]
        public static void Lifecycle_Register_InvokesBuildingConstructed()
        {
            var settlement = GetFirstSettlement();
            if (settlement == null) TestAssert.Skip("No settlements");

            var p = new TestLifecycleParticipant();
            LifecycleRegistry.Register(p);
            try
            {
                LifecycleRegistry.InvokeOnBuildingConstructed(settlement, null, 0);
                TestAssert.AreEqual(1, p.BuildingConstructedCount);
            }
            finally { LifecycleRegistry.Unregister(p); }
        }

        [EmpireTest("Registry")]
        public static void Lifecycle_Register_InvokesBattleResolved()
        {
            var settlement = GetFirstSettlement();
            if (settlement == null) TestAssert.Skip("No settlements");

            var p = new TestLifecycleParticipant();
            LifecycleRegistry.Register(p);
            try
            {
                MilitaryOperation op = MakeSyntheticOp(settlement);
                LifecycleRegistry.InvokeOnBattleResolved(op, true, null);
                TestAssert.AreEqual(1, p.BattleResolvedCount);
            }
            finally { LifecycleRegistry.Unregister(p); }
        }

        /// <summary>
        /// Builds a minimal <see cref="MilitaryOperation"/> for tests that exercise the op-aware
        /// registry overloads. Not registered with the manager — purely a transient stand-in.
        /// </summary>
        private static MilitaryOperation MakeSyntheticOp(WorldSettlementFC home)
        {
            var op = new MilitaryOperation(-1, null, home?.Tile ?? RimWorld.Planet.PlanetTile.Invalid, home);
            op.aggressor.homeSettlement = home;
            op.aggressor.faction = FactionCache.PlayerColonyFaction;
            return op;
        }

        [EmpireTest("Registry")]
        public static void Lifecycle_Unregister_StopsInvocations()
        {
            var settlement = GetFirstSettlement();
            if (settlement == null) TestAssert.Skip("No settlements");

            var p = new TestLifecycleParticipant();
            LifecycleRegistry.Register(p);
            LifecycleRegistry.Unregister(p);
            LifecycleRegistry.InvokeOnSettlementCreated(settlement);
            TestAssert.AreEqual(0, p.SettlementCreatedCount);
        }

        [EmpireTest("Registry")]
        public static void Lifecycle_DuplicateRegister_Ignored()
        {
            var settlement = GetFirstSettlement();
            if (settlement == null) TestAssert.Skip("No settlements");

            var p = new TestLifecycleParticipant();
            LifecycleRegistry.Register(p);
            LifecycleRegistry.Register(p); // duplicate
            try
            {
                LifecycleRegistry.InvokeOnSettlementCreated(settlement);
                TestAssert.AreEqual(1, p.SettlementCreatedCount, "Duplicate should be ignored");
            }
            finally { LifecycleRegistry.Unregister(p); }
        }

        [EmpireTest("Registry")]
        public static void Lifecycle_Exception_DoesNotCrash()
        {
            var settlement = GetFirstSettlement();
            if (settlement == null) TestAssert.Skip("No settlements");

            var bad = new ThrowingLifecycleParticipant();
            LifecycleRegistry.Register(bad);
            try
            {
                TestAssert.DoesNotThrow(() => LifecycleRegistry.InvokeOnSettlementCreated(settlement));
            }
            finally { LifecycleRegistry.Unregister(bad); }
        }

        [EmpireTest("Registry")]
        public static void Lifecycle_MultipleParticipants_AllInvoked()
        {
            var settlement = GetFirstSettlement();
            if (settlement == null) TestAssert.Skip("No settlements");

            var p1 = new TestLifecycleParticipant();
            var p2 = new TestLifecycleParticipant();
            LifecycleRegistry.Register(p1);
            LifecycleRegistry.Register(p2);
            try
            {
                LifecycleRegistry.InvokeOnSettlementCreated(settlement);
                TestAssert.AreEqual(1, p1.SettlementCreatedCount, "First participant should be invoked");
                TestAssert.AreEqual(1, p2.SettlementCreatedCount, "Second participant should be invoked");
            }
            finally
            {
                LifecycleRegistry.Unregister(p1);
                LifecycleRegistry.Unregister(p2);
            }
        }

        [EmpireTest("Registry")]
        public static void Lifecycle_ExceptionDoesNotBlockOthers()
        {
            var settlement = GetFirstSettlement();
            if (settlement == null) TestAssert.Skip("No settlements");

            var bad = new ThrowingLifecycleParticipant();
            var good = new TestLifecycleParticipant();
            LifecycleRegistry.Register(bad);
            LifecycleRegistry.Register(good);
            try
            {
                LifecycleRegistry.InvokeOnSettlementCreated(settlement);
                TestAssert.AreEqual(1, good.SettlementCreatedCount,
                    "Good participant should still be invoked after bad one throws");
            }
            finally
            {
                LifecycleRegistry.Unregister(bad);
                LifecycleRegistry.Unregister(good);
            }
        }

        // ============================
        // BattleModifierRegistry
        // ============================

        [EmpireTest("Registry")]
        public static void BattleModifier_Register_ModifiesForce()
        {
            var c = new TestBattleModifier { LevelBonus = 2.0 };
            BattleModifierRegistry.Register(c);
            try
            {
                var force = new MilitaryForce { militaryLevel = 5, militaryEfficiency = 1.0, forceRemaining = 5 };
                BattleModifierRegistry.InvokeModifyForce(null, force, true);
                TestAssert.AreEqual(7.0, force.militaryLevel, message: "Level should increase by 2");
            }
            finally { BattleModifierRegistry.Unregister(c); }
        }

        [EmpireTest("Registry")]
        public static void BattleModifier_Unregister_NoEffect()
        {
            var c = new TestBattleModifier { LevelBonus = 2.0 };
            BattleModifierRegistry.Register(c);
            BattleModifierRegistry.Unregister(c);
            var force = new MilitaryForce { militaryLevel = 5, militaryEfficiency = 1.0, forceRemaining = 5 };
            BattleModifierRegistry.InvokeModifyForce(null, force, true);
            TestAssert.AreEqual(5.0, force.militaryLevel, message: "Level should be unchanged");
        }

        [EmpireTest("Registry")]
        public static void BattleModifier_DuplicateRegister_Ignored()
        {
            var c = new TestBattleModifier { LevelBonus = 2.0 };
            BattleModifierRegistry.Register(c);
            BattleModifierRegistry.Register(c);
            try
            {
                var force = new MilitaryForce { militaryLevel = 5, militaryEfficiency = 1.0, forceRemaining = 5 };
                BattleModifierRegistry.InvokeModifyForce(null, force, true);
                TestAssert.AreEqual(7.0, force.militaryLevel, message: "Should only apply once");
            }
            finally { BattleModifierRegistry.Unregister(c); }
        }

        [EmpireTest("Registry")]
        public static void BattleModifier_Exception_DoesNotCrash()
        {
            var bad = new ThrowingBattleModifier();
            BattleModifierRegistry.Register(bad);
            try
            {
                var force = new MilitaryForce { militaryLevel = 5, militaryEfficiency = 1.0, forceRemaining = 5 };
                TestAssert.DoesNotThrow(() => BattleModifierRegistry.InvokeModifyForce(null, force, true));
            }
            finally { BattleModifierRegistry.Unregister(bad); }
        }

        // ============================
        // SilverPaymentRegistry
        // ============================

        [EmpireTest("Registry")]
        public static void SilverPayment_Register_ModifiesAmount()
        {
            var c = new TestPaymentModifier { Discount = 50 };
            SilverPaymentRegistry.Register(c);
            try
            {
                var ctx = new SilverPaymentContext(200, "test");
                SilverPaymentRegistry.InvokeModifiers(ctx);
                TestAssert.AreEqual(150, ctx.Amount, message: "Amount should be reduced by 50");
            }
            finally { SilverPaymentRegistry.Unregister(c); }
        }

        [EmpireTest("Registry")]
        public static void SilverPayment_Unregister_NoEffect()
        {
            var c = new TestPaymentModifier { Discount = 50 };
            SilverPaymentRegistry.Register(c);
            SilverPaymentRegistry.Unregister(c);
            var ctx = new SilverPaymentContext(200, "test");
            SilverPaymentRegistry.InvokeModifiers(ctx);
            TestAssert.AreEqual(200, ctx.Amount, message: "Amount should be unchanged");
        }

        [EmpireTest("Registry")]
        public static void SilverPayment_DuplicateRegister_Ignored()
        {
            var c = new TestPaymentModifier { Discount = 50 };
            SilverPaymentRegistry.Register(c);
            SilverPaymentRegistry.Register(c);
            try
            {
                var ctx = new SilverPaymentContext(200, "test");
                SilverPaymentRegistry.InvokeModifiers(ctx);
                TestAssert.AreEqual(150, ctx.Amount, message: "Should only apply once");
            }
            finally { SilverPaymentRegistry.Unregister(c); }
        }

        [EmpireTest("Registry")]
        public static void SilverPayment_Exception_DoesNotCrash()
        {
            var bad = new ThrowingPaymentModifier();
            SilverPaymentRegistry.Register(bad);
            try
            {
                var ctx = new SilverPaymentContext(200, "test");
                TestAssert.DoesNotThrow(() => SilverPaymentRegistry.InvokeModifiers(ctx));
            }
            finally { SilverPaymentRegistry.Unregister(bad); }
        }

        [EmpireTest("Registry")]
        public static void SilverPayment_ReturnsContext()
        {
            var ctx = new SilverPaymentContext(100, "test");
            SilverPaymentContext returned = SilverPaymentRegistry.InvokeModifiers(ctx);
            TestAssert.IsTrue(ReferenceEquals(ctx, returned), "Should return the same context object");
        }

        // ============================
        // DefenseValidatorRegistry
        // ============================

        [EmpireTest("Registry")]
        public static void DefenseValidator_AllAllow_ReturnsTrue()
        {
            var c = new TestDefenseValidator { Allow = true };
            DefenseValidatorRegistry.Register(c);
            try
            {
                TestAssert.IsTrue(DefenseValidatorRegistry.CanDefend(null, null));
            }
            finally { DefenseValidatorRegistry.Unregister(c); }
        }

        [EmpireTest("Registry")]
        public static void DefenseValidator_OneRejects_ReturnsFalse()
        {
            var c = new TestDefenseValidator { Allow = false };
            DefenseValidatorRegistry.Register(c);
            try
            {
                TestAssert.IsFalse(DefenseValidatorRegistry.CanDefend(null, null));
            }
            finally { DefenseValidatorRegistry.Unregister(c); }
        }

        [EmpireTest("Registry")]
        public static void DefenseValidator_EmptyRegistry_ReturnsTrue()
        {
            TestAssert.IsTrue(DefenseValidatorRegistry.CanDefend(null, null),
                "No validators means default allow");
        }

        [EmpireTest("Registry")]
        public static void DefenseValidator_Exception_DoesNotCrash()
        {
            var bad = new ThrowingDefenseValidator();
            DefenseValidatorRegistry.Register(bad);
            try
            {
                TestAssert.DoesNotThrow(() => DefenseValidatorRegistry.CanDefend(null, null));
            }
            finally { DefenseValidatorRegistry.Unregister(bad); }
        }

        [EmpireTest("Registry")]
        public static void DefenseValidator_Unregister_StopsRejection()
        {
            var c = new TestDefenseValidator { Allow = false };
            DefenseValidatorRegistry.Register(c);
            DefenseValidatorRegistry.Unregister(c);
            TestAssert.IsTrue(DefenseValidatorRegistry.CanDefend(null, null),
                "After unregister, should allow again");
        }

        // ============================
        // TaxTickRegistry
        // ============================

        [EmpireTest("Registry")]
        public static void TaxTick_Register_InvokesPreTax()
        {
            var c = new TestTaxTicker();
            TaxTickRegistry.Register(c);
            try
            {
                TaxTickRegistry.InvokePreTaxResolution(null);
                TestAssert.AreEqual(1, c.PreTaxCount);
            }
            finally { TaxTickRegistry.Unregister(c); }
        }

        [EmpireTest("Registry")]
        public static void TaxTick_Register_InvokesPostTax()
        {
            var c = new TestTaxTicker();
            TaxTickRegistry.Register(c);
            try
            {
                TaxTickRegistry.InvokePostTaxResolution(null);
                TestAssert.AreEqual(1, c.PostTaxCount);
            }
            finally { TaxTickRegistry.Unregister(c); }
        }

        [EmpireTest("Registry")]
        public static void TaxTick_Unregister_StopsInvocations()
        {
            var c = new TestTaxTicker();
            TaxTickRegistry.Register(c);
            TaxTickRegistry.Unregister(c);
            TaxTickRegistry.InvokePreTaxResolution(null);
            TestAssert.AreEqual(0, c.PreTaxCount);
        }

        [EmpireTest("Registry")]
        public static void TaxTick_DuplicateRegister_Ignored()
        {
            var c = new TestTaxTicker();
            TaxTickRegistry.Register(c);
            TaxTickRegistry.Register(c);
            try
            {
                TaxTickRegistry.InvokePreTaxResolution(null);
                TestAssert.AreEqual(1, c.PreTaxCount, "Should only invoke once");
            }
            finally { TaxTickRegistry.Unregister(c); }
        }

        [EmpireTest("Registry")]
        public static void TaxTick_Exception_DoesNotCrash()
        {
            var bad = new ThrowingTaxTicker();
            TaxTickRegistry.Register(bad);
            try
            {
                TestAssert.DoesNotThrow(() => TaxTickRegistry.InvokePreTaxResolution(null));
                TestAssert.DoesNotThrow(() => TaxTickRegistry.InvokePostTaxResolution(null));
            }
            finally { TaxTickRegistry.Unregister(bad); }
        }

        // ============================
        // SquadAssignmentRegistry
        // ============================

        [EmpireTest("Registry")]
        public static void SquadAssignment_AllAllow_ReturnsTrue()
        {
            var c = new TestSquadValidator { Allow = true };
            SquadAssignmentRegistry.Register(c);
            try
            {
                bool result = SquadAssignmentRegistry.CanAssign(null, null, out string reason);
                TestAssert.IsTrue(result);
                TestAssert.IsNull(reason, "Reason should be null on allow");
            }
            finally { SquadAssignmentRegistry.Unregister(c); }
        }

        [EmpireTest("Registry")]
        public static void SquadAssignment_OneRejects_ReturnsFalseWithReason()
        {
            var c = new TestSquadValidator { Allow = false, RejectReason = "too expensive" };
            SquadAssignmentRegistry.Register(c);
            try
            {
                bool result = SquadAssignmentRegistry.CanAssign(null, null, out string reason);
                TestAssert.IsFalse(result);
                TestAssert.AreEqual((object)"too expensive", (object)reason);
            }
            finally { SquadAssignmentRegistry.Unregister(c); }
        }

        [EmpireTest("Registry")]
        public static void SquadAssignment_EmptyRegistry_ReturnsTrue()
        {
            bool result = SquadAssignmentRegistry.CanAssign(null, null, out string reason);
            TestAssert.IsTrue(result, "No validators means default allow");
            TestAssert.IsNull(reason);
        }

        [EmpireTest("Registry")]
        public static void SquadAssignment_Exception_DoesNotCrash()
        {
            var bad = new ThrowingSquadValidator();
            SquadAssignmentRegistry.Register(bad);
            try
            {
                TestAssert.DoesNotThrow(() => SquadAssignmentRegistry.CanAssign(null, null, out string reason));
            }
            finally { SquadAssignmentRegistry.Unregister(bad); }
        }

        // ============================
        // MainTableRegistry
        // ============================

        [EmpireTest("Registry")]
        public static void MainTable_Register_AppearsInTabs()
        {
            var tab = new TestMainTab();
            MainTableRegistry.Register(tab);
            try
            {
                TestAssert.Contains(MainTableRegistry.Tabs, tab);
            }
            finally { MainTableRegistry.Unregister(tab); }
        }

        [EmpireTest("Registry")]
        public static void MainTable_Unregister_RemovedFromTabs()
        {
            var tab = new TestMainTab();
            MainTableRegistry.Register(tab);
            MainTableRegistry.Unregister(tab);
            TestAssert.IsFalse(MainTableRegistry.Tabs.Contains(tab), "Tab should be removed");
        }

        [EmpireTest("Registry")]
        public static void MainTable_InvokesPostClose()
        {
            var tab = new TestMainTab();
            MainTableRegistry.Register(tab);
            try
            {
                MainTableRegistry.InvokePostCloseWindow();
                TestAssert.AreEqual(1, tab.PostCloseCount);
            }
            finally { MainTableRegistry.Unregister(tab); }
        }

        [EmpireTest("Registry")]
        public static void MainTable_DuplicateRegister_Ignored()
        {
            var tab = new TestMainTab();
            MainTableRegistry.Register(tab);
            MainTableRegistry.Register(tab);
            try
            {
                MainTableRegistry.InvokePostCloseWindow();
                TestAssert.AreEqual(1, tab.PostCloseCount, "Should only invoke once");
            }
            finally { MainTableRegistry.Unregister(tab); }
        }

        [EmpireTest("Registry")]
        public static void MainTable_Exception_DoesNotCrash()
        {
            var bad = new ThrowingMainTab();
            MainTableRegistry.Register(bad);
            try
            {
                TestAssert.DoesNotThrow(() => MainTableRegistry.InvokePostCloseWindow());
            }
            finally { MainTableRegistry.Unregister(bad); }
        }

        // ============================
        // BuildingFilterRegistry
        // ============================

        [EmpireTest("Registry")]
        public static void BuildingFilter_Register_AppearsInFilters()
        {
            var filter = new BuildingFilter("Test", null, def => true);
            BuildingFilterRegistry.Register(filter);
            try
            {
                TestAssert.Contains(BuildingFilterRegistry.Filters, filter);
            }
            finally { BuildingFilterRegistry.Unregister(filter); }
        }

        [EmpireTest("Registry")]
        public static void BuildingFilter_Unregister_RemovedFromFilters()
        {
            var filter = new BuildingFilter("Test", null, def => true);
            BuildingFilterRegistry.Register(filter);
            BuildingFilterRegistry.Unregister(filter);
            TestAssert.IsFalse(BuildingFilterRegistry.Filters.Contains(filter), "Filter should be removed");
        }

        [EmpireTest("Registry")]
        public static void BuildingFilter_DuplicateRegister_Ignored()
        {
            var filter = new BuildingFilter("Test", null, def => true);
            BuildingFilterRegistry.Register(filter);
            BuildingFilterRegistry.Register(filter);
            try
            {
                int count = 0;
                foreach (BuildingFilter f in BuildingFilterRegistry.Filters)
                    if (ReferenceEquals(f, filter)) count++;
                TestAssert.AreEqual(1, count, "Should only appear once");
            }
            finally { BuildingFilterRegistry.Unregister(filter); }
        }

        // ============================
        // EmpireCacheUtil (external invalidators)
        // ============================

        [EmpireTest("Registry")]
        public static void CacheInvalidator_Register_InvokesOnInvalidateAll()
        {
            int count = 0;
            EmpireCacheUtil.RegisterCacheInvalidator("_test", () => count++);
            try
            {
                EmpireCacheUtil.InvalidateAll();
                TestAssert.AreEqual(1, count);
            }
            finally { EmpireCacheUtil.UnregisterCacheInvalidator("_test"); }
        }

        [EmpireTest("Registry")]
        public static void CacheInvalidator_SurvivesInvalidateAll()
        {
            int count = 0;
            EmpireCacheUtil.RegisterCacheInvalidator("_test", () => count++);
            try
            {
                EmpireCacheUtil.InvalidateAll();
                EmpireCacheUtil.InvalidateAll();
                TestAssert.AreEqual(2, count, "Callback should survive across InvalidateAll calls");
            }
            finally { EmpireCacheUtil.UnregisterCacheInvalidator("_test"); }
        }

        [EmpireTest("Registry")]
        public static void CacheInvalidator_DuplicateKey_ReplacesOld()
        {
            int oldCount = 0;
            int newCount = 0;
            EmpireCacheUtil.RegisterCacheInvalidator("_test", () => oldCount++);
            EmpireCacheUtil.RegisterCacheInvalidator("_test", () => newCount++);
            try
            {
                EmpireCacheUtil.InvalidateAll();
                TestAssert.AreEqual(0, oldCount, "Old callback should not fire");
                TestAssert.AreEqual(1, newCount, "New callback should fire");
            }
            finally { EmpireCacheUtil.UnregisterCacheInvalidator("_test"); }
        }

        [EmpireTest("Registry")]
        public static void CacheInvalidator_Exception_DoesNotBlockOthers()
        {
            int count = 0;
            EmpireCacheUtil.RegisterCacheInvalidator("_test_bad", () => throw new InvalidOperationException("test"));
            EmpireCacheUtil.RegisterCacheInvalidator("_test_good", () => count++);
            try
            {
                TestAssert.DoesNotThrow(() => EmpireCacheUtil.InvalidateAll());
                TestAssert.AreEqual(1, count, "Good callback should still fire after bad one throws");
            }
            finally
            {
                EmpireCacheUtil.UnregisterCacheInvalidator("_test_bad");
                EmpireCacheUtil.UnregisterCacheInvalidator("_test_good");
            }
        }

        [EmpireTest("Registry")]
        public static void CacheInvalidator_Unregister_StopsInvocations()
        {
            int count = 0;
            EmpireCacheUtil.RegisterCacheInvalidator("_test", () => count++);
            EmpireCacheUtil.UnregisterCacheInvalidator("_test");
            EmpireCacheUtil.InvalidateAll();
            TestAssert.AreEqual(0, count, "Callback should not fire after unregister");
        }
    }
}
