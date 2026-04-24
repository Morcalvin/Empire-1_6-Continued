using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FactionColonies
{
    public static class TravelUtil
    {
        public const int timespanFallback = GenDate.TicksPerDay * 10;
        public static int ReturnTicksToArrive(PlanetTile currentTile, PlanetTile destinationTile)
        {
            LogUtil.Message($"ReturnTicksToArrive Debug: currentTile={currentTile}, destinationTile={destinationTile}");

            // Cross-layer (e.g. surface <-> orbital): caravan pathing is impossible.
            if (currentTile.Layer != destinationTile.Layer)
            {
                bool podsAvailable = FactionCache.TechTransportPods?.IsFinished ?? false;
                if (podsAvailable)
                {
                    int multiplier = (currentTile, destinationTile).AreTilesInAnyShuttleRange() ? 5 : 10;
                    return Find.WorldGrid.TraversalDistanceBetween(currentTile, destinationTile) * multiplier;
                }
                return timespanFallback; //10-day fallback
            }

            bool tilesInShuttleRange = (currentTile, destinationTile).AreTilesInAnyShuttleRange();
            bool medievalOnly = FCSettings.medievalTechOnly;
            bool podsResearched = FactionCache.TechTransportPods?.IsFinished ?? false;

            if (!medievalOnly)
            {
                bool tilesValid = (currentTile, destinationTile).AreValidTiles();
                LogUtil.Message($"ReturnTicksToArrive Debug: tilesValid={tilesValid}, medievalOnly={medievalOnly}, podsResearched={podsResearched}");

                if (!tilesValid)
                {
                    int fallbackTime = podsResearched ? 30000 : timespanFallback;
                    LogUtil.Message($"ReturnTicksToArrive Debug: Invalid tiles, returning fallback time: {fallbackTime} ticks ({fallbackTime / 60000f:F1} days)");
                    return fallbackTime;
                }
                if (podsResearched)
                {
                    int multiplier = tilesInShuttleRange ? 5 : 10;
                    return Find.WorldGrid.TraversalDistanceBetween(currentTile, destinationTile) * multiplier;
                }
            }

            // Both tiles are on the same layer here (cross-layer returns above).
            // Use that layer's pathing rather than hardcoding surface.
            var layer = currentTile.Layer;
            using (var pathing = new WorldPathing(layer))
            {
                using (WorldPath tempPath = pathing.FindPath(currentTile, destinationTile, null))
                {
                    if (tempPath == WorldPath.NotFound) return timespanFallback;

                    return CaravanArrivalTimeEstimator.EstimatedTicksToArrive(currentTile, destinationTile, tempPath, 0f, CaravanTicksPerMoveUtility.GetTicksPerMove(null), Find.TickManager.TicksAbs);
                }
            }
        }

    }
}
