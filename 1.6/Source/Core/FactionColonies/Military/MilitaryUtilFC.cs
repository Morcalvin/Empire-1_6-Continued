using System.Collections.Generic;
using System.Linq;
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
            // (with linkedOperationId), and the "settlement in danger" letter.
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
        /// Empire settlement (<paramref name="settlementOfMilitaryForce"/>). If there's no linked
        /// op (legacy save data), falls through to the pre-Phase-2 event-mutation path.
        /// </summary>
        public static void ChangeDefendingMilitaryForce(FCEvent evt, WorldSettlementFC settlementOfMilitaryForce)
        {
            FactionFC factionfc = FactionCache.FactionComp;
            if (factionfc is null) return;
            WorldSettlementFC homeSettlement = factionfc.ReturnSettlementByLocation(evt.location);

            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            MilitaryOperation op = manager?.GetOp(evt.linkedOperationId);

            if (op is object)
            {
                if (settlementOfMilitaryForce == op.defender?.homeSettlement)
                {
                    Messages.Message("FCMilitaryAlreadyDefendingSettlement".Translate(), MessageTypeDefOf.RejectInput);
                    return;
                }

                // Release the previous defender's commitment (foreign settlement marker or external).
                ReleaseCurrentDefender(op, homeSettlement);

                MilitaryForce newForce;
                if (settlementOfMilitaryForce == homeSettlement)
                {
                    newForce = MilitaryForce.CreateMilitaryForceFromSettlement(homeSettlement);
                    op.defender.homeSettlement = homeSettlement;
                    op.defender.force = newForce;
                    op.externalDefenderSource = null;
                    Messages.Message("FCDefendingMilitaryReset".Translate(), MessageTypeDefOf.NeutralEvent);
                }
                else
                {
                    MilitaryForce homeForce = MilitaryForce.CreateMilitaryForceFromSettlement(homeSettlement, isAttacking: true);
                    newForce = MilitaryForce.CreateMilitaryForceFromSettlement(settlementOfMilitaryForce, homeDefendingForce: homeForce);
                    op.defender.homeSettlement = settlementOfMilitaryForce;
                    op.defender.force = newForce;
                    op.externalDefenderSource = null;

                    // The foreign defender's DefendFriendlySettlement commitment is now derived
                    // from op.defender.homeSettlement via the comp's computed properties
                    // (militaryBusy / militaryJob / militaryLocation / militaryEnemy). No shadow
                    // writes needed.

                    Find.LetterStack.ReceiveLetter("FCMilitaryAction".Translate(), "FCForeignMilitarySwitch".Translate(
                        settlementOfMilitaryForce.Name, homeSettlement?.Name ?? "", newForce.militaryLevel),
                        LetterDefOf.NeutralEvent);
                }

                // Mirror the new force onto the warning event so legacy comp.StartDefence (which
                // reads evt.militaryForceDefending directly) sees the updated defender.
                evt.militaryForceDefending = newForce;
                evt.externalDefenderSource = null;

                // comp.defenderForce is now a computed property reading from op.defender.force,
                // so updating op.defender.force above is sufficient — no shadow write needed.
                return;
            }

            // Legacy fallback (no linked op — e.g. pre-refactor save mid-warning).
            ChangeDefendingMilitaryForce_Legacy(evt, settlementOfMilitaryForce, factionfc, homeSettlement);
        }

        /// <summary>
        /// Replaces the op's defender with the given external <see cref="IAutoDefender"/>.
        /// Falls back to the legacy event-mutation path when no linked op exists.
        /// </summary>
        public static void ChangeDefendingToExternalForce(FCEvent evt, IAutoDefender defender)
        {
            FactionFC factionfc = FactionCache.FactionComp;
            if (factionfc is null) return;

            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            MilitaryOperation op = manager?.GetOp(evt.linkedOperationId);

            if (op is object)
            {
                if (op.externalDefenderSource is object && op.externalDefenderSource == defender.WorldObject)
                {
                    Messages.Message("FCMilitaryAlreadyDefendingSettlement".Translate(), MessageTypeDefOf.RejectInput);
                    return;
                }

                ReleaseCurrentDefender(op, factionfc.ReturnSettlementByLocation(evt.location));

                op.defender.homeSettlement = null;
                op.defender.force = defender.CreateDefendingForce();
                op.externalDefenderSource = defender.WorldObject;
                defender.OnDefenseStarted(evt.settlementFCDefending ?? op.targetObject);

                evt.militaryForceDefending = op.defender.force;
                evt.externalDefenderSource = defender.WorldObject;

                // comp.defenderForce is computed from op.defender.force.
                Messages.Message("FCExternalDefenderAssigned".Translate(defender.WorldObject.LabelCap),
                    MessageTypeDefOf.NeutralEvent);
                return;
            }

            // Legacy fallback.
            ChangeDefendingToExternalForce_Legacy(evt, defender, factionfc);
        }

        /// <summary>
        /// Clears the previous defender's commitment markers (foreign settlement's
        /// DefendFriendlySettlement shadow or external auto-defender's OnDefenseReplaced).
        /// </summary>
        private static void ReleaseCurrentDefender(MilitaryOperation op, WorldSettlementFC homeSettlement)
        {
            // External defender being replaced: notify it.
            if (op.externalDefenderSource is object)
            {
                IAutoDefender old = AutoDefenderRegistry.FindByWorldObject(op.externalDefenderSource);
                old?.OnDefenseReplaced();
                op.externalDefenderSource = null;
                return;
            }

            // Foreign Empire settlement being replaced: clearing op.defender.homeSettlement
            // will be the caller's responsibility (it sets a new defender). The comp's computed
            // properties auto-update.
        }

        public static MilitaryForce ReturnDefendingMilitaryForce(FCEvent evt)
        {
            return evt.militaryForceDefending;
        }

        public static FCEvent ReturnMilitaryEventByLocation(PlanetTile location)
        {
            return FactionCache.FactionComp.FindEventByDefAndLocation(FCEventDefOf.settlementBeingAttacked, location);
        }

        public static IReadOnlyList<FCEvent> ReturnMilitaryEventsByLocation(PlanetTile location)
        {
            return FactionCache.FactionComp.FindAllEventsByDefAndLocation(FCEventDefOf.settlementBeingAttacked, location);
        }

        /* -*-*-*-*- Legacy fallback paths -*-*-*-*-
         * Used when a defensive event has no linkedOperationId (pre-refactor save loaded
         * mid-warning). They mutate the FCEvent's militaryForce* fields directly and dispatch
         * the foreign defender's DefendFriendlySettlement marker via the comp's legacy
         * SendMilitary code path. Kept for save-format back-compat for one version.
         */

        private static void ChangeDefendingMilitaryForce_Legacy(FCEvent evt, WorldSettlementFC settlementOfMilitaryForce,
            FactionFC factionfc, WorldSettlementFC homeSettlement)
        {
            MilitaryForce tmpMilitaryForce = null;

            if (evt.militaryForceDefending?.homeSettlement is object
                && settlementOfMilitaryForce == evt.militaryForceDefending.homeSettlement)
            {
                Messages.Message("FCMilitaryAlreadyDefendingSettlement".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            WorldSettlementFC target = Find.World.worldObjects.WorldObjectAt<WorldSettlementFC>(evt.location);

            if (evt.militaryForceDefending?.homeSettlement is object
                && evt.militaryForceDefending.homeSettlement != homeSettlement)
            {
#pragma warning disable 0618
                evt.militaryForceDefending.homeSettlement.MilitaryComp?.ReturnMilitary(false);
#pragma warning restore 0618
            }
            else if (evt.externalDefenderSource is object)
            {
                IAutoDefender autoDefender = AutoDefenderRegistry.FindByWorldObject(evt.externalDefenderSource);
                autoDefender?.OnDefenseReplaced();
                evt.externalDefenderSource = null;
            }

            if (settlementOfMilitaryForce != homeSettlement)
            {
                tmpMilitaryForce = MilitaryForce.CreateMilitaryForceFromSettlement(homeSettlement, isAttacking: true);
            }

            factionfc.RemoveMilitaryTarget(evt.location);
            evt.militaryForceDefending = MilitaryForce.CreateMilitaryForceFromSettlement(
                settlementOfMilitaryForce, homeDefendingForce: tmpMilitaryForce);

            if (target?.MilitaryComp is null) return;
            // comp.defenderForce is computed from manager state — no shadow write needed.

            if (settlementOfMilitaryForce == homeSettlement)
            {
                Messages.Message("FCDefendingMilitaryReset".Translate(), MessageTypeDefOf.NeutralEvent);
            }
            else
            {
                settlementOfMilitaryForce.MilitaryComp?.SendMilitary(
                    evt.settlementFCDefending.Tile, MilitaryJobDefOf.DefendFriendlySettlement, -1,
                    evt.militaryForceAttackingFaction);
                Find.LetterStack.ReceiveLetter("FCMilitaryAction".Translate(), "FCForeignMilitarySwitch".Translate(
                    settlementOfMilitaryForce.Name, homeSettlement?.Name ?? "", evt.militaryForceDefending.militaryLevel),
                    LetterDefOf.NeutralEvent);
            }
        }

        private static void ChangeDefendingToExternalForce_Legacy(FCEvent evt, IAutoDefender defender, FactionFC factionfc)
        {
            if (evt.externalDefenderSource is object && evt.externalDefenderSource == defender.WorldObject)
            {
                Messages.Message("FCMilitaryAlreadyDefendingSettlement".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (evt.militaryForceDefending?.homeSettlement is object
                && evt.militaryForceDefending.homeSettlement != factionfc.ReturnSettlementByLocation(evt.location))
            {
#pragma warning disable 0618
                evt.militaryForceDefending.homeSettlement.MilitaryComp?.ReturnMilitary(false);
#pragma warning restore 0618
            }
            else if (evt.externalDefenderSource is object)
            {
                IAutoDefender old = AutoDefenderRegistry.FindByWorldObject(evt.externalDefenderSource);
                old?.OnDefenseReplaced();
            }

            factionfc.RemoveMilitaryTarget(evt.location);
            evt.militaryForceDefending = defender.CreateDefendingForce();
            evt.externalDefenderSource = defender.WorldObject;
            defender.OnDefenseStarted(evt.settlementFCDefending);

            // comp.defenderForce is now a computed property — no shadow write needed.

            Messages.Message("FCExternalDefenderAssigned".Translate(defender.WorldObject.LabelCap),
                MessageTypeDefOf.NeutralEvent);
        }
    }
}
