using FactionColonies.util;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI.Group;

namespace FactionColonies
{
    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    class PawnDraftGizmos
    {
        public static void Postfix(ref Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            // Early exit checks BEFORE any allocations - most pawns will exit here
            if (__result == null || __instance?.Faction == null || __instance.Map == null)
            {
                return;
            }

            WorldSettlementFC settlementFc = __instance.Map.Parent as WorldSettlementFC;
            if (settlementFc == null)
            {
                return;
            }

            Faction playerColonyFaction = FindFC.EmpireFaction;

            // Only allow drafting Empire defenders during an active battle
            if (__instance.Faction == playerColonyFaction && settlementFc.MilitaryComp?.isUnderAttack == true)
            {
                Pawn pawn = __instance;
                var milComp = settlementFc.MilitaryComp;

                Command_Toggle draftColonists = new Command_Toggle
                {
                    hotKey = KeyBindingDefOf.Command_ColonistDraft,
                    isActive = () => false,
                    toggleAction = () =>
                    {
                        if (pawn.Faction == Faction.OfPlayer) return;
                        pawn.SetFaction(Faction.OfPlayer);
                        // SetFaction → AddAndRemoveDynamicComponents creates pawn.drafter for OfPlayer pawns
                        if (pawn.drafter != null)
                            pawn.drafter.Drafted = true;
                        // Track drafted NPC for faction restoration after battle
                        if (milComp != null && !milComp.draftedNPCs.Contains(pawn))
                            milComp.draftedNPCs.Add(pawn);
                    },
                    defaultDesc = "CommandToggleDraftDesc".Translate(),
                    icon = TexCommand.Draft,
                    turnOnSound = SoundDefOf.DraftOn,
                    groupKey = 81729172,
                    defaultLabel = "CommandDraftLabel".Translate()
                };

                if (pawn.Downed)
                {
                    draftColonists.Disable("IsIncapped".Translate(pawn.LabelShort, pawn));
                }

                draftColonists.tutorTag = "Draft";
                __result = __result.Append(draftColonists);
                return;
            }

            // Undraft toggle for drafted Empire NPCs (only during active battle)
            if (__instance.Faction == Faction.OfPlayer && __instance.Drafted
                && settlementFc.MilitaryComp?.isUnderAttack == true
                && settlementFc.MilitaryComp.draftedNPCs.Contains(__instance))
            {
                Pawn found = __instance;
                var milComp = settlementFc.MilitaryComp;

                List<Gizmo> output = __result.ToList();
                foreach (Gizmo gizmo in output)
                {
                    Command_Toggle action = gizmo as Command_Toggle;
                    if (action != null && action.hotKey == KeyBindingDefOf.Command_ColonistDraft)
                    {
                        action.toggleAction = () =>
                        {
                            found.SetFaction(FindFC.EmpireFaction);
                            milComp.draftedNPCs.Remove(found);
                            // Re-add to defenders list and defense lord after undrafting. Routes
                            // through the BattlefieldContext so per-op pawn lists stay aligned.
                            if (milComp.defenders.Any())
                            {
                                BattlefieldContext bf = FindFC.MilitaryManager?.GetBattlefield(milComp.WorldSettlement.Tile);
                                bf?.RegisterPawnsAsDefenders(new List<Pawn> { found }, assignToLord: false);

                                Pawn anchor = milComp.defenders.FirstOrDefault();
                                Lord defenderLord = anchor?.GetLord();
                                if (defenderLord != null && !defenderLord.ownedPawns.Contains(found))
                                {
                                    defenderLord.AddPawn(found);
                                    defenderLord.CurLordToil.UpdateAllDuties();
                                }
                            }
                        };
                        break;
                    }
                }

                __result = output;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    class PrisonerGizmosPatch
    {
        /// <summary>
        /// Checks if pawn is a valid prisoner that can be sent to settlements.
        /// Optimized to avoid expensive quest gizmo enumeration when possible.
        /// </summary>
        private static bool CanSendPrisoner(Pawn pawn)
        {
            // Fast checks first
            if (pawn.guest == null) return false;
            if (!pawn.guest.IsPrisoner) return false;
            if (!pawn.guest.PrisonerIsSecure) return false;

            // Only do expensive quest check if basic checks pass
            return QuestUtility.GetQuestRelatedGizmos(pawn).EnumerableNullOrEmpty();
        }

        /// <param name="prisoner"></param>
        /// <returns>A <c>Command_Action</c> that sends the selected <paramref name="prisoner"/> to an empire settlementFC.</returns>
        /// TODO: Replace this with needing to actually send the prisoner to the settlement via caravan or droppod? At the very least, the transfer shouldn't be instantaneous
        private static Command_Action SendPrisonerAction(Pawn prisoner) => new Command_Action
        {
            defaultLabel = "FCSendToSettlement".Translate(),
            defaultDesc = "",
            icon = TexLoad.iconMilitary,
            action = delegate
            {
                if (prisoner.Map.dangerWatcher.DangerRating != StoryDanger.None)
                {
                    Messages.Message("FCCantSendWithDangerLevel".Translate(prisoner.Map.dangerWatcher.DangerRating.ToString()), MessageTypeDefOf.RejectInput);
                    return;
                }

                List<FloatMenuOption> settlementList = FindFC.Settlements.Select(settlement => new FloatMenuOption("FCFloatMenuOptionSendPrisonerToSettlement".Translate(settlement.Name, settlement.settlementLevel, settlement.prisonerList.Count()), delegate
                {
                    //disappear prisoner
                    TravelUtil.SendPrisoner(prisoner, settlement);

                    foreach (var bed in Find.Maps.Where(map => map.IsPlayerHome).SelectMany(map => map.listerBuildings.allBuildingsColonist).OfType<Building_Bed>().Where(bed => bed.OwnersForReading.Any(bedPawn => bedPawn == prisoner)))
                    {
                        bed.ForOwnerType = BedOwnerType.Colonist;
                        bed.ForOwnerType = BedOwnerType.Prisoner;
                    }
                })).ToList();

                Find.WindowStack.Add(new FloatMenu(settlementList));
            }
        };

        public static void Postfix(ref Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            // Early exit for non-prisoners (most common case) hmmmm
            if (__instance.guest == null || !__instance.guest.IsPrisoner)
            {
                return;
            }

            if (FindFC.FactionComp is null) return;
            if (!FindFC.PolicyManager.IsActionAllowed(FCActionType.SendPrisoner)) return;
            if (!CanSendPrisoner(__instance)) return;

            __result = __result.Append(SendPrisonerAction(__instance));
        }
    }
}
