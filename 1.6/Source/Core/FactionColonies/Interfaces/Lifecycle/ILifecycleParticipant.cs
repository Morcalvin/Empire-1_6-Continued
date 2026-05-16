using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Unified lifecycle hook for settlement, building, military, and research events.
    /// Register implementations via <see cref="LifecycleRegistry"/>.
    /// Use <see cref="LifecycleParticipantBase"/> to avoid stubbing unused methods.
    /// </summary>
    public interface ILifecycleParticipant
    {
        void OnSettlementCreated(WorldSettlementFC settlement);
        void OnSettlementRemoved(WorldSettlementFC settlement);
        void OnSettlementUpgraded(WorldSettlementFC settlement, int oldLevel, int newLevel);
        void OnSettlementTypeChanged(WorldSettlementFC settlement, WorldSettlementDef oldDef, WorldSettlementDef newDef);
        void OnBuildingConstructed(WorldSettlementFC settlement, BuildingFCDef building, int slot);
        void OnBuildingDeconstructed(WorldSettlementFC settlement, BuildingFCDef building, int slot);
        /// <summary>Called immediately after a <see cref="MilitaryOperation"/> is created and registered.
        /// Fires for every op (offensive, defensive, deploy). For defensive ops the defending squad
        /// is reachable via <c>op.defender.squad</c> when an Empire settlement is the defender.</summary>
        void OnOperationCreated(MilitaryOperation op);
        /// <summary>Called when an op resolves. Any squads referenced by <c>op.aggressor.squad</c>
        /// or <c>op.defender.squad</c> are freed at this point.</summary>
        void OnOperationResolved(MilitaryOperation op);
        /// <summary>Called after the battle simulation / manual battle has produced a result.</summary>
        void OnBattleResolved(MilitaryOperation op, bool victory, BattleResult result);
        void OnResearchCompleted(ResearchProjectDef project);
        /// <summary>
        /// Called when a mercenary is killed. Notification only. No built-in replacement
        /// behavior is gated by listeners (auto-replacement was removed; refilling empty
        /// slots is a player-driven action via <see cref="MercenarySquadFC.FillEmptySlots"/>).
        /// </summary>
        void OnMercenaryDeath(MercenaryDeathEvent evt);
        /// <summary>
        /// Called immediately after a fresh <see cref="MercenarySquadFC"/> is hired (silver paid,
        /// squad created from a template, added to <c>mercenarySquads</c>) and before the player
        /// has assigned it to a settlement. <c>squad.settlement</c> is null at this point.
        /// </summary>
        void OnSquadHired(MercenarySquadFC squad);
        /// <summary>
        /// Called when a squad is dismissed by the player. The squad has been removed from
        /// <c>mercenarySquads</c>.
        /// </summary>
        void OnSquadDismissed(MercenarySquadFC squad);
        /// <summary>
        /// Called after a squad's loadout is brought up to its source template via
        /// <see cref="SquadUpgradeUtil.UpgradeToTemplate"/>. Silver has already been paid.
        /// </summary>
        void OnSquadUpgraded(MercenarySquadFC squad);
    }
}
