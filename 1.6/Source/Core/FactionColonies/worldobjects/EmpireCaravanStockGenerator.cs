using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// StockGenerator for Empire caravan traders. Generates stock for a single resource type,
    /// aggregated across all empire settlements. Unlike <see cref="EmpireStockGenerator"/>,
    /// this reads <see cref="FactionCache.FactionComp"/> directly.
    /// </summary>
    public class EmpireCaravanStockGenerator : StockGenerator
    {
        /// <summary>Set in XML — which resource this caravan specializes in.</summary>
        public ResourceTypeDef resourceDef;

        /// <summary>
        /// Distinct ThingDef count to pick from the resource pool. Caps trader variety so each
        /// item gets a meaningful budget slice (otherwise budget is spread across hundreds of
        /// candidates and produces 1-3 stacks of every low-value product).
        /// Override per-resource in XML when the pool conflates very different value tiers
        /// (e.g. animals + animal products).
        /// </summary>
        public IntRange varietyRange = new IntRange(15, 25);

        /// <summary>
        /// Hard cap on total pawns spawned per generator run. Applies to any race-bearing
        /// ThingDef in the candidate pool — animals today, but also humanlikes, mechanoids,
        /// or any other race a future resource might surface. Orthogonal to
        /// <see cref="varietyRange"/> (which caps distinct ThingDef *kinds*); this caps
        /// individual *pawns*. Budget that would have produced extra pawns is redirected
        /// to non-pawn items in the same candidate pool, falling back to silver if the
        /// pool has none. Default to 15; a sane (if high) fallback.
        /// </summary>
        public int maxPawnCount = 15;

        /// <summary>Randomness range for per-item budget (multiplier).</summary>
        private const float BudgetRandomMin = 0.5f;
        private const float BudgetRandomMax = 1.5f;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            FactionFC factionComp = FindFC.FactionComp;
            if (factionComp is null || resourceDef is null)
                yield break;

            if (!resourceDef.ResourceTypeAllowedByTech(factionComp.techLevel))
                yield break;

            float extraScale = EmpireStockGenerator.ExtraScaling();

            // Aggregate budget and ThingDefs across all settlements for this resource
            double totalBudget = 0;
            HashSet<ThingDef> allThingDefs = new HashSet<ThingDef>();

            foreach (WorldSettlementFC settlement in factionComp.settlements)
            {
                ResourceFC res = settlement.GetResource(resourceDef);
                if (res is null || res.assignedWorkers <= 0)
                    continue;

                totalBudget += res.grossMarketValue;

                List<ThingDef> thingDefs = res.GenerateThingDefList();
                if (!thingDefs.NullOrEmpty())
                {
                    foreach (ThingDef td in thingDefs)
                        allThingDefs.Add(td);
                }
            }

            if (totalBudget <= 0 || allThingDefs.Count == 0)
                yield break;

            totalBudget *= extraScale;

            // Generate silver proportional to budget
            int silverCount = Mathf.Max(100, Mathf.RoundToInt((float)totalBudget));
            foreach (Thing silver in StockGeneratorUtility.TryMakeForStock(ThingDefOf.Silver, silverCount, faction))
            {
                yield return silver;
            }

            // Filter to tradeable defs
            List<ThingDef> candidates = allThingDefs
                .Where(td => td.tradeability.TraderCanSell())
                .InRandomOrder()
                .ToList();

            if (candidates.Count == 0)
                yield break;

            // Cap variety so we don't see a thousand stacks of single items (or a thousand animals)
            int variety = Mathf.Min(varietyRange.RandomInRange, candidates.Count);
            if (variety <= 0)
                yield break;
            candidates = candidates.Take(variety).ToList();

            float perItemBudget = (float)totalBudget / variety;

            /* Split candidates: any race-bearing ThingDef yields Pawns and is subject to
               maxPawnCount; everything else is an item. Order within each bucket is
               preserved from the already-shuffled candidates. */
            List<ThingDef> pawnCandidates = new List<ThingDef>();
            List<ThingDef> itemCandidates = new List<ThingDef>();
            foreach (ThingDef td in candidates)
            {
                if (td.race is object)
                    pawnCandidates.Add(td);
                else
                    itemCandidates.Add(td);
            }

            /* Plan pawn yields up to maxPawnCount. Surplus per-item budget is captured
               as divertedBudget for redistribution to items below. */
            List<KeyValuePair<PawnKindDef, int>> plannedPawns = new List<KeyValuePair<PawnKindDef, int>>();
            int runningPawnTotal = 0;
            float divertedBudget = 0f;

            foreach (ThingDef td in pawnCandidates)
            {
                float randomizedBudget = perItemBudget * Rand.Range(BudgetRandomMin, BudgetRandomMax);
                float marketValue = td.BaseMarketValue;
                if (marketValue <= 0f)
                    marketValue = 1f;

                int desiredCount = Mathf.Max(1, Mathf.RoundToInt(randomizedBudget / marketValue));
                int remainingCap = Mathf.Max(0, maxPawnCount - runningPawnTotal);
                int allowedCount = Mathf.Min(desiredCount, remainingCap);

                if (allowedCount > 0)
                {
                    PawnKindDef pawnKind = td.race.AnyPawnKind;
                    if (pawnKind is object)
                    {
                        plannedPawns.Add(new KeyValuePair<PawnKindDef, int>(pawnKind, allowedCount));
                        runningPawnTotal += allowedCount;
                    }
                    else
                    {
                        // No valid PawnKindDef — divert this slot's full budget too
                        allowedCount = 0;
                    }
                }

                divertedBudget += (desiredCount - allowedCount) * marketValue;
            }

            // Emit planned pawns
            foreach (KeyValuePair<PawnKindDef, int> entry in plannedPawns)
            {
                for (int i = 0; i < entry.Value; i++)
                {
                    PawnGenerationRequest request = new PawnGenerationRequest(entry.Key, null, PawnGenerationContext.NonPlayer);
                    Pawn pawn = PawnGenerator.GeneratePawn(request);
                    if (pawn is object) yield return pawn;
                }
            }

            /* Emit non-pawn items. Diverted budget (from capped pawns) is split evenly
               across item candidates as a per-item bonus on top of the normal randomized
               share. If no item candidates exist, fall through to the silver fallback. */
            float bonusPerItem = (itemCandidates.Count > 0) ? divertedBudget / itemCandidates.Count : 0f;

            foreach (ThingDef td in itemCandidates)
            {
                float randomizedBudget = perItemBudget * Rand.Range(BudgetRandomMin, BudgetRandomMax) + bonusPerItem;
                float marketValue = td.BaseMarketValue;
                if (marketValue <= 0f)
                    marketValue = 1f;

                int stackCount = Mathf.Max(1, Mathf.RoundToInt(randomizedBudget / marketValue));

                foreach (Thing thing in StockGeneratorUtility.TryMakeForStock(td, stackCount, faction))
                {
                    yield return thing;
                }
            }

            // Fallback: no items to absorb the diverted budget — emit it as extra silver
            if (itemCandidates.Count == 0 && divertedBudget > 0f)
            {
                int bonusSilver = Mathf.RoundToInt(divertedBudget);
                if (bonusSilver > 0)
                {
                    foreach (Thing silver in StockGeneratorUtility.TryMakeForStock(ThingDefOf.Silver, bonusSilver, faction))
                    {
                        yield return silver;
                    }
                }
            }
        }

        /// <summary>
        /// Accepts items matching this caravan's resource type or common essentials (food,
        /// medicine, non-armor apparel). Rejects dangerous/worthless items via the shared blocklist.
        /// Silver is always accepted — the trader generates it as stock and uses it as currency.
        /// </summary>
        public override bool HandlesThingDef(ThingDef thingDef)
        {
            if (thingDef == ThingDefOf.Silver)
                return true;

            if (EmpireTradeFilterUtil.ShouldReject(thingDef))
                return false;

            return EmpireTradeFilterUtil.IsCommonEssential(thingDef)
                || (resourceDef is object && resourceDef.AllowsForTrade(thingDef));
        }

        public override IEnumerable<string> ConfigErrors(TraderKindDef parentDef)
        {
            if (resourceDef is null)
                yield return "EmpireCaravanStockGenerator has no resourceDef defined";
        }
    }
}
