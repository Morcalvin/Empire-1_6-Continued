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

        /// <summary>Creates the force a specific squad would project. Currently a thin wrapper
        /// over <see cref="CreateMilitaryForceFromSettlement"/> using <paramref name="squad"/>'s
        /// billet — squads inherit their settlement's military level and combat efficiency.
        /// Submods can replace this with a per-squad implementation later.</summary>
        public static MilitaryForce CreateMilitaryForceFromSquad(MercenarySquadFC squad, bool isAttacking = false, MilitaryForce homeDefendingForce = null)
        {
            if (squad?.settlement is null) return null;
            return CreateMilitaryForceFromSettlement(squad.settlement, isAttacking, homeDefendingForce);
        }

        public static MilitaryForce CreateMilitaryForceFromSettlement(WorldSettlementFC settlement, bool isAttacking = false, MilitaryForce homeDefendingForce = null)
        {
            FactionFC faction = FactionCache.FactionComp;

            double reinforcerLevel = settlement.settlementMilitaryLevel;
            double reinforcerEff = settlement.GetStatValue(FCStatDefOf.militaryCombatEfficiency);

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

            return new MilitaryForce(combinedLevel, blendedEff, settlement, FactionCache.PlayerColonyFaction);
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

        public static MilitaryForce CreateMilitaryForceFromFaction(Faction faction, bool handicap)
        {
            double militaryLevel = 1;
            double efficiency = 1;
            if (faction != null && faction.def != null)
            {
                GetMilitaryLevelAndEfficiencyFromTechLevel(faction.def.techLevel, out militaryLevel, out efficiency);

                if (faction.def.defName == "Insect")
                {
                    militaryLevel = 4;
                    efficiency = 1.2;
                }
            }

            double value = militaryLevel + MilitaryUtil.RandomAttackModifier();
            value = Math.Max(value, 1);

            FactionFC factionComp = FactionCache.FactionComp;

            // Apply Empire Threat Level scaling
            value *= ThreatScalingUtil.ComputeEmpireThreatLevel(factionComp);

            // Apply storyteller-curve adaptation
            if (factionComp.threatAdaptation != null)
            {
                value *= factionComp.threatAdaptation.ThreatFactor;
            }

            if (handicap)
            {
                value = Math.Min(value, ThreatScalingUtil.ComputeHandicapCap(factionComp));
            }

            MilitaryForce returnForce = new MilitaryForce(value, efficiency, null, faction);
            return returnForce;
        }
    }
}