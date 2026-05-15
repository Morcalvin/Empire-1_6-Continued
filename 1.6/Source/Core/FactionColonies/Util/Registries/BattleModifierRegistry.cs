using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;

namespace FactionColonies
{
    /// <summary>
    /// Holds the three lists of power-modifying contributors. Implementations register
    /// here at mod init; <see cref="WorldComponent_EnemyPower"/> is the only sanctioned
    /// caller of the Invoke methods (it owns the lifecycle of cached entries and the
    /// engagement-time hook).
    /// </summary>
    public static class BattleModifierRegistry
    {
        private static readonly List<IFactionPowerModifier> _factionModifiers = new List<IFactionPowerModifier>();
        private static readonly List<ISettlementPowerModifier> _settlementModifiers = new List<ISettlementPowerModifier>();
        private static readonly List<IBattleModifier> _battleModifiers = new List<IBattleModifier>();

        /* === Registration === */

        public static void Register(IFactionPowerModifier modifier)
        {
            if (modifier is object && !_factionModifiers.Contains(modifier)) _factionModifiers.Add(modifier);
        }
        public static void Register(ISettlementPowerModifier modifier)
        {
            if (modifier is object && !_settlementModifiers.Contains(modifier)) _settlementModifiers.Add(modifier);
        }
        public static void Register(IBattleModifier modifier)
        {
            if (modifier is object && !_battleModifiers.Contains(modifier)) _battleModifiers.Add(modifier);
        }

        public static void Unregister(IFactionPowerModifier modifier) => _factionModifiers.Remove(modifier);
        public static void Unregister(ISettlementPowerModifier modifier) => _settlementModifiers.Remove(modifier);
        public static void Unregister(IBattleModifier modifier) => _battleModifiers.Remove(modifier);

        public static void ClearAll()
        {
            _factionModifiers.Clear();
            _settlementModifiers.Clear();
            _battleModifiers.Clear();
        }

        public static IReadOnlyList<IFactionPowerModifier> FactionModifiers => _factionModifiers;
        public static IReadOnlyList<ISettlementPowerModifier> SettlementModifiers => _settlementModifiers;
        public static IReadOnlyList<IBattleModifier> BattleModifiers => _battleModifiers;

        /* === Invocation (worldcomp-only) === */

        /// <summary>
        /// Faction-level cache pass. Called by <see cref="WorldComponent_EnemyPower"/> after
        /// <c>ComputeFactionBaseline</c> populates a faction entry's level/efficiency.
        /// </summary>
        public static void InvokeFactionPowerModifiers(Faction faction, EnemyPower power)
        {
            foreach (IFactionPowerModifier modifier in _factionModifiers)
            {
                try { modifier.ModifyFactionPower(faction, power); }
                catch (Exception e) { LogUtil.Error($"IFactionPowerModifier {modifier.GetType().Name} threw in ModifyFactionPower: {e}"); }
            }
        }

        /// <summary>
        /// Settlement-level cache pass. Called by <see cref="WorldComponent_EnemyPower"/> after
        /// a settlement entry is mirrored from its faction's baseline.
        /// </summary>
        public static void InvokeSettlementPowerModifiers(Settlement settlement, EnemyPower power)
        {
            foreach (ISettlementPowerModifier modifier in _settlementModifiers)
            {
                try { modifier.ModifySettlementPower(settlement, power); }
                catch (Exception e) { LogUtil.Error($"ISettlementPowerModifier {modifier.GetType().Name} threw in ModifySettlementPower: {e}"); }
            }
        }

        /// <summary>
        /// Attack-time pass. Called by <see cref="WorldComponent_EnemyPower"/>'s resolution
        /// helpers (ResolveDefenderForceForOp, ResolveDefenderBounds, ApplyBattleModifiers).
        /// </summary>
        public static void InvokeBattleModifiers(BattleForceContext ctx, MilitaryForce force, bool isAttacker)
        {
            foreach (IBattleModifier modifier in _battleModifiers)
            {
                try { modifier.ModifyForce(ctx, force, isAttacker); }
                catch (Exception e) { LogUtil.Error($"IBattleModifier {modifier.GetType().Name} threw in ModifyForce: {e}"); }
            }
        }
    }
}
