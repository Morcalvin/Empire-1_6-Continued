using System;
using UnityEngine;
using Verse;
using RimWorld;

namespace FactionColonies.util
{
    /// <summary>
    /// Single source of truth for the squad-status label rendered across the squad picker,
    /// the squads tab, and the settlement card. Priority order (first match wins):
    /// Unassigned -> Busy in op -> Traveling (travel cooldown) -> Healing (injuries) ->
    /// Empty Slots -> Ready. The Traveling state is the new shrunken cooldown; the Healing
    /// state replaces the old multi-day cooldown's role and is informational only (it does
    /// NOT gate selection; the squad picker still allows deploying an injured squad).
    /// </summary>
    public static class SquadStatusUtil
    {
        /// <summary>Resolves the squad's current display status. <paramref name="isReady"/> is
        /// true only when the squad is fully available with no travel/healing/empty-slot
        /// caveats. UI uses it to color the row green vs amber.</summary>
        public static void Resolve(MercenarySquadFC squad, out string label, out Color color, out bool isReady)
        {
            isReady = false;
            label = "?";
            color = AccentUtil.MilInactive;
            if (squad is null) return;

            if (!squad.IsAssigned)
            {
                label = "FCSquadStatusUnassigned".Translate();
                color = AccentUtil.MilInactive;
                return;
            }

            int now = Find.TickManager.TicksGame;
            MilitaryOperation op = squad.Operation;
            if (op is object && op.kind != MilitaryJobDefOf.Cooldown
                && op.phase != MilitaryOperationPhase.CooldownPending)
            {
                int ticksLeft = Math.Max(0, op.nextPhaseTick - now);
                string opLabel = op.kind?.label ?? "?";
                label = "FCSquadStatusBusyOp".Translate(opLabel, ticksLeft.ToTimeString());
                color = AccentUtil.MilActiveMission;
                return;
            }

            // Travel cooldown: squad is post-op and still returning home. Distinct from the
            // old long cooldown; this is just travel time (24h defense / TravelUtil offense).
            if (squad.nextAvailableTick > now)
            {
                int ticksLeft = squad.nextAvailableTick - now;
                label = "FCSquadStatusTraveling".Translate(ticksLeft.ToTimeString());
                color = AccentUtil.MilCooldown;
                return;
            }

            // Healing: informational only. Squad is back at home but injured; combat power
            // is reduced and a healing-time estimate is shown so the player can decide
            // whether to deploy now or wait. Empty slots are surfaced separately below.
            int healTicks = SquadHealingEstimator.TicksToFullEffectiveness(squad);
            if (healTicks > 0)
            {
                label = "FCSquadStatusHealing".Translate(healTicks.ToTimeString());
                color = AccentUtil.MilCooldown;
                return;
            }

            int emptySlots = squad.EmptySlotCount;
            if (emptySlots > 0)
            {
                label = "FCSquadStatusEmptySlots".Translate(emptySlots);
                color = AccentUtil.MilCooldown;
                return;
            }

            label = "FCSquadStatusReady".Translate();
            color = AccentUtil.MilReady;
            isReady = true;
        }
    }
}
