using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace FactionColonies
{
    /// <summary>
    /// Per-tile shared object that owns the manual-battle map and lord refs for all
    /// <see cref="MilitaryOperation"/>s targeting a single tile.
    /// <para>One <c>BattlefieldContext</c> exists per active battle tile, regardless of how many
    /// concurrent ops it serves. When two ops target the same tile (e.g. two attackers on one
    /// settlement) they share the map: the second op's pawns spawn into the existing map and
    /// either join the existing attacker lord or get a fresh one if the previous lord was
    /// destroyed.</para>
    /// <para>Lifecycle: a context outlives any single op. After the last op detaches, if the
    /// player is still on the map the context enters <see cref="awaitingPlayerExit"/>; a new
    /// op arriving in that state re-engages by calling <see cref="TryReengage"/>. The context
    /// is destroyed once <see cref="activeOps"/> is empty AND the player has left.</para>
    /// <para>Phase 1: skeleton + serialization. Phase 2 fills method bodies.</para>
    /// </summary>
    public class BattlefieldContext : IExposable
    {
        public PlanetTile tile = PlanetTile.Invalid;

        /// <summary>The battle map. Lazy: created when the first op transitions to Engaged on this tile.</summary>
        public Map map;

        /// <summary>Lord for the attacking side. May be null between waves; re-created when needed.</summary>
        public Lord attackerLord;

        /// <summary>Lord for the defending side. May be null between waves; re-created when needed.</summary>
        public Lord defenderLord;

        /// <summary>Ops currently using this battlefield. The context cannot be destroyed while non-empty.</summary>
        public List<MilitaryOperation> activeOps = new List<MilitaryOperation>();

        /// <summary>True when the last op has resolved but the player is still on-map.
        /// A new op arriving in this state triggers <see cref="TryReengage"/>.</summary>
        public bool awaitingPlayerExit;

        public BattlefieldContext() { }

        public BattlefieldContext(PlanetTile tile)
        {
            this.tile = tile;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref tile, "tile", PlanetTile.Invalid);
            Scribe_References.Look(ref map, "map");
            Scribe_References.Look(ref attackerLord, "attackerLord");
            Scribe_References.Look(ref defenderLord, "defenderLord");
            Scribe_Collections.Look(ref activeOps, "activeOps", LookMode.Reference);
            Scribe_Values.Look(ref awaitingPlayerExit, "awaitingPlayerExit", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && activeOps is null)
                activeOps = new List<MilitaryOperation>();
        }

        /* -*-*-*-*- Lifecycle -*-*-*-*-
         * Phase 2 fills these.
         */

        public void Join(MilitaryOperation op)
        {
            throw new NotImplementedException("BattlefieldContext.Join filled in Phase 2.");
        }

        public void Detach(MilitaryOperation op)
        {
            throw new NotImplementedException("BattlefieldContext.Detach filled in Phase 2.");
        }

        public void SpawnParticipantOnMap(MilitaryOperationParticipant participant)
        {
            throw new NotImplementedException("BattlefieldContext.SpawnParticipantOnMap filled in Phase 2.");
        }

        /// <summary>Called when a new op joins while <see cref="awaitingPlayerExit"/> is true.
        /// Spawns fresh attacker pawns/lord on the existing map; if defender pawns survived,
        /// rebuilds the defender lord.</summary>
        public void TryReengage(MilitaryOperation op)
        {
            throw new NotImplementedException("BattlefieldContext.TryReengage filled in Phase 2.");
        }
    }
}
