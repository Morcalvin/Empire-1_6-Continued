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

        /// <summary>
        /// Op-aware invocation. Preferred entry point for squad deployed. Dispatches to
        /// <see cref="ILifecycleParticipantWithOp.OnSquadDeployed(MilitaryOperation)"/> when supported,
        /// otherwise falls back to the legacy
        /// <see cref="ILifecycleParticipant.OnSquadDeployed(WorldSettlementFC, MilitaryJobDef, bool)"/>
        /// (deriving settlement / job from the op).
        /// </summary>
        public static void InvokeOnSquadDeployed(MilitaryOperation op)
        {
            if (op is null) return;
            WorldSettlementFC settlement = op.aggressor?.homeSettlement ?? op.defender?.homeSettlement;
            foreach (ILifecycleParticipant p in _participants)
            {
                try
                {
                    if (p is ILifecycleParticipantWithOp opAware)
                    {
                        opAware.OnSquadDeployed(op);
                    }
                    else if (settlement is object)
                    {
#pragma warning disable 0618 // legacy fallback for participants without op-aware overrides
                        p.OnSquadDeployed(settlement, op.kind, false);
#pragma warning restore 0618
                    }
                }
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnSquadDeployed: {e}"); }
                if (settlement is object) settlement.InvalidateStatCache();
            }
            if (settlement is object) settlement.InvalidateStatCache();
        }

        [Obsolete("Use InvokeOnSquadDeployed(MilitaryOperation) instead. Will be removed in a future version.")]
        public static void InvokeOnSquadDeployed(WorldSettlementFC settlement, MilitaryJobDef job, bool isExtraSquad = false)
        {
            foreach (ILifecycleParticipant p in _participants)
            {
#pragma warning disable 0618 // intentionally calling the legacy interface method on legacy callers
                try { p.OnSquadDeployed(settlement, job, isExtraSquad); }
#pragma warning restore 0618
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnSquadDeployed: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
        }

        /// <summary>Op-aware invocation. Preferred entry point for squad recalled.</summary>
        public static void InvokeOnSquadRecalled(MilitaryOperation op)
        {
            if (op is null) return;
            WorldSettlementFC settlement = op.aggressor?.homeSettlement ?? op.defender?.homeSettlement;
            foreach (ILifecycleParticipant p in _participants)
            {
                try
                {
                    if (p is ILifecycleParticipantWithOp opAware)
                    {
                        opAware.OnSquadRecalled(op);
                    }
                    else if (settlement is object)
                    {
#pragma warning disable 0618
                        p.OnSquadRecalled(settlement);
#pragma warning restore 0618
                    }
                }
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnSquadRecalled: {e}"); }
                if (settlement is object) settlement.InvalidateStatCache();
            }
            if (settlement is object) settlement.InvalidateStatCache();
        }

        [Obsolete("Use InvokeOnSquadRecalled(MilitaryOperation) instead. Will be removed in a future version.")]
        public static void InvokeOnSquadRecalled(WorldSettlementFC settlement)
        {
            foreach (ILifecycleParticipant p in _participants)
            {
#pragma warning disable 0618
                try { p.OnSquadRecalled(settlement); }
#pragma warning restore 0618
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnSquadRecalled: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
        }

        /// <summary>Op-aware invocation. Preferred entry point for battle resolved.</summary>
        public static void InvokeOnBattleResolved(MilitaryOperation op, bool victory, BattleResult result)
        {
            if (op is null) return;
            WorldSettlementFC settlement = op.aggressor?.homeSettlement ?? op.defender?.homeSettlement;
            foreach (ILifecycleParticipant p in _participants)
            {
                try
                {
                    if (p is ILifecycleParticipantWithOp opAware)
                    {
                        opAware.OnBattleResolved(op, victory, result);
                    }
                    else if (settlement is object)
                    {
#pragma warning disable 0618
                        p.OnBattleResolved(settlement, op.kind, victory, result);
#pragma warning restore 0618
                    }
                }
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnBattleResolved: {e}"); }
                if (settlement is object) settlement.InvalidateStatCache();
            }
            if (settlement is object) settlement.InvalidateStatCache();
        }

        [Obsolete("Use InvokeOnBattleResolved(MilitaryOperation, bool, BattleResult) instead. Will be removed in a future version.")]
        public static void InvokeOnBattleResolved(WorldSettlementFC settlement, MilitaryJobDef job, bool victory, BattleResult result)
        {
            foreach (ILifecycleParticipant p in _participants)
            {
#pragma warning disable 0618
                try { p.OnBattleResolved(settlement, job, victory, result); }
#pragma warning restore 0618
                catch (Exception e) { LogUtil.Error($"ILifecycleParticipant {p.GetType().Name} threw in OnBattleResolved: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
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
