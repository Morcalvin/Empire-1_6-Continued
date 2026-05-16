using System;
using System.Collections.Generic;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Dispatches settlement, military operation, mercenary squad, and research lifecycle events
    /// to participants implementing one or more of <see cref="ISettlementListener"/>,
    /// <see cref="IMilitaryOperationListener"/>, <see cref="IMercenarySquadListener"/>,
    /// <see cref="IResearchListener"/>. A participant implementing multiple listener interfaces
    /// is registered against all matching domain lists in a single <see cref="Register"/> call.
    /// </summary>
    public static class LifecycleRegistry
    {
        private static readonly List<ISettlementListener>        _settlement = new List<ISettlementListener>();
        private static readonly List<IMilitaryOperationListener> _militaryOp = new List<IMilitaryOperationListener>();
        private static readonly List<IMercenarySquadListener>    _mercSquad  = new List<IMercenarySquadListener>();
        private static readonly List<IResearchListener>          _research   = new List<IResearchListener>();

        public static void Register(object participant)
        {
            if (participant is null) return;
            bool any = false;
            if (participant is ISettlementListener s        && !_settlement.Contains(s)) { _settlement.Add(s); any = true; }
            if (participant is IMilitaryOperationListener m && !_militaryOp.Contains(m)) { _militaryOp.Add(m); any = true; }
            if (participant is IMercenarySquadListener q    && !_mercSquad.Contains(q))  { _mercSquad.Add(q);  any = true; }
            if (participant is IResearchListener r          && !_research.Contains(r))   { _research.Add(r);   any = true; }

            // If the object already implements an interface but was already in every list,
            // any will remain false. The "implements at least one listener" test is the
            // type check above — gate the warning on that, not on whether anything was added.
            bool implementsAny = participant is ISettlementListener
                || participant is IMilitaryOperationListener
                || participant is IMercenarySquadListener
                || participant is IResearchListener;
            if (!implementsAny)
            {
                LogUtil.Warning($"LifecycleRegistry.Register: {participant.GetType().Name} implements no listener interface; ignored");
            }
        }

        public static void Unregister(object participant)
        {
            if (participant is null) return;
            if (participant is ISettlementListener s)        _settlement.Remove(s);
            if (participant is IMilitaryOperationListener m) _militaryOp.Remove(m);
            if (participant is IMercenarySquadListener q)    _mercSquad.Remove(q);
            if (participant is IResearchListener r)          _research.Remove(r);
        }

        public static void ClearAll()
        {
            _settlement.Clear();
            _militaryOp.Clear();
            _mercSquad.Clear();
            _research.Clear();
        }

        /* Settlement */
        public static void InvokeOnSettlementCreated(WorldSettlementFC settlement)
        {
            foreach (ISettlementListener p in _settlement)
            {
                try { p.OnSettlementCreated(settlement); }
                catch (Exception e) { LogUtil.Error($"ISettlementListener {p.GetType().Name} threw in OnSettlementCreated: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
        }

        public static void InvokeOnSettlementRemoved(WorldSettlementFC settlement)
        {
            foreach (ISettlementListener p in _settlement)
            {
                try { p.OnSettlementRemoved(settlement); }
                catch (Exception e) { LogUtil.Error($"ISettlementListener {p.GetType().Name} threw in OnSettlementRemoved: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
        }

        public static void InvokeOnSettlementUpgraded(WorldSettlementFC settlement, int oldLevel, int newLevel)
        {
            foreach (ISettlementListener p in _settlement)
            {
                try { p.OnSettlementUpgraded(settlement, oldLevel, newLevel); }
                catch (Exception e) { LogUtil.Error($"ISettlementListener {p.GetType().Name} threw in OnSettlementUpgraded: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
        }

        public static void InvokeOnSettlementTypeChanged(WorldSettlementFC settlement, WorldSettlementDef oldDef, WorldSettlementDef newDef)
        {
            foreach (ISettlementListener p in _settlement)
            {
                try { p.OnSettlementTypeChanged(settlement, oldDef, newDef); }
                catch (Exception e) { LogUtil.Error($"ISettlementListener {p.GetType().Name} threw in OnSettlementTypeChanged: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
        }

        /* Building */
        public static void InvokeOnBuildingConstructed(WorldSettlementFC settlement, BuildingFCDef building, int slot)
        {
            foreach (ISettlementListener p in _settlement)
            {
                try { p.OnBuildingConstructed(settlement, building, slot); }
                catch (Exception e) { LogUtil.Error($"ISettlementListener {p.GetType().Name} threw in OnBuildingConstructed: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
        }

        public static void InvokeOnBuildingDeconstructed(WorldSettlementFC settlement, BuildingFCDef building, int slot)
        {
            foreach (ISettlementListener p in _settlement)
            {
                try { p.OnBuildingDeconstructed(settlement, building, slot); }
                catch (Exception e) { LogUtil.Error($"ISettlementListener {p.GetType().Name} threw in OnBuildingDeconstructed: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                settlement.InvalidateStatCache();
            }
            settlement.InvalidateStatCache();
        }

        /* Military Operation */

        public static void InvokeOnOperationCreated(MilitaryOperation op)
        {
            if (op is null) return;
            WorldSettlementFC settlement = op.aggressor?.homeSettlement ?? op.defender?.homeSettlement;
            foreach (IMilitaryOperationListener p in _militaryOp)
            {
                try { p.OnOperationCreated(op); }
                catch (Exception e) { LogUtil.Error($"IMilitaryOperationListener {p.GetType().Name} threw in OnOperationCreated: {e}"); }
                if (settlement is object) settlement.InvalidateStatCache();
            }
            if (settlement is object) settlement.InvalidateStatCache();
        }

        public static void InvokeOnOperationResolved(MilitaryOperation op)
        {
            if (op is null) return;
            WorldSettlementFC settlement = op.aggressor?.homeSettlement ?? op.defender?.homeSettlement;
            foreach (IMilitaryOperationListener p in _militaryOp)
            {
                try { p.OnOperationResolved(op); }
                catch (Exception e) { LogUtil.Error($"IMilitaryOperationListener {p.GetType().Name} threw in OnOperationResolved: {e}"); }
                if (settlement is object) settlement.InvalidateStatCache();
            }
            if (settlement is object) settlement.InvalidateStatCache();
        }

        public static void InvokeOnBattleResolved(MilitaryOperation op, bool victory, BattleResult result)
        {
            if (op is null) return;
            WorldSettlementFC settlement = op.aggressor?.homeSettlement ?? op.defender?.homeSettlement;
            foreach (IMilitaryOperationListener p in _militaryOp)
            {
                try { p.OnBattleResolved(op, victory, result); }
                catch (Exception e) { LogUtil.Error($"IMilitaryOperationListener {p.GetType().Name} threw in OnBattleResolved: {e}"); }
                if (settlement is object) settlement.InvalidateStatCache();
            }
            if (settlement is object) settlement.InvalidateStatCache();
        }

        /* Mercenary */
        public static void InvokeOnMercenaryDeath(MercenaryDeathEvent evt)
        {
            foreach (IMercenarySquadListener p in _mercSquad)
            {
                try { p.OnMercenaryDeath(evt); }
                catch (Exception e) { LogUtil.Error($"IMercenarySquadListener {p.GetType().Name} threw in OnMercenaryDeath: {e}"); }
            }
        }

        /* Squad lifecycle */
        public static void InvokeOnSquadHired(MercenarySquadFC squad)
        {
            if (squad is null) return;
            foreach (IMercenarySquadListener p in _mercSquad)
            {
                try { p.OnSquadHired(squad); }
                catch (Exception e) { LogUtil.Error($"IMercenarySquadListener {p.GetType().Name} threw in OnSquadHired: {e}"); }
            }
        }

        public static void InvokeOnSquadDismissed(MercenarySquadFC squad)
        {
            if (squad is null) return;
            foreach (IMercenarySquadListener p in _mercSquad)
            {
                try { p.OnSquadDismissed(squad); }
                catch (Exception e) { LogUtil.Error($"IMercenarySquadListener {p.GetType().Name} threw in OnSquadDismissed: {e}"); }
            }
        }

        public static void InvokeOnSquadUpgraded(MercenarySquadFC squad)
        {
            if (squad is null) return;
            foreach (IMercenarySquadListener p in _mercSquad)
            {
                try { p.OnSquadUpgraded(squad); }
                catch (Exception e) { LogUtil.Error($"IMercenarySquadListener {p.GetType().Name} threw in OnSquadUpgraded: {e}"); }
            }
        }

        /* Research */
        public static void InvokeOnResearchCompleted(ResearchProjectDef project)
        {
            foreach (IResearchListener p in _research)
            {
                try { p.OnResearchCompleted(project); }
                catch (Exception e) { LogUtil.Error($"IResearchListener {p.GetType().Name} threw in OnResearchCompleted: {e}"); }
                // Intentional: invalidate per-participant so the next participant sees fresh cache
                FactionCache.FactionComp?.InvalidateAllSettlementStatCaches();
            }
            FactionCache.FactionComp?.InvalidateAllSettlementStatCaches();
        }
    }
}
