using System;
using RimWorld;
using Verse;

namespace FactionColonies
{
    public static class MilitaryEfficiencyUtil
    {
        private const float QualityScaleFactor = 4f;

        /// <summary>
        /// Applies a combat efficiency hediff (buff or debuff) to a pawn based on
        /// the force's military efficiency. Removes any existing FC combat efficiency
        /// hediff first.
        /// </summary>
        public static void ApplyCombatEfficiencyHediff(Pawn pawn, double efficiency)
        {
            if (pawn == null || pawn.health == null) return;

            float dampedDelta = (float)((efficiency - 1.0) * FCSettings.efficiencyDamping);
            RemoveCombatEfficiencyHediff(pawn);

            if (Math.Abs(dampedDelta) < 0.001f) return;

            HediffDef hediffDef;
            float severity;

            if (dampedDelta > 0f)
            {
                hediffDef = FCHediffDefOf.FC_CombatEfficiency_Buff;
                severity = dampedDelta;
            }
            else
            {
                hediffDef = FCHediffDefOf.FC_CombatEfficiency_Debuff;
                severity = Math.Abs(dampedDelta);
            }

            Hediff hediff = HediffMaker.MakeHediff(hediffDef, pawn);
            hediff.Severity = severity;
            pawn.health.AddHediff(hediff);
        }

        /// <summary>
        /// Removes any FC combat efficiency hediff (buff or debuff) from the pawn.
        /// </summary>
        public static void RemoveCombatEfficiencyHediff(Pawn pawn)
        {
            if (pawn?.health?.hediffSet is null) return;

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(FCHediffDefOf.FC_CombatEfficiency_Buff)
                ?? pawn.health.hediffSet.GetFirstHediffOfDef(FCHediffDefOf.FC_CombatEfficiency_Debuff);
            if (existing != null)
            {
                pawn.health.RemoveHediff(existing);
            }
        }

        /// <summary>
        /// Shifts the quality of all equipment and apparel on a pawn based on
        /// combat efficiency. Only for squad pawns whose gear comes from loadout templates.
        /// </summary>
        public static void ShiftPawnGearQuality(Pawn pawn, double efficiency)
        {
            if (pawn == null) return;

            float dampedDelta = (float)((efficiency - 1.0) * FCSettings.efficiencyDamping);
            int shift = (int)Math.Round(dampedDelta * QualityScaleFactor);
            if (shift == 0) return;

            if (pawn.equipment != null)
            {
                foreach (ThingWithComps weapon in pawn.equipment.AllEquipmentListForReading)
                {
                    ShiftThingQuality(weapon, shift);
                }
            }

            if (pawn.apparel != null)
            {
                foreach (Apparel apparel in pawn.apparel.WornApparel)
                {
                    ShiftThingQuality(apparel, shift);
                }
            }
        }

        private static void ShiftThingQuality(Thing thing, int shift)
        {
            CompQuality compQ = thing.TryGetComp<CompQuality>();
            if (compQ == null) return;

            int newQual = Math.Min(Math.Max((int)compQ.Quality + shift, (int)QualityCategory.Awful), (int)QualityCategory.Legendary);
            compQ.SetQuality((QualityCategory)newQual, null);
        }
    }
}
