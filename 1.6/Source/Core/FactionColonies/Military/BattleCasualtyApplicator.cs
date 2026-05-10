using System;
using System.Collections.Generic;
using FactionColonies.util;
using RimWorld;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/
    /* BattleCasualtyApplicator                                                    */
    /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/

    /// <summary>
    /// Translates an auto-resolved battle's abstract force decrement into real hediffs
    /// and deaths on a squad's deployed mercenary pawns. Without this, an auto-resolved
    /// loss never touches the merc healing or death-lifecycle systems and the squad is
    /// functionally invincible.
    /// <para>Algorithm:
    ///   rate = (initial - remaining) / initial, scaled by mercenaryCasualtyRateMultiplier
    ///   deathChance = max(0, (rate - threshold) / (1 - threshold)) * maxDeathFraction
    ///                 scaled by mercenaryDeathChanceMultiplier
    ///   pick floor(deployedCount * rate) victims; per-victim roll for death vs injury.</para>
    /// <para>Deaths route through <c>Pawn.Kill</c>, which the existing MercenaryDied harmony
    /// patch handles (squad.dead++, LifecycleRegistry.InvokeOnMercenaryDeath, slot nulling).
    /// Injuries apply 1-3 Cut hediffs on random non-vital body parts; severity scales
    /// with the casualty rate so a 30% loss feels mild and a 75%+ loss feels grim.</para>
    /// </summary>
    public static class BattleCasualtyApplicator
    {
        /* Injury severity bounds at the low and high ends of the casualty-rate spectrum.
         * Linear interpolation between them based on the squad's casualty rate. */
        private const float MildSeverityMin = 3f;
        private const float MildSeverityMax = 6f;
        private const float SevereSeverityMin = 10f;
        private const float SevereSeverityMax = 18f;

        public static void ApplyCasualtiesToSquad(
            MercenarySquadFC squad,
            double initialForce,
            double remainingForce,
            WorldSettlementFC statsContext)
        {
            if (!FCSettings.applyAutoResolveInjuries) return;
            if (squad is null) return;
            if (initialForce <= 0) return;

            FactionFC faction = FactionCache.FactionComp;
            if (faction is null) return;

            /* Casualty rate is the fraction of force lost on this side. Multiply by the stat
             * so policies/events/buildings can soften or amplify squad attrition through the
             * existing FCStatModifier pipeline (no new registry needed). */
            double rawRate = (initialForce - remainingForce) / initialForce;
            if (rawRate < 0) rawRate = 0;
            if (rawRate > 1) rawRate = 1;

            double rateMult = faction.GetStatValue(FCStatDefOf.mercenaryCasualtyRateMultiplier, statsContext);
            double finalRate = rawRate * (rateMult > 0 ? rateMult : 1.0);
            if (finalRate < 0) finalRate = 0;
            if (finalRate > 1) finalRate = 1;

            if (finalRate <= 0) return;

            /* Death chance ramps from 0 at the threshold to maxDeathFraction at rate=1.0.
             * Below the threshold no casualty dies, only injuries. The stat lets submods
             * make a squad more or less lethal-prone. */
            float threshold = FCSettings.autoResolveCasualtyDeathThreshold;
            float maxDeathFraction = FCSettings.autoResolveCasualtyMaxDeathFraction;
            double deathChance = 0;
            if (finalRate > threshold && threshold < 1f)
            {
                deathChance = ((finalRate - threshold) / (1.0 - threshold)) * maxDeathFraction;
            }
            double deathMult = faction.GetStatValue(FCStatDefOf.mercenaryDeathChanceMultiplier, statsContext);
            deathChance *= (deathMult > 0 ? deathMult : 1.0);
            if (deathChance < 0) deathChance = 0;
            if (deathChance > 1) deathChance = 1;

            /* Candidate pool: alive mercs in the squad. Animals are intentionally included;
             * they're part of the deployed force and the abstract simulation rolled them
             * into the same forceRemaining number. */
            List<Pawn> candidates = new List<Pawn>();
            if (squad.mercenaries != null)
            {
                foreach (Mercenary m in squad.mercenaries)
                {
                    if (m?.pawn is object && !m.pawn.Dead) candidates.Add(m.pawn);
                }
            }
            if (squad.animals != null)
            {
                foreach (Mercenary m in squad.animals)
                {
                    if (m?.pawn is object && !m.pawn.Dead) candidates.Add(m.pawn);
                }
            }
            if (candidates.Count == 0) return;

            int victimCount = (int)Math.Floor(candidates.Count * finalRate);
            if (victimCount <= 0 && finalRate > 0) victimCount = 1;
            if (victimCount > candidates.Count) victimCount = candidates.Count;

            candidates.Shuffle();
            for (int i = 0; i < victimCount; i++)
            {
                Pawn victim = candidates[i];
                if (victim is null || victim.Dead) continue;

                try
                {
                    if (deathChance > 0 && Rand.Value < deathChance)
                    {
                        /* Kill flows through the MercenaryDied harmony patch which handles
                         * squad.dead, lifecycle hook, slot nulling, equipment cleanup. */
                        victim.Kill(null);
                    }
                    else
                    {
                        ApplyInjuryHediffs(victim, finalRate);
                    }
                }
                catch (Exception e)
                {
                    LogUtil.Error($"BattleCasualtyApplicator: failed to apply casualty to {victim?.LabelShortCap}: {e}");
                }
            }
        }

        /// <summary>
        /// Adds 1-3 Cut hediffs on random non-vital body parts. Severity is linearly
        /// interpolated between the mild and severe bounds based on the casualty rate, so a
        /// near-wipe squad ends up genuinely incapacitated rather than lightly scratched.
        /// </summary>
        private static void ApplyInjuryHediffs(Pawn pawn, double rate)
        {
            if (pawn?.health?.hediffSet is null) return;

            int woundCount = rate >= 0.75 ? Rand.RangeInclusive(2, 3) : Rand.RangeInclusive(1, 2);

            float t = (float)Math.Min(1.0, rate / 0.75);
            float sevMin = Mathf.Lerp(MildSeverityMin, SevereSeverityMin, t);
            float sevMax = Mathf.Lerp(MildSeverityMax, SevereSeverityMax, t);

            for (int i = 0; i < woundCount; i++)
            {
                BodyPartRecord part = PickWoundablePart(pawn);
                if (part is null) return;

                Hediff hediff = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part);
                hediff.Severity = Rand.Range(sevMin, sevMax);
                pawn.health.AddHediff(hediff, part, null, null);
            }
        }

        /// <summary>
        /// Picks a non-missing body part that isn't a consciousness source (brain), so the
        /// applicator can't accidentally kill a pawn through brain damage when the death
        /// roll already decided it should survive as injured. Returns null when no suitable
        /// part exists, in which case the caller skips the wound.
        /// </summary>
        private static BodyPartRecord PickWoundablePart(Pawn pawn)
        {
            List<BodyPartRecord> all = new List<BodyPartRecord>();
            foreach (BodyPartRecord p in pawn.health.hediffSet.GetNotMissingParts())
            {
                if (p?.def?.tags != null && p.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource)) continue;
                all.Add(p);
            }
            return all.Count == 0 ? null : all.RandomElement();
        }
    }
}
