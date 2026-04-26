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
        /// Op-aware invocation. Preferred entry point. For each registered modifier, dispatches
        /// to <see cref="IBattleModifierWithOp.ModifyForce(MilitaryOperation, MilitaryForce, bool)"/>
        /// when supported, or falls back to the legacy <see cref="IBattleModifier.ModifyForce(MilitaryForce, bool)"/>.
        /// </summary>
        public static void InvokeModifyForce(MilitaryOperation op, MilitaryForce force, bool isAttacker)
        {
            foreach (IBattleModifier modifier in _modifiers)
            {
                try
                {
                    if (modifier is IBattleModifierWithOp opAware)
                    {
                        opAware.ModifyForce(op, force, isAttacker);
                    }
                    else
                    {
#pragma warning disable 0618 // legacy fallback for impls that don't yet support op context
                        modifier.ModifyForce(force, isAttacker);
#pragma warning restore 0618
                    }
                }
                catch (Exception e)
                {
                    LogUtil.Error($"IBattleModifier {modifier.GetType().Name} threw in ModifyForce: {e}");
                }
            }
        }

        /// <summary>
        /// Legacy invocation without op context. Kept as a transitional fallback while the base
        /// mod migrates to <see cref="InvokeModifyForce(MilitaryOperation, MilitaryForce, bool)"/>.
        /// </summary>
        [Obsolete("Use InvokeModifyForce(MilitaryOperation, MilitaryForce, bool) instead. Will be removed in a future version.")]
        public static void InvokeModifyForce(MilitaryForce force, bool isAttacker)
        {
            InvokeModifyForce(null, force, isAttacker);
        }
    }
}
