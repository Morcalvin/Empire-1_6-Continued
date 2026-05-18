using System.Collections.Generic;
using FactionColonies.util;

namespace FactionColonies
{
    /* Owns the four-way loop pattern (location-targeted vs faction-wide, add vs remove)
     * and the "event_" + defName source-id literal used by FCEventDef stat application.
     *
     * IMPORTANT: must pass evt.def.statModifiers directly (never a copy) so
     * WorldSettlementFC.RemoveStatModifiers's reference-equality match holds. */
    internal static class EventStatModifierApplier
    {
        private static string SourceId(FCEvent evt) => "event_" + evt.def.defName;

        /// <summary>Applies an event's stat + permanent stat modifiers to its targeted
        /// settlements (or all settlements if untargeted) and invalidates the faction
        /// stat cache once at the end.</summary>
        public static void Apply(FCEvent evt, FactionFC faction)
        {
            if (evt?.def is null || faction is null) return;

            string sourceId = SourceId(evt);
            string label = evt.def.label;
            bool hasStats = evt.def.statModifiers != null && evt.def.statModifiers.Count > 0;
            bool hasPermanent = evt.def.permanentStatModifiers != null && evt.def.permanentStatModifiers.Count > 0;
            if (!hasStats && !hasPermanent) return;

            if (evt.settlementTraitLocations.Count > 0)
            {
                foreach (WorldSettlementFC location in evt.settlementTraitLocations)
                {
                    if (location is null) continue;
                    if (hasStats) location.AddStatModifiers(evt.def.statModifiers, sourceId, label);
                    if (hasPermanent) location.AddPermanentModifiers(evt.def.permanentStatModifiers, sourceId, label);
                }
            }
            else
            {
                foreach (WorldSettlementFC settlement in faction.settlements)
                {
                    if (settlement is null) continue;
                    if (hasStats) settlement.AddStatModifiers(evt.def.statModifiers, sourceId, label);
                    if (hasPermanent) settlement.AddPermanentModifiers(evt.def.permanentStatModifiers, sourceId, label);
                }
            }

            faction.InvalidateFactionStatCache();
        }

        /// <summary>Removes the event's stat modifiers from its targeted settlements
        /// (or all settlements) and applies prosperityLost. Permanent modifiers are NOT
        /// removed. Invalidates the faction stat cache once at the end.</summary>
        public static void Remove(FCEvent evt, FactionFC faction)
        {
            if (evt?.def is null || faction is null) return;

            string sourceId = SourceId(evt);
            bool hasStats = evt.def.statModifiers != null && evt.def.statModifiers.Count > 0;
            double prosperityLost = evt.def.prosperityLost;

            if (evt.settlementTraitLocations.Count > 0)
            {
                /* Strip null references; events whose targeted settlements were removed
                 * mid-flight would otherwise NRE inside the loop. */
                evt.settlementTraitLocations.RemoveAll(s => s == null);

                foreach (WorldSettlementFC location in evt.settlementTraitLocations)
                {
                    if (location is null) continue;
                    if (hasStats) location.RemoveStatModifiers(evt.def.statModifiers, sourceId);
                    location.prosperity -= prosperityLost;
                }
            }
            else
            {
                foreach (WorldSettlementFC settlement in faction.settlements)
                {
                    if (settlement is null) continue;
                    if (hasStats) settlement.RemoveStatModifiers(evt.def.statModifiers, sourceId);
                    settlement.prosperity -= prosperityLost;
                }
            }

            faction.InvalidateFactionStatCache();
        }

        /// <summary>Settlement-scoped reapply used by WorldSettlementFC.PostLoadInit.
        /// Does NOT touch the faction stat cache — the load path owns its own cascade.</summary>
        public static void ApplyForSettlement(FCEvent evt, WorldSettlementFC settlement)
        {
            if (evt?.def?.statModifiers is null || evt.def.statModifiers.Count == 0) return;
            if (settlement is null) return;
            if (evt.settlementTraitLocations.Count != 0 && !evt.settlementTraitLocations.Contains(settlement)) return;

            settlement.AddStatModifiers(evt.def.statModifiers, SourceId(evt), evt.def.label);
        }
    }
}
