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
    /// <para>Phase 1: skeleton + serialization. Phase 2 fills transition method bodies.</para>
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

        /* -*-*-*-*- Transitions -*-*-*-*-
         * Phase 2 fills these. They each fire the right registries internally so
         * callers don't have to remember which hook goes where.
         */

        public void BeginEngagement()
        {
            throw new NotImplementedException("MilitaryOperation.BeginEngagement filled in Phase 2.");
        }

        public void CompleteBattle(BattleResult result)
        {
            throw new NotImplementedException("MilitaryOperation.CompleteBattle filled in Phase 2.");
        }

        public void EnterCooldown()
        {
            throw new NotImplementedException("MilitaryOperation.EnterCooldown filled in Phase 2.");
        }

        public void Resolve()
        {
            throw new NotImplementedException("MilitaryOperation.Resolve filled in Phase 2.");
        }

        public BattlefieldContext AttachToBattlefield()
        {
            throw new NotImplementedException("MilitaryOperation.AttachToBattlefield filled in Phase 2.");
        }

        public void DetachFromBattlefield()
        {
            throw new NotImplementedException("MilitaryOperation.DetachFromBattlefield filled in Phase 2.");
        }

        /// <summary>
        /// Called by <see cref="MilitaryOperationManager"/> (via FCEventMaker) when one of this op's
        /// scheduled FCEvents fires. Phase 2 implements the phase machine here:
        /// Traveling+arrival → BeginEngagement → handler.ResolvesManually branch;
        /// CooldownPending+cooldown → Resolve.
        /// </summary>
        public void OnEventFired(FCEvent evt)
        {
            throw new NotImplementedException("MilitaryOperation.OnEventFired filled in Phase 2.");
        }
    }
}
