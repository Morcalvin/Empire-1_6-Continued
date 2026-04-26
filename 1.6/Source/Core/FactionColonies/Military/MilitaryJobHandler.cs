using System;
using RimWorld;
using RimWorld.Planet;

namespace FactionColonies
{
    public abstract class MilitaryJobHandler
    {
        public MilitaryJobDef def;

        /// <summary>Called from SendMilitary. Create the FCEvent, send letters, etc.</summary>
        [Obsolete("Override OnOpCreated(MilitaryOperation) instead. Will be removed in a future version.")]
        public abstract void OnDeployed(WorldObjectComp_SettlementMilitary milComp, PlanetTile location, int timeToFinish, Faction enemy);

        /// <summary>Called from ProcessMilitaryEvent when the event timer fires. Returns the BattleResult.</summary>
        [Obsolete("Override OnAutoResolve(MilitaryOperation) and ApplyResult(MilitaryOperation, BattleResult) instead. Will be removed in a future version.")]
        public abstract BattleResult OnResolved(WorldObjectComp_SettlementMilitary milComp);

        /// <summary>Returns whether this job can target the given faction. Used to filter hostile menu options.</summary>
        public virtual bool IsValidTarget(Faction targetFaction) => true;

        /// <summary>
        /// If true, <see cref="WorldObjectComp_SettlementMilitary.ProcessMilitaryEvent"/> delegates
        /// resolution to <see cref="OnManualResolve(WorldObjectComp_SettlementMilitary)"/> instead
        /// of calling <see cref="OnResolved"/>.
        /// The handler owns cooldown timing and lifecycle notification.
        /// </summary>
        [Obsolete("Override ResolvesManually(MilitaryOperation) instead. Will be removed in a future version.")]
        public virtual bool ResolvesManually => false;

        /// <summary>
        /// Called instead of <see cref="OnResolved"/> when <see cref="ResolvesManually"/> is true.
        /// The handler generates a battle map and manages the async lifecycle.
        /// Must call <c>milComp.CooldownMilitaryFinal()</c> when the battle ends.
        /// </summary>
        [Obsolete("Override OnManualResolve(MilitaryOperation) instead. Will be removed in a future version.")]
        public virtual void OnManualResolve(WorldObjectComp_SettlementMilitary milComp) { }

        /* -*-*-*-*- Op-aware methods (Phase 1: scaffolding) -*-*-*-*-
         * The new model passes a MilitaryOperation through to the handler so it can read
         * participants, target, and phase rather than reaching into the comp. Phase 2 splits
         * the concrete handlers (Raid / Capture / Enslave) into OnAutoResolve + ApplyResult and
         * wires SendMilitary to call them. Until then these defaults are unused at runtime.
         */

        /// <summary>
        /// Called immediately after <see cref="MilitaryOperationManager"/> registers an op of
        /// this handler's <see cref="def"/>. Use this for handler-specific op setup
        /// (logging, modifying participant data, etc.). Default: no-op.
        /// </summary>
        public virtual void OnOpCreated(MilitaryOperation op) { }

        /// <summary>
        /// Op-aware auto-resolution. Returns the <see cref="BattleResult"/>; side effects
        /// (loot, prisoners, settlement capture) live in <see cref="ApplyResult"/>.
        /// Phase 2 makes this abstract once handlers are split.
        /// </summary>
        public virtual BattleResult OnAutoResolve(MilitaryOperation op)
        {
            throw new NotImplementedException(
                $"OnAutoResolve(op) not implemented for handler {GetType().Name}. " +
                "Phase 2 will split the legacy OnResolved(milComp) into OnAutoResolve + ApplyResult.");
        }

        /// <summary>
        /// Op-aware manual resolution. Submods that opt in via <see cref="ResolvesManually"/>
        /// override this to spawn pawns / lords on the op's <see cref="BattlefieldContext"/>.
        /// Submods are responsible for calling <c>op.CompleteBattle(result)</c> when the
        /// player-driven battle resolves. Default: no-op.
        /// </summary>
        public virtual void OnManualResolve(MilitaryOperation op) { }

        /// <summary>
        /// Side-effect application for an op's resolution: loot, prisoners, settlement capture,
        /// XP, etc. Called by <c>op.CompleteBattle(result)</c> after a battle resolves (auto or
        /// manual). Splitting this from <see cref="OnAutoResolve"/> lets manual handlers reuse
        /// the same outcome handling. Default: no-op.
        /// </summary>
        public virtual void ApplyResult(MilitaryOperation op, BattleResult result) { }
    }
}
