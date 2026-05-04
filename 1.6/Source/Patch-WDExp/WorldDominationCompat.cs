using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Reflection;
using TSA_WorldDomination;
using Verse;

namespace FactionColonies.WDExp
{
    /// <summary>
    /// Compatibility patches for "World Domination - Experimental" (TSA.WorldDominationExperimental).
    /// This assembly is only loaded when WD is active (via LoadFolders.xml).
    ///
    /// Fixes:
    /// 1. Excludes PColony from WD's daily action queue (prevents automated raids/growth/expansion)
    /// 2. Intercepts WD raids on Empire settlements (routes through Empire's defense system)
    /// 3. Prevents WD from using Empire settlements as raid actors
    /// 4. Scales enemy force in Empire battles based on WD settlement strength
    /// 5. Syncs PColony diplomacy after WD allegiance changes
    /// 6. Excludes PColony from WD's leader/underdog/balance mechanics
    /// 7. Prevents WD's CheckDefeated intercept from destroying Empire settlements
    /// </summary>
    [StaticConstructorOnStartup]
    public static class WorldDominationCompatInit
    {
        static WorldDominationCompatInit()
        {
            new Harmony("com.Matathias.Empire.WDExp").PatchAll(Assembly.GetExecutingAssembly());
            BattleModifierRegistry.Register(new WDStrengthBattleModifier());
            LogUtil.MessageForce("World Domination (Experimental) compatibility module loaded.");
        }
    }

    // ================================================================
    // Patch 1: Exclude PColony from WD's action queue
    // WD checks IsExcludedFaction to decide which factions get daily
    // actions. PColony is not f.IsPlayer, so it passes by default.
    // ================================================================
    [HarmonyPatch(typeof(WorldActions_Utils), "IsExcludedFaction")]
    public static class Patch_IsExcludedFaction
    {
        private static void Postfix(Faction f, ref bool __result)
        {
            if (__result) return;
            if (FactionCache.IsPlayerColonyFaction(f))
            {
                __result = true;
            }
        }
    }

    // ================================================================
    // Patch 2: Intercept WD raids targeting Empire settlements
    // WD's ExecuteTravelerRaid checks target.Faction.IsPlayer
    // to route player attacks, but PColony is not IsPlayer.
    // Without this patch, WD runs simulated combat and ResolveVictory
    // calls target.Destroy(), permanently deleting Empire settlement data.
    // ================================================================
    [HarmonyPatch(typeof(Raid_Simulated), "ExecuteTravelerRaid")]
    public static class Patch_ExecuteTravelerRaid
    {
        private static bool Prefix(WorldObject_Traveler traveler, WorldComponent_SpreadManager manager)
        {
            WorldSettlementFC empireSettlement = traveler.targetObject as WorldSettlementFC;
            if (empireSettlement == null) return true;

            if (traveler.Faction == null) return true;

            // Convert WD traveler strength to Empire military force
            double techLevel;
            double efficiency;
            MilitaryForce.GetMilitaryLevelAndEfficiencyFromTechLevel(
                traveler.Faction.def.techLevel, out techLevel, out efficiency);

            double wdMilitaryLevel = traveler.travelerStrength / WDStrengthBattleModifier.SCALE_FACTOR;
            MilitaryForce attackingForce = new MilitaryForce(wdMilitaryLevel, efficiency, null, traveler.Faction);

            // Route through Empire's defense system (1-day warning + auto-battle/manual)
            bool queued = MilitaryUtilFC.AttackPlayerSettlement(attackingForce, empireSettlement, traveler.Faction);

            if (queued)
            {
                LogUtil.Message("WD raid on Empire settlement " + empireSettlement.Name +
                    " intercepted (WD strength " + traveler.travelerStrength.ToString("F0") +
                    " -> Empire force " + attackingForce.forceRemaining + ")");
            }
            else
            {
                LogUtil.Message("WD raid on Empire settlement " + empireSettlement.Name +
                    " dropped (settlement already under attack).");
            }

            return false;
        }
    }

    // ================================================================
    // Patch 3: Prevent WD from using Empire settlements as raid actors
    // Belt-and-suspenders with Patch 1. If PColony somehow enters the
    // action queue, this prevents its settlements from being selected.
    // ================================================================
    [HarmonyPatch(typeof(WorldActions_Utils), "IsSettlementProtected")]
    public static class Patch_IsSettlementProtected
    {
        private static void Postfix(Settlement s, ref bool __result)
        {
            if (__result) return;
            if (s is WorldSettlementFC)
            {
                __result = true;
            }
        }
    }

