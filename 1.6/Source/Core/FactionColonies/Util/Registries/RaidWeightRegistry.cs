using RimWorld;
using System;
using System.Collections.Generic;

namespace FactionColonies
{
    public static class RaidWeightRegistry
    {
        private static readonly List<IRaidWeightProvider> _providers = new List<IRaidWeightProvider>();

        public static void Register(IRaidWeightProvider provider)
        {
            if (!_providers.Contains(provider)) _providers.Add(provider);
        }

        public static void Unregister(IRaidWeightProvider provider) => _providers.Remove(provider);
        public static void ClearAll() => _providers.Clear();
        public static IReadOnlyList<IRaidWeightProvider> Providers => _providers;

        /// <summary>
        /// Returns the combined weight multiplier for a settlement from all registered providers.
        /// If no providers are registered, returns 1.0 (no modification).
        /// </summary>
        public static float GetCombinedWeight(WorldSettlementFC settlement, Faction attackingFaction)
        {
            if (_providers.Count == 0) return 1f;

            float combined = 1f;
            foreach (IRaidWeightProvider p in _providers)
            {
                try
                {
                    combined *= p.GetSettlementRaidWeight(settlement, attackingFaction);
                }
                catch (Exception e)
                {
                    LogUtil.Error($"IRaidWeightProvider {p.GetType().Name} threw in GetSettlementRaidWeight: {e}");
                }
            }
            return combined;
        }
    }
}
