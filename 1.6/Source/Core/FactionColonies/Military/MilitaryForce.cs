using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FactionColonies
{
    public class MilitaryForce : IExposable
    {
        public double militaryLevel;
        public double militaryEfficiency;
        public double forceRemaining;
        public int random;
        public WorldSettlementFC homeSettlement;
        public Faction homeFaction;

        /// <summary>forceRemaining with defender advantage applied (for display).</summary>
        public double DefensivePower => Math.Round(forceRemaining * FCSettings.defenderAdvantage);

        public void ExposeData()
        {
            Scribe_Values.Look(ref militaryLevel, "militaryLevel");
            Scribe_Values.Look(ref militaryEfficiency, "militaryEfficiency");
            Scribe_Values.Look(ref forceRemaining, "forceRemaining");
            Scribe_Values.Look(ref random, "random");
            Scribe_References.Look(ref homeSettlement, "homeSettlement");
            Scribe_References.Look(ref homeFaction, "homeFaction");
        }

        public MilitaryForce()
        {
        }

        public MilitaryForce(double militaryLevel, double militaryEfficiency, WorldSettlementFC homeSettlement, Faction homeFaction)
        {
            this.militaryLevel = militaryLevel;
            this.militaryEfficiency = militaryEfficiency;
            this.homeSettlement = homeSettlement;
            this.homeFaction = homeFaction;
            forceRemaining = Math.Max(1, Math.Round(militaryLevel * militaryEfficiency));
        }

        /// <summary>Creates the force that this <paramref name="squad"/> projects. Reads the squad's
        /// power via <see cref="SquadPowerRegistry"/> (loadout-cost-derived militaryLevel +
        /// combat efficiency from the billet), then applies faction-level isAttacking /
        /// isDefending bonuses and the optional <paramref name="homeDefendingForce"/> blend.
        /// Returns null if the squad is unassigned (no billet to anchor the force).</summary>
        public static MilitaryForce CreateMilitaryForceFromSquad(MercenarySquadFC squad, bool isAttacking = false, MilitaryForce homeDefendingForce = null)
        {
            if (squad?.settlement is null) return null;

            SquadPower power = SquadPowerRegistry.Resolve(squad);
            return CombineForce(power.militaryLevel, power.militaryEfficiency, squad.settlement, isAttacking, homeDefendingForce);
        }

        /// <summary>Half-power synthetic force for a settlement with squad capacity but no
        /// stationed squad. Lets empty billets still participate in the squad-driven battle
        /// pipeline at a reduced effectiveness, while <see cref="WorldSettlementFC.SquadCap"/>
        /// of 0 (structurally non-military) yields null. Power is the squad-equivalent of the
        /// settlement's military level — half of what a fully-kitted squad at that level would
        /// project.</summary>
        public static MilitaryForce CreateMilitaryForceFromUnstaffedBillet(WorldSettlementFC settlement, bool isAttacking = false, MilitaryForce homeDefendingForce = null)
        {
            if (settlement is null) return null;
            if (settlement.SquadCap <= 0) return null;

            double level = Math.Max(1, settlement.settlementMilitaryLevel) * 0.5;
            double efficiency = 1.0;
            FactionFC faction = FactionCache.FactionComp;
            if (faction is object)
            {
                efficiency = faction.GetStatValue(FCStatDefOf.militaryCombatEfficiency, settlement);
            }
            return CombineForce(level, efficiency, settlement, isAttacking, homeDefendingForce);
        }

        public static MilitaryForce CreateMilitaryForceFromSettlement(WorldSettlementFC settlement, bool isAttacking = false, MilitaryForce homeDefendingForce = null)
        {
            double reinforcerLevel = settlement.settlementMilitaryLevel;
            double reinforcerEff = settlement.GetStatValue(FCStatDefOf.militaryCombatEfficiency);
            return CombineForce(reinforcerLevel, reinforcerEff, settlement, isAttacking, homeDefendingForce);
        }

        /* Shared force assembly: blend reinforcer level/efficiency with the optional
         * home-defending force, then apply the faction's attacking/defending stat
         * bonuses. Used by every public force factory so the bonus chain stays in one
         * place. */
        private static MilitaryForce CombineForce(double reinforcerLevel, double reinforcerEff,
            WorldSettlementFC anchorSettlement, bool isAttacking, MilitaryForce homeDefendingForce)
        {
            FactionFC faction = FactionCache.FactionComp;

            double combinedLevel = reinforcerLevel;
            double blendedEff = reinforcerEff;

            if (homeDefendingForce != null && homeDefendingForce.homeSettlement != null)
            {
                double homeLevel = homeDefendingForce.homeSettlement.settlementMilitaryLevel;
                double homeEff = homeDefendingForce.homeSettlement.GetStatValue(FCStatDefOf.militaryCombatEfficiency);
                combinedLevel = reinforcerLevel + homeLevel;
                if (combinedLevel > 0)
                {
                    blendedEff = (reinforcerLevel * reinforcerEff + homeLevel * homeEff) / combinedLevel;
                }
            }
            else if (homeDefendingForce != null)
            {
                combinedLevel += homeDefendingForce.militaryLevel;
            }

            if (faction is object)
            {
                if (isAttacking)
                {
                    combinedLevel += faction.GetStatValue(FCStatDefOf.militaryLevelBonusAttacking);
                    blendedEff *= faction.GetStatValue(FCStatDefOf.militaryEfficiencyBonusAttacking);
                }
                else
                {
                    combinedLevel += faction.GetStatValue(FCStatDefOf.militaryLevelBonusDefending);
                    blendedEff *= faction.GetStatValue(FCStatDefOf.militaryEfficiencyBonusDefending);
                }
            }

            return new MilitaryForce(combinedLevel, blendedEff, anchorSettlement, FactionCache.PlayerColonyFaction);
        }

        public static void GetMilitaryLevelAndEfficiencyFromTechLevel(TechLevel techlevel, out double militaryLevel, out double efficiency)
        {
            switch (techlevel)
            {
                case TechLevel.Undefined:
                    militaryLevel = 1;
                    efficiency = .5;
                    break;
                case TechLevel.Animal:
                    militaryLevel = 1;
                    efficiency = .5;
                    break;
                case TechLevel.Neolithic:
                    militaryLevel = 2;
                    efficiency = .9;
                    break;
                case TechLevel.Medieval:
                    militaryLevel = 3;
                    efficiency = 1;
                    break;
                case TechLevel.Industrial:
                    militaryLevel = 5;
                    efficiency = 1.1;
                    break;
                case TechLevel.Spacer:
                    militaryLevel = 6;
                    efficiency = 1.2;
                    break;
                case TechLevel.Ultra:
                    militaryLevel = 7;
                    efficiency = 1.3;
                    break;
                case TechLevel.Archotech:
                    militaryLevel = 9;
                    efficiency = 1.5;
                    break;
                default:
                    militaryLevel = 1;
                    efficiency = 1;
                    LogUtil.Message("Defaulted GetMilitaryLevelAndEfficiencyFromTechLevel switch case");
                    break;
            }
        }

        public static MilitaryForce CreateMilitaryForceFromEnemySettlement(Settlement settlement)
        {
            double militaryLevel = 1;
            double efficiency = 1;

            if (settlement?.Faction?.def != null)
            {
                GetMilitaryLevelAndEfficiencyFromTechLevel(settlement.Faction.def.techLevel, out militaryLevel, out efficiency);
            }

            MilitaryForce returnForce = new MilitaryForce(militaryLevel, efficiency, null, settlement?.Faction);
            return returnForce;
        }

        /// <summary>
        /// Build a synthetic faction-derived <see cref="MilitaryForce"/>. Used as a fallback
        /// (e.g. AI attacks against the player, or when no enemy <see cref="Settlement"/> is
        /// associated with the operation). Settlement-aware paths should go through
        /// <see cref="WorldComponent_EnemySettlementPower"/> instead so the displayed estimate
        /// and the actual battle force agree.
        /// </summary>
        public static MilitaryForce CreateMilitaryForceFromFaction(Faction faction, bool handicap)
        {
            double level, efficiency;
            MilitaryUtil.ComputeFactionBaselinePower(faction, FactionCache.FactionComp,
                out level, out efficiency);

            double value = Math.Max(1, level
                + MilitaryUtil.RollVarianceOffset(MilitaryUtil.DefaultLevelVariance));

            if (handicap)
            {
                value = Math.Min(value, ThreatScalingUtil.ComputeHandicapCap(FactionCache.FactionComp));
            }

            return new MilitaryForce(value, efficiency, null, faction);
        }
    }
}