using System.Collections.Generic;
using Verse;

namespace FactionColonies
{
    public enum FCProductionCostScope : byte
    {
        AffectedSettlements = 0,
        FactionWide = 1
    }

    public enum FCIncomeCostScope : byte
    {
        FactionWide = 0,
        AffectedSettlements = 1
    }

    /* Pair of {resource, coefficient} used by FCDynamicCostExtension.costPerResourceProduction.
     * Plain POCO so RimWorld's XML loader populates fields directly. */
    public class ResourceCostCoefficient
    {
        public ResourceTypeDef resource;
        public float coefficient;
    }

    /// <summary>
    /// Opt-in DefModExtension on <see cref="FCOptionDef"/> that adds income- and/or
    /// production-based silver-cost scaling on top of the automatic per-settlement multiplier.
    /// Contributions are additive — the extension can only raise the cost, never lower it.
    /// </summary>
    public class FCDynamicCostExtension : DefModExtension
    {
        /// <summary>Coefficient applied to current income (silver/cycle). 0.05 = +5% of income.</summary>
        public float costPerEmpireIncomeUnit = 0f;

        /// <summary>Coefficient x current InstantaneousProduction of each resource over the scope.</summary>
        public List<ResourceCostCoefficient> costPerResourceProduction = new List<ResourceCostCoefficient>();

        /// <summary>Which settlements contribute to the resource-production sum.</summary>
        public FCProductionCostScope productionScope = FCProductionCostScope.AffectedSettlements;

        /// <summary>Which settlements contribute to the income sum.</summary>
        public FCIncomeCostScope incomeScope = FCIncomeCostScope.FactionWide;
    }
}
