using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Single source of truth for one ongoing military operation. Holds participants, phase,
    /// timer, and a back-reference to its <see cref="BattlefieldContext"/> (if any). Scheduled
    /// FCEvents reference the op via <see cref="FCEvent.linkedOperationId"/>; on fire they call
    /// back into <see cref="OnEventFired"/>.
    /// <para>Created and owned by <see cref="MilitaryOperationManager"/>; do not instantiate directly.</para>
    /// </summary>
    public class MilitaryOperation : IExposable, ILoadReferenceable
    {
        /* Identity */
        public int id = -1;
        public MilitaryJobDef kind;

        /* Phase / timing */
        public MilitaryOperationPhase phase = MilitaryOperationPhase.Scheduled;
        public int phaseStartedTick = -1;
        /// <summary>Tick at which the op's timer is expected to next advance the phase.
        /// Mirrors / replaces FCEvent.timeTillTrigger semantics for op-driven timers.</summary>
        public int nextPhaseTick = -1;

        /* Target */
        public PlanetTile targetTile = PlanetTile.Invalid;
        public WorldObject targetObject;

        /* Participants */
        public MilitaryOperationParticipant aggressor = new MilitaryOperationParticipant();
        public MilitaryOperationParticipant defender = new MilitaryOperationParticipant();

        /* External auto-defender that supplied the defending force, if any. Persisted so the
         * appropriate IAutoDefender callbacks fire on engagement and resolution. */
        public WorldObject externalDefenderSource;

        /* Result, set on resolution */
        public BattleResult result;

        /* Wakeup events scheduled by this op (arrival, cooldown, ...). */
        public List<FCEvent> sourceEvents = new List<FCEvent>();

        /// <summary>Tile of the active <see cref="BattlefieldContext"/> this op is attached to.
        /// <see cref="PlanetTile.Invalid"/> when the op has no battlefield (auto-resolved or pre-engagement).</summary>
        public PlanetTile battlefieldRef = PlanetTile.Invalid;

        public MilitaryOperation() { }

        public MilitaryOperation(int id, MilitaryJobDef kind, PlanetTile targetTile, WorldObject targetObject)
        {
            this.id = id;
            this.kind = kind;
            this.targetTile = targetTile;
            this.targetObject = targetObject;
            this.phaseStartedTick = Find.TickManager.TicksGame;
        }

        public bool HasMapPresence => battlefieldRef.Valid;

        /// <summary>True when the player's empire is on the defending side (settlement under attack).</summary>
        public bool IsDefensive => defender?.faction is object
                                && FactionCache.PlayerColonyFaction is object
                                && defender.faction == FactionCache.PlayerColonyFaction;

        /// <summary>True when the player's empire is on the aggressor side (offensive op).</summary>
        public bool IsOffensive => aggressor?.faction is object
                                && FactionCache.PlayerColonyFaction is object
                                && aggressor.faction == FactionCache.PlayerColonyFaction;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", -1);
            Scribe_Defs.Look(ref kind, "kind");
            Scribe_Values.Look(ref phase, "phase", MilitaryOperationPhase.Scheduled);
            Scribe_Values.Look(ref phaseStartedTick, "phaseStartedTick", -1);
            Scribe_Values.Look(ref nextPhaseTick, "nextPhaseTick", -1);
            Scribe_Values.Look(ref targetTile, "targetTile", PlanetTile.Invalid);
            Scribe_References.Look(ref targetObject, "targetObject");
            Scribe_Deep.Look(ref aggressor, "aggressor");
            Scribe_Deep.Look(ref defender, "defender");
            Scribe_References.Look(ref externalDefenderSource, "externalDefenderSource");
            Scribe_Deep.Look(ref result, "result");
            Scribe_Collections.Look(ref sourceEvents, "sourceEvents", LookMode.Reference);
            Scribe_Values.Look(ref battlefieldRef, "battlefieldRef", PlanetTile.Invalid);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (aggressor is null) aggressor = new MilitaryOperationParticipant();
                if (defender is null) defender = new MilitaryOperationParticipant();
                if (sourceEvents is null) sourceEvents = new List<FCEvent>();
            }
        }

        public string GetUniqueLoadID() => "MilitaryOperation_" + id;

        /* -*-*-*-*- Event scheduling helper -*-*-*-*- */

        /// <summary>
        /// Creates an FCEvent linked back to this op (via <see cref="FCEvent.linkedOperationId"/>),
        /// calls <c>DefineEvent</c> on it (which queues it on FactionFC), and tracks it in
        /// <see cref="sourceEvents"/>. Used by the manager / handlers to schedule arrival / warning /
        /// cooldown wakeups.
        /// </summary>
        public FCEvent ScheduleEvent(FCEventDef eventDef, PlanetTile location, int ticksFromNow,
            string customDescription = null)
        {
            FactionFC factionFC = FactionCache.FactionComp;
            if (factionFC is null)
            {
                LogUtil.Error($"MilitaryOperation.ScheduleEvent: no FactionFC available; op id={id}.");
                return null;
            }
            FCEvent evt = FCEventMaker.MakeEvent(eventDef);
            evt.linkedOperationId = id;
            if (!string.IsNullOrEmpty(customDescription))
            {
                evt.hasCustomDescription = true;
                evt.customDescription = customDescription;
            }
            evt.DefineEvent(factionFC, location, ticksFromNow);
            sourceEvents.Add(evt);
            return evt;
        }

        /* -*-*-*-*- Transitions -*-*-*-*-
         * Each transition fires the right registries / hooks internally so callers don't have to
         * remember which hook goes where. Methods are idempotent in the sense that calling them
         * out of phase is logged and ignored rather than throwing.
         */

        /// <summary>
        /// Move the op from its pre-engagement phase (Scheduled / Traveling) into Engaged.
        /// Computes any missing forces (e.g. defender side for an offensive op fights a faction-
        /// derived force), runs <see cref="BattleModifierRegistry"/> in op-aware mode, and fires
        /// <see cref="IAutoDefender.OnDefenseStarted"/> if an external auto-defender supplied the
        /// defending force.
        /// </summary>
        public void BeginEngagement()
        {
            phase = MilitaryOperationPhase.Engaged;
            phaseStartedTick = Find.TickManager.TicksGame;

            // Lazily create defender force when an offensive op fights a non-Empire faction:
            // CreateOffensiveOp leaves it unset because the enemy is faction-level, not settlement-level.
            if (defender.force is null && defender.faction is object && !IsDefensive)
            {
                defender.force = MilitaryForce.CreateMilitaryForceFromFaction(defender.faction, false);
            }

            if (aggressor.force is object)
                BattleModifierRegistry.InvokeModifyForce(this, aggressor.force, isAttacker: true);
            if (defender.force is object)
                BattleModifierRegistry.InvokeModifyForce(this, defender.force, isAttacker: false);

            if (externalDefenderSource is object)
            {
                IAutoDefender def = AutoDefenderRegistry.FindByWorldObject(externalDefenderSource);
                def?.OnDefenseStarted(targetObject);
            }
        }

        /// <summary>
        /// Apply the resolved <paramref name="battleResult"/> to the op: store it, run the handler's
        /// <see cref="MilitaryJobHandler.ApplyResult"/> for outcome side effects (loot, prisoners,
        /// settlement capture, ...), fire <see cref="LifecycleRegistry"/> + auto-defender hooks,
        /// then advance to <see cref="MilitaryOperationPhase.CooldownPending"/>.
        /// </summary>
        public void CompleteBattle(BattleResult battleResult)
        {
            this.result = battleResult;
            bool victory = IsDefensive
                ? battleResult is object && battleResult.DefenderVictory
                : battleResult is object && battleResult.AttackerVictory;

            // Outcome side effects via handler (offensive ops; defensive ops generally have null kind
            // or DefendFriendlySettlement and the handler.ApplyResult is a no-op default).
            try
            {
                kind?.Handler?.ApplyResult(this, battleResult);
            }
            catch (Exception e)
            {
                LogUtil.Error($"MilitaryOperation.CompleteBattle: handler {kind?.Handler?.GetType().Name} threw in ApplyResult: {e}");
            }

            LifecycleRegistry.InvokeOnBattleResolved(this, victory, battleResult);

            if (externalDefenderSource is object)
            {
                IAutoDefender def = AutoDefenderRegistry.FindByWorldObject(externalDefenderSource);
                def?.OnDefenseComplete(victory, battleResult);
            }

            // Overwhelming-victory shortcut: foreign defender that won without losing any defenders
            // is freed immediately (no cooldown). The home settlement defending itself never gets
            // the shortcut — it's only for foreign settlements lending their squad.
            if (IsDefensive && victory && IsOverwhelmingVictory(battleResult))
            {
                bool foreignDefender = defender?.homeSettlement is object
                                    && defender.homeSettlement != (targetObject as WorldSettlementFC);
                if (foreignDefender)
                {
                    Find.LetterStack.ReceiveLetter(
                        "FCOverwhelmingVictory".Translate(),
                        "FCOverwhelmingVictoryDesc".Translate(),
                        LetterDefOf.PositiveEvent);
                    Resolve();
                    return;
                }
            }

            EnterCooldown();
        }

        private static bool IsOverwhelmingVictory(BattleResult result)
        {
            if (result is null) return false;
            // defenderInitialForce / defenderRemainingForce are populated by SimulateBattleFc for
            // auto-resolve and by comp.EndBattle for manual battles (pawn counts in that case).
            // Treat zero-zero as not-overwhelming to avoid false positives on legacy/error paths.
            if (result.defenderInitialForce <= 0) return false;
            return result.defenderRemainingForce >= result.defenderInitialForce;
        }

        /// <summary>
        /// Move the op into <see cref="MilitaryOperationPhase.CooldownPending"/> and schedule a
        /// <c>cooldownMilitary</c> FCEvent linked back to this op. Duration: 3-day base + per-job
        /// stat offset + dead-pawn penalty (see <see cref="ComputeCooldownTicks"/>).
        /// </summary>
        public void EnterCooldown()
        {
            phase = MilitaryOperationPhase.CooldownPending;
            phaseStartedTick = Find.TickManager.TicksGame;

            int cooldownTicks = ComputeCooldownTicks();

            // Cooldown event fires on the home settlement's tile (or target tile if there's no home
            // — e.g. external defender ops). Aggressor home preferred since that's where the squad
            // returns; falls back to defender home for purely-defensive ops.
            PlanetTile cooldownTile = aggressor?.homeSettlement?.Tile
                                   ?? defender?.homeSettlement?.Tile
                                   ?? targetTile;
            if (!cooldownTile.Valid) cooldownTile = targetTile;

            string desc = aggressor?.homeSettlement is object
                ? "FCMilitaryForcesReorganizing".Translate(aggressor.homeSettlement.Name).ToString()
                : null;
            ScheduleEvent(FCEventDefOf.cooldownMilitary, cooldownTile, cooldownTicks, desc);
        }

        private int ComputeCooldownTicks()
        {
            FactionFC faction = FactionCache.FactionComp;
            int cooldown = GenDate.TicksPerDay * 3;
            if (faction is object)
                cooldown += (int)faction.GetStatValue(FCStatDefOf.militaryCooldownOffset);
            if (kind is object && kind.cooldownStatDef is object && faction is object)
                cooldown += (int)faction.GetStatValue(kind.cooldownStatDef);

            // Dead-pawn cooldown: only applies to offensive ops (defensive ops don't track
            // squad deaths the same way; that work was on the comp's defense flow).
            if (kind is object && kind.deadPawnCooldown && FCSettings.deadPawnsIncreaseMilitaryCooldown)
            {
                int deaths = aggressor?.squad?.dead ?? 0;
                if (deaths > 0)
                {
                    int deadMultiplier = 10000;
                    if (faction is object)
                        deadMultiplier += (int)faction.GetStatValue(FCStatDefOf.deadPawnCooldownOffset);
                    cooldown += deaths * deadMultiplier;
                }
            }
            cooldown = Math.Max(cooldown, 0);
            if (DebugSettings.godMode) cooldown = 1;
            return cooldown;
        }

        /// <summary>
        /// Final terminal transition. Fires <see cref="LifecycleRegistry.InvokeOnSquadRecalled"/>
        /// and unregisters from the manager (which also detaches from any battlefield context).
        /// </summary>
        public void Resolve()
        {
            phase = MilitaryOperationPhase.Resolved;
            phaseStartedTick = Find.TickManager.TicksGame;

            LifecycleRegistry.InvokeOnSquadRecalled(this);

            FactionCache.MilitaryManager?.Unregister(this);
        }

        /// <summary>
        /// Get-or-create the <see cref="BattlefieldContext"/> at <see cref="targetTile"/> and
        /// register this op on it. Sets <see cref="battlefieldRef"/> to the context's tile.
        /// </summary>
        public BattlefieldContext AttachToBattlefield()
        {
            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            if (manager is null) return null;
            BattlefieldContext ctx = manager.GetOrCreateBattlefield(targetTile);
            ctx.Join(this);
            battlefieldRef = targetTile;
            return ctx;
        }

        public void DetachFromBattlefield()
        {
            if (!battlefieldRef.Valid) return;
            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            BattlefieldContext ctx = manager?.GetBattlefield(battlefieldRef);
            ctx?.Detach(this);
            battlefieldRef = PlanetTile.Invalid;
        }

        /// <summary>
        /// Called by <see cref="MilitaryOperationManager"/> (via FCEventMaker) when one of this op's
        /// scheduled FCEvents fires. Phase machine:
        /// <para>Scheduled / Traveling + arrival/warning event → <see cref="BeginEngagement"/>,
        /// then handler.ResolvesManually → <see cref="MilitaryJobHandler.OnManualResolve(MilitaryOperation)"/>
        /// (submod fires <see cref="CompleteBattle"/> later) or auto-resolve via
        /// <see cref="MilitaryJobHandler.OnAutoResolve"/> + <see cref="CompleteBattle"/>.</para>
        /// <para>CooldownPending + cooldown event → <see cref="Resolve"/>.</para>
        /// </summary>
        public void OnEventFired(FCEvent evt)
        {
            if (evt is null) return;

            // Cleanup: drop the fired event from sourceEvents (it's done now).
            sourceEvents.RemoveAll(e => e is null || e.loadID == evt.loadID);

            if (phase == MilitaryOperationPhase.Scheduled || phase == MilitaryOperationPhase.Traveling)
            {
                BeginEngagement();

                MilitaryJobHandler handler = kind?.Handler;
                if (handler is object)
                {
                    // Offensive op (handler-driven). Manual handlers own when CompleteBattle fires.
                    if (handler.ResolvesManually(this))
                    {
                        try { handler.OnManualResolve(this); }
                        catch (Exception e)
                        {
                            LogUtil.Error($"MilitaryOperation.OnEventFired: handler {handler.GetType().Name} threw in OnManualResolve: {e}");
                            AutoResolveAndComplete();
                        }
                        return;
                    }
                    AutoResolveAndComplete();
                    return;
                }

                // Defensive op (no handler). Route through BattlefieldContext.StartDefense which
                // owns map generation / pawn spawning / auto-resolve. EndBattle later walks ops at
                // the tile and fires CompleteBattle on each, scheduling the linked cooldown event.
                if (IsDefensive)
                {
                    if (targetObject is WorldSettlementFC defendedSettlement && defendedSettlement.MilitaryComp is object)
                    {
                        try
                        {
                            BattlefieldContext bf = FactionCache.MilitaryManager?.GetOrCreateBattlefield(targetTile);
                            bf?.StartDefense(this);
                        }
                        catch (Exception e)
                        {
                            LogUtil.Error($"MilitaryOperation.OnEventFired: StartDefense threw for op id={id}: {e}");
                            AutoResolveAndComplete();
                        }
                        return;
                    }
                    // Target has no comp (external IRaidTarget) — auto-resolve via simulator.
                    AutoResolveAndComplete();
                    return;
                }

                // Fallback: shouldn't happen normally.
                AutoResolveAndComplete();
                return;
            }

            if (phase == MilitaryOperationPhase.CooldownPending)
            {
                Resolve();
                return;
            }

            LogUtil.Warning($"MilitaryOperation.OnEventFired: ignoring event '{evt.def?.defName}' " +
                            $"on op id={id} in unexpected phase {phase}.");
        }

        private void AutoResolveAndComplete()
        {
            BattleResult r;
            try
            {
                if (kind?.Handler is object)
                {
                    r = kind.Handler.OnAutoResolve(this);
                }
                else
                {
                    // Defensive ops with no handler use the simulator directly.
                    r = SimulateBattleFc.FightBattle(aggressor.force, defender.force);
                }
            }
            catch (Exception e)
            {
                LogUtil.Error($"MilitaryOperation.AutoResolveAndComplete: handler threw: {e}. Op id={id}.");
                r = new BattleResult { winner = BattleWinner.Error };
            }
            CompleteBattle(r);
        }
    }
}
