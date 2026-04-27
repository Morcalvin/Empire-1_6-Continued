using System;
using System.Collections.Generic;

namespace FactionColonies
{
    public static class BattleModifierRegistry
    {
        private static readonly List<IBattleModifier> _modifiers = new List<IBattleModifier>();

        public static void Register(IBattleModifier modifier)
        {
            if (!_modifiers.Contains(modifier)) _modifiers.Add(modifier);
        }
        public static void Unregister(IBattleModifier modifier) => _modifiers.Remove(modifier);
        public static void ClearAll() => _modifiers.Clear();
        public static IReadOnlyList<IBattleModifier> Modifiers => _modifiers;

        /// <summary>
        /// Invokes every registered <see cref="IBattleModifier"/> against <paramref name="force"/>
        /// in the context of <paramref name="op"/>. Modifiers that throw are logged and skipped.
        /// </summary>
        public static void InvokeModifyForce(MilitaryOperation op, MilitaryForce force, bool isAttacker)
        {
            foreach (IBattleModifier modifier in _modifiers)
            {
                try
                {
                    modifier.ModifyForce(op, force, isAttacker);
                }
                catch (Exception e)
                {
                    LogUtil.Error($"IBattleModifier {modifier.GetType().Name} threw in ModifyForce: {e}");
                }
            }
        }
    }
}
