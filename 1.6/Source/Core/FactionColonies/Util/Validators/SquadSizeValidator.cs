using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Built-in <see cref="ISquadAssignmentValidator"/>. Rejects assignment when the squad's
    /// active mercenary count exceeds the target settlement's <see cref="WorldSettlementFC.MaxSquadSize"/>.
    /// </summary>
    public class SquadSizeValidator : ISquadAssignmentValidator
    {
        public bool CanAssign(WorldSettlementFC settlement, MercenarySquadFC squad, out string reason)
        {
            reason = null;
            if (settlement is null || squad?.mercenaries is null) return true;
            int size = 0;
            foreach (Mercenary m in squad.mercenaries)
            {
                if (m?.loadout is null) continue;
                if (m.loadout.isBlank) continue;
                size++;
            }
            int max = settlement.MaxSquadSize;
            if (size > max)
            {
                reason = "FCSquadSizeExceeded".Translate(settlement.Name, size, max);
                return false;
            }
            return true;
        }
    }
}
