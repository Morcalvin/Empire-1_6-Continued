using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace FactionColonies
{
    public class FCPolicyBehavior_Militaristic : FCPolicyBehavior
    {
        private CooldownAbility extraSquadCooldown = new CooldownAbility();

        public override void PostInitialize()
        {
            var ext = Ext<FCPolicyBehaviorExt_Militaristic>();
            if (extraSquadCooldown.cooldownTicks == 0)
                extraSquadCooldown.cooldownTicks = GenDate.TicksPerDay * ext.extraSquadCooldownDays;
        }

        public override void OnEnacted(FactionFC faction)
        {
            foreach (WorldSettlementFC settlement in faction.settlements)
            {
                TryPlaceBuilding(settlement);
            }
        }

        public override void OnSettlementCreated(FactionFC faction, WorldSettlementFC settlement)
        {
            TryPlaceBuilding(settlement);
        }

        private void TryPlaceBuilding(WorldSettlementFC settlement)
        {
            WorldObjectComp_SettlementBuildings buildingsComp = settlement.BuildingsComp;
            if (buildingsComp == null) return;

            string defName = Ext<FCPolicyBehaviorExt_Militaristic>().autoPlaceBuildingDefName;
            BuildingFCDef building = DefDatabase<BuildingFCDef>.GetNamed(defName);
            if (!building.CanBeBuiltForSettlementType(settlement.settlementDef)) return;
            if (buildingsComp.HasBuilding(building)) return;

            int slots = buildingsComp.NumBuildingSlots;
            for (int i = 0; i < slots; i++)
            {
                if (buildingsComp.BuildingSlotIsEmpty(i))
                {
                    settlement.ConstructBuilding(building, i);
                    return;
                }
            }
        }

        public override double ModifyBuildingUpkeep(BuildingFCDef building, double currentUpkeep, WorldSettlementFC settlement)
        {
            if (building.statModifiers.Any(m => m.stat == FCStatDefOf.militaryBaseLevel
                                             || m.stat == FCStatDefOf.militaryCombatEfficiency))
                return Math.Max(currentUpkeep - Ext<FCPolicyBehaviorExt_Militaristic>().militaryBuildingUpkeepDiscount, 0);
            return currentUpkeep;
        }

        public override void OnSquadDeployed(FactionFC faction, MilitaryOperation op, WorldSettlementFC settlement, bool isExtraSquad)
        {
            if (isExtraSquad)
                extraSquadCooldown.Use();
        }

        public override IEnumerable<FloatMenuOption> GetExtraDeploymentOptions(
            FactionFC faction, WorldSettlementFC settlement, WorldObjectComp_SettlementMilitary milComp)
        {
            if (!extraSquadCooldown.IsReady)
            {
                Messages.Message("FCXDaysToRedeploy".Translate(
                    Math.Round(extraSquadCooldown.DaysRemaining, 1)), MessageTypeDefOf.RejectInput);
                yield break;
            }

            if (milComp.militarySquad?.outfit == null)
                yield break;

            var ext = Ext<FCPolicyBehaviorExt_Militaristic>();
            int cost = (int)Math.Round(milComp.militarySquad.outfit.UpdateEquipmentTotalCost() * ext.extraSquadCostFraction);
            yield return new FloatMenuOption("FCDeploySecondarySquad".Translate(cost), delegate
            {
                if (PaymentUtil.GetSilver() >= cost)
                {
                    List<FloatMenuOption> deploymentOptions = new List<FloatMenuOption>
                    {
                        new FloatMenuOption("FCWalkIntoMapDeploymentOption".Translate(), delegate
                        {
                            MilitaryUtil.CallinExtraForces(settlement, false);
                            Find.WindowStack.currentlyDrawnWindow.Close();
                        })
                    };

                    if (!FCSettings.medievalTechOnly &&
                        (FactionCache.TechTransportPods?.IsFinished ?? false))
                    {
                        deploymentOptions.Add(new FloatMenuOption("FCDropPodDeploymentOption".Translate(), delegate
                        {
                            MilitaryUtil.CallinExtraForces(settlement, true);
                            Find.WindowStack.currentlyDrawnWindow.Close();
                        }));
                    }

                    Find.WindowStack.Add(new FloatMenu(deploymentOptions));
                }
                else
                {
                    Messages.Message("FCNotEnoughSilverToDeploySquad".Translate(), MessageTypeDefOf.RejectInput);
                }
            });
        }

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref extraSquadCooldown, "extraSquadCooldown");
            extraSquadCooldown = extraSquadCooldown ?? new CooldownAbility();
        }

        // Debug accessors
        public bool DebugCooldownReady() => extraSquadCooldown.IsReady;
        public float DebugCooldownDays() => extraSquadCooldown.DaysRemaining;
        public void DebugResetCooldown() => extraSquadCooldown.tickLastUsed = -1;
    }
}
