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
                            if (FCSettings.deadPawnsIncreaseMilitaryCooldown)
                            {
                                squad.dead += 1;
                            }

                            squad.settlement.GainHappiness(-1d);
                        }

                        // Fire death event before replacement — listeners can cancel auto-replacement
                        MercenaryDeathEvent deathEvt = new MercenaryDeathEvent(merc, squad, squad.settlement);
                        LifecycleRegistry.InvokeOnMercenaryDeath(deathEvt);

                        if (!deathEvt.CancelReplacement)
                        {
                            squad.PassPawnToDeadMercenaries(merc);
                        }
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
