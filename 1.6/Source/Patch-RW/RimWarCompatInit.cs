using HarmonyLib;
using RimWar.Planet;
using RimWorld.Planet;
using System;
using System.Reflection;
using Verse;

namespace FactionColonies.RW
{
    /// <summary>
    /// Compatibility patches for RimWar (Torann.RimWar).
    /// This assembly is only loaded when RimWar is active (via LoadFolders.xml).
    ///
    /// Fixes:
    /// 1. Makes Empire settlements visible to RimWar's settlement tracking
    /// 2. Overrides RimWar point values with Empire's actual military/economic strength
    /// 3. No-ops RimWar's broken Empire detection check
    /// 4. Prevents RimWar from capturing/duplicating Empire settlements
    /// 5. Suppresses vassal unit request gizmos on Empire settlements
    /// 6. Scales enemy force in Empire battles based on RimWar settlement strength
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RimWarCompatInit
    {
        static RimWarCompatInit()
        {
            new Harmony("com.Matathias.Empire.RW").PatchAll(Assembly.GetExecutingAssembly());
            BattleModifierRegistry.Register(new RWStrengthBattleModifier());
            LogUtil.MessageForce("RimWar compatibility module loaded.");
        }
    }

    /// <summary>
    /// When Empire sends a military squad to raid an NPC settlement, the defender
    /// force is normally derived from tech level alone. This modifier overrides the
    /// defender's military level using the target's RimWar points, so settlements
    /// that have grown powerful through RimWar's warfare system are harder to raid.
    ///
    /// Uses sqrt(points) / 20 scaling:
    ///   400 pts -> level 1, 3600 -> 3, 10000 -> 5, 19600 -> 7, 32400 -> 9
    /// </summary>
    public class RWStrengthBattleModifier : IBattleModifier
    {
        public void ModifyForce(MilitaryOperation op, MilitaryForce force, bool isAttacker)
        {
            if (isAttacker || op is null) return;

            // The op gives us the target tile directly — no need for stateful tracking.
            PlanetTile targetTile = op.targetTile;
            if (!targetTile.Valid) return;

            Settlement target = Find.WorldObjects.SettlementAt(targetTile);
            if (target is null) return;

            RimWarSettlementComp rwsc = target.GetComponent<RimWarSettlementComp>();
            if (rwsc is null || rwsc.RimWarPoints <= 0) return;

            // Convert RimWar points to Empire military level via sqrt scaling
            double rwLevel = Math.Sqrt(rwsc.RimWarPoints) / 20.0;
            rwLevel = Math.Max(rwLevel, 1.0);

            force.militaryLevel = rwLevel;
            force.forceRemaining = Math.Round(rwLevel * force.militaryEfficiency);

            LogUtil.Message("RW strength " + rwsc.RimWarPoints + " -> Empire defender force " + force.forceRemaining);
        }
    }
}
