using RimWorld;

namespace FactionColonies
{
    /// <summary>
    /// Server-style handler for a <see cref="MilitaryJobDef"/>. The manager calls these methods
    /// against an active <see cref="MilitaryOperation"/> at the appropriate phase transitions.
    /// Use the four virtual methods to implement a job: <see cref="OnOpCreated"/> (schedule
    /// arrival event, send letters), <see cref="OnAutoResolve"/> (compute the battle result),
    /// <see cref="ApplyResult"/> (loot / prisoners / settlement capture / XP), and
    /// <see cref="OnManualResolve"/> + <see cref="ResolvesManually"/> for player-driven battles.
    /// </summary>
    public abstract class MilitaryJobHandler
    {
        public MilitaryJobDef def;

        /// <summary>Returns whether this job can target the given faction. Used to filter hostile menu options.</summary>
        public virtual bool IsValidTarget(Faction targetFaction) => true;

        /// <summary>
        /// Called immediately after <see cref="MilitaryOperationManager"/> registers an op of
        /// this handler's <see cref="def"/>. Use for handler-specific op setup: scheduling the
        /// arrival event, sending the player a "we're sending forces" letter, etc. Default: no-op.
        /// </summary>
        public virtual void OnOpCreated(MilitaryOperation op) { }

        /// <summary>
        /// If true, <see cref="MilitaryOperation.OnEventFired"/> delegates resolution to
        /// <see cref="OnManualResolve(MilitaryOperation)"/> instead of calling
        /// <see cref="OnAutoResolve(MilitaryOperation)"/>. The handler is responsible for calling
        /// <see cref="MilitaryOperation.CompleteBattle"/> when the player-driven battle resolves.
        /// </summary>
        public virtual bool ResolvesManually(MilitaryOperation op) => false;

        /// <summary>
        /// Op-aware auto-resolution. Returns the <see cref="BattleResult"/>; side effects
        /// (loot, prisoners, settlement capture) live in <see cref="ApplyResult"/>.
        /// </summary>
        public abstract BattleResult OnAutoResolve(MilitaryOperation op);

        /// <summary>
        /// Op-aware manual resolution. Submods that opt in via <see cref="ResolvesManually"/>
        /// override this to spawn pawns / lords on the op's <see cref="BattlefieldContext"/>.
        /// Submods are responsible for calling <c>op.CompleteBattle(result)</c> when the
        /// player-driven battle resolves. Default: no-op.
        /// </summary>
        public virtual void OnManualResolve(MilitaryOperation op) { }

        /// <summary>
        /// Side-effect application for an op's resolution: loot, prisoners, settlement capture,
        /// XP, etc. Called by <c>op.CompleteBattle(result)</c> after a battle resolves (auto or
        /// manual). Splitting this from <see cref="OnAutoResolve"/> lets manual handlers reuse
        /// the same outcome handling. Default: no-op.
        /// </summary>
        public virtual void ApplyResult(MilitaryOperation op, BattleResult result) { }
    }
}
