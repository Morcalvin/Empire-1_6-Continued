using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace FactionColonies
{
    public static class FoundingValidatorRegistry
    {
        private static readonly List<ISettlementFoundingValidator> _validators = new List<ISettlementFoundingValidator>();
        private static readonly List<string> _emptyDescriptions = new List<string>();

        public static void Register(ISettlementFoundingValidator validator)
        {
            if (!_validators.Contains(validator)) _validators.Add(validator);
        }
        public static void Unregister(ISettlementFoundingValidator validator) => _validators.Remove(validator);
        public static void ClearAll() => _validators.Clear();

        /// <summary>
        /// Returns true if all registered validators allow settlement founding.
        /// Appends rejection reasons to <paramref name="reason"/>.
        /// </summary>
        public static bool CanFound(PlanetTile tile, WorldSettlementDef type, StringBuilder reason)
        {
            bool allowed = true;
            foreach (ISettlementFoundingValidator validator in _validators)
            {
                try
                {
                    if (!validator.CanFoundSettlement(tile, type, out string r))
                    {
                        if (reason != null && !r.NullOrEmpty())
                        {
                            if (reason.Length > 0) reason.AppendLine();
                            reason.Append(r);
                        }
                        allowed = false;
                    }
                }
                catch (Exception e)
                {
                    LogUtil.Error($"ISettlementFoundingValidator {validator.GetType().Name} threw in CanFoundSettlement: {e}");
                }
            }
            return allowed;
        }

        /// <summary>
        /// Collects all non-null additional cost descriptions from registered validators.
        /// </summary>
        public static List<string> GetCostDescriptions(PlanetTile tile, WorldSettlementDef type)
        {
            if (_validators.Count == 0) return _emptyDescriptions;
            List<string> descriptions = new List<string>();
            foreach (ISettlementFoundingValidator validator in _validators)
            {
                try
                {
                    string desc = validator.GetAdditionalCostDescription(tile, type);
                    if (!desc.NullOrEmpty()) descriptions.Add(desc);
                }
                catch (Exception e)
                {
                    LogUtil.Error($"ISettlementFoundingValidator {validator.GetType().Name} threw in GetAdditionalCostDescription: {e}");
                }
            }
            return descriptions;
        }

        /// <summary>
        /// Notifies all validators that a settlement was successfully submitted for founding.
        /// Called after silver payment in DoFoundSettlement().
        /// </summary>
        public static void NotifyFounded(PlanetTile tile, WorldSettlementDef type)
        {
            foreach (ISettlementFoundingValidator validator in _validators)
            {
                try
                {
                    validator.OnSettlementFounded(tile, type);
                }
                catch (Exception e)
                {
                    LogUtil.Error($"ISettlementFoundingValidator {validator.GetType().Name} threw in OnSettlementFounded: {e}");
                }
            }
        }
    }
}
