using FactionColonies.util;
using HarmonyLib;
using RimWorld;
using System.Linq;
using Verse;
using Verse.AI;

namespace FactionColonies
{
    /// <summary>
    /// Prevents battle participants from walking off the edge of an Empire settlement's battle
    /// map mid-fight. Allows the player's own caravan / shuttle colonists to leave at will,
    /// since they're volunteers — but Empire NPCs (defenders, squad mercenaries, settlement
    /// inhabitants) and the NPCs the player has drafted into combat are committed to the battle.
    ///
    /// Hostile attackers are *not* blocked: when their lord enters its exit toil they're
    /// supposed to retreat off the map, which is how the battle ends.
    /// </summary>
    [HarmonyPatch(typeof(JobDriver_Goto), "TryExitMap")]
    public class Patch
    {
        static bool Prefix(ref JobDriver_Goto __instance)
        {
            Pawn pawn = __instance.pawn;
            if (!(pawn.Map?.Parent is WorldSettlementFC settlement)) return true;

            var military = settlement.MilitaryComp;
            if (military == null || !military.isUnderAttack) return true;

            // Player's own colonists who weren't drafted by us are volunteers (caravan / shuttle
            // joiners) and may leave at will — even though they're aggregated into
            // military.defenders for combat purposes.
            if (pawn.Faction == Faction.OfPlayer && !military.draftedNPCs.Contains(pawn))
                return true;

            // Empire NPCs / squad mercenaries spawned for this battle are tracked on each op's
            // defender.pawns; the comp's `defenders` enumerable aggregates across ops at this tile.
            if (military.defenders.Contains(pawn)) return false;

            // Belt-and-suspenders for pawns that should have been registered as defenders but
            // weren't (e.g., a submod path that bypasses our spawn helpers): block any squad
            // mercenary or any drafted pawn left over after the player-faction check above.
            if (pawn.IsMercenary()) return false;
            if (pawn.Drafted) return false;

            return true;
        }
    }
}
