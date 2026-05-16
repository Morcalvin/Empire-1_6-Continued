using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Convenience base class for <see cref="ILifecycleParticipant"/>.
    /// All methods are empty virtuals. Override only what you need.
    /// </summary>
    public abstract class LifecycleParticipantBase : ILifecycleParticipant
    {
        public virtual void OnSettlementCreated(WorldSettlementFC settlement) { }
        public virtual void OnSettlementRemoved(WorldSettlementFC settlement) { }
        public virtual void OnSettlementUpgraded(WorldSettlementFC settlement, int oldLevel, int newLevel) { }
        public virtual void OnSettlementTypeChanged(WorldSettlementFC settlement, WorldSettlementDef oldDef, WorldSettlementDef newDef) { }
        public virtual void OnBuildingConstructed(WorldSettlementFC settlement, BuildingFCDef building, int slot) { }
        public virtual void OnBuildingDeconstructed(WorldSettlementFC settlement, BuildingFCDef building, int slot) { }
        public virtual void OnOperationCreated(MilitaryOperation op) { }
        public virtual void OnOperationResolved(MilitaryOperation op) { }
        public virtual void OnBattleResolved(MilitaryOperation op, bool victory, BattleResult result) { }
        public virtual void OnResearchCompleted(ResearchProjectDef project) { }
        public virtual void OnMercenaryDeath(MercenaryDeathEvent evt) { }
        public virtual void OnSquadHired(MercenarySquadFC squad) { }
        public virtual void OnSquadDismissed(MercenarySquadFC squad) { }
        public virtual void OnSquadUpgraded(MercenarySquadFC squad) { }
    }
}
