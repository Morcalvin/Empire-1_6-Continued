using RimWorld;
using Verse;

namespace FactionColonies
{
    [DefOf]
    public class FCStatDefOf
    {
        /* Military */
        public static FCStatDef militaryBaseLevel;
        public static FCStatDef militaryCombatEfficiency;
        public static FCStatDef militaryLevelBonusDefending;
        public static FCStatDef militaryLevelBonusAttacking;
        public static FCStatDef militaryEfficiencyBonusAttacking;
        public static FCStatDef militaryEfficiencyBonusDefending;
        public static FCStatDef militaryCooldownOffset;
        public static FCStatDef raidCooldownOffset;
        public static FCStatDef mercHealRateMultiplier;
        public static FCStatDef mercenaryDeathChanceMultiplier;
        public static FCStatDef mercenaryCasualtyRateMultiplier;
        public static FCStatDef mercenaryDeathHappinessPenalty;
        public static FCStatDef squadCapPerSettlement;
        public static FCStatDef maxSquadSize;

        /* Threat Scaling */
        public static FCStatDef threatScalingBase;
        public static FCStatDef threatScalingMultiplier;

        /* Battle Penalties */
        public static FCStatDef battleProsperityLossMultiplier;
        public static FCStatDef battleHappinessLossMultiplier;
        public static FCStatDef battleLoyaltyLossMultiplier;
        public static FCStatDef buildingDestructionChance;

        /* Economy */
        public static FCStatDef taxBasePercentage;
        public static FCStatDef taxBaseRandomModifier;
        public static FCStatDef taxBonusFlat;
        public static FCStatDef titheValueMultiplier;
        public static FCStatDef lootMultiplier;
        public static FCStatDef settlementCostMultiplier;
        public static FCStatDef buildTimeMultiplier;
        public static FCStatDef createSettlementBaseCost;
        public static FCStatDef createSettlementMultiplier;
        public static FCStatDef researchContributionMultiplier;

        /* Workers */
        public static FCStatDef workerBaseCost;
        public static FCStatDef workerBaseMax;
        public static FCStatDef workerBaseOverMax;
        public static FCStatDef extraWorkersSoftcap;
        public static FCStatDef overMaxWorkersAdjustment;
        public static FCStatDef workerProductionBase;
        public static FCStatDef workerProductionMultiplier;

        /* Prosperity */
        public static FCStatDef prosperityGainedBase;
        public static FCStatDef prosperityLostBase;

        /* Happiness (base) */
        public static FCStatDef happinessLostBase;
        public static FCStatDef happinessGainedBase;

        /* Happiness (multipliers) */
        public static FCStatDef happinessLostMultiplier;
        public static FCStatDef happinessGainedMultiplier;

        /* Loyalty (base) */
        public static FCStatDef loyaltyLostBase;
        public static FCStatDef loyaltyGainedBase;

        /* Loyalty (multipliers) */
        public static FCStatDef loyaltyLostMultiplier;
        public static FCStatDef loyaltyGainedMultiplier;

        /* Unrest (base) */
        public static FCStatDef unrestLostBase;
        public static FCStatDef unrestGainedBase;

        /* Unrest (multipliers) */
        public static FCStatDef unrestLostMultiplier;
        public static FCStatDef unrestGainedMultiplier;

        static FCStatDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(FCStatDefOf));
        }
    }
}
