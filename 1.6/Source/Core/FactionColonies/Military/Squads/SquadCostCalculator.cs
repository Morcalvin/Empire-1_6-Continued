using System;

namespace FactionColonies
{
    /// <summary>
    /// Cost calculations for a mercenary squad.
    /// </summary>
    public static class SquadCostCalculator
    {
        /// <summary>Sum of equipment market values across all currently-equipped mercenaries,
        /// read from each merc's <see cref="Mercenary.EffectiveLoadout"/> . Changes to the unit
        /// template after a hire/fill/upgrade are not reflected here — only what was applied to
        /// the pawn.</summary>
        public static double GetCurrentLoadoutCost(MercenarySquadFC squad)
        {
            double total = 0;
            if (squad?.mercenaries is null) return total;
            foreach (Mercenary merc in squad.mercenaries)
            {
                if (merc is null || merc.IsEmptySlot) continue;
                if (merc.pawn is object && merc.pawn.Dead) continue;
                MilUnitFC current = merc.EffectiveLoadout;
                if (current is null) continue;
                total += current.getTotalCost;
            }
            return total;
        }

        /// <summary>Loadout cost weighted by per-pawn combat effectiveness — a downed pawn
        /// contributes 0 (effectively an empty slot for power-projection), an injured pawn
        /// contributes a fraction (worst of consciousness/manipulation/moving), and a healthy
        /// pawn contributes their full loadout value. Used by
        /// <see cref="SquadPowerRegistry.ComputeBasePower"/> so squad combat power scales with
        /// pawn health. Cost displays (deployment / upgrade / inspection) keep using
        /// <see cref="GetCurrentLoadoutCost"/>; only the power projection cares about health.</summary>
        public static double GetEffectiveLoadoutCost(MercenarySquadFC squad)
        {
            double total = 0;
            if (squad?.mercenaries is null) return total;
            foreach (Mercenary merc in squad.mercenaries)
            {
                if (merc is null || merc.IsEmptySlot) continue;
                MilUnitFC current = merc.EffectiveLoadout;
                if (current is null) continue;
                double effectiveness = SquadEffectivenessUtil.PawnEffectiveness(merc.pawn);
                if (effectiveness <= 0) continue;
                total += current.getTotalCost * effectiveness;
            }
            return total;
        }

        /// <summary>Silver cost to deploy this squad on an offensive op or to a player map.
        /// Computed as <c>FCSettings.squadDeploymentCostPercentage * GetCurrentLoadoutCost</c>,
        /// rounded to int.</summary>
        public static int DeploymentCost(MercenarySquadFC squad)
        {
            return MilitaryDeploymentUtil.CalculateDeploymentCost(GetCurrentLoadoutCost(squad));
        }

        /// <summary>Total silver to refill all fillable empty slots, summed over each slot's
        /// blueprint cost * <see cref="FCSettings.squadHireCostMultiplier"/>. The blueprint
        /// is the merc's <see cref="Mercenary.BlueprintLoadout"/> (personalization snapshot
        /// or pool reference). Slots whose blueprint is null or blank contribute zero — they
        /// are pure placeholders kept around to keep slot indices aligned with the template.</summary>
        public static int FillEmptySlotsCost(MercenarySquadFC squad)
        {
            int total = 0;
            if (squad?.mercenaries is null) return total;
            foreach (Mercenary m in squad.mercenaries)
            {
                if (m is null || !m.IsEmptySlot) continue;
                MilUnitFC blueprint = m.BlueprintLoadout;
                if (blueprint is null || blueprint.isBlank) continue;
                total += (int)Math.Round(blueprint.getTotalCost * FCSettings.squadHireCostMultiplier);
            }
            return total;
        }
    }
}
