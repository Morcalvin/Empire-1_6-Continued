using RimWorld.Planet;
using System.Text;
using Verse;

namespace FactionColonies.util
{
    public static class WorldTileChecker
    {
        public static bool IsValidTileForNewSettlement(PlanetTile tile, WorldSettlementDef settlementdef, StringBuilder reason = null)
        {
            if (tile == -1)
            {
                reason?.Append("FCSelectedInvalidTile".Translate());
                return false;
            }

            if (settlementdef.planetLayers.Count == 0)
            {
                if (tile.Layer != Find.WorldGrid.Surface)
                {
                    reason?.Append("FCInvalidPlanetLayer".Translate());
                    return false;
                }
            }
            else
            {
                if (!settlementdef.planetLayers.Contains(tile.Layer.Def))
                {
                    reason?.Append("FCInvalidPlanetLayer".Translate());
                    return false;
                }
            }

            if (!(settlementdef.GetSettlementTypeExtension().TileIsValidForSettlement(tile, reason)))
            {
                return false;
            }

            return true;
        }
    }
}
