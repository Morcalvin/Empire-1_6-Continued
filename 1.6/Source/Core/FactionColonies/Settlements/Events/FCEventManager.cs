using FactionColonies.util;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;

namespace FactionColonies
{
    // Owns the faction's event queue and auxiliary cooldown / fire-count bookkeeping.
    // The only code that mutates the queue lives here; everywhere else reads via
    // the IReadOnlyList facade (exposed through FactionFC.Events) or calls one of
    // the methods below.
    //
    // Side effects that cascade to settlements (stat modifiers, cache invalidation)
    // are NOT handled here. They live on FactionFC.AddEvent, which calls
    // Enqueue(evt) as its one queue-touching step.
    //
    // Maintains two in-memory indexes for O(1) event lookups:
    //   defIndex         — groups events by FCEventDef
    //   defLocationIndex — groups events by (FCEventDef, tile) compound key
    // Both are rebuilt on load and maintained automatically by all mutation methods.
    public class FCEventManager : IExposable
    {
        private List<FCEvent> events = new List<FCEvent>();
        private Dictionary<string, int> eventCooldowns = new Dictionary<string, int>();
        private Dictionary<string, int> eventFireCounts = new Dictionary<string, int>();
        private int version;

        /* Indexes (not serialized — rebuilt on load) */
        private Dictionary<FCEventDef, List<FCEvent>> defIndex = new Dictionary<FCEventDef, List<FCEvent>>();
        private Dictionary<DefTileKey, List<FCEvent>> defLocationIndex = new Dictionary<DefTileKey, List<FCEvent>>();

        private static readonly IReadOnlyList<FCEvent> EmptyEventList = new List<FCEvent>();

        public IReadOnlyList<FCEvent> Events => events;
        public int Version => version;
        public int Count => events.Count;

        /* Index maintenance */
        private void IndexAdd(FCEvent evt)
        {
            if (evt?.def is null) return;

            if (!defIndex.TryGetValue(evt.def, out List<FCEvent> defList))
            {
                defList = new List<FCEvent>();
                defIndex[evt.def] = defList;
            }
            defList.Add(evt);

            var key = new DefTileKey(evt.def, evt.location);
            if (!defLocationIndex.TryGetValue(key, out List<FCEvent> locList))
            {
                locList = new List<FCEvent>();
                defLocationIndex[key] = locList;
            }
            locList.Add(evt);
        }

        private void IndexRemove(FCEvent evt)
        {
            if (evt?.def is null) return;

            if (defIndex.TryGetValue(evt.def, out List<FCEvent> defList))
            {
                defList.Remove(evt);
                if (defList.Count == 0) defIndex.Remove(evt.def);
            }

            var key = new DefTileKey(evt.def, evt.location);
            if (defLocationIndex.TryGetValue(key, out List<FCEvent> locList))
            {
                locList.Remove(evt);
                if (locList.Count == 0) defLocationIndex.Remove(key);
            }
        }

        private void IndexClear()
        {
            defIndex.Clear();
            defLocationIndex.Clear();
        }

        private void IndexRebuild()
        {
            IndexClear();
            foreach (FCEvent evt in events)
                IndexAdd(evt);
        }

        /* Indexed query methods */
        /// <summary>All events with the given def. Returns empty list if none.</summary>
        public IReadOnlyList<FCEvent> GetByDef(FCEventDef def)
        {
            if (def is null) return EmptyEventList;
            return defIndex.TryGetValue(def, out List<FCEvent> list) ? list : EmptyEventList;
        }

        /// <summary>True if any event with the given def exists in the queue.</summary>
        public bool AnyWithDef(FCEventDef def)
        {
            return def is object && defIndex.TryGetValue(def, out List<FCEvent> list) && list.Count > 0;
        }

        /// <summary>All events with the given def at the given tile. Returns empty list if none.</summary>
        public IReadOnlyList<FCEvent> GetByDefAndLocation(FCEventDef def, PlanetTile tile)
        {
            if (def is null) return EmptyEventList;
            var key = new DefTileKey(def, tile);
            return defLocationIndex.TryGetValue(key, out List<FCEvent> list) ? list : EmptyEventList;
        }

        /// <summary>First event matching (def, tile), or null if none.</summary>
        public FCEvent FindFirstByDefAndLocation(FCEventDef def, PlanetTile tile)
        {
            if (def is null) return null;
            var key = new DefTileKey(def, tile);
            if (!defLocationIndex.TryGetValue(key, out List<FCEvent> list) || list.Count == 0) return null;
            return list[0];
        }

