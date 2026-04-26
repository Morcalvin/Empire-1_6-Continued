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

        /* -*-*-*-*- Lifecycle -*-*-*-*- */

        /// <summary>
        /// Register <paramref name="op"/> on this battlefield. Idempotent. Clears
        /// <see cref="awaitingPlayerExit"/> so a re-engagement (new attack arriving while the
        /// player is still on a post-battle map) gets a fresh battle window. The actual
        /// pawn / lord spawning is the caller's responsibility (typically via
        /// <see cref="SpawnParticipantOnMap"/> after Join).
        /// </summary>
        public void Join(MilitaryOperation op)
        {
            if (op is null) return;
            if (activeOps is null) activeOps = new List<MilitaryOperation>();
            if (!activeOps.Contains(op)) activeOps.Add(op);
            awaitingPlayerExit = false;
        }

        /// <summary>
        /// Remove <paramref name="op"/> from this battlefield. If no ops remain and the player
        /// is still on the map, transitions into <see cref="awaitingPlayerExit"/>; otherwise the
        /// context is cleaned up out of the manager. Pawn / lord cleanup is left to
        /// the comp / map's own removal path; this method only manages op membership.
        /// </summary>
        public void Detach(MilitaryOperation op)
        {
            if (op is null) return;
            activeOps?.Remove(op);

            if (activeOps == null || activeOps.Count == 0)
            {
                bool playerOnMap = map is object && map.mapPawns?.FreeColonistsSpawnedCount > 0;
                if (playerOnMap)
                {
                    awaitingPlayerExit = true;
                }
                else
                {
                    awaitingPlayerExit = false;
                    FactionCache.MilitaryManager?.RemoveBattlefield(tile);
                }
            }
        }

        /// <summary>
        /// Spawn the participant's pawns onto this battlefield's map and attach them to the
        /// appropriate side's lord. Phase 2 continuation will fill this in with the spawning
        /// logic currently in <c>SettlementMilitary.SpawnWaveAttackers</c> /
        /// <c>GenerateWaveReinforcements</c>.
        /// </summary>
        public void SpawnParticipantOnMap(MilitaryOperationParticipant participant)
        {
            throw new NotImplementedException(
                "BattlefieldContext.SpawnParticipantOnMap is filled in by Phase 2 continuation.");
        }

        /// <summary>
        /// Called when a new op joins while <see cref="awaitingPlayerExit"/> is true.
        /// Spawns fresh attacker pawns/lord on the existing map; if defender pawns survived,
        /// rebuilds the defender lord. Phase 2 continuation work.
        /// </summary>
        public void TryReengage(MilitaryOperation op)
        {
            throw new NotImplementedException(
                "BattlefieldContext.TryReengage is filled in by Phase 2 continuation.");
        }
    }
}
