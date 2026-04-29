using System;
using System.Collections.Generic;

namespace FactionColonies
{
    /// <summary>
    /// Resolves a <see cref="MercenarySquadFC"/> to its projected combat power
    /// (<see cref="SquadPower"/>). Two squads at the same billet project distinct
    /// forces because the base computation reads each squad's actual loadout cost.
    /// <para>The registry first computes a base power from loadout cost + the
    /// home settlement's <see cref="FCStatDefOf.militaryCombatEfficiency"/>, then
    /// chains every registered <see cref="ISquadPowerModifier"/> in descending
    /// <see cref="ISquadPowerModifier.Priority"/> order. Each modifier receives
    /// the running power and returns the modified value, so multiple submods
    /// compose (veterancy + specialist + augmentation each contribute).</para>
    /// </summary>
    public static class SquadPowerRegistry
    {
        private static readonly List<ISquadPowerModifier> _modifiers = new List<ISquadPowerModifier>();
        private static List<ISquadPowerModifier> _sortedCache;

        public static void Register(ISquadPowerModifier modifier)
        {
            if (modifier is null) return;
            if (!_modifiers.Contains(modifier))
            {
                _modifiers.Add(modifier);
                _sortedCache = null;
            }
        }

        public static void Unregister(ISquadPowerModifier modifier)
        {
            if (_modifiers.Remove(modifier)) _sortedCache = null;
        }

        public static void ClearAll()
        {
            _modifiers.Clear();
            _sortedCache = null;
        }

        public static IReadOnlyList<ISquadPowerModifier> Modifiers => _modifiers;

        /// <summary>Resolves <paramref name="squad"/> to its projected combat power.
        /// Returns a sensible default (level 1, efficiency 1) for a null or unassigned
        /// squad — callers don't need a null check on the result.</summary>
        public static SquadPower Resolve(MercenarySquadFC squad)
        {
            if (squad?.settlement is null) return new SquadPower(1, 1);

            SquadPower power = ComputeBasePower(squad);

            List<ISquadPowerModifier> sorted = GetSortedModifiers();
            for (int i = 0; i < sorted.Count; i++)
            {
                ISquadPowerModifier modifier = sorted[i];
                try
                {
                    power = modifier.ModifyPower(squad, power);
                }
                catch (Exception e)
                {
                    LogUtil.Error($"ISquadPowerModifier {modifier.GetType().Name} threw in ModifyPower: {e}");
                }
            }
            return power;
        }

        /// <summary>Inverse of <c>MilitaryCustomizationUtil.CalculateSquadBudget</c>:
        /// <c>cost = 1000 + 500·L + 600·L²  →  L = (-500 + √(250000 + 2400·(cost-1000))) / 1200</c>.
        /// Floored at 1 (matches the minimum settlement military level) so a near-zero-
        /// cost squad still has a projection. Result is a double so downstream can
        /// blend it before clamping.</summary>
        public static double LevelFromCost(double cost)
        {
            if (cost <= 1000) return 1.0;
            double disc = 250000.0 + 2400.0 * (cost - 1000.0);
            if (disc < 0) return 1.0;
            double level = (-500.0 + Math.Sqrt(disc)) / 1200.0;
            return Math.Max(1.0, level);
        }

        private static SquadPower ComputeBasePower(MercenarySquadFC squad)
        {
            double level = LevelFromCost(squad.GetCurrentLoadoutCost());
            double efficiency = 1.0;
            FactionFC faction = FactionCache.FactionComp;
            if (faction is object && squad.settlement is object)
            {
                efficiency = faction.GetStatValue(FCStatDefOf.militaryCombatEfficiency, squad.settlement);
            }
            return new SquadPower(level, efficiency);
        }

        private static List<ISquadPowerModifier> GetSortedModifiers()
        {
            if (_sortedCache != null) return _sortedCache;
            List<ISquadPowerModifier> copy = new List<ISquadPowerModifier>(_modifiers);
            copy.Sort((a, b) => b.Priority.CompareTo(a.Priority)); // highest first
            _sortedCache = copy;
            return _sortedCache;
        }
    }
}
