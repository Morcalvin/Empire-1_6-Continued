using System;
using System.Collections.Generic;

namespace FactionColonies
{
    public static class AutoResolveDurationRegistry
    {
        private static readonly List<IAutoResolveDurationProvider> _providers = new List<IAutoResolveDurationProvider>();

        public static void Register(IAutoResolveDurationProvider provider)
        {
            if (!_providers.Contains(provider)) _providers.Add(provider);
        }
        public static void Unregister(IAutoResolveDurationProvider provider) => _providers.Remove(provider);
        public static void ClearAll() => _providers.Clear();
        public static IReadOnlyList<IAutoResolveDurationProvider> Providers => _providers;

        /// <summary>
        /// Invokes every registered <see cref="IAutoResolveDurationProvider"/> against
        /// <paramref name="durationTicks"/> in the context of <paramref name="op"/> and
        /// <paramref name="result"/>. Providers that throw are logged and skipped.
        /// </summary>
        public static void InvokeModifyDuration(MilitaryOperation op, BattleResult result, ref int durationTicks)
        {
            foreach (IAutoResolveDurationProvider provider in _providers)
            {
                try
                {
                    provider.ModifyDuration(op, result, ref durationTicks);
                }
                catch (Exception e)
                {
                    LogUtil.Error($"IAutoResolveDurationProvider {provider.GetType().Name} threw in ModifyDuration: {e}");
                }
            }
        }
    }
}
