using RimWorld;
using System.Collections.Generic;
using Verse;

namespace FactionColonies
{
    public enum FCStatAggregation : byte
    {
        Additive,
        Multiplicative
    }

    /// <summary>
    /// Defines a named stat that policies, buildings, and events can modify.
    /// Referenced by defName in XML, resolved at load time — typos become XML errors at startup.
    /// </summary>
    public class FCStatDef : Def
    {
        /// <summary>
        /// The identity value for this stat's aggregation: 0 for Additive, 1 for Multiplicative.
        /// </summary>
        public double IdentityValue => aggregation == FCStatAggregation.Multiplicative ? 1.0 : 0.0;

        /// <summary>
        /// How multiple modifiers combine: Additive sums values, Multiplicative multiplies them.
        /// </summary>
        public FCStatAggregation aggregation = FCStatAggregation.Additive;

        /// <summary>
        /// Whether this stat applies at the settlement level (propagated to settlements).
        /// If false, it's faction-level only.
        /// </summary>
        public bool appliesToSettlements = true;

        /// <summary>
        /// Translation key for description display (e.g., "FCTraitDesc_MilitaryLevel").
        /// </summary>
        public string descriptionKey;

        /// <summary>
        /// If true, lower values are "better" for UI coloring purposes (e.g., costs, losses).
        /// </summary>
        public bool invertedForDisplay;

        /// <summary>
        /// If non-zero, the raw stat value is divided by this before display.
        /// Used for tick-based stats (e.g., 2500 to convert ticks to in-game hours).
        /// </summary>
        public double displayDivisor;

        /// <summary>
        /// If non-null, this stat is a resource production stat linked to this ResourceTypeDef.
        /// Used for description formatting (resource name + icon instead of generic descriptionKey).
        /// </summary>
        public ResourceTypeDef linkedResource;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string err in base.ConfigErrors())
                yield return err;

            if (linkedResource != null)
            {
                if (linkedResource.productionAdditiveStat == this && aggregation != FCStatAggregation.Additive)
                    yield return defName + ": linked as productionAdditiveStat on " + linkedResource.defName + " but aggregation is not Additive";
                if (linkedResource.productionMultiplierStat == this && aggregation != FCStatAggregation.Multiplicative)
                    yield return defName + ": linked as productionMultiplierStat on " + linkedResource.defName + " but aggregation is not Multiplicative";
            }
        }
    }

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
        public static FCStatDef deadPawnCooldownOffset;
        public static FCStatDef mercHealRateMultiplier;
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
