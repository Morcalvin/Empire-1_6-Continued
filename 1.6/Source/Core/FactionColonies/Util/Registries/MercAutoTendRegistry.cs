using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace FactionColonies
{
    /* Registry of IMercAutoTendProvider implementations. Walked once per off-map merc per
     * hourly heal tick to determine the tending doctor (if any) and the medicine ThingDef.
     * Both consumer methods are self-contained — the medicine pick seeds with the tech-level
     * default internally, so callers only ever invoke PickMedicine, never PickDefaultMedicine. */
    public static class MercAutoTendRegistry
    {
        private static readonly List<IMercAutoTendProvider> _providers = new List<IMercAutoTendProvider>();

        public static void Register(IMercAutoTendProvider provider)
        {
            if (!_providers.Contains(provider)) _providers.Add(provider);
        }
        public static void Unregister(IMercAutoTendProvider provider) => _providers.Remove(provider);
        public static void ClearAll() => _providers.Clear();
        public static IReadOnlyList<IMercAutoTendProvider> Providers => _providers;

        /// <summary>
        /// First non-null doctor wins. Returns null if no provider supplies one.
        /// </summary>
        public static Pawn PickDoctor(Mercenary patient, WorldSettlementFC settlement)
        {
            foreach (IMercAutoTendProvider t in _providers)
            {
                try
                {
                    Pawn doctor = t.ProvideTendingDoctor(patient, settlement);
                    if (doctor != null) return doctor;
                }
                catch (Exception e)
                {
                    LogUtil.Error($"IMercAutoTendProvider {t.GetType().Name} threw in ProvideTendingDoctor: {e}");
                }
            }

            return null;
        }

        /// <summary>
        /// Seeds with the tech-level default, then chains through every provider's override.
        /// Each provider sees the running choice and may pass it through or replace it.
        /// </summary>
        public static ThingDef PickMedicine(Mercenary patient, WorldSettlementFC settlement)
        {
            ThingDef current = PickDefaultMedicine();
            foreach (IMercAutoTendProvider t in _providers)
            {
                try
                {
                    current = t.OverrideTendingMedicine(patient, settlement, current);
                }
                catch (Exception e)
                {
                    LogUtil.Error($"IMercAutoTendProvider {t.GetType().Name} threw in OverrideTendingMedicine: {e}");
                }
            }
            return current;
        }

        /* Tech-level -> default medicine. Maps the player colony faction's def techLevel to
         * the abstracted medicine quality used for tending. Only Ultra/Archotech factions get access to
         * glitterworld medicine. */
        private static ThingDef PickDefaultMedicine()
        {
            Faction faction = FactionCache.PlayerColonyFaction;
            TechLevel tech = faction?.def?.techLevel ?? TechLevel.Industrial;
            switch (tech)
            {
                case TechLevel.Animal:
                    return null;
                case TechLevel.Neolithic:
                case TechLevel.Medieval:
                    return ThingDefOf.MedicineHerbal;
                case TechLevel.Industrial:
                case TechLevel.Spacer:
                    return ThingDefOf.MedicineIndustrial;
                case TechLevel.Ultra:
                case TechLevel.Archotech:
                    return ThingDefOf.MedicineUltratech;
                default:
                    return ThingDefOf.MedicineIndustrial;
            }
        }
    }
}
