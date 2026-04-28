using System.Collections.Generic;
using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FactionColonies
{
    public static class MilitaryUtilFC
    {
        /// <summary>
        /// Schedules a defensive operation against an Empire settlement: creates a
        /// <see cref="MilitaryOperation"/> via the manager, runs auto-defender selection,
        /// and queues the 24-hour <c>settlementBeingAttacked</c> warning event linked back
        /// to the op. Returns true on success, false if the attack was rejected (already
        /// under attack or no comp).
        /// </summary>
        public static bool AttackPlayerSettlement(MilitaryForce attackingForce, WorldSettlementFC settlement, Faction enemyFaction)
        {
            if (settlement?.MilitaryComp is null)
            {
                LogUtil.Warning($"AttackPlayerSettlement rejected: {settlement?.Name ?? "null"} has no MilitaryComp. " +
                    $"Attacker {enemyFaction?.Name ?? "null"} dropped.");
                return false;
            }

            var milComp = settlement.MilitaryComp;
            FCEvent existingEvent = ReturnMilitaryEventByLocation(settlement.Tile);

            // Allow new attacks if a manual battle is active on the map (wave defense)
            // or the map is still loaded post-battle (map reuse). Reject only when there's
            // a pending warning-phase event and no active battle map yet.
            bool hasActiveBattle = milComp.isUnderAttack && settlement.HasMap;
            bool hasPostBattleMap = !milComp.isUnderAttack && settlement.HasMap;
            if (!hasActiveBattle && !hasPostBattleMap && (milComp.isUnderAttack || existingEvent is object))
            {
                LogUtil.Warning($"AttackPlayerSettlement rejected: {settlement.Name} is already under attack " +
                    $"(isUnderAttack={milComp.isUnderAttack}, existingEvent={existingEvent is object}). " +
                    $"Attacker {enemyFaction?.Name ?? "null"} dropped.");
                return false;
            }

            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            if (manager is null)
            {
                LogUtil.Error("AttackPlayerSettlement: MilitaryManager unavailable.");
                return false;
            }

            // Manager handles op creation, auto-defender selection, warning event scheduling
            // (with linkedOperation back-reference), and the "settlement in danger" letter.
            MilitaryOperation op = manager.CreateDefensiveOp(settlement, attackingForce, enemyFaction);
            return op is object;
        }

        /// <summary>
        /// Attacks an external <see cref="IRaidTarget"/> registered via <see cref="RaidTargetRegistry"/>.
        /// Routes through <see cref="MilitaryOperationManager.CreateDefensiveOp"/> with the target's
        /// world object as the op's <c>targetObject</c>. The 24-hour warning, auto-defender selection,
        /// and forecast letter all happen inside the manager.
        /// </summary>
        public static void AttackRaidTarget(MilitaryForce attackingForce, IRaidTarget target, Faction enemyFaction)
        {
            if (target?.WorldObject is null) return;
            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            if (manager is null)
            {
                LogUtil.Error("AttackRaidTarget: MilitaryManager unavailable.");
                return;
            }

            MilitaryOperation op = manager.CreateDefensiveOp(target.WorldObject, attackingForce, enemyFaction);
            if (op is object)
            {
                target.IsUnderAttack = true;
            }
        }

        /// <summary>
        /// Replaces the defending side of the op linked to <paramref name="evt"/> with a new
        /// Empire settlement (<paramref name="settlementOfMilitaryForce"/>). No-op if no linked op
        /// exists (the warning event must be op-linked, which is true for any save processed by
        /// <see cref="MilitaryMigrationUtil"/> on load).
        /// </summary>
        public static void ChangeDefendingMilitaryForce(FCEvent evt, WorldSettlementFC settlementOfMilitaryForce)
        {
            FactionFC factionfc = FactionCache.FactionComp;
            if (factionfc is null) return;
            WorldSettlementFC homeSettlement = factionfc.ReturnSettlementByLocation(evt.location);

            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            MilitaryOperation op = evt.linkedOperation;
            if (op is null)
            {
                LogUtil.Warning($"ChangeDefendingMilitaryForce: warning event at tile {evt.location} has no linked op.");
                return;
            }

            if (settlementOfMilitaryForce == op.defender?.homeSettlement)
            {
                Messages.Message("FCMilitaryAlreadyDefendingSettlement".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            // Release the previous defender's commitment (foreign settlement marker or external).
            ReleaseCurrentDefender(op);

            // Reindex by detaching the op from manager indices, mutating the defender, then
            // re-registering. _bySquad / _bySettlement are keyed off op.defender.* and would
            // otherwise lag the swap.
            manager.Unregister(op);

            MilitaryForce newForce;
            if (settlementOfMilitaryForce == homeSettlement)
            {
                newForce = MilitaryForce.CreateMilitaryForceFromSettlement(homeSettlement);
                op.defender.homeSettlement = homeSettlement;
                op.defender.squad = homeSettlement?.MilitaryComp?.militarySquad;
                op.defender.force = newForce;
                op.externalDefenderSource = null;
                Messages.Message("FCDefendingMilitaryReset".Translate(), MessageTypeDefOf.NeutralEvent);
            }
            else
            {
                MilitaryForce homeForce = MilitaryForce.CreateMilitaryForceFromSettlement(homeSettlement, isAttacking: true);
                newForce = MilitaryForce.CreateMilitaryForceFromSettlement(settlementOfMilitaryForce, homeDefendingForce: homeForce);
                op.defender.homeSettlement = settlementOfMilitaryForce;
                op.defender.squad = settlementOfMilitaryForce.MilitaryComp?.militarySquad;
                op.defender.force = newForce;
                op.externalDefenderSource = null;

                Find.LetterStack.ReceiveLetter("FCMilitaryAction".Translate(), "FCForeignMilitarySwitch".Translate(
                    settlementOfMilitaryForce.Name, homeSettlement?.Name ?? "", newForce.militaryLevel),
                    LetterDefOf.NeutralEvent);
            }

            manager.Register(op);
        }

        /// <summary>
        /// Replaces the op's defender with the given external <see cref="IAutoDefender"/>.
        /// No-op if no linked op exists.
        /// </summary>
        public static void ChangeDefendingToExternalForce(FCEvent evt, IAutoDefender defender)
        {
            FactionFC factionfc = FactionCache.FactionComp;
            if (factionfc is null) return;

            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            MilitaryOperation op = evt.linkedOperation;
            if (op is null)
            {
                LogUtil.Warning($"ChangeDefendingToExternalForce: warning event at tile {evt.location} has no linked op.");
                return;
            }

            if (op.externalDefenderSource is object && op.externalDefenderSource == defender.WorldObject)
            {
                Messages.Message("FCMilitaryAlreadyDefendingSettlement".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            ReleaseCurrentDefender(op);

            // Reindex via Unregister + Register so the manager indices reflect the new
            // defender.homeSettlement / defender.squad (both go null for an external defender).
            manager.Unregister(op);

            op.defender.homeSettlement = null;
            op.defender.squad = null;
            op.defender.force = defender.CreateDefendingForce();
            op.externalDefenderSource = defender.WorldObject;

            manager.Register(op);

            // OnDefenseStarted fires from MilitaryOperation.BeginEngagement when the warning event
            // resolves — not here. Firing it now would cause a double-fire: once at swap time and
            // again at engagement, with only a single matching OnDefenseComplete.

            Messages.Message("FCExternalDefenderAssigned".Translate(defender.WorldObject.LabelCap),
                MessageTypeDefOf.NeutralEvent);
        }

        /// <summary>
        /// Clears the previous defender's commitment markers. For external defenders, fires the
        /// auto-defender's <c>OnDefenseReplaced</c> hook. Foreign Empire settlement defenders are
        /// released by the caller assigning a new <c>op.defender.homeSettlement</c>.
        /// </summary>
        private static void ReleaseCurrentDefender(MilitaryOperation op)
        {
            if (op.externalDefenderSource is object)
            {
                IAutoDefender old = AutoDefenderRegistry.FindByWorldObject(op.externalDefenderSource);
                old?.OnDefenseReplaced();
                op.externalDefenderSource = null;
            }
        }

        public static FCEvent ReturnMilitaryEventByLocation(PlanetTile location)
        {
            return FactionCache.FactionComp.FindEventByDefAndLocation(FCEventDefOf.settlementBeingAttacked, location);
        }

        public static IReadOnlyList<FCEvent> ReturnMilitaryEventsByLocation(PlanetTile location)
        {
            return FactionCache.FactionComp.FindAllEventsByDefAndLocation(FCEventDefOf.settlementBeingAttacked, location);
        }
    }
}
