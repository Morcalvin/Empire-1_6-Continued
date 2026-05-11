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
                if (__instance.Faction != FactionCache.PlayerColonyFaction) __instance.SetFaction(FactionCache.PlayerColonyFaction);
                var util = FactionCache.FactionComp?.militaryCustomizationUtil;
                if (util is null) return true;
                MercenarySquadFC squad = util.ReturnSquadFromUnit(__instance);
                if (squad != null)
                {
                    Mercenary merc = util.ReturnMercenaryFromUnit(__instance, squad);
                    if (merc != null)
                    {
                        if (squad.settlement != null)
                        {
                            const double basePenalty = 1.0;
                            double offset = FactionCache.FactionComp?
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
                        FactionCache.FactionComp?.militaryCustomizationUtil?.RebuildMercenaryPawnSet();
                    }

                    squad.RemoveDroppedEquipment();
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
            if (FactionCache.FactionComp?.militaryCustomizationUtil?.IsMercenaryPawn(corpse.InnerPawn) == true)
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
            if (FactionCache.FactionComp?.militaryCustomizationUtil?.IsMercenaryPawn(pawn) == true)
            {
                return false;
            }

            return true;
        }
    }

}
