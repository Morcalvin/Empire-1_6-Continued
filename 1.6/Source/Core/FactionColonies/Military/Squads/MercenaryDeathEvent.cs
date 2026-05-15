namespace FactionColonies
{
    /// <summary>
    /// Event data passed to <see cref="ILifecycleParticipant.OnMercenaryDeath"/> when a mercenary is killed.
    /// Listeners can inspect the dying mercenary's data and optionally set <see cref="CancelReplacement"/>
    /// to prevent the default auto-replacement behavior.
    /// </summary>
    public class MercenaryDeathEvent
    {
        /// <summary>The mercenary that was killed. Still has its original pawn, loadout, and custom data at this point.</summary>
        public Mercenary Mercenary { get; private set; }

        /// <summary>The squad the mercenary belonged to.</summary>
        public MercenarySquadFC Squad { get; private set; }

        /// <summary>The settlement the squad is assigned to (may be null if squad has no settlement).</summary>
        public WorldSettlementFC Settlement { get; private set; }

        /// <summary>
        /// Set to true to prevent the default auto-replacement of the dead mercenary.
        /// When true, <see cref="MercenarySquadFC.PassPawnToDeadMercenaries"/> will NOT be called,
        /// and the listener is responsible for handling the empty slot.
        /// </summary>
        public bool CancelReplacement { get; set; }

        public MercenaryDeathEvent(Mercenary mercenary, MercenarySquadFC squad, WorldSettlementFC settlement)
        {
            Mercenary = mercenary;
            Squad = squad;
            Settlement = settlement;
            CancelReplacement = false;
        }
    }
}
