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
    /// FCEvents reference the op via <see cref="FCEvent.linkedOperation"/>; on fire they call
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
        /// <summary>
        /// Tick at which the op's timer is expected to next advance the phase.
        /// Mirrors / replaces FCEvent.timeTillTrigger semantics for op-driven timers
        /// </summary>
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

        /* Live state of an auto-resolved battle: per-round rolls accumulated as the battle
         * unfolds (one round per hour). Null for manual-resolve and pre-engagement ops.
         * After CompleteBattle this points to the same object as <see cref="result"/>. */
        public BattleResult battleResult;

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
            Scribe_Deep.Look(ref battleResult, "battleResult");
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
        /// Creates an FCEvent linked back to this op (via <see cref="FCEvent.linkedOperation"/>),
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
            evt.linkedOperation = this;
            if (!customDescription.NullOrEmpty())
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
        /// <summary>
        /// Builds a <see cref="BattleForceContext"/> snapshot from this op's current state.
        /// Used by <see cref="BeginEngagement"/> and by job-handler fallbacks that need to
        /// invoke worldcomp battle helpers.
        /// </summary>
        public BattleForceContext BuildBattleContext()
        {
            return new BattleForceContext
            {
                kind = this.kind,
                targetTile = this.targetTile,
                targetObject = this.targetObject,
                aggressor = this.aggressor,
                defender = this.defender
            };
        }

        public void BeginEngagement()
        {
            phase = MilitaryOperationPhase.Engaged;
            phaseStartedTick = Find.TickManager.TicksGame;

            BattleForceContext ctx = BuildBattleContext();

            WorldComponent_EnemyPower enemyPower = FactionCache.EnemyPower;

            // Lazily create defender force when an offensive op fights a non-Empire faction:
            // CreateOffensiveOp leaves it unset because the enemy is faction-level, not settlement-level.
            // ResolveDefenderForceForOp samples + applies battle modifiers in one shot.
            if (defender.force is null && defender.faction is object && !IsDefensive)
            {
                defender.force = enemyPower?.ResolveDefenderForceForOp(this, ctx);
                if (defender.force is null)
                    LogUtil.Warning($"BeginEngagement: defender force could not be resolved for op id={id} (faction={defender.faction?.Name}). Battle will run with a null defender force.");
            }
            else if (defender.force is object)
            {
                // Defender force was set out-of-band (defensive op with a pre-set squad force);
                // just apply battle modifiers, don't re-sample.
                enemyPower?.ApplyBattleModifiers(ctx, defender.force, isAttacker: false);
            }

            if (aggressor.force is object)
            {
                enemyPower?.ApplyBattleModifiers(ctx, aggressor.force, isAttacker: true);
            }

            if (externalDefenderSource is object)
            {
                IAutoDefender def = AutoDefenderRegistry.FindByWorldObject(externalDefenderSource);
                def?.OnDefenseStarted(targetObject);
            }
        }

        /// <summary>
        /// Initialise <see cref="battleResult"/> from current participant forces and schedule
        /// the first per-round event. The flow:
        ///   T+0   Preparing (no roll)
        ///   T+1h  flip to Engaged (no roll, just status change)
        ///   T+2h  round 1 rolls
        ///   T+3h+ round N rolls
        /// Continues until one side reaches 0 force, at which point <see cref="CompleteBattle"/>
        /// fires with the same <see cref="BattleResult"/> instance.
        /// <para>If the op is not in <see cref="MilitaryOperationPhase.Engaged"/>, falls through
        /// to <see cref="CompleteBattle"/> immediately with an Error result as a safety net.</para>
        /// </summary>
        public void BeginAutoResolveProgress()
        {
            if (phase != MilitaryOperationPhase.Engaged)
            {
                LogUtil.Warning($"BeginAutoResolveProgress: op id={id} not in Engaged (phase={phase}); falling through to immediate CompleteBattle.");
                CompleteBattle(new BattleResult { winner = BattleWinner.Error });
                return;
            }

            if (aggressor?.force is null || defender?.force is null)
            {
                LogUtil.Error($"BeginAutoResolveProgress: missing force on op id={id} (aggressor={(aggressor?.force is object)}, defender={(defender?.force is object)}).");
                CompleteBattle(new BattleResult { winner = BattleWinner.Error });
                return;
            }

            MilitaryForce atk = aggressor.force;
            MilitaryForce def = defender.force;

            // Apply defender advantage in place on the live defender.forceRemaining so
            // MilitaryForce-based readers like CalculateDefenderWinChance see the same
            // post-advantage baseline. The result object snapshots this value as
            // defenderInitialForce.
            def.forceRemaining = Math.Round(def.forceRemaining * FCSettings.defenderAdvantage);

            battleResult = new BattleResult
            {
                attackerInitialForce = atk.forceRemaining,
                defenderInitialForce = def.forceRemaining,
                attackerForceRemaining = atk.forceRemaining,
                defenderForceRemaining = def.forceRemaining,
                attackerEfficiency = atk.militaryEfficiency,
                defenderEfficiency = def.militaryEfficiency,
                attackerLabel = aggressor.squad?.DisplayName ?? aggressor.homeSettlement?.Name ?? aggressor.faction?.Name ?? "?",
                defenderLabel = defender.homeSettlement?.Name ?? defender.squad?.DisplayName ?? defender.faction?.Name ?? "?",
                attackerFactionName = aggressor.faction?.Name ?? "?",
                defenderFactionName = defender.faction?.Name ?? "?",
                attackerFaction = aggressor.faction,
                defenderFaction = defender.faction,
                targetTile = targetTile,
                subPhase = BattleSubPhase.Preparing
            };

            ScheduleNextRoundEvent();
        }

        private void ScheduleNextRoundEvent()
        {
            PlanetTile evtTile = targetTile.Valid
                ? targetTile
                : (defender?.homeSettlement?.Tile ?? aggressor?.homeSettlement?.Tile ?? PlanetTile.Invalid);
            int interval = Math.Max(1, FCSettings.autoResolveTicksPerRound);
            if (DebugSettings.godMode) interval = 1;
            ScheduleEvent(FCEventDefOf.autoResolveBattleRound, evtTile, interval);
        }

        /// <summary>
        /// Advance the battle by one tick of the per-round clock. Called from
        /// <see cref="OnEventFired"/> when an <c>autoResolveBattleRound</c> event fires.
        /// First call (Preparing -> Engaged) doesn't roll - just flips status and reschedules.
        /// Subsequent calls roll one round, append a <see cref="RoundEntry"/>, and either
        /// schedule the next round or hand off to <see cref="CompleteBattle"/>.
        /// </summary>
        private void AdvanceBattleProgress()
        {
            if (battleResult is null)
            {
                LogUtil.Error($"AdvanceBattleProgress: op id={id} has null battleResult; using Error result.");
                CompleteBattle(new BattleResult { winner = BattleWinner.Error });
                return;
            }

            if (battleResult.subPhase == BattleSubPhase.Preparing)
            {
                battleResult.subPhase = BattleSubPhase.Engaged;
                ScheduleNextRoundEvent();
                return;
            }

            // Engaged or RollsInProgress: roll one round.
            battleResult.subPhase = BattleSubPhase.RollsInProgress;
            SimulateBattleFc.RoundOutcome outcome = SimulateBattleFc.SimulateRound(aggressor.force, defender.force);
            if (outcome.attackerWonRound)
            {
                defender.force.forceRemaining -= 1;
                battleResult.defenderForceRemaining -= 1;
            }
            else
            {
                aggressor.force.forceRemaining -= 1;
                battleResult.attackerForceRemaining -= 1;
            }

            battleResult.rounds.Add(new RoundEntry
            {
                roundNumber = battleResult.rounds.Count + 1,
                attackerRawRoll = outcome.attackerRawRoll,
                defenderRawRoll = outcome.defenderRawRoll,
                attackerScore = outcome.attackerScore,
                defenderScore = outcome.defenderScore,
                attackerWonRound = outcome.attackerWonRound,
                attackerForceAfter = battleResult.attackerForceRemaining,
                defenderForceAfter = battleResult.defenderForceRemaining
            });

            if (battleResult.IsComplete)
            {
                battleResult.winner = battleResult.attackerForceRemaining <= 0
                    ? BattleWinner.Defender : BattleWinner.Attacker;
                battleResult.totalRounds = battleResult.rounds.Count;
                battleResult.subPhase = BattleSubPhase.Resolved;
                CompleteBattle(battleResult);
            }
            else
            {
                ScheduleNextRoundEvent();
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
            if (phase != MilitaryOperationPhase.Engaged)
            {
                LogUtil.Warning($"MilitaryOperation.CompleteBattle: ignoring re-entry on op id={id} in phase {phase}.");
                return;
            }

            this.result = battleResult;
            bool victory = IsDefensive
                ? battleResult is object && battleResult.DefenderVictory
                : battleResult is object && battleResult.AttackerVictory;

            // Outcome side effects via handler. Offensive handlers apply loot/prisoners/capture;
            // MilitaryJobHandler_Defend applies settlement-side effects (loyalty/happiness/buildings).
            try
            {
                kind?.Handler?.ApplyResult(this, battleResult);
            }
            catch (Exception e)
            {
                LogUtil.Error($"MilitaryOperation.CompleteBattle: handler {kind?.Handler?.GetType().Name} threw in ApplyResult: {e}");
            }

            // Translate abstract per-side force losses to real injuries/deaths on each
            // participating Empire squad's deployed mercs. Manual battles already dealt
            // damage on the map, so they're skipped via wasManualBattle. Both aggressor
            // and defender squads are processed — one or both may be empty (offensive ops
            // have no defender squad; pure-defensive ops have no aggressor squad).
            if (battleResult is object && !battleResult.wasManualBattle
                && battleResult.winner != BattleWinner.Error)
            {
                if (aggressor?.squad is object)
                {
                    BattleCasualtyApplicator.ApplyCasualtiesToSquad(
                        aggressor.squad,
                        battleResult.attackerInitialForce,
                        battleResult.attackerForceRemaining,
                        aggressor.homeSettlement);
                }
                if (defender?.squad is object && defender.squad != aggressor?.squad)
                {
                    BattleCasualtyApplicator.ApplyCasualtiesToSquad(
                        defender.squad,
                        battleResult.defenderInitialForce,
                        battleResult.defenderForceRemaining,
                        defender.homeSettlement);
                }
            }

            // Roll up squad injuries onto the loadout BEFORE listeners run, so OnBattleResolved
            // observers see post-battle injury counts. Pawn deaths are already reflected in
            // squad.dead via the Pawn.Kill harmony patch — this call records the wound list.
            MilitaryCustomizationUtil mcu = FactionCache.FactionComp?.militaryCustomizationUtil;
            if (mcu is object)
            {
                if (aggressor?.squad is object) mcu.RegisterSquadInjuries(aggressor.squad);
                if (defender?.squad is object && defender.squad != aggressor?.squad)
                    mcu.RegisterSquadInjuries(defender.squad);
            }

            LifecycleRegistry.InvokeOnBattleResolved(this, victory, battleResult);

            if (externalDefenderSource is object)
            {
                IAutoDefender def = AutoDefenderRegistry.FindByWorldObject(externalDefenderSource);
                def?.OnDefenseComplete(victory, battleResult);
            }

            // Drive EmpireThreatAdaptation from every battle the empire participates in
            // (offensive and defensive), so raid outcomes tune the threat curve alongside
            // defensive ones. Skip on Error results — the battle didn't really happen.
            if (battleResult is object && battleResult.winner != BattleWinner.Error)
            {
                EmpireThreatAdaptation adapt = FactionCache.FactionComp?.threatAdaptation;
                if (adapt is object)
                {
                    if (victory) adapt.Notify_BattleWon();
                    else adapt.Notify_BattleLost();
                }
            }

            // Overwhelming-victory shortcut: any battle the empire wins without taking a single
            // casualty on the winning side resolves immediately (no cooldown). Applies uniformly
            // to offensive raid/capture/enslave wins, self-defense, foreign-defender assists, and
            // external IAutoDefender contributions. Deploy ops are excluded — squad presence on
            // the player map isn't a discrete battle, so the shortcut isn't meaningful there.
            if (victory && kind != MilitaryJobDefOf.Deploy && battleResult.IsOverwhelmingVictory)
            {
                Find.LetterStack.ReceiveLetter(
                    "FCOverwhelmingVictory".Translate(),
                    "FCOverwhelmingVictoryDesc".Translate(),
                    LetterDefOf.PositiveEvent);
                Resolve();
                return;
            }

            // Crushing-defeat letter: the loser-side mirror of overwhelming victory. The
            // squad was wiped without scoring a single kill on the enemy. Defensive settlement
            // penalty multiplication is applied inside MilitaryJobHandler_Defend.ApplyResult;
            // the squad wipe is applied by BattleCasualtyApplicator above; the cooldown
            // extension is applied inside EnterCooldown by checking result.IsCrushingDefeat.
            if (!victory && kind != MilitaryJobDefOf.Deploy && battleResult.IsOverwhelmingVictory)
            {
                Find.LetterStack.ReceiveLetter(
                    "FCCrushingDefeat".Translate(),
                    "FCCrushingDefeatDesc".Translate(),
                    LetterDefOf.NegativeEvent);
            }

            EnterCooldown();
        }

        /// <summary>
        /// Move the op into <see cref="MilitaryOperationPhase.CooldownPending"/> and schedule a
        /// <c>cooldownMilitary</c> FCEvent linked back to this op. Cooldown duration is now the
        /// abstract travel time for the squad to return to its home settlement: 24h flat for
        /// defenses (squad re-organizes locally), or <see cref="TravelUtil.ReturnTicksToArrive"/>
        /// from the target tile back to the home settlement for offensive ops. The long heal
        /// cycle that used to be conflated with cooldown is now surfaced as a separate Healing
        /// status driven by <see cref="SquadHealingEstimator"/>.
        /// </summary>
        public void EnterCooldown()
        {
            if (phase == MilitaryOperationPhase.CooldownPending || phase == MilitaryOperationPhase.Resolved)
            {
                LogUtil.Warning($"MilitaryOperation.EnterCooldown: ignoring re-entry on op id={id} in phase {phase}.");
                return;
            }

            phase = MilitaryOperationPhase.CooldownPending;
            phaseStartedTick = Find.TickManager.TicksGame;

            int cooldownTicks = ComputeCooldownTicks();

            // Each participating squad gets its own travel gate via squad.nextAvailableTick.
            // The FCEvent below drives the op's Resolve(); squad availability ("can launch a
            // new op?") reads from the squad directly, so multiple squads at one settlement
            // hold independent per-squad travel timers.
            int wakeTick = Find.TickManager.TicksGame + cooldownTicks;
            // Mirror onto nextPhaseTick so the busy-status display (which reads
            // op.nextPhaseTick) shows the travel countdown rather than 0.0 d.
            nextPhaseTick = wakeTick;
            if (aggressor?.squad is object) aggressor.squad.nextAvailableTick = wakeTick;
            if (defender?.squad is object && defender.squad != aggressor?.squad)
                defender.squad.nextAvailableTick = wakeTick;

            // squad.dead is no longer consumed by the cooldown system but the field stays
            // for save-compat and future analytics. Don't reset it here.

            // Cooldown event fires on the home settlement's tile (or target tile if there's no home
            // — e.g. external defender ops). Aggressor home preferred since that's where the squad
            // returns; falls back to defender home for purely-defensive ops.
            PlanetTile cooldownTile = aggressor?.homeSettlement?.Tile
                                   ?? defender?.homeSettlement?.Tile
                                   ?? targetTile;
            if (!cooldownTile.Valid) cooldownTile = targetTile;

            // Defensive ops have null aggressor.homeSettlement (the aggressor is the enemy faction);
            // use the defender's home so the cooldown letter still names a settlement.
            WorldSettlementFC cooldownHome = aggressor?.homeSettlement ?? defender?.homeSettlement;
            string desc = cooldownHome is object
                ? "FCMilitaryForcesReorganizing".Translate(cooldownHome.Name).ToString()
                : null;
            ScheduleEvent(FCEventDefOf.cooldownMilitary, cooldownTile, cooldownTicks, desc);
        }

        private int ComputeCooldownTicks()
        {

            if (DebugSettings.godMode) return 1;

            // Deploy ops have the same short 24hr timer as foreign defensive deployments.
            if (kind == MilitaryJobDefOf.Deploy) return GenDate.TicksPerDay;

            // Defensive engagement: when the squad defended its own home, there's no march
            // back — the cooldown is zero and the squad goes straight to Ready / Healing.
            // For foreign defenses (squad sent to defend an ally) we use 24h as an abstract
            // march-home window without forcing a per-defense travel calc.
            if (IsDefensive)
            {
                bool homeDefense = defender?.squad?.settlement is object
                                && defender.squad.settlement == defender.homeSettlement;
                return homeDefense ? 0 : GenDate.TicksPerDay;
            }

            // Offensive: actual computed return trip from the target tile back to the squad's home.
            WorldSettlementFC home = aggressor?.homeSettlement;
            if (home is null) return GenDate.TicksPerDay;
            PlanetTile from = targetTile.Valid ? targetTile : home.Tile;
            int travel = TravelUtil.ReturnTicksToArrive(from, home.Tile);
            return Math.Max(0, travel);
        }

        /// <summary>
        /// Final terminal transition. Fires <see cref="LifecycleRegistry.InvokeOnOperationResolved"/>
        /// and unregisters from the manager (which also detaches from any battlefield context).
        /// </summary>
        public void Resolve()
        {
            if (phase == MilitaryOperationPhase.Resolved) return;

            phase = MilitaryOperationPhase.Resolved;
            phaseStartedTick = Find.TickManager.TicksGame;

            // Defense-in-depth: re-register injuries for participating squads. By Resolve time
            // (post-cooldown), pawns are off-map and any wounds carried back are catchable here.
            // HashSet.Add is idempotent, so this is a no-op for already-tracked mercs.
            MilitaryCustomizationUtil mcu = FactionCache.FactionComp?.militaryCustomizationUtil;
            if (mcu is object)
            {
                if (aggressor?.squad is object) mcu.RegisterSquadInjuries(aggressor.squad);
                if (defender?.squad is object && defender.squad != aggressor?.squad)
                    mcu.RegisterSquadInjuries(defender.squad);
            }

            LifecycleRegistry.InvokeOnOperationResolved(this);

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
        /// <see cref="BeginAutoResolveProgress"/>.</para>
        /// <para>Engaged + autoResolveBattleRound event → <see cref="AdvanceBattleProgress"/>
        /// (rolls one round, schedules next, or hands off to <see cref="CompleteBattle"/>).</para>
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
                    // Handler-driven op (offensive raid/capture/enslave OR defensive defend-own-
                    // settlement). Manual handlers own when CompleteBattle fires.
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

                // Handler-less op (only Deploy / Cooldown state defs reach here, and neither
                // schedules wakeup events that hit this branch). Auto-resolve as a safety net.
                LogUtil.Warning($"MilitaryOperation.OnEventFired: handler-less op id={id} kind={kind?.defName} reached engagement path; auto-resolving.");
                AutoResolveAndComplete();
                return;
            }

            if (phase == MilitaryOperationPhase.Engaged
                && evt.def == FCEventDefOf.autoResolveBattleRound)
            {
                AdvanceBattleProgress();
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
            if (aggressor?.force is null || defender?.force is null)
            {
                LogUtil.Error($"MilitaryOperation.AutoResolveAndComplete: missing force on op id={id} " +
                              $"(aggressor={(aggressor?.force is object)}, defender={(defender?.force is object)}).");
                CompleteBattle(new BattleResult { winner = BattleWinner.Error });
                return;
            }
            BeginAutoResolveProgress();
        }
    }
}
