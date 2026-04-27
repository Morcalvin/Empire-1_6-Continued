using System;
using System.Collections.Generic;
using Verse;

namespace FactionColonies
{
    public static class LifecycleRegistry
    {
        private static readonly List<ILifecycleParticipant> _participants = new List<ILifecycleParticipant>();

        public static void Register(ILifecycleParticipant participant)
        {
            if (!_participants.Contains(participant)) _participants.Add(participant);
        }
        public static void Unregister(ILifecycleParticipant participant) => _participants.Remove(participant);
        public static void ClearAll() => _participants.Clear();
        public static IReadOnlyList<ILifecycleParticipant> Participants => _participants;

        /* Settlement */
        public static void InvokeOnSettlementCreated(WorldSettlementFC settlement)
        {
            foreach (ILifecycleParticipant p in _participants)
            {
                try { p.OnSettlementCreated(settlement); }
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnSettlementCreated: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
        }

        public static void InvokeOnSettlementRemoved(WorldSettlementFC settlement)
        {
            foreach (ILifecycleParticipant p in _participants)
            {
                try { p.OnSettlementRemoved(settlement); }
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnSettlementRemoved: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
        }

        public static void InvokeOnSettlementUpgraded(WorldSettlementFC settlement, int oldLevel, int newLevel)
        {
            foreach (ILifecycleParticipant p in _participants)
            {
                try { p.OnSettlementUpgraded(settlement, oldLevel, newLevel); }
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnSettlementUpgraded: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
        }

        public static void InvokeOnSettlementTypeChanged(WorldSettlementFC settlement, WorldSettlementDef oldDef, WorldSettlementDef newDef)
        {
            foreach (ILifecycleParticipant p in _participants)
            {
                try { p.OnSettlementTypeChanged(settlement, oldDef, newDef); }
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnSettlementTypeChanged: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
        }

        /* Building */
        public static void InvokeOnBuildingConstructed(WorldSettlementFC settlement, BuildingFCDef building, int slot)
        {
            foreach (ILifecycleParticipant p in _participants)
            {
                try { p.OnBuildingConstructed(settlement, building, slot); }
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnBuildingConstructed: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
        }

        public static void InvokeOnBuildingDeconstructed(WorldSettlementFC settlement, BuildingFCDef building, int slot)
        {
            foreach (ILifecycleParticipant p in _participants)
            {
                try { p.OnBuildingDeconstructed(settlement, building, slot); }
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnBuildingDeconstructed: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
        }

        /* Military */

        public static void InvokeOnSquadDeployed(MilitaryOperation op)
        {
            if (op is null) return;
            WorldSettlementFC settlement = op.aggressor?.homeSettlement ?? op.defender?.homeSettlement;
            foreach (ILifecycleParticipant p in _participants)
            {
                try { p.OnSquadDeployed(op); }
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnSquadDeployed: {e}"); }
                if (settlement is object) settlement.InvalidateStatCache();
            }
            if (settlement is object) settlement.InvalidateStatCache();
        }

        public static void InvokeOnSquadRecalled(MilitaryOperation op)
        {
            if (op is null) return;
            WorldSettlementFC settlement = op.aggressor?.homeSettlement ?? op.defender?.homeSettlement;
            foreach (ILifecycleParticipant p in _participants)
            {
                try { p.OnSquadRecalled(op); }
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnSquadRecalled: {e}"); }
                if (settlement is object) settlement.InvalidateStatCache();
            }
            if (settlement is object) settlement.InvalidateStatCache();
        }

        public static void InvokeOnBattleResolved(MilitaryOperation op, bool victory, BattleResult result)
        {
            if (op is null) return;
            WorldSettlementFC settlement = op.aggressor?.homeSettlement ?? op.defender?.homeSettlement;
            foreach (ILifecycleParticipant p in _participants)
            {
                try { p.OnBattleResolved(op, victory, result); }
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnBattleResolved: {e}"); }
                if (settlement is object) settlement.InvalidateStatCache();
            }
            if (settlement is object) settlement.InvalidateStatCache();
        }

        /* Mercenary */
        public static void InvokeOnMercenaryDeath(MercenaryDeathEvent evt)
        {
            foreach (ILifecycleParticipant p in _participants)
            {
                try { p.OnMercenaryDeath(evt); }
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnMercenaryDeath: {e}"); }
            }
        }

        /* Research */
        public static void InvokeOnResearchCompleted(ResearchProjectDef project)
        {
            foreach (ILifecycleParticipant p in _participants)
            {
                try { p.OnResearchCompleted(project); }
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnResearchCompleted: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                FactionCache.FactionComp?.InvalidateAllSettlementStatCaches();
            }
            FactionCache.FactionComp?.InvalidateAllSettlementStatCaches();
        }
    }
}
