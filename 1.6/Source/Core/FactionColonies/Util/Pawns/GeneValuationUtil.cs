using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FactionColonies.util
{
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*
     * Smarter xenotype cost valuator. Replaces the simple product-of-marketValueFactor
     * formula previously inlined at MilUnitFC.UpdateEquipmentTotalCost and at two
     * FCWindow_XenoPicker sites.
     *
     * Formula: xenoFactor = max(MIN_FACTOR, 1 + sum over genes of GeneBonus(gene))
     *   GeneBonus = mvfBonus    * W_MVF       // marketValueFactor - 1 (signed)
     *             + metBonus    * W_MET       // max(0, -biostatMet) — capped; positive metabolism doesn't reduce price
     *             + arcBonus    * W_ARC       // biostatArc
     *             + abilCount   * W_ABIL      // abilities.Count
     *             + effectScore * W_EFFECTS   // category-weighted statFactors/Offsets/capMods/aptitudes (signed)
     *             + painBonus   * W_PAIN      // (1 - painFactor) — signed; painFactor > 1 reduces price
     *             + dmgRBonus   * W_DMGRESIST // sum of (1 - damageFactor) — signed; > 1 damage taken reduces price
     *
     * MIN_FACTOR is a hard floor (0.1) to prevent negative or zero costs from awful xenotypes.
     *
     * Components are cached per-gene and per-xenotype (immutable defs). Custom xenotypes
     * are cached by name and invalidated through FactionCache.InvalidateCustomXenotypeCache.
     * Weights are applied at read time, so changing FCSettings sliders does not require
     * cache invalidation.
     *-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/
    public static class GeneValuationUtil
    {
        public struct GeneValueComponents
        {
            public float MvfBonus;
            public float MetBonus;
            public float ArcBonus;
            public int   AbilityCount;
            public float EffectScore;
            public float PainBonus;
            public float DmgResistBonus;

            public float ApplyWeights()
            {
                return MvfBonus       * FCSettings.geneValueWeightMvf
                     + MetBonus       * FCSettings.geneValueWeightMet
                     + ArcBonus       * FCSettings.geneValueWeightArc
                     + AbilityCount   * FCSettings.geneValueWeightAbility
                     + EffectScore    * FCSettings.geneValueWeightEffects
                     + PainBonus      * FCSettings.geneValueWeightPain
                     + DmgResistBonus * FCSettings.geneValueWeightDmgResist;
            }

            public void Add(GeneValueComponents other)
            {
                MvfBonus       += other.MvfBonus;
                MetBonus       += other.MetBonus;
                ArcBonus       += other.ArcBonus;
                AbilityCount   += other.AbilityCount;
                EffectScore    += other.EffectScore;
                PainBonus      += other.PainBonus;
                DmgResistBonus += other.DmgResistBonus;
            }
        }

        /* Minimum factor: sanity floor to prevent zero/negative costs from awful xenotypes. */
        private const float MIN_FACTOR = 0.1f;

        private static readonly Dictionary<GeneDef, GeneValueComponents> _geneComponentsCache
            = new Dictionary<GeneDef, GeneValueComponents>();
        private static readonly Dictionary<XenotypeDef, GeneValueComponents> _xenotypeComponentsCache
            = new Dictionary<XenotypeDef, GeneValueComponents>();
        private static readonly Dictionary<string, GeneValueComponents> _customXenotypeComponentsCache
            = new Dictionary<string, GeneValueComponents>();

        /* Called by FactionCache.InvalidateCustomXenotypeCache so player-edited xenotypes
         * pick up gene list changes. */
        public static void InvalidateCustomXenotypeCache()
        {
            _customXenotypeComponentsCache.Clear();
        }

        /* Cached fast path for vanilla / def-based xenotypes. */
        public static float XenotypeFactor(XenotypeDef xenotype)
        {
            if (xenotype is null) return 1f;
            GeneValueComponents components;
            if (!_xenotypeComponentsCache.TryGetValue(xenotype, out components))
            {
                components = ComputeXenotypeComponents(xenotype.genes);
                _xenotypeComponentsCache[xenotype] = components;
            }
            return ApplyCap(1f + components.ApplyWeights());
        }

        /* Cached path for player-defined custom xenotypes, keyed by name. */
        public static float XenotypeFactor(CustomXenotype custom)
        {
            if (custom is null) return 1f;
            string key = custom.name;
            if (key is null) return ApplyCap(1f + ComputeXenotypeComponents(custom.genes).ApplyWeights());
            GeneValueComponents components;
            if (!_customXenotypeComponentsCache.TryGetValue(key, out components))
            {
                components = ComputeXenotypeComponents(custom.genes);
                _customXenotypeComponentsCache[key] = components;
            }
            return ApplyCap(1f + components.ApplyWeights());
        }

        /* Uncached fallback. Callers should prefer the typed overloads for hot paths. */
        public static float XenotypeFactor(IEnumerable<GeneDef> genes)
        {
            if (genes is null) return 1f;
            return ApplyCap(1f + ComputeXenotypeComponents(genes).ApplyWeights());
        }

        /* Applies the floor always; applies the configurable upper cap only when the unlimited
         * checkbox is unset. Rounds to 2 decimal places. */
        private static float ApplyCap(float factor)
        {
            if (factor < MIN_FACTOR) factor = MIN_FACTOR;
            if (!FCSettings.geneValueFactorUnlimited)
            {
                float cap = FCSettings.geneValueMaxFactor;
                factor = factor > cap ? cap : factor;
            }
            return (float)Math.Round((double)factor, 2);
        }

        /* Per-gene bonus contribution (additive). Public for future tooltip/UI breakdown. */
        public static float GeneBonus(GeneDef gene)
        {
            if (gene is null) return 0f;
            return ComputeGeneComponentsCached(gene).ApplyWeights();
        }

        /* Debug/inspection accessors — force-populate the cache and return raw components.
         * Used by the "Dump Gene/Xenotype Valuation Cache" debug actions. */
        public static GeneValueComponents GetGeneComponents(GeneDef gene)
        {
            if (gene is null) return default(GeneValueComponents);
            return ComputeGeneComponentsCached(gene);
        }

        public static GeneValueComponents GetXenotypeComponents(XenotypeDef xenotype)
        {
            if (xenotype is null) return default(GeneValueComponents);
            GeneValueComponents components;
            if (!_xenotypeComponentsCache.TryGetValue(xenotype, out components))
            {
                components = ComputeXenotypeComponents(xenotype.genes);
                _xenotypeComponentsCache[xenotype] = components;
            }
            return components;
        }

        public static GeneValueComponents GetXenotypeComponents(CustomXenotype custom)
        {
            if (custom is null) return default(GeneValueComponents);
            string key = custom.name;
            if (key is null) return ComputeXenotypeComponents(custom.genes);
            GeneValueComponents components;
            if (!_customXenotypeComponentsCache.TryGetValue(key, out components))
            {
                components = ComputeXenotypeComponents(custom.genes);
                _customXenotypeComponentsCache[key] = components;
            }
            return components;
        }

        /* Unclamped factor (1 + weighted sum). Lets debug code show what got floored/capped. */
        public static float RawXenotypeFactor(XenotypeDef xenotype)
        {
            return 1f + GetXenotypeComponents(xenotype).ApplyWeights();
        }

        public static float RawXenotypeFactor(CustomXenotype custom)
        {
            return 1f + GetXenotypeComponents(custom).ApplyWeights();
        }

        private static GeneValueComponents ComputeXenotypeComponents(IEnumerable<GeneDef> genes)
        {
            GeneValueComponents sum = default(GeneValueComponents);
            if (genes is null) return sum;
            foreach (GeneDef gene in genes)
            {
                if (gene is null) continue;
                sum.Add(ComputeGeneComponentsCached(gene));
            }
            return sum;
        }

        private static GeneValueComponents ComputeGeneComponentsCached(GeneDef gene)
        {
            GeneValueComponents components;
            if (!_geneComponentsCache.TryGetValue(gene, out components))
            {
                components = ComputeGeneComponents(gene);
                _geneComponentsCache[gene] = components;
            }
            return components;
        }

        private static GeneValueComponents ComputeGeneComponents(GeneDef gene)
        {
            GeneValueComponents c = default(GeneValueComponents);

            /* Signed components — values <1 (mvf), >1 (painFactor, damageFactor) represent downsides
             * and reduce the final cost factor. Met stays capped at 0 since positive metabolism is a
             * design budget benefit, not a combat drawback. */
            c.MvfBonus     = gene.marketValueFactor - 1f;
            c.MetBonus     = Mathf.Max(0, -gene.biostatMet);
            c.ArcBonus     = gene.biostatArc;
            c.AbilityCount = gene.abilities is object ? gene.abilities.Count : 0;
            c.EffectScore  = EffectScore(gene);
            c.PainBonus    = 1f - gene.painFactor;

            if (gene.damageFactors is object)
            {
                float sum = 0f;
                foreach (DamageFactor df in gene.damageFactors)
                    sum += 1f - df.factor;
                c.DmgResistBonus = sum;
            }

            return c;
        }

        /* Category-weighted score of a gene's actual mechanical effects. Picks up modded
         * stats automatically as long as the mod author categorizes them sensibly. */
        private static float EffectScore(GeneDef gene)
        {
            float score = 0f;

            if (gene.statFactors is object)
            {
                foreach (StatModifier sf in gene.statFactors)
                {
                    if (sf.stat is null) continue;
                    float contribution = sf.value - 1f;
                    /* "Lower is better" stats invert: Flammability=0.1 (good) shouldn't score as -0.9. */
                    if (InverseDirectionStats.Contains(sf.stat)) contribution = -contribution;
                    score += contribution * CategoryWeight(sf.stat.category);
                }
            }

            if (gene.statOffsets is object)
            {
                foreach (StatModifier so in gene.statOffsets)
                {
                    if (so.stat is null) continue;
                    /* Normalize the offset against the stat's natural baseline. For 0- and 1-baseline
                     * stats the raw offset passes through; high-baseline stats (e.g. ComfyTemperatureMax
                     * default 40) get normalized. */
                    float denom = Mathf.Max(1f, Mathf.Abs(so.stat.defaultBaseValue));
                    float normalized = so.value / denom;
                    /* Same polarity inversion as statFactors: ComfyTemperatureMin = -10 (more cold tolerance)
                     * is a positive, not a negative. */
                    if (InverseDirectionStats.Contains(so.stat)) normalized = -normalized;
                    /* Clamp per-offset to ±1 so a single absolute-unit offset (e.g. +10°) can't dominate.
                     * Many gene contributions still add up freely — only single-offset outliers are bounded. */
                    normalized = Mathf.Clamp(normalized, -1f, 1f);
                    score += normalized * CategoryWeight(so.stat.category);
                }
            }

            if (gene.capMods is object)
            {
                /* Pawn capacities are inherently combat-relevant (sight/manipulation/moving/
                 * consciousness all affect combat output), so we don't gate them by category. */
                foreach (PawnCapacityModifier cm in gene.capMods)
                {
                    score += cm.offset + (cm.postFactor - 1f);
                }
            }

            if (gene.aptitudes is object)
            {
                foreach (Aptitude apt in gene.aptitudes)
                {
                    score += apt.level * SkillWeight(apt.skill) * 0.05f;
                }
            }

            return score;
        }

        /* Stats where lower = better. Used in EffectScore to invert sign so genes that
         * reduce these stats (FireResistant.Flammability=0.1, Unstoppable.StaggerDurationFactor=0,
         * Robust.IncomingDamageFactor=0.75, ComfyTemperatureMin offsets, etc.) score positively. */
        private static HashSet<StatDef> _inverseDirectionStats;
        private static HashSet<StatDef> InverseDirectionStats
        {
            get
            {
                if (_inverseDirectionStats is null)
                {
                    _inverseDirectionStats = new HashSet<StatDef>();
                    AddIfPresent("ComfyTemperatureMin");      // lower = more cold tolerance
                    AddIfPresent("StaggerDurationFactor");    // lower = recovers faster
                    AddIfPresent("Flammability");             // lower = doesn't catch fire
                    AddIfPresent("IncomingDamageFactor");     // lower = tougher
                    AddIfPresent("AimingDelayFactor");        // lower = faster aim
                    AddIfPresent("MentalBreakThreshold");     // lower = mentally tougher
                    AddIfPresent("HungerRateMultiplier");     // lower = eats less
                }
                return _inverseDirectionStats;
            }
        }

        private static void AddIfPresent(string defName)
        {
            StatDef d = DefDatabase<StatDef>.GetNamedSilentFail(defName);
            if (d is object) _inverseDirectionStats.Add(d);
        }

        private static float CategoryWeight(StatCategoryDef category)
        {
            if (category is null) return 0.1f;
            if (category == StatCategoryDefOf.PawnCombat) return 1.0f;
            if (category == StatCategoryDefOf.PawnHealth || category == StatCategoryDefOf.PawnResistances) return 0.6f;
            if (category == StatCategoryDefOf.BasicsImportant || category == StatCategoryDefOf.BasicsPawnImportant) return 0.5f;
            if (category == StatCategoryDefOf.Basics || category == StatCategoryDefOf.BasicsPawn) return 0.3f;
            if (category == StatCategoryDefOf.PawnWork || category == StatCategoryDefOf.PawnMisc) return 0.1f;
            /* PawnSocial and unknown categories contribute nothing. */
            return 0f;
        }

        private static float SkillWeight(SkillDef skill)
        {
            if (skill is null) return 0f;
            if (skill == SkillDefOf.Shooting || skill == SkillDefOf.Melee) return 1.0f;
            if (skill == SkillDefOf.Medicine) return 0.5f;
            return 0f;
        }
    }
}
