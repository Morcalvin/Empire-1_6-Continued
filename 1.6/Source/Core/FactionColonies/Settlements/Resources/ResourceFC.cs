using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public class ResourceFC : IExposable
    {
        public ResourceTypeDef def;
        public string label => def?.LabelCap;
        public WorldSettlementFC settlement;
        private int savedAssignedWorkers;
        public int assignedWorkers
        {
            get
            {
                return savedAssignedWorkers;
            }
            set
            {
                savedAssignedWorkers = value;
                FindFC.FactionComp?.SetDirtyResourceDisplayCache(def);
            }
        }

        /* All bonuses and maluses, even from biome or hilliness, should be applied through productionAdditives and productionMultipliers */
        private Dictionary<string, ProductionBonus> productionAdditives = new Dictionary<string, ProductionBonus>();
        private Dictionary<string, ProductionBonus> productionMultipliers = new Dictionary<string, ProductionBonus>();

        private bool dirtyProductionBaseCache = true;
        private bool dirtyProductionMultCache = true;
        private bool dirtyProductionBaseDescCache = true;
        private bool dirtyProductionMultDescCache = true;
        private double cachedProductionBase = 1;
        private double cachedProductionMult = 1;
        private TaggedString cachedProdBaseDesc = "";
        private TaggedString cachedProdMultDesc = "";
        private Texture2D iconLoaded;

        private double accumulatedProduction = 0;
        private int accumulationDays = 0;

        /* Don't expost tithes publicly. We want values to be added or removed *only* through our special functions, so that we can dirty or set
         * cached values appropriately. */
        private Dictionary<ThingQualityTuple, int> tithes = new Dictionary<ThingQualityTuple, int>();
        private bool dirtyTitheCache = true;
        private double cachedTitheTotalValue = 0;
        /// <summary>
        /// The amount of budget available to this resource for random tithing. If the lowest-value random tithing thing is still higher in value than the available titheStock,
        /// then production is rolled over to the next tax period, until enough has accrued to actually produce the tithe.
        /// </summary>
        // TODO: alert the player when tithing has rolled over?
        public double randomTitheStock = 0;
        public bool disburseTitheStock = false;

        public bool tithesPaused = false;
        public bool hasRandomTithe = false;
        public bool autoMaxRandomTithe = false;
        public ThingFilter randomTitheFilter = new ThingFilter();
        private bool dirtyRandomTitheCache = true;
        private List<ThingDef> thingsForRandomTithes = new List<ThingDef>();
        private bool dirtyFilteredRandomTitheCache = true;
        private List<ThingDef> filteredThingsForRandomTithes = new List<ThingDef>();
        public string storedRandomTitheBudgetBuffer = "";
        public int storedRandomTitheBudget = 0;
        private int oldStoredRandomTitheBudget = 0;

        // Submods can register named allocations to siphon production away from taxes and tithes.
        // Each mod registers under its own key so multiple submods compose correctly.
        // Not persisted — submods are expected to re-register their allocations on load.
        private struct StockpileEntry
        {
            public double amount;
            public Action onEvicted; // invoked if the entry is evicted at tax time; null is allowed
        }
        private Dictionary<string, StockpileEntry> stockpileAllocations = new Dictionary<string, StockpileEntry>();
        public double totalStockpileAllocation => stockpileAllocations.Values.Sum(e => e.amount);

        /// <summary>
        /// Attempts to register a named production diversion for a stockpile.
        /// Returns false without registering if the amount would push total diversions above <see cref="rawTotalProduction"/>.
        /// If the key already exists, the old entry is replaced (using the new amount in the capacity check).
        /// </summary>
        /// <param name="key">Unique identifier for the calling mod (e.g. "MyMod.MyFeature").</param>
        /// <param name="onEvicted">Optional callback invoked if this entry is later evicted at tax time due to insufficient production.</param>
        public bool SetStockpileAllocation(string key, double amount, Action onEvicted = null)
        {
            double currentForKey = stockpileAllocations.TryGetValue(key, out var existing) ? existing.amount : 0;
            if (totalStockpileAllocation - currentForKey + amount > rawTotalProduction)
                return false;
            stockpileAllocations[key] = new StockpileEntry { amount = amount, onEvicted = onEvicted };
            settlement?.DirtyProfitCache();
            return true;
        }

        /// <summary>Removes a previously registered stockpile allocation. The eviction callback is NOT invoked.</summary>
        public void ClearStockpileAllocation(string key)
        {
            stockpileAllocations.Remove(key);
            settlement?.DirtyProfitCache();
        }

        /// <summary>
        /// Evicts stockpile entries (largest first) until the total allocation fits within <see cref="rawTotalProduction"/>.
        /// Called at tax time after resource caches are refreshed. Invokes each evicted entry's callback.
        /// </summary>
        public void PruneStockpileAllocations()
        {
            if (totalStockpileAllocation <= rawTotalProduction + 0.01)
                return;
            foreach (var key in stockpileAllocations
                         .OrderByDescending(kv => kv.Value.amount)
                         .Select(kv => kv.Key)
                         .ToList())
            {
                if (totalStockpileAllocation <= rawTotalProduction + 0.01) break;
                var entry = stockpileAllocations[key];
                stockpileAllocations.Remove(key);
                entry.onEvicted?.Invoke();
            }
        }
        public int randomTitheBudget
        {
            get
            {
                if (!hasRandomTithe) return 0;
                if (autoMaxRandomTithe)
                {
                    RefreshTitheCacheIfDirty();
                    return Math.Max(0, (int)(GetTitheIncome() - cachedTitheTotalValue));
                }
                return storedRandomTitheBudget;
            }
            set
            {
                storedRandomTitheBudget = value;
                settlement.DirtyProfitCache();
            }
        }

        public double production => Math.Max(0, productionBase * productionMult);
        public double productionBase
        {
            get
            {
                if (dirtyProductionBaseCache)
                {
                    cachedProductionBase = CalculateProductionBase();
                    dirtyProductionBaseCache = false;
                }
                return cachedProductionBase;
            }
        }
        public double productionMult
        {
            get
            {
                if (dirtyProductionMultCache)
                {
                    cachedProductionMult = CalculateProductionMult();
                    dirtyProductionMultCache = false;
                }
                return cachedProductionMult;
            }
        }
        private void RefreshTitheCacheIfDirty()
        {
            if (dirtyTitheCache)
            {
                PruneTitheList();
                cachedTitheTotalValue = CalcTotalTitheValue();
                dirtyTitheCache = false;
            }
        }
        public double titheTotalValue
        {
            get
            {
                // Pool resources are always counted as though they are tithing, since you can't actually get any silver from them.
                // TODO: change this? Make it possible to control how much of a pool resources's pool goes into the actual pool, and how much gets shipped as silver?
                if (def.isPoolResource)
                {
                    return taxableProductionMarketValue;
                }
                if (tithesPaused)
                {
                    return 0;
                }
                RefreshTitheCacheIfDirty();
                return cachedTitheTotalValue + randomTitheBudget;
            }
        }
        public double titheTotalValueNoRandom => titheTotalValue - randomTitheBudget;
        /* NOTE: the production property chain flows as follows:
         *  rawTotalProduction          — gross output (units), before any splits. Display this as "Total Production".
         *  effectiveRawTotalProduction — post-stockpile output (units); what remains after submod allocations are diverted.
         *  grossMarketValue            — silver value of rawTotalProduction; displayed as "Raw Income" (before any deductions).
         *  stockpileMarketValue        — silver value of totalStockpileAllocation; used in Net Income tooltip breakdown.
         *  taxableProductionMarketValue — silver value of effectiveRawTotalProduction; the budget available to taxes and tithes.
         *  actualIncome                — taxableProductionMarketValue minus tithe costs; displayed as "Net Income".
         *                                Can be negative if tithe modifiers push the tithe value above taxable production.
         */
        /// <summary>Current snapshot: production * workers, ignoring accumulation.</summary>
        public double InstantaneousProduction => production * assignedWorkers;
        /// <summary>Period average if available, otherwise falls back to instantaneous.</summary>
        public double AccumulatedAverageProduction => accumulationDays > 0
            ? accumulatedProduction / accumulationDays
            : InstantaneousProduction;
        public int AccumulationDays => accumulationDays;

        public double rawTotalProduction =>
            (settlement != null && settlement.IsCalculatingTax && accumulationDays > 0)
                ? AccumulatedAverageProduction
                : InstantaneousProduction;
        public double effectiveRawTotalProduction => rawTotalProduction - totalStockpileAllocation;
        public double grossMarketValue => rawTotalProduction * FCSettings.silverPerResource;
        public double stockpileMarketValue => totalStockpileAllocation * FCSettings.silverPerResource;
        public double taxableProductionMarketValue => effectiveRawTotalProduction * FCSettings.silverPerResource;
        /// <summary>
        /// Aggregates additional tithe budget (in silver) from all <see cref="ITitheBudgetModifier"/> comps
        /// on this settlement. Added to the tithe income cap; in actualIncome, only the portion of tithe
        /// covered by this budget is offset (capped to titheTotalValue).
        /// </summary>
        public double externalTitheBudget
        {
            get
            {
                double total = 0;
                if (settlement != null)
                {
                    foreach (WorldObjectComp comp in settlement.AllComps)
                    {
                        if (comp is ITitheBudgetModifier provider)
                            total += provider.GetExternalTitheBudget(this);
                    }
                }
                return total;
            }
        }
        public double actualIncome => taxableProductionMarketValue - titheTotalValue + Math.Min(titheTotalValue, externalTitheBudget);
        /// <summary>What actualIncome would be at tax time, using the period average instead of instantaneous production.</summary>
        public double averageActualIncome => accumulationDays > 0
            ? (AccumulatedAverageProduction - totalStockpileAllocation) * FCSettings.silverPerResource - titheTotalValue + Math.Min(titheTotalValue, externalTitheBudget)
            : actualIncome;

        public bool canTithe => !def.isPoolResource && def.canTithe;

        public Texture2D getIcon
        {
            get
            {
                if (iconLoaded != null) return iconLoaded;

                if (def != null)
                {
                    iconLoaded = def.Icon;
                }
                else
                {
                    LogUtil.Error("Failed to load icon for ResourceFC: no associated resourceDef!");
                    iconLoaded = TexLoad.questionmark;
                }
                return iconLoaded;
            }
        }

        public ResourceFC()
        {
        }

        public ResourceFC(ResourceTypeDef resourceDef, WorldSettlementFC settlement = null)
        {
            this.settlement = settlement;
            def = resourceDef;
            if (resourceDef == null)
            {
                /* This is a super bad case that should never happen. Find a way to make this a bigger error? */
                LogUtil.Error($"Created ResourceFC with NULL resourceDef!");
            }
            randomTitheFilter = new ThingFilter();
            randomTitheBudget = 0;
            productionAdditives.Clear();
            productionMultipliers.Clear();
            if (settlement != null)
            {
                SetBaseResourceBonuses();
            }
            ResetThingFilter();
        }


        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Collections.Look(ref productionAdditives, "productionAdditives", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref productionMultipliers, "productionMultiplers", LookMode.Value, LookMode.Deep);

            //tithe and income data
            Scribe_Values.Look(ref savedAssignedWorkers, "assignedWorkers");
            Scribe_Collections.Look(ref tithes, "tithes", LookMode.Deep, LookMode.Value);
            Scribe_Deep.Look(ref randomTitheFilter, "filter");
            Scribe_Values.Look(ref storedRandomTitheBudget, "randomTitheBudget");
            Scribe_Values.Look(ref hasRandomTithe, "hasRandomTithe");
            Scribe_Values.Look(ref autoMaxRandomTithe, "autoMaxRandomTithe");
            Scribe_Values.Look(ref tithesPaused, "tithesPaused", defaultValue: false);

            //Tax Stock
            Scribe_Values.Look(ref randomTitheStock, "taxStock");
            Scribe_Values.Look(ref disburseTitheStock, "disburseTaxStock");

            Scribe_References.Look(ref settlement, "settlement");

            Scribe_Values.Look(ref accumulatedProduction, "accumulatedProduction", 0);
            Scribe_Values.Look(ref accumulationDays, "accumulationDays", 0);
        }

        /// <summary>
        /// Calculates the total production base from three sources:
        /// 1. ProductionBonus dict: static environmental bonuses (biome, hilliness, settlement type)
        /// 2. FCStatDef system: dynamic bonuses from buildings, policies, events
        /// 3. IResourceProductionModifier comps: dynamic per-resource bonuses from WorldObjectComps
        /// </summary>
        private double CalculateProductionBase()
        {
            double dictBase = ResourceFormulas.CalculateProductionBase(productionAdditives.Values.Select(p => p.value));
            double statBase = (settlement != null && def.productionAdditiveStat != null)
                ? settlement.GetStatValue(def.productionAdditiveStat) : 0;
            double compBase = 0;
            if (settlement != null)
            {
                foreach (WorldObjectComp comp in settlement.AllComps)
                {
                    if (comp is IResourceProductionModifier provider)
                        compBase += provider.GetResourceAdditiveModifier(this);
                }
            }
            double workerBase = (settlement != null) ? settlement.GetStatValue(FCStatDefOf.workerProductionBase) : 0;
            return dictBase + statBase + compBase + workerBase;
        }
        /// <summary>
        /// Calculates the total production multiplier from three sources:
        /// 1. ProductionBonus dict: static environmental multipliers (biome, hilliness, settlement type)
        /// 2. FCStatDef system: dynamic multipliers from buildings, policies, events
        /// 3. IResourceProductionModifier comps: dynamic per-resource multipliers from WorldObjectComps
        /// Also includes the settlement tax bonus.
        /// </summary>
        private double CalculateProductionMult()
        {
            double taxBonus = settlement?.GetSettlementTaxBonus() ?? 1;
            double dictMult = ResourceFormulas.CalculateProductionMult(productionMultipliers.Values.Select(p => p.value), 1.0);
            double statMult = (settlement != null && def.productionMultiplierStat != null)
                ? settlement.GetStatValue(def.productionMultiplierStat) : 1;
            double compMult = 1;
            if (settlement != null)
            {
                foreach (WorldObjectComp comp in settlement.AllComps)
                {
                    if (comp is IResourceProductionModifier provider)
                        compMult *= provider.GetResourceMultiplierModifier(this);
                }
            }
            double prosperityMult = (settlement != null) ? (settlement.prosperity / 100.0) : 1.0;
            double workerMult = (settlement != null) ? settlement.GetStatValue(FCStatDefOf.workerProductionMultiplier) : 1;
            return dictMult * statMult * compMult * taxBonus * prosperityMult * workerMult;
        }
        public double GetTitheModifierPerWorker()
        {
            return settlement.GetStatValue(FCStatDefOf.taxBaseRandomModifier) + FCSettings.productionTitheMod;
        }
        public double GetTotalTitheModifierForWorkers()
        {
            return GetTitheModifierPerWorker() * assignedWorkers;
        }
        public double GetTitheIncome()
        {
            double multForTotal = FindFC.FactionComp.GetStatValue(FCStatDefOf.titheValueMultiplier, settlement);
            return ((taxableProductionMarketValue + GetTotalTitheModifierForWorkers()) * multForTotal) + externalTitheBudget;
        }
        public void RefreshOnRandomTitheBudgetChange()
        {
            if (storedRandomTitheBudget != oldStoredRandomTitheBudget)
            {
                oldStoredRandomTitheBudget = storedRandomTitheBudget;
                settlement.DirtyProfitCache();
            }
        }
        public void SetDirtyCache()
        {
            SetDirtyCacheProdBase();
            SetDirtyCacheProdMult();
            dirtyTitheCache = true;
            SetDirtyRandomTitheCache();
            dirtyFilteredRandomTitheCache = true;
            FindFC.FactionComp?.SetDirtyResourceDisplayCache(def);
        }
        public void SetDirtyRandomTitheCache()
        {
            dirtyRandomTitheCache = true;
            settlement.DirtyGrandThingList();
        }
        public void SetDirtyCacheProdBase()
        {
            dirtyProductionBaseCache = true;
            dirtyProductionBaseDescCache = true;
            dirtyTitheCache = true;
        }
        public void SetDirtyCacheProdMult()
        {
            dirtyProductionMultCache = true;
            dirtyProductionMultDescCache = true;
            dirtyTitheCache = true;
        }

        public void AccumulateDailyProduction()
        {
            accumulatedProduction += InstantaneousProduction;
            accumulationDays++;
        }

        public void ResetAccumulator()
        {
            accumulatedProduction = 0;
            accumulationDays = 0;
        }

        public ResourcePool CreatePool()
        {
            ResourcePool pool = new ResourcePool
            {
                resource = def,
                pool = 0
            };
            if (def.isPoolResource)
            {
                pool.pool += def.GetModExtension<ResourcePoolExtension>().CreatePool(effectiveRawTotalProduction, settlement);
            }
            return pool;
        }

        /*
         * Production Bonus Initialization
         */

        /// <summary>
        /// Populates both <see cref="productionAdditives"/> and <see cref="productionMultipliers"/> from
        /// the biome, settlement type, and any <see cref="ResourceProductionExtension"/>s on the resource def.
        /// Called once at construction.
        /// </summary>
        private void SetBaseResourceBonuses()
        {
            if (settlement == null) return;

            string settlementId = settlement.Name ?? "nullsettlement";

            // --- Biome bonuses ---
            ResourceAvailability biomeRes = settlement.biomeDef.GetBiomeResource(def);
            if (biomeRes == null)
            {
                LogUtil.Error($"Found NULL biomeBonus for resource {def} in settlement {settlement.Name}, despite the ResourceFC already existing");
            }
            else
            {
                string biomeId = $"{def.defName}_biome_{settlement.biomeDef.defName}_{settlementId}";
                if (biomeRes.additive != 0)
                    AddProductionAdditive(biomeId, biomeRes.additive, settlement.biomeDef.LabelCap);
                if (biomeRes.multiplier != 1)
                    AddProductionMultiplier(biomeId, biomeRes.multiplier, settlement.biomeDef.LabelCap);
            }

            // --- Settlement type bonuses ---
            ResourceAvailability settleRes = settlement.settlementDef.GetSettlementResource(def);
            if (settleRes != null)
            {
                string settleId = $"{def.defName}_settle_{settlement.settlementDef.defName}_{settlementId}";
                if (settleRes.additive != 0)
                    AddProductionAdditive(settleId, settleRes.additive, settlement.settlementDef.LabelCap);
                if (settleRes.multiplier != 1)
                    AddProductionMultiplier(settleId, settleRes.multiplier, settlement.settlementDef.LabelCap);
            }

            // --- ResourceProductionExtension bonuses ---
            if (def?.modExtensions != null)
            {
                foreach (ResourceProductionExtension ext in def.modExtensions.OfType<ResourceProductionExtension>())
                {
                    ext.ContributeToBreakdown(
                        settlement.Tile,
                        settlement,
                        (suffix, v, label) => AddProductionAdditive($"{def.defName}_ext_{suffix}_{settlementId}", v, label),
                        (suffix, v, label) => AddProductionMultiplier($"{def.defName}_ext_{suffix}_{settlementId}", v, label));
                }
            }

            // --- Tile landmark bonuses ---
            Landmark landmark = settlement.Tile.Tile?.Landmark;
            TileLandmarkResourceExtension lmExt = landmark?.def?.GetModExtension<TileLandmarkResourceExtension>();
            if (lmExt?.bonuses != null)
            {
                foreach (TileResourceBonus entry in lmExt.bonuses)
                {
                    if (entry.resource != def) continue;
                    string lmId = $"{def.defName}_landmark_{landmark.def.defName}_{settlementId}";
                    string lmLabel = entry.label.NullOrEmpty() ? landmark.def.LabelCap.ToString() : entry.label;
                    if (entry.additive != 0)
                        AddProductionAdditive(lmId, entry.additive, lmLabel);
                    if (entry.multiplier != 1)
                        AddProductionMultiplier(lmId, entry.multiplier, lmLabel);
                }
            }

            // --- Tile mutator bonuses ---
            IList<TileMutatorDef> tileMutators = settlement.Tile.Tile?.Mutators;
            if (tileMutators != null && tileMutators.Count > 0)
            {
                foreach (TileMutatorDef mut in tileMutators)
                {
                    TileMutatorResourceExtension mutExt = mut?.GetModExtension<TileMutatorResourceExtension>();
                    if (mutExt?.bonuses is null) continue;
                    foreach (TileResourceBonus entry in mutExt.bonuses)
                    {
                        if (entry.resource != def) continue;
                        string mutId = $"{def.defName}_mutator_{mut.defName}_{settlementId}";
                        string mutLabel = entry.label.NullOrEmpty() ? mut.LabelCap.ToString() : entry.label;
                        if (entry.additive != 0)
                            AddProductionAdditive(mutId, entry.additive, mutLabel);
                        if (entry.multiplier != 1)
                            AddProductionMultiplier(mutId, entry.multiplier, mutLabel);
                    }
                }
            }
        }
        public void AddProductionAdditive(string id, double value, string desc)
        {
            ProductionBonus additive = new ProductionBonus(value, desc);

            if (desc.NullOrEmpty())
            {
                LogUtil.Warning($"Created a production additive for resource {label} in settlement {settlement.Name} with an empty description! (id: {id})");
            }
            AddProductionAdditive(id, additive);
        }

        private void AddProductionAdditive(string id, ProductionBonus additive)
        {
            try
            {
                productionAdditives.Add(id, additive);
            }
            catch (Exception e)
            {
                LogUtil.Error($"Failed when adding ProductionBonus additive with id {id}: {e.Message}");
            }
            SetDirtyCacheProdBase();
        }
        public void RemoveProductionAdditiveById(string id)
        {
            productionAdditives.Remove(id);
            SetDirtyCacheProdBase();
        }
        public TaggedString GetProductionAdditivesDesc()
        {
            if (dirtyProductionBaseDescCache)
            {
                TaggedString desc = "";
                foreach (ProductionBonus additive in productionAdditives.Values)
                {
                    desc += TextUtil.ColorizeAdditiveBonus(additive.value) + " - " + additive.desc + "\n";
                }
                if (def.productionAdditiveStat != null && settlement != null)
                {
                    desc += settlement.GetStatDesc(def.productionAdditiveStat);
                }
                if (settlement != null)
                {
                    foreach (WorldObjectComp comp in settlement.AllComps)
                    {
                        if (comp is IResourceProductionModifier provider)
                        {
                            string compDesc = provider.GetResourceAdditiveDesc(this);
                            if (!compDesc.NullOrEmpty())
                                desc += compDesc + "\n";
                        }
                    }
                }
                if (settlement != null)
                {
                    desc += settlement.GetStatDesc(FCStatDefOf.workerProductionBase);
                }
                cachedProdBaseDesc = desc.Trim();
                dirtyProductionBaseDescCache = false;
            }
            return cachedProdBaseDesc;
        }

        public void AddProductionMultiplier(string id, double value, string desc)
        {
            ProductionBonus multiplier = new ProductionBonus(value, desc);

            if (desc.NullOrEmpty())
            {
                LogUtil.Warning($"Created a production multiplier for resource {label} in settlement {settlement.Name} with an empty description! (id: {id})");
            }
            AddProductionMultiplier(id, multiplier);
        }

        private void AddProductionMultiplier(string id, ProductionBonus multiplier)
        {
            try
            {
                productionMultipliers.Add(id, multiplier);
            }
            catch (Exception e)
            {
                LogUtil.Error($"Failed when adding ProductionBonus multiplier with id {id}: {e.Message}");
            }
            SetDirtyCacheProdMult();
        }
        public void RemoveProductionMultiplierById(string id)
        {
            productionMultipliers.Remove(id);
            SetDirtyCacheProdMult();
        }
        public TaggedString GetProductionMultipliersDesc()
        {
            if (dirtyProductionMultDescCache)
            {
                TaggedString desc = "";
                foreach (ProductionBonus multiplier in productionMultipliers.Values)
                {
                    desc += TextUtil.ColorizeMultiplierBonus(multiplier.value) + " - " + multiplier.desc + "\n";
                }
                if (def.productionMultiplierStat != null && settlement != null)
                {
                    desc += settlement.GetStatDesc(def.productionMultiplierStat);
                }
                if (settlement != null)
                {
                    foreach (WorldObjectComp comp in settlement.AllComps)
                    {
                        if (comp is IResourceProductionModifier provider)
                        {
                            string compDesc = provider.GetResourceMultiplierDesc(this);
                            if (!compDesc.NullOrEmpty())
                                desc += compDesc + "\n";
                        }
                    }
                }
                if (settlement != null)
                {
                    desc += settlement.GetStatDesc(FCStatDefOf.workerProductionMultiplier);
                }
                desc += TextUtil.ColorizeMultiplierBonus(settlement?.GetSettlementTaxBonus() ?? 1) + " - " + "FCTaxBase".Translate();

                if (settlement != null)
                {
                    double prosperityMult = settlement.prosperity / 100.0;
                    desc += "\n" + TextUtil.ColorizeMultiplierBonus(prosperityMult) + " - " + "FCProsperity".Translate().CapitalizeFirst()
                        + " (" + (int)settlement.prosperity + "%)";
                }

                cachedProdMultDesc = desc.Trim();
                dirtyProductionMultDescCache = false;
            }
            return cachedProdMultDesc;
        }

        /*
         * Filter functions
         */
        public void ResetThingFilter()
        {
            FactionFC faction = FindFC.FactionComp;

            if (def == null)
                return;

            def.FilterResource(randomTitheFilter, faction.techLevel, this);
        }
        /// <summary>
        /// Generates a list of ThingDefs that can be generated as tithes for this resource.
        /// </summary>
        /// <returns></returns>
        public List<ThingDef> GenerateThingDefList()
        {
            if (def.isPoolResource)
            {
                LogUtil.Error($"Attempted to generate thing list for pool resource {def.defName} in settlement {settlement.Name}");
                return null;
            }
            if (dirtyRandomTitheCache)
            {
                if (randomTitheFilter == null)
                {
                    randomTitheFilter = new ThingFilter();
                    ResetThingFilter();
                }

                FactionFC faction = FindFC.FactionComp;
                ThingSetMaker thingSetMaker = new ThingSetMaker_Count();
                ThingSetMakerParams param = new ThingSetMakerParams();
                param.filter = new ThingFilter();
                param.techLevel = FindFC.EmpireFaction.def.techLevel;
                param.countRange = new IntRange(1, 1);

                TechLevel tmplevel = TechLevel.Undefined;
                ThingSetMaker tmp = def.GetModExtension<ResourceFilterExtension>()?.GetThingSetMaker(out tmplevel, this);
                if (tmp != null)
                {
                    thingSetMaker = tmp;
                    param.techLevel = tmplevel;
                }

                def.FilterResource(param.filter, faction.techLevel, this);

                /* AllGenerateableThingsDebug(param).ToList() was taken from PaymentUtil.debugGenerateTithe(), which was used to generate the selection float menu
                 * in the settlement screen. Is this really the right function to use? TODO: look into this. */
                thingsForRandomTithes = thingSetMaker.AllGeneratableThingsDebug(param).ToList();
                dirtyRandomTitheCache = false;
            }
            return thingsForRandomTithes;
        }
        public List<ThingDef> GetRandomTitheFilterThings()
        {
            if (dirtyFilteredRandomTitheCache)
            {
                if (randomTitheFilter == null)
                {
                    randomTitheFilter = new ThingFilter();
                    ResetThingFilter();
                }

                filteredThingsForRandomTithes = new List<ThingDef>();
                List<ThingDef> possibleThings = GenerateThingDefList();

                foreach (ThingDef thingDef in possibleThings)
                {
                    if (randomTitheFilter.Allows(thingDef))
                    {
                        filteredThingsForRandomTithes.Add(thingDef);
                    }
                }

                dirtyFilteredRandomTitheCache = false;
            }
            return filteredThingsForRandomTithes;
        }
        public void ClearRandomTitheFilter()
        {
            if (randomTitheFilter == null)
            {
                randomTitheFilter = new ThingFilter();
            }
            randomTitheFilter.SetDisallowAll();
            SetDirtyRandomTitheCache();
            dirtyFilteredRandomTitheCache = true;
        }
        public void SetAllRandomTitheFilter()
        {
            if (randomTitheFilter == null)
            {
                randomTitheFilter = new ThingFilter();
            }
            ResetThingFilter();
            SetDirtyRandomTitheCache();
            dirtyFilteredRandomTitheCache = true;
        }
        public void SetRandomTitheFilterAllow(ThingDef thing, bool allow)
        {
            if (randomTitheFilter == null)
            {
                randomTitheFilter = new ThingFilter();
                ResetThingFilter();
                SetDirtyRandomTitheCache();
            }

            randomTitheFilter.SetAllow(thing, allow);
            dirtyFilteredRandomTitheCache = true;
        }
        // could cache this with a dictionary, but probably best to see if there's an actual performance problem first
        public bool GetRandomTitheFilterAllow(ThingDef thing)
        {
            return randomTitheFilter.Allows(thing);
        }
        /* - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - *
         *   Tithe functions                                                                                                                                             *
         * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - */
        /// <summary>
        /// Adds a given quantity of thing to the tithes list.
        /// <para>This function does not check if the given <paramref name="quantity"/> of <paramref name="thing"/> can actually be afforded.</para>
        /// <para>This function dirties the tithe cache, forcing a recalculation of the total tithe value.</para>
        /// </summary>
        /// <param name="thing">A ThingQualityTuple specifying the ThingDef, QualityCategory, and StuffDef of the thing to add.</param>
        /// <param name="quantity">The quantity to add to the tithes list. Should always be a non-zero positive value.</param>
        /// <returns>TRUE if the thing was successfully added to the tithes dictionary, FALSE otherwise.</returns>
        public bool AddToTitheList(ThingQualityTuple thing, int quantity, bool forceToQuantity = false)
        {
            if (quantity < 0)
            {
                LogUtil.Error($"Tried to add a negative quantity of objects to the tithes list for resource {def.LabelCap}. You should use DecrementInTitheList() instead.");
                return false;
            }
            LogUtil.Message($"Resource {def.LabelCap} adding new thing to tithe list: [{thing.thingDef.LabelCap} | {TextUtil.GetQualityLabelCap(thing.quality)} | {thing.stuffDef?.LabelCap ?? "null stuff"}] with quantity {quantity}");

            if (tithes.ContainsKey(thing))
            {
                int totalNum;
                if (forceToQuantity)
                {
                    totalNum = quantity;
                }
                else
                {
                    totalNum = tithes[thing] + quantity;
                }
                tithes[thing] = totalNum;
            }
            else
            {
                tithes.Add(thing, quantity);
            }

            dirtyTitheCache = true;
            settlement.DirtyProfitCache();
            return true;
        }
        /// <summary>
        /// Removes a given <paramref name="quantity"/> of <paramref name="thing"/> from the tithes list.
        /// <para>This function dirties the tithe cache, forcing a recalculation of the total tithe value.</para>
        /// <para>This function does not remove <paramref name="thing"/> from the tithes list if its quantity reaches 0. For that, use RemoveFromTitheList().</para>
        /// </summary>
        /// <param name="thing">A ThingQualityTuple specifying the ThingDef, QualityCategory, and StuffDef of the thing to decrement.</param>
        /// <param name="quantity">The quantity to remove from the tithes list. Should always be a non-zero positive value.</param>
        public void DecrementInTitheList(ThingQualityTuple thing, int quantity)
        {
            if (quantity == 0)
            {
                LogUtil.Warning($"Tried to remove 0 objects from the tithes list for resource {def.LabelCap}");
                return;
            }
            if (quantity < 0)
            {
                LogUtil.Error($"Tried to remove a negative quantity of objects from the tithes list for resource {def.LabelCap}. You should use addToTithesList() instead.");
                return;
            }

            if (tithes.ContainsKey(thing))
            {
                tithes[thing] -= quantity;
                if (tithes[thing] < 0)
                {
                    tithes[thing] = 0;
                }
            }
            else
            {
                LogUtil.Warning($"Tried to remove {thing.thingDef.LabelCap} from tithes list for resource {def.LabelCap}, but it doesn't exist");
            }
            dirtyTitheCache = true;
            settlement.DirtyProfitCache();
        }
        /// <summary>
        /// Fully removes the given <paramref name="thing"/> from the tithes list.
        /// <para>This function dirties the tithe cache, forcing a recalculation of the total tithe value.</para>
        /// <para>We should only fully remove an item from the tithes list if the player commands it so. Use DecrementInTitheList() otherwise, so that things with a quantity of 0 remain in the tithes list.</para>
        /// </summary>
        /// <param name="thing">A ThingQualityTuple specifying the ThingDef, QualityCategory, and StuffDef of the thing to remove.</param>
        public void RemoveFromTitheList(ThingQualityTuple thing)
        {
            if (tithes.ContainsKey(thing))
            {
                LogUtil.Message($"Resource {def.LabelCap} removing thing from tithe list: {thing.thingDef.LabelCap} | {TextUtil.GetQualityLabelCap(thing.quality)} | {thing.stuffDef?.LabelCap ?? "null stuff"}");
                tithes.Remove(thing);
                dirtyTitheCache = true;
                settlement.DirtyProfitCache();
            }
        }
        public ThingQualityTuple GetTitheListKey(ThingQualityTuple thing)
        {
            if (tithes.ContainsKey(thing))
            {
                return thing;
            }
            else
            {
                return null;
            }
        }
        public bool HasTitheListKey(ThingQualityTuple thing)
        {
            return tithes.ContainsKey(thing);
        }
        public int GetTitheListValue(ThingQualityTuple thing)
        {
            if (tithes.ContainsKey(thing))
            {
                return tithes[thing];
            }
            else
            {
                return 0;
            }
        }
        public List<ThingQualityTuple> GetTitheListKeys()
        {
            return tithes.Keys.ToList();
        }
        public List<int> GetTitheListValues()
        {
            return tithes.Values.ToList();
        }
        public int GetTitheListCount()
        {
            return tithes.Count;
        }
        public bool CanSetTitheQuality(out QualityCategory maxQuality)
        {
            //TODO: add a building or something that enables selecting item quality when tithing
            maxQuality = QualityCategory.Legendary;
            return true;
        }
        public List<QualityCategory> GetValidTitheQualities(QualityCategory maxQuality)
        {
            List<QualityCategory> list = QualityUtility.AllQualityCategories;
            for (int i = list.Count - 1; i > 0; i--)
            {
                if (list[i] > maxQuality)
                {
                    list.RemoveAt(i);
                }
            }

            return list;
        }
        public bool CanSetTitheStuff()
        {
            //TODO: add a building or something that enables selecting item stuff when tithing
            return true;
        }
        public List<ThingDef> GetStuffListForThingDef(ThingDef thing)
        {
            return CraftUtil.GetThingStuffs(thing, settlement.GetGrandThingList());
        }
        public float TitheThingValue(ThingQualityTuple thing)
        {
            return CraftUtil.ThingValue(thing);
        }
        public float TitheThingValue(ThingDef thing, ThingDef stuff, QualityCategory quality)
        {
            return CraftUtil.ThingValue(thing, stuff, quality);
        }
        public float TitheThingTotalValue(ThingQualityTuple thing, int quanity)
        {
            return CraftUtil.ThingValue(thing) * quanity;
        }
        public bool CanAffordThingAmount(ThingQualityTuple thing, int quanity)
        {
            double available;
            if (autoMaxRandomTithe)
            {
                RefreshTitheCacheIfDirty();
                available = GetTitheIncome() - cachedTitheTotalValue;
            }
            else
            {
                available = GetTitheIncome() - titheTotalValue;
            }
            return ResourceFormulas.CanAffordThingAmount(TitheThingTotalValue(thing, quanity), available);
        }
        public int MaxThingCanAfford(ThingQualityTuple thing)
        {
            double available;
            if (autoMaxRandomTithe)
            {
                RefreshTitheCacheIfDirty();
                available = GetTitheIncome() - cachedTitheTotalValue;
            }
            else
            {
                available = GetTitheIncome() - titheTotalValue;
            }
            return MaxThingCanAfford(thing, available);
        }
        public int MaxThingCanAfford(ThingQualityTuple thing, double budget)
        {
            return ResourceFormulas.MaxThingCanAfford(budget, CraftUtil.ThingValue(thing));
        }
        public float CalcTotalTitheValue()
        {
            float total = 0;
            foreach (var (key, value) in tithes)
            {
                total += TitheThingTotalValue(key, value);
            }

            return total;
        }
        public ThingQualityTuple FindHighestValueTitheThing()
        {
            ThingQualityTuple maxthing = null;
            float maxval = 0;
            foreach (var (key, value) in tithes)
            {
                if (value <= 0) continue; // skip zero-quantity entries
                float val = TitheThingValue(key);
                if (val > maxval)
                {
                    maxthing = key;
                    maxval = val;
                }
            }
            return maxthing;
        }
        /// <summary>
        /// Removes items from the tithes dictionary if the total value of the tithes is higher than the raw total production.
        /// <para>This function dirties the tithe cache, forcing a recalculation of the total tithe value.</para>
        /// <para>NOTE: The algorithm is heavy-handed. Calling this function with high frequency is ill-advised.</para>
        /// </summary>
        // Could probably make the algorithm slightly less heavy by just subtracting values from totalValue instead of constantly re-calling
        //   CalcTotalTitheValue(), but I'm paranoid about the values misaligning. So leaving as is. If optimization is necessary, that's a
        //   decent place to start.
        public void PruneTitheList()
        {
            if (tithesPaused)
            {
                return;
            }
            if (tithes.Count == 0)
            {
                return;
            }

            double totalValue = 0;
            double titheIncome = GetTitheIncome();
            int maxIterations = tithes.Count * 3 + 5;
            int iterations = 0;
            while ((totalValue = CalcTotalTitheValue()) > titheIncome && tithes.Count > 0)
            {
                if (++iterations > maxIterations)
                {
                    LogUtil.Error($"PruneTitheList() for resource {def.LabelCap} exceeded max iterations ({maxIterations}). Bailing out to prevent freeze.");
                    break;
                }
                ThingQualityTuple maxValueThing = FindHighestValueTitheThing();
                if (maxValueThing == null)
                {
                    /* This case shouldn't be possible. But *just* in case, we'll throw an error and bail out if we get here. */
                    /* With tithe injections, this case IS now possible. Downgrade the error to a regular message */
                    LogUtil.Message($"Got NULL when trying to find highest value thing in tithes list for resource {def.LabelCap}. Bailing out of PruneTitheList()");
                    return;
                }
                int quantity = tithes[maxValueThing];
                double totalThingValue = TitheThingTotalValue(maxValueThing, quantity);
                if (totalValue - totalThingValue < titheIncome)
                {
                    double budget = titheIncome - (totalValue - totalThingValue);
                    int newQuantity = MaxThingCanAfford(maxValueThing, budget);
                    int removeNum = quantity - newQuantity;
                    DecrementInTitheList(maxValueThing, removeNum);
                }
                else
                {
                    DecrementInTitheList(maxValueThing, quantity);
                }
            }
            if (totalValue > titheIncome && tithes.Count == 0 && !autoMaxRandomTithe)
            {
                /* In this case, the total tithe value must consist entirely of the random tithe budget. So just cap the random tithe budget at
                 * titheIncome */
                randomTitheBudget = (int)titheIncome;
            }

            /* One final sanity check. Probably not necessary? If this impacts performance too much, then nuke it. Probably fine though */
            if ((totalValue = CalcTotalTitheValue()) > titheIncome)
            {
                LogUtil.Error($"Reached end of PruneTitheList() for resource {def.LabelCap}, but total tithe value {totalValue} is still greater than tithe income {titheIncome}!");
            }
            dirtyTitheCache = true;
        }
        public List<Thing> GenerateTithe(out int extraSilver)
        {
            int outSilver = 0;
            List<Thing> titheItems = new List<Thing>();

            //Sanity check to make sure that this function is only being called after the proper prep
            if (!settlement.IsCalculatingTax)
            {
                LogUtil.Error($"Attempted to generate tithe for resource {label} in settlement {settlement.Name} outside of tax phase");
                extraSilver = outSilver;
                return null;
            }

            if (def.isPoolResource)
            {
                LogUtil.Error($"Attempted to generate tithe for pool resource {label} in settlement {settlement.Name}");
                extraSilver = outSilver;
                return null;
            }

            // Pre-tax generation hook
            def.GetModExtension<ResourceTaxExtension>()?.OnPreTaxGeneration(this, settlement);

            // Prepare the tithe filter. I don't think it should ever be null here, but we'll account for that, just in case.
            if (randomTitheFilter == null)
            {
                randomTitheFilter = new ThingFilter();
                ResetThingFilter();
            }

            // Determine random tithing budget
            if (disburseTitheStock && randomTitheStock > 0)
            {
                outSilver += (int)randomTitheStock;
                randomTitheStock = 0;
            }
            double randomBudget = randomTitheBudget + randomTitheStock;
            if (hasRandomTithe && randomBudget > 0)
            {
                if (!randomTitheFilter.AllowedThingDefs.Any())
                {
                    randomTitheStock = randomBudget;
                    Find.LetterStack.ReceiveLetter("FCNoTitheLetterLabel".Translate(settlement.Name), "FCNoTitheLetterDesc".Translate(settlement.Name, label, randomTitheStock), LetterDefOf.NeutralEvent);
                }
                else
                {
                    // Calculate the random tithe

                    double minimum = randomTitheFilter.AllowedThingDefs.Aggregate<ThingDef, double>(double.MaxValue, (current, thing) => Math.Min(thing?.BaseMarketValue ?? 100, current));
                    LogUtil.Message($"{settlement.Name}, resource {label}, minimum random tithe: {minimum}, budget: {randomBudget}");
                    if (minimum <= randomBudget)
                    {
                        List<Thing> randomTitheList = new List<Thing>();
                        ThingSetMaker thingSetMaker = new ThingSetMaker_MarketValue();
                        ThingSetMakerParams param = new ThingSetMakerParams();
                        param.totalMarketValueRange = new FloatRange((float)randomBudget, (float)(randomBudget + GetTotalTitheModifierForWorkers()));
                        param.filter = randomTitheFilter;
                        param.techLevel = FindFC.EmpireFaction.def.techLevel;

                        LogUtil.Message($"  randomTitheFilter has {randomTitheFilter.AllowedDefCount} allowed items");

                        TechLevel tmplevel = TechLevel.Undefined;
                        ThingSetMaker tmp = def.GetModExtension<ResourceFilterExtension>()?.GetThingSetMaker(out tmplevel, this);
                        if (tmp != null)
                        {
                            thingSetMaker = tmp;
                            param.techLevel = tmplevel;
                        }
                        else
                        {
                            param.countRange = new IntRange(def.titheMinCount, def.titheMaxCountBase + (def.titheMaxCountScaler * assignedWorkers));
                        }
                        randomTitheList = thingSetMaker.Generate(param);

                        if (randomTitheList is null || randomTitheList.Count == 0)
                        {
                            LogUtil.Message($"Resource {label} in settlement {settlement.Name} generated an empty random tithe list");
                        }
                        else
                        {
                            for (int i = 0; i < randomTitheList.Count; i++)
                            {
                                LogUtil.Message($"  randomTitheList[{i}]: {randomTitheList[i].LabelCap}");
                            }
                            titheItems.AddRange(randomTitheList);
                            randomTitheStock = 0;
                        }
                    }
                    else
                    {
                        randomTitheStock = randomBudget;
                        Find.LetterStack.ReceiveLetter("FCNoTitheLetterLabel".Translate(settlement.Name), "FCNoTitheLetterDesc2".Translate(settlement.Name, label, randomTitheStock), LetterDefOf.NeutralEvent);
                    }

                }
            }

            // Now handle specified tithes. We (should) have called PruneTitheList before this, so we shouldn't have to worry about the math adding up
            if (tithes.Count > 0)
            {
                /* Iterate over the dictionary, creating a new thing for each entry, and adding each such thing to the list of tithe items */
                foreach (ThingQualityTuple key in tithes.Keys)
                {
                    int quantity = tithes[key];
                    if (quantity == 0)
                    {
                        continue;
                    }

                    /* Try to generate the list through the resource's ResourceFilterExtension */
                    List<Thing> things = def.GetModExtension<ResourceFilterExtension>()?.GenerateSpecificThings(key.thingDef, quantity, key.quality, key.stuffDef, this);
                    if (things is null)
                    {
                        /* If we're here, then the resource doesn't have a special implementation for GenerateSpecificThings(). So try to make things the generic way. */
                        for (int i = 0; i < quantity; i++)
                        {
                            Thing thing = ThingMaker.MakeThing(key.thingDef, key.stuffDef);

                            if (CraftUtil.ThingHasQuality(key.thingDef))
                            {
                                CompQuality thingQuality = thing.TryGetComp<CompQuality>();
                                thingQuality.SetQuality(key.quality, ArtGenerationContext.Outsider);
                            }
                            titheItems.Add(thing);
                        }
                    }
                    else
                    {
                        titheItems.AddRange(things);
                    }
                }
            }

            // Post-tax generation hook
            def.GetModExtension<ResourceTaxExtension>()?.OnPostTaxGeneration(this, settlement, titheItems, ref outSilver);

            extraSilver = outSilver;
            return titheItems;
        }
        /* - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - *
         *   End Tithe functions                                                                                                                                         *
         * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - * - */

        public int CompareForUI(ResourceFC compareDef)
        {
            if (compareDef == null)
            {
                return -2;
            }
            if (compareDef.def == null)
            {
                return -1;
            }
            if (this.def == null)
            {
                return 1;
            }
            return ResourceTypeDef.SortForUI(this.def, compareDef.def);
        }
        public static int SortForUI(ResourceFC a, ResourceFC b)
        {
            return a.CompareForUI(b);
        }
    }
}
