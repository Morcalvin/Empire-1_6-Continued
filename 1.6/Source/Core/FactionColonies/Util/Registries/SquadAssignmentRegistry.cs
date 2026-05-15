using System;
using System.Collections.Generic;

namespace FactionColonies
{
    public static class SquadAssignmentRegistry
    {
        private static readonly List<ISquadAssignmentValidator> _validators = new List<ISquadAssignmentValidator>();

        public static void Register(ISquadAssignmentValidator validator)
        {
            if (!_validators.Contains(validator)) _validators.Add(validator);
        }
        public static void Unregister(ISquadAssignmentValidator validator) => _validators.Remove(validator);
        public static void ClearAll() => _validators.Clear();

        /// <summary>
        /// Returns true if all registered validators allow the assignment.
        /// On first rejection, outputs the reason string.
        /// </summary>
        public static bool CanAssign(WorldSettlementFC settlement, MercenarySquadFC squad, out string reason)
        {
            reason = null;
            foreach (ISquadAssignmentValidator validator in _validators)
            {
                try
                {
                    if (!validator.CanAssign(settlement, squad, out reason)) return false;
                }
                catch (Exception e)
                {
                    LogUtil.Error($"ISquadAssignmentValidator {validator.GetType().Name} threw in CanAssign: {e}");
                }
            }
            return true;
        }
    }
}
