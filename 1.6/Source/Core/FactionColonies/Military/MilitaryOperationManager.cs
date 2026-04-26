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

        /* -*-*-*-*- Mutation -*-*-*-*-
         * Phase 2 fills these.
         */

        public void Register(MilitaryOperation op)
        {
            throw new NotImplementedException("MilitaryOperationManager.Register filled in Phase 2.");
        }

        public void Unregister(MilitaryOperation op)
        {
            throw new NotImplementedException("MilitaryOperationManager.Unregister filled in Phase 2.");
        }

        /// <summary>Tick driver. Replaces FCEventMaker's military switch — military events
        /// dispatch via op.OnEventFired in the new model.</summary>
        public void Tick()
        {
            // Phase 2 implements; safe no-op in Phase 1 since no ops exist yet.
        }

        public MilitaryOperation CreateOffensiveOp(WorldSettlementFC homeSettlement, WorldObject target,
            MilitaryJobDef jobDef, Faction enemy)
        {
            throw new NotImplementedException("MilitaryOperationManager.CreateOffensiveOp filled in Phase 2.");
        }

        public MilitaryOperation CreateDefensiveOp(WorldObject target, MilitaryForce attackerForce,
            Faction attackerFaction)
        {
            throw new NotImplementedException("MilitaryOperationManager.CreateDefensiveOp filled in Phase 2.");
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
