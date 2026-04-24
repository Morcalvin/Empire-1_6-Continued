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

}
