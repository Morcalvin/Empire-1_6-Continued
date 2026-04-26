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
#pragma warning disable 0618 // implementing the obsolete interface methods on the base class is intentional; warnings live at call sites that invoke them
        public virtual void OnSquadDeployed(WorldSettlementFC settlement, MilitaryJobDef job, bool isExtraSquad) { }
        public virtual void OnSquadRecalled(WorldSettlementFC settlement) { }
        public virtual void OnBattleResolved(WorldSettlementFC settlement, MilitaryJobDef job, bool victory, BattleResult result) { }
#pragma warning restore 0618
        public virtual void OnResearchCompleted(ResearchProjectDef project) { }
        public virtual void OnMercenaryDeath(MercenaryDeathEvent evt) { }
    }

    /// <summary>
    /// Op-aware convenience base class for <see cref="ILifecycleParticipantWithOp"/>. Inherits
    /// the 11 empty virtuals from <see cref="LifecycleParticipantBase"/> and adds empty virtuals
    /// for the three op-aware military hooks. Subclasses override only what they need.
    /// </summary>
    public abstract class LifecycleParticipantWithOpBase : LifecycleParticipantBase, ILifecycleParticipantWithOp
    {
        public virtual void OnSquadDeployed(MilitaryOperation op) { }
        public virtual void OnSquadRecalled(MilitaryOperation op) { }
        public virtual void OnBattleResolved(MilitaryOperation op, bool victory, BattleResult result) { }
    }
}
