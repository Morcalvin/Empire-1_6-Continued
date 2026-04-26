using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using FactionColonies.util;

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
        /// or external <see cref="IRaidTarget"/>). Returns the registered op (or <c>null</c> if
        /// rejected — already-active defense, missing target comp, etc).
        /// <para>Runs auto-defender selection: scans Empire settlements with <c>autoDefend</c>
        /// for the strongest non-busy non-attacked one that beats the target's level, plus the
        /// best <see cref="IAutoDefender"/> registry entry in range. Whichever is stronger wins;
        /// if a foreign defender wins, its comp gets a legacy <c>DefendFriendlySettlement</c>
        /// marker for back-compat with code that still checks <c>militaryBusy</c>/<c>militaryJob</c>.</para>
        /// <para>Schedules the 24-hour <c>settlementBeingAttacked</c> warning event linked back
        /// to the op via <see cref="FCEvent.linkedOperationId"/>. The event also carries the
        /// force fields so legacy <c>comp.StartDefence</c> can consume it unchanged when the
        /// op fires the manual-battle path.</para>
        /// </summary>
        public MilitaryOperation CreateDefensiveOp(WorldObject target, MilitaryForce attackerForce,
            Faction attackerFaction)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (attackerForce is null) throw new ArgumentNullException(nameof(attackerForce));

            FactionFC factionFC = FactionCache.FactionComp;
            if (factionFC is null) return null;

            // Resolve the target settlement (when target is a WorldSettlementFC) so we can run
            // the auto-defender selection. External raid targets get a force generated from
            // their virtual military level via the IAutoDefender path or stay as-is.
            WorldSettlementFC targetSettlement = target as WorldSettlementFC;

            int newId = nextOperationId++;
            // Defensive ops have no MilitaryJobDef — kind is null. CompleteBattle skips the
            // handler.ApplyResult dispatch when no handler is set; settlement-side effects come
            // from comp.EndBattle / op.CompleteBattle's defensive path.
            var op = new MilitaryOperation(newId, null, target.Tile, target);
            op.phase = MilitaryOperationPhase.Scheduled;
            op.nextPhaseTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;

            op.aggressor.faction = attackerFaction;
            op.aggressor.force = attackerForce;

            op.defender.faction = FactionCache.PlayerColonyFaction;
            if (targetSettlement is object)
            {
                op.defender.homeSettlement = targetSettlement;
                op.defender.force = MilitaryForce.CreateMilitaryForceFromSettlement(targetSettlement);
            }
            // For external raid targets, defender.force is set below by the auto-defender path.

            Register(op);

            // Auto-defender selection. Picks the strongest replacement defender (Empire foreign
            // settlement or external IAutoDefender) if it beats whatever the op currently uses.
            ApplyAutoDefenderSelection(op, target, targetSettlement, factionFC);

            // Schedule the warning event. Force fields are populated so legacy comp.StartDefence
            // (driven from op.OnEventFired's manual-battle branch) can consume the event.
            FCEvent warningEvent = op.ScheduleEvent(
                FCEventDefOf.settlementBeingAttacked, target.Tile, GenDate.TicksPerDay);
            if (warningEvent is object)
            {
                // Populate legacy event fields so BattlefieldContext.StartDefence (driven from
                // op.OnEventFired's manual-battle branch) can construct its DefenseWave from the
                // event. These FCEvent fields are [Obsolete] going forward — once the wave model
                // reads from MilitaryOperation directly, this block goes away.
#pragma warning disable 0618
                warningEvent.militaryForceAttacking = op.aggressor.force;
                warningEvent.militaryForceAttackingFaction = op.aggressor.faction;
                warningEvent.militaryForceDefending = op.defender.force;
                warningEvent.militaryForceDefendingFaction = op.defender.faction;
                warningEvent.settlementFCDefending = target;
                warningEvent.externalDefenderSource = op.externalDefenderSource;
#pragma warning restore 0618
                warningEvent.hasDestination = true;

                // Description + win-chance forecast (mirrors old AttackPlayerSettlement letter).
                string desc = "FCSettlementAboutToBeAttacked".Translate(target.Label, attackerFaction?.Name ?? "").ToString();
                if (op.aggressor.force is object && op.defender.force is object)
                {
                    double winChance = SimulateBattleFc.CalculateDefenderWinChance(op.aggressor.force, op.defender.force);
                    desc += "\n\n" + "FCBattleForecast".Translate(
                        op.aggressor.force.forceRemaining,
                        op.aggressor.force.militaryEfficiency.ToString("0.##"),
                        op.defender.force.DefensivePower,
                        op.defender.force.militaryEfficiency.ToString("0.##"),
                        (winChance * 100).ToString("F0"));
                }
                if (op.externalDefenderSource is object)
                {
                    desc += "\n\n" + "FCExternalDefenderAutoAssigned".Translate(op.externalDefenderSource.LabelCap);
                }
                if (FCSettings.battleMode == BattleMode.Hybrid)
                    desc += "\n\n" + "FCSettlementAttackHybridHint".Translate();
                warningEvent.hasCustomDescription = true;
                warningEvent.customDescription = desc;
            }

            LifecycleRegistry.InvokeOnSquadDeployed(op);

            // "Settlement in danger" letter, mirrors old AttackPlayerSettlement.
            try
            {
                Find.LetterStack.ReceiveLetter(
                    "FCSettlementInDanger".Translate(),
                    warningEvent?.customDescription ?? "",
                    LetterDefOf.ThreatBig,
                    new LookTargets(target));
            }
            catch (Exception e)
            {
                LogUtil.Error($"CreateDefensiveOp: failed sending FCSettlementInDanger letter: {e}");
            }

            return op;
        }

        /// <summary>
        /// Runs auto-defender selection for a freshly-created defensive op.
        /// Mutates <paramref name="op"/>'s <c>defender</c> participant if a stronger foreign
        /// settlement or external <see cref="IAutoDefender"/> is selected.
        /// </summary>
        private static void ApplyAutoDefenderSelection(MilitaryOperation op, WorldObject target,
            WorldSettlementFC targetSettlement, FactionFC factionFC)
        {
            // Find strongest eligible Empire foreign defender.
            WorldSettlementFC bestForeign = null;
            foreach (WorldSettlementFC candidate in factionFC.settlements)
            {
                if (candidate == targetSettlement) continue;
                var mc = candidate.MilitaryComp;
                if (mc is null) continue;
                if (!mc.autoDefend || mc.militaryBusy || mc.isUnderAttack) continue;
                if (targetSettlement is object && !DefenseValidatorRegistry.CanDefend(candidate, targetSettlement)) continue;
                if (bestForeign is null || candidate.settlementMilitaryLevel > bestForeign.settlementMilitaryLevel)
                {
                    bestForeign = candidate;
                }
            }

            // Find best external auto-defender in range.
            IAutoDefender bestExternal = AutoDefenderRegistry.FindBestDefender(target.Tile, 0);

            int targetLevel = targetSettlement?.settlementMilitaryLevel ?? 0;
            int foreignLevel = bestForeign?.settlementMilitaryLevel ?? 0;
            int externalLevel = bestExternal?.MilitaryLevel ?? 0;

            // Foreign settlement wins if it beats both the target's level and any external option.
            if (bestForeign is object && foreignLevel > targetLevel && foreignLevel >= externalLevel)
            {
                MilitaryForce homeForce = targetSettlement is object
                    ? MilitaryForce.CreateMilitaryForceFromSettlement(targetSettlement, isAttacking: true)
                    : null;
                op.defender.homeSettlement = bestForeign;
                op.defender.force = MilitaryForce.CreateMilitaryForceFromSettlement(bestForeign, isAttacking: false, homeDefendingForce: homeForce);
                op.externalDefenderSource = null;
                return;
            }

            // External wins if it beats the target's level (and the foreign was not stronger).
            // OnDefenseStarted fires from MilitaryOperation.BeginEngagement when the warning event
            // resolves — not here at op creation. externalDefenderSource is the contract that
            // makes BeginEngagement notify the defender.
            if (bestExternal is object && externalLevel > targetLevel)
            {
                op.defender.force = bestExternal.CreateDefendingForce();
                op.externalDefenderSource = bestExternal.WorldObject;
                return;
            }

            // No replacement defender — op.defender keeps its target-settlement default.
        }

        /// <summary>
        /// Creates a "deploy" operation: the empire's squad is spawned on a player map (typically
        /// the home colony) for direct combat support. Unlike offensive ops, there's no arrival
        /// event — the squad is already physical when this fires. The op stays in <c>Engaged</c>
        /// until the squad's lord finalizes (via <see cref="MilitaryOperation.CompleteBattle"/>),
        /// then transitions through cooldown like any other op.
        /// </summary>
        public MilitaryOperation CreateDeployOp(WorldSettlementFC homeSettlement, PlanetTile deployTile)
        {
            if (homeSettlement is null) throw new ArgumentNullException(nameof(homeSettlement));

            int newId = nextOperationId++;
            // Use the current map's WorldObject as the targetObject if present, otherwise null.
            WorldObject targetObject = Find.WorldObjects.WorldObjectAt<WorldObject>(deployTile);
            var op = new MilitaryOperation(newId, MilitaryJobDefOf.Deploy, deployTile, targetObject);
            op.phase = MilitaryOperationPhase.Engaged;
            op.phaseStartedTick = Find.TickManager.TicksGame;
            op.aggressor.faction = FactionCache.PlayerColonyFaction;
            op.aggressor.homeSettlement = homeSettlement;
            op.aggressor.squad = homeSettlement.MilitaryComp?.militarySquad;
            // No defender — Deploy isn't an attack operation, just squad presence.

            Register(op);
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
