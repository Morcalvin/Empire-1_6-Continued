using FactionColonies.util;
using HarmonyLib;
using Verse;

namespace FactionColonies
{
    [HarmonyPatch(typeof(Pawn), "Kill")]
    class MercenaryDied
    {
        static bool Prefix(Pawn __instance)
        {
            if (__instance.IsMercenary())
            {
                if (__instance.Faction != FindFC.EmpireFaction) __instance.SetFaction(FindFC.EmpireFaction);
                var mfc = FindFC.Military;
                if (mfc is null) return true;
                MercenarySquadFC squad = mfc.ReturnSquadFromUnit(__instance);
                if (squad != null)
                {
                    Mercenary merc = mfc.ReturnMercenaryFromUnit(__instance, squad);
                    if (merc != null)
                    {
                        if (squad.settlement != null)
                        {
                            const double basePenalty = 1.0;
                            double offset = FindFC.FactionComp?
                                .GetStatValue(FCStatDefOf.mercenaryDeathHappinessPenalty, squad.settlement) ?? 0;
                            double total = basePenalty + offset;
                            if (total < 0) total = 0;
                            squad.settlement.GainHappiness(-total);
                        }

                        // Fire death event so submods can react. Auto-replacement was removed by
                        // the strict-manual outfit refactor; the merc's slot is left as an empty
                        // placeholder (pawn = null) and the player must explicitly use
                        // "Fill Empty Slots" in the inspection window to refill it.
                        MercenaryDeathEvent deathEvt = new MercenaryDeathEvent(merc, squad, squad.settlement);
                        LifecycleRegistry.InvokeOnMercenaryDeath(deathEvt);

                        // Mark the slot empty — keep the Mercenary entry so its loadout reference
                        // survives for Fill, but null its pawn.
                        merc.pawn = null;
                        FindFC.Military?.RebuildMercenaryPawnSet();
                    }

                    squad.Equipment.RemoveDroppedEquipment();
                }
                else
                {
                    LogUtil.Warning("Mercenary Errored out. Did not find squad.");
                }

                __instance.equipment?.DestroyAllEquipment();
                __instance.apparel?.DestroyAll();
                //__instance.Destroy();
                return true;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(DeathActionWorker_Simple), "PawnDied")]
    class MercenaryAnimalDied
    {
        static bool Prefix(Corpse corpse)
        {
            if (FindFC.Military?.IsMercenaryPawn(corpse.InnerPawn) == true)
            {
                //corpse.InnerPawn.SetFaction(FactionColonies.getPlayerColonyFaction());
                corpse.Destroy();
                return false;
            }

            return true;
        }
    }

    // [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    class TryGiveJobFleeAnimal
    {
        static bool Prefix(Pawn pawn)
        {
            if (FindFC.Military?.IsMercenaryPawn(pawn) == true)
            {
                return false;
            }

            return true;
        }
    }

    // Strip FC_CombatEfficiency hediffs whenever a pawn leaves the map. Catches every exit path
    // (caravan reformation with captured enemies, fleeing off-map, external defender return,
    // map removal via MapDeiniter.DespawnAll) without enumerating them by hand.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    class StripCombatEfficiencyOnDeSpawn
    {
        static void Prefix(Pawn __instance)
        {
            MilitaryEfficiencyUtil.RemoveCombatEfficiencyHediff(__instance);
        }
    }

}
