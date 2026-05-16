using System.Collections.Generic;

namespace FactionColonies
{
    public static class DefenseValidatorRegistry
    {
        private static readonly RegistryList<IDefenseValidator> _list = new RegistryList<IDefenseValidator>();

        internal static void Register(IDefenseValidator v) => _list.Register(v);
        internal static void Unregister(IDefenseValidator v) => _list.Unregister(v);
        internal static void ClearAll() => _list.ClearAll();
        public static IReadOnlyList<IDefenseValidator> Validators => _list.Items;

        /// <summary>
        /// Returns true if all registered validators allow the defense assignment.
        /// </summary>
        public static bool CanDefend(WorldSettlementFC defender, WorldSettlementFC target)
            => RegistryDispatch.All(_list.Items, v => v.CanDefend(defender, target), nameof(IDefenseValidator.CanDefend));
    }
}