    // ================================================================
    // Patch 4: Scale enemy force based on WD settlement strength
    // Uses IBattleModifier so it integrates with Empire's existing
    // battle modifier pipeline. When Empire attacks a settlement that
    // has CompViralSpread, the defender's force is scaled from WD
    // strength instead of just tech level.
    //
    // BattleModifierRegistry calls modifiers in order:
    //   InvokeModifyForce(MFA, isAttacker=true)  <- attacker first
    //   InvokeModifyForce(MFB, isAttacker=false) <- defender second
    // We capture the attacker on the first call to find the target.
    // ================================================================
    public class WDStrengthBattleModifier : IBattleModifier
    {
        public const double SCALE_FACTOR = 100.0;

        public void ModifyForce(BattleForceContext ctx, MilitaryForce force, bool isAttacker)
        {
            if (isAttacker || ctx is null) return;

            if (!ctx.targetTile.Valid) return;
            Settlement target = Find.WorldObjects.SettlementAt(ctx.targetTile);
            if (target == null) return;

            CompViralSpread comp = target.GetComponent<CompViralSpread>();
            if (comp == null) return;

            float totalDefense = comp.GetTotalLocalDefensePower();
            if (totalDefense <= 0f) return;

            double wdForce = totalDefense / SCALE_FACTOR;
            force.militaryLevel = wdForce;
            force.forceRemaining = Math.Round(wdForce * force.militaryEfficiency);

            LogUtil.Message("WD defense power " + totalDefense.ToString("F0") + " (tier " + comp.tier + ") -> Empire defender force " + force.forceRemaining);
        }
    }

    // ================================================================
    // Patch 5: Sync PColony relations after WD diplomacy changes
    // WD randomly shifts faction allegiances and forms coalitions.
    // PColony must mirror the player faction's relations.
    // ================================================================
    [HarmonyPatch(typeof(WorldActions_DiplomacyBuffsNerfs), "TryChangeAllegiances")]
    public static class Patch_TryChangeAllegiances
    {
        private static void Postfix()
        {
            if (FactionCache.PlayerColonyFaction != null)
            {
                RelationsUtilFC.ResetPlayerColonyRelations();
            }
        }
    }

    [HarmonyPatch(typeof(WorldActions_DiplomacyBuffsNerfs), "FormAntiLeaderCoalition")]
    public static class Patch_FormAntiLeaderCoalition
    {
        private static void Postfix()
        {
            if (FactionCache.PlayerColonyFaction != null)
            {
                RelationsUtilFC.ResetPlayerColonyRelations();
            }
        }
    }

    // ================================================================
    // Patch 6: Exclude PColony from WD's world power statistics
    // GetWorldPowerStats collects all non-player factions. PColony
    // passes this filter (it's not Faction.OfPlayer). If included,
    // PColony could be selected as world leader (triggering handicap)
    // or underdog (triggering buff), both of which are inappropriate
    // for a player-controlled empire.
    // ================================================================
    [HarmonyPatch(typeof(WorldStatsUtils), "GetWorldPowerStats")]
    public static class Patch_GetWorldPowerStats
    {
        private static void Postfix(SpreadLogEntry.GlobalWorldStats __result)
        {
            if (__result == null) return;

            if (FactionCache.PlayerColonyFaction == null) return;

            SpreadLogEntry.FactionStat removed = null;
            for (int i = 0; i < __result.FactionStats.Count; i++)
            {
                if (FactionCache.IsPlayerColonyFaction(__result.FactionStats[i].faction))
                {
                    removed = __result.FactionStats[i];
                    __result.FactionStats.RemoveAt(i);
                    break;
                }
            }

            if (removed != null)
            {
                __result.GlobalTotalStr -= removed.TotalStr;
                for (int t = 1; t <= 4; t++)
                {
                    __result.GlobalTierStr[t] -= removed.strength[t];
                }
            }
        }
    }

    // ================================================================
    // Patch 7: Prevent WD's CheckDefeated intercept from destroying
    // Empire settlements. WD's Patch_InterceptDefeat runs at
    // Priority.High and calls factionBase.Destroy() on defeated
    // non-player settlements. PColony is not IsPlayer, so Empire
    // settlements would be destroyed and replaced with ruins/outpost
    // opportunities. This prefix-on-the-prefix skips WD's logic
    // for WorldSettlementFC, letting Empire's own base patch handle it.
    // ================================================================
    [HarmonyPatch(typeof(Patch_InterceptDefeat), "Prefix")]
    public static class Fix_WD_InterceptDefeat
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Settlement factionBase)
        {
            return !(factionBase is WorldSettlementFC);
        }
    }
}
