using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Single source of truth for all active <see cref="MilitaryOperation"/>s on a faction.
    /// Lives as a field on <see cref="FactionFC"/> and is reachable via
    /// <c>FactionCache.MilitaryManager</c>.
    /// <para>Owns the canonical <c>active</c> list, indexed lookups (by tile, by squad, by
    /// settlement), and the per-tile <see cref="BattlefieldContext"/> dictionary. Indices are
    /// <c>[Unsaved]</c> and rebuilt from <c>active</c> at load (in <see cref="RebuildIndices"/>).</para>
    /// <para>Phase 1: skeleton + serialization. Phase 2 fills query and mutation method bodies.</para>
    /// </summary>
    public class MilitaryOperationManager : IExposable
    {
        /* Saved state */
        public List<MilitaryOperation> active = new List<MilitaryOperation>();
        public Dictionary<PlanetTile, BattlefieldContext> battlefields = new Dictionary<PlanetTile, BattlefieldContext>();
        public int nextOperationId = 1;

        /* Indices — rebuilt on load, not scribed */
        [Unsaved] private Dictionary<PlanetTile, List<MilitaryOperation>> _byTile;
        [Unsaved] private Dictionary<MercenarySquadFC, MilitaryOperation> _bySquad;
        [Unsaved] private Dictionary<WorldSettlementFC, List<MilitaryOperation>> _bySettlement;

        /* Scribe scratch buffers for the battlefields dict. RimWorld's
         * Scribe_Collections.Look(ref dict) requires working lists during load. */
        [Unsaved] private List<PlanetTile> _battlefieldKeyScratch;
        [Unsaved] private List<BattlefieldContext> _battlefieldValueScratch;

        public MilitaryOperationManager()
        {
            _byTile = new Dictionary<PlanetTile, List<MilitaryOperation>>();
            _bySquad = new Dictionary<MercenarySquadFC, MilitaryOperation>();
            _bySettlement = new Dictionary<WorldSettlementFC, List<MilitaryOperation>>();
        }

        public bool IsEmpty => (active is null || active.Count == 0)
                            && (battlefields is null || battlefields.Count == 0);

        public IReadOnlyList<MilitaryOperation> Active => active;
        public IReadOnlyDictionary<PlanetTile, BattlefieldContext> Battlefields => battlefields;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref active, "active", LookMode.Deep);
            Scribe_Collections.Look(ref battlefields, "battlefields",
                LookMode.Value, LookMode.Deep,
                ref _battlefieldKeyScratch, ref _battlefieldValueScratch);
            Scribe_Values.Look(ref nextOperationId, "nextOperationId", 1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (active is null) active = new List<MilitaryOperation>();
                if (battlefields is null) battlefields = new Dictionary<PlanetTile, BattlefieldContext>();
                EnsureIndicesAllocated();
            }
        }

        private void EnsureIndicesAllocated()
        {
            if (_byTile is null) _byTile = new Dictionary<PlanetTile, List<MilitaryOperation>>();
            if (_bySquad is null) _bySquad = new Dictionary<MercenarySquadFC, MilitaryOperation>();
            if (_bySettlement is null) _bySettlement = new Dictionary<WorldSettlementFC, List<MilitaryOperation>>();
        }

        /* -*-*-*-*- Mutation -*-*-*-*- */

        /// <summary>Adds <paramref name="op"/> to the active list and updates indices.
        /// Idempotent: registering the same op twice is a no-op.</summary>
        public void Register(MilitaryOperation op)
        {
            if (op is null) return;
            if (active is null) active = new List<MilitaryOperation>();
            if (active.Contains(op)) return;
            active.Add(op);
            IndexAdd(op);
        }

        /// <summary>Removes <paramref name="op"/> from the active list, drops it from indices,
        /// and detaches from any <see cref="BattlefieldContext"/>. Safe to call after <c>op.Resolve</c>
        /// has already removed map state.</summary>
        public void Unregister(MilitaryOperation op)
        {
            if (op is null) return;
            // Detach first so the context can clean up before the op's tile reference is wiped.
            if (op.battlefieldRef.Valid)
            {
                BattlefieldContext ctx = GetBattlefield(op.battlefieldRef);
                ctx?.Detach(op);
            }
            IndexRemove(op);
            if (active is object) active.Remove(op);
        }

        /// <summary>Tick driver. Replaces FCEventMaker's military switch — military events
        /// dispatch via op.OnEventFired in the new model. Currently a no-op (event-driven).</summary>
        public void Tick()
        {
            // Reserved for future op-driven timer work (e.g. checking for stalled manual battles).
        }

        /// <summary>
        /// Creates an offensive operation: empire <paramref name="homeSettlement"/> sends its
        /// squad on a job (raid / capture / enslave / defend-friendly) against
        /// <paramref name="target"/> belonging to <paramref name="enemy"/>. Returns the
        /// registered op. The handler's <see cref="MilitaryJobHandler.OnOpCreated"/> is called
        /// after registration so it can schedule the arrival event and send any letters.
        /// </summary>
        public MilitaryOperation CreateOffensiveOp(WorldSettlementFC homeSettlement, WorldObject target,
            MilitaryJobDef jobDef, Faction enemy, int timeToFinish)
        {
            if (homeSettlement is null) throw new ArgumentNullException(nameof(homeSettlement));
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (jobDef is null) throw new ArgumentNullException(nameof(jobDef));

            int newId = nextOperationId++;
            var op = new MilitaryOperation(newId, jobDef, target.Tile, target);
            op.phase = MilitaryOperationPhase.Traveling;
            op.nextPhaseTick = Find.TickManager.TicksGame + Math.Max(0, timeToFinish);

            op.aggressor.faction = FactionCache.PlayerColonyFaction;
            op.aggressor.homeSettlement = homeSettlement;
            op.aggressor.squad = homeSettlement.MilitaryComp?.militarySquad;
            op.aggressor.force = MilitaryForce.CreateMilitaryForceFromSettlement(homeSettlement, isAttacking: true);

            op.defender.faction = enemy;
            // op.defender.force is computed lazily in BeginEngagement via CreateMilitaryForceFromFaction.

            Register(op);

            try
            {
                jobDef.Handler?.OnOpCreated(op);
            }
            catch (Exception e)
            {
                LogUtil.Error($"MilitaryOperationManager.CreateOffensiveOp: handler {jobDef.Handler?.GetType().Name} threw in OnOpCreated: {e}");
            }

            LifecycleRegistry.InvokeOnSquadDeployed(op);
            return op;
        }

        /// <summary>
        /// Creates a defensive operation: <paramref name="attackerFaction"/> launches
        /// <paramref name="attackerForce"/> at <paramref name="target"/> (an Empire settlement
        /// or external <see cref="IRaidTarget"/>). Returns the registered op. Schedules a
        /// 24-hour <c>settlementBeingAttacked</c> warning event linked to the op.
        /// </summary>
        public MilitaryOperation CreateDefensiveOp(WorldObject target, MilitaryForce attackerForce,
            Faction attackerFaction)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (attackerForce is null) throw new ArgumentNullException(nameof(attackerForce));

            int newId = nextOperationId++;
            // Defensive ops have no MilitaryJobDef — kind is null. CompleteBattle handles a null
            // handler via the simulator directly.
            var op = new MilitaryOperation(newId, null, target.Tile, target);
            op.phase = MilitaryOperationPhase.Scheduled;
            op.nextPhaseTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;

            op.aggressor.faction = attackerFaction;
            op.aggressor.force = attackerForce;

            op.defender.faction = FactionCache.PlayerColonyFaction;
            if (target is WorldSettlementFC ws)
            {
                op.defender.homeSettlement = ws;
                op.defender.force = MilitaryForce.CreateMilitaryForceFromSettlement(ws);
            }
            else
            {
                // External raid target — caller fills in defender.force later (or it stays the
                // settlement-derived default once an auto-defender is assigned).
            }

            Register(op);

            // Schedule the 24-hour warning. Defensive ops own their wakeup directly (no handler).
            op.ScheduleEvent(FCEventDefOf.settlementBeingAttacked, target.Tile, GenDate.TicksPerDay);

            LifecycleRegistry.InvokeOnSquadDeployed(op);
            return op;
        }

        /// <summary>Get-or-create a <see cref="BattlefieldContext"/> for <paramref name="tile"/>.</summary>
        public BattlefieldContext GetOrCreateBattlefield(PlanetTile tile)
        {
            if (battlefields is null) battlefields = new Dictionary<PlanetTile, BattlefieldContext>();
            if (!battlefields.TryGetValue(tile, out BattlefieldContext ctx))
            {
                ctx = new BattlefieldContext(tile);
                battlefields[tile] = ctx;
            }
            return ctx;
        }

        /// <summary>Removes the battlefield at <paramref name="tile"/> from the manager.
        /// Does not clean up its map / pawns — that's the context's job during <c>Detach</c>.</summary>
        internal void RemoveBattlefield(PlanetTile tile)
        {
            if (battlefields is object) battlefields.Remove(tile);
        }

        /* -*-*-*-*- Queries -*-*-*-*-
         * Phase 1 implements safe stubs returning empty/false so call sites added in Phase 2
         * have a working surface area while op transitions are still stubbed.
         */

        public IReadOnlyList<MilitaryOperation> GetOpsAt(PlanetTile tile)
        {
            EnsureIndicesAllocated();
            if (_byTile.TryGetValue(tile, out List<MilitaryOperation> list)) return list;
            return Array.Empty<MilitaryOperation>();
        }

        public MilitaryOperation GetOpForSquad(MercenarySquadFC squad)
        {
            if (squad is null) return null;
            EnsureIndicesAllocated();
            _bySquad.TryGetValue(squad, out MilitaryOperation op);
            return op;
        }

        public IReadOnlyList<MilitaryOperation> GetOpsForSettlement(WorldSettlementFC settlement)
        {
            if (settlement is null) return Array.Empty<MilitaryOperation>();
            EnsureIndicesAllocated();
            if (_bySettlement.TryGetValue(settlement, out List<MilitaryOperation> list)) return list;
            return Array.Empty<MilitaryOperation>();
        }

        public bool HasDefenseAt(WorldSettlementFC settlement)
        {
            if (settlement is null) return false;
            IReadOnlyList<MilitaryOperation> ops = GetOpsForSettlement(settlement);
            for (int i = 0; i < ops.Count; i++)
            {
                MilitaryOperation op = ops[i];
                if (op.defender.homeSettlement == settlement) return true;
            }
            return false;
        }

        public bool HasOffensiveOpFrom(WorldSettlementFC settlement)
        {
            if (settlement is null) return false;
            IReadOnlyList<MilitaryOperation> ops = GetOpsForSettlement(settlement);
            for (int i = 0; i < ops.Count; i++)
            {
                MilitaryOperation op = ops[i];
                if (op.aggressor.homeSettlement == settlement) return true;
            }
            return false;
        }

        public bool IsSquadBusy(MercenarySquadFC squad) => GetOpForSquad(squad) is object;

        public bool IsTileOccupiedBy(PlanetTile tile, MilitaryJobDef kind)
        {
            IReadOnlyList<MilitaryOperation> ops = GetOpsAt(tile);
            for (int i = 0; i < ops.Count; i++)
            {
                if (ops[i].kind == kind) return true;
            }
            return false;
        }

        public BattlefieldContext GetBattlefield(PlanetTile tile)
        {
            if (battlefields is null) return null;
            battlefields.TryGetValue(tile, out BattlefieldContext ctx);
            return ctx;
        }

        public MilitaryOperation GetOp(int id)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].id == id) return active[i];
            }
            return null;
        }

        /// <summary>Rebuilds the per-tile / per-squad / per-settlement indices from <see cref="active"/>.
        /// Called from <c>FactionFC.FinalizeInit</c> and after migration.</summary>
        public void RebuildIndices()
        {
            EnsureIndicesAllocated();
            _byTile.Clear();
            _bySquad.Clear();
            _bySettlement.Clear();

            if (active is null) return;
            for (int i = 0; i < active.Count; i++)
            {
                MilitaryOperation op = active[i];
                if (op is null) continue;
                IndexAdd(op);
            }
        }

        internal void IndexAdd(MilitaryOperation op)
        {
            EnsureIndicesAllocated();
            if (op.targetTile.Valid)
            {
                if (!_byTile.TryGetValue(op.targetTile, out List<MilitaryOperation> tileList))
                {
                    tileList = new List<MilitaryOperation>();
                    _byTile[op.targetTile] = tileList;
                }
                if (!tileList.Contains(op)) tileList.Add(op);
            }
            if (op.aggressor?.squad is object) _bySquad[op.aggressor.squad] = op;
            if (op.defender?.squad is object) _bySquad[op.defender.squad] = op;
            IndexSettlement(op, op.aggressor?.homeSettlement);
            IndexSettlement(op, op.defender?.homeSettlement);
        }

        internal void IndexRemove(MilitaryOperation op)
        {
            EnsureIndicesAllocated();
            if (op.targetTile.Valid && _byTile.TryGetValue(op.targetTile, out List<MilitaryOperation> tileList))
            {
                tileList.Remove(op);
                if (tileList.Count == 0) _byTile.Remove(op.targetTile);
            }
            if (op.aggressor?.squad is object) _bySquad.Remove(op.aggressor.squad);
            if (op.defender?.squad is object) _bySquad.Remove(op.defender.squad);
            UnindexSettlement(op, op.aggressor?.homeSettlement);
            UnindexSettlement(op, op.defender?.homeSettlement);
        }

        private void IndexSettlement(MilitaryOperation op, WorldSettlementFC settlement)
        {
            if (settlement is null) return;
            if (!_bySettlement.TryGetValue(settlement, out List<MilitaryOperation> list))
            {
                list = new List<MilitaryOperation>();
                _bySettlement[settlement] = list;
            }
            if (!list.Contains(op)) list.Add(op);
        }

        private void UnindexSettlement(MilitaryOperation op, WorldSettlementFC settlement)
        {
            if (settlement is null) return;
            if (_bySettlement.TryGetValue(settlement, out List<MilitaryOperation> list))
            {
                list.Remove(op);
                if (list.Count == 0) _bySettlement.Remove(settlement);
            }
        }
    }
}
