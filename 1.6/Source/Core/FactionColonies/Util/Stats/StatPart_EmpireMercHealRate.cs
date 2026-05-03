using RimWorld;
using Verse;

namespace FactionColonies
{
    /* Multiplies InjuryHealingFactor for off-map mercenary pawns currently registered for
     * Empire's hourly heal tick. Composes the user-facing slider (FCSettings.mercenaryHealRatePerHour)
     * and the per-settlement mercHealRateMultiplier stat into a single factor.
     *
     * Rebaseline math: vanilla heals one wound per HealthTickInterval call for
     * (8 * HealthScale * 0.01 * IHF) ~ 0.08 * IHF HP. Our hourly tick calls HealthTickInterval
     * once per hour, so to make the slider's "1.0 = 1 HP/hour for an untended at-base merc"
     * intent hold, we multiply IHF by (1 / 0.08) = 12.5 at slider 1.0. */
    public class StatPart_EmpireMercHealRate : StatPart
    {
        private const float RebaselineFactor = 12.5f;

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (TryGetMercFactor(req, out float factor))
                val *= factor;
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!TryGetMercFactor(req, out float factor)) return null;
            return "FCEmpireMercHealRateStatExplanation".Translate() + ": x" + factor.ToStringPercent();
        }

        private static bool TryGetMercFactor(StatRequest req, out float factor)
        {
            factor = 1f;
            if (!req.HasThing || !(req.Thing is Pawn pawn)) return false;

            Mercenary merc = FactionCache.FactionComp?.militaryCustomizationUtil?.GetRegisteredInjuredMerc(pawn);
            if (merc is null) return false;

            // On-map mercs follow vanilla healing — we only boost while abstracted at base.
            if (pawn.Map != null) return false;

            WorldSettlementFC settlement = merc.settlement ?? merc.squad?.getSettlement;
            FactionFC faction = FactionCache.FactionComp;
            double settlementMult = (faction is object && settlement is object)
                ? faction.GetStatValue(FCStatDefOf.mercHealRateMultiplier, settlement)
                : 1.0;

            factor = RebaselineFactor * FCSettings.mercenaryHealRatePerHour * (float)settlementMult;
            return true;
        }
    }
}