        /// <summary>True if any event with the given (def, tile) exists.</summary>
        public bool AnyWithDefAndLocation(FCEventDef def, PlanetTile tile)
        {
            if (def is null) return false;
            var key = new DefTileKey(def, tile);
            return defLocationIndex.TryGetValue(key, out List<FCEvent> list) && list.Count > 0;
        }

        /* Mutation methods */
        // Raw append. Does NOT apply stat modifiers or invalidate caches;
        // FactionFC.AddEvent is responsible for cascading side effects.
        public void Enqueue(FCEvent evt)
        {
            if (evt is null) return;
            events.Add(evt);
            IndexAdd(evt);
            version++;
        }

        public bool Remove(FCEvent evt)
        {
            if (evt is null) return false;
            if (!events.Remove(evt)) return false;
            IndexRemove(evt);
            evt.phase = FCEventPhase.Completed;
            version++;
            return true;
        }

        public int RemoveWhere(Predicate<FCEvent> match)
        {
            if (match is null) return 0;
            int removed = 0;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                if (!match(events[i])) continue;
                IndexRemove(events[i]);
                events[i].phase = FCEventPhase.Completed;
                events.RemoveAt(i);
                removed++;
            }
            if (removed > 0) version++;
            return removed;
        }

        public void Clear()
        {
            if (events.Count == 0) return;
            events.Clear();
            IndexClear();
            version++;
        }

        // Collects every Queued event whose timeTillTrigger has passed, returning them as a new list.
        // Events stay in the queue; ProcessEvents transitions phase Queued -> Fired (tentative)
        // -> Completed (default at end of body) inside its per-event re-entrancy guard, and a sweep
        // after the loop removes Completed events. Skips events already in Fired or Completed phase.
        public List<FCEvent> CollectDueEvents(int currentTick)
        {
            List<FCEvent> due = null;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                if (!events[i].IsQueued) continue;
                if (events[i].timeTillTrigger > currentTick) continue;
                if (due is null) due = new List<FCEvent>();
                due.Add(events[i]);
            }
            return due;
        }

        // Bulk seed from a legacy list. Used only by FactionFC's save-migration
        // path to move pre-manager events into the manager. Bumps version once.
        public void SeedFromLegacy(IEnumerable<FCEvent> legacyEvents)
        {
            if (legacyEvents is null) return;
            bool any = false;
            foreach (FCEvent evt in legacyEvents)
            {
                if (evt is null) continue;
                events.Add(evt);
                IndexAdd(evt);
                any = true;
            }
            if (any) version++;
        }

        public void RecordCooldown(FCEventDef def)
        {
            if (def is null) return;
            eventCooldowns[def.defName] = Find.TickManager.TicksGame;
        }

        public bool IsOnCooldown(FCEventDef def)
        {
            if (def is null || def.cooldownTicks <= 0) return false;
            if (!eventCooldowns.TryGetValue(def.defName, out int lastTick)) return false;
            return Find.TickManager.TicksGame - lastTick < def.cooldownTicks;
        }

        public void RecordFired(FCEventDef def)
        {
            if (def is null) return;
            eventFireCounts.TryGetValue(def.defName, out int count);
            eventFireCounts[def.defName] = count + 1;
        }

        public bool HasReachedMaxFireCount(FCEventDef def)
        {
            if (def is null || def.maxFireCount <= 0) return false;
            if (!eventFireCounts.TryGetValue(def.defName, out int count)) return false;
            return count >= def.maxFireCount;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref events, "events", LookMode.Deep);
            if (events is null) events = new List<FCEvent>();
            Scribe_Collections.Look(ref eventCooldowns, "eventCooldowns", LookMode.Value, LookMode.Value);
            if (eventCooldowns is null) eventCooldowns = new Dictionary<string, int>();
            Scribe_Collections.Look(ref eventFireCounts, "eventFireCounts", LookMode.Value, LookMode.Value);
            if (eventFireCounts is null) eventFireCounts = new Dictionary<string, int>();

            // Indexes are transient — rebuild as soon as the events list is populated.
            // Must run in LoadingVars (not PostLoadInit): WorldSettlementFC.PostLoadInit
            // fires ISettlementPostLoadInit callbacks that query the index, and the
            // PostLoadIniter HashSet can schedule WorldSettlementFC before FCEventManager.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                IndexRebuild();
        }
    }
}
