using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace FactionColonies
{
    //Mil customization class
    public class MilitaryCustomizationUtil : IExposable
    {
        public List<MilUnitFC> units = new List<MilUnitFC>();
        public List<MilSquadFC> squads = new List<MilSquadFC>();

        public List<MercenarySquadFC> mercenarySquads = new List<MercenarySquadFC>();
        public List<MilitaryFireSupport> fireSupport = new List<MilitaryFireSupport>();
        public List<MilitaryFireSupport> fireSupportDefs = new List<MilitaryFireSupport>();
        public MilUnitFC blankUnit;
        public List<Mercenary> deadPawns = new List<Mercenary>();
        public int tickChanged;

        private HashSet<Pawn> mercenaryPawnSet = new HashSet<Pawn>();

        public bool IsMercenaryPawn(Pawn pawn) => mercenaryPawnSet.Contains(pawn);

        public void RebuildMercenaryPawnSet()
        {
            mercenaryPawnSet.Clear();
            foreach (MercenarySquadFC squad in mercenarySquads)
            {
                foreach (Mercenary merc in squad.mercenaries)
                {
                    if (merc?.pawn != null)
                        mercenaryPawnSet.Add(merc.pawn);
                }
                if (squad.animals != null)
                {
                    foreach (Mercenary animal in squad.animals)
                    {
                        if (animal?.pawn != null)
                            mercenaryPawnSet.Add(animal.pawn);
                    }
                }
            }
        }

        public MilitaryCustomizationUtil()
        {
            //set load stuff here
            if (units == null)
            {
                units = new List<MilUnitFC>();
            }

            if (squads == null)
            {
                squads = new List<MilSquadFC>();
            }

            if (blankUnit == null)
            {
                //blankUnit = new MilUnitFC(true);
            }

            if (mercenarySquads == null)
            {
                mercenarySquads = new List<MercenarySquadFC>();
            }

            if (deadPawns == null)
            {
                deadPawns = new List<Mercenary>();
            }

            if (fireSupportDefs == null)
            {
                fireSupportDefs = new List<MilitaryFireSupport>();
            }
        }

        public void CheckMilitaryUtilForErrors()
        {
            if (blankUnit is null)
                blankUnit = MilTemplateFactory.CreateUnit(true);
            if (squads is null) return;

            try { ValidateTemplateUnits(); }
            catch (Exception ex) { LogUtil.Error($"Error in ValidateTemplateUnits: {ex}"); }

            try
            {
                ValidateDeployedSquadOutfits();
                PropagateTemplateChanges();
            }
            catch (Exception ex) { LogUtil.Error($"Error in squad reconciliation: {ex}"); }
        }

        /// <summary>
        /// Validates that all unit references in squad templates are still valid.
        /// Replaces invalid refs with blankUnit and re-outfits affected deployed squads.
        /// </summary>
        public void ValidateTemplateUnits()
        {
            foreach (MilSquadFC squad in squads)
            {
                if (squad?.Units is null) continue;

                bool changed = false;
                for (int count = 0; count < MilSquadFC.MaxSquadSize && count < squad.Units.Count; count++)
                {
                    if (squad.Units[count] != null &&
                        (units.Contains(squad.Units[count]) || squad.Units[count] == blankUnit)) continue;
                    squad.SetUnit(count, blankUnit);
                    changed = true;
                }

                if (!changed) continue;
                foreach (var squadMerc in mercenarySquads.Where(squadMerc =>
                    squadMerc.outfit != null && squadMerc.outfit == squad))
                {
                    squadMerc.OutfitSquad(squad);
                }
            }
        }

        /// <summary>
        /// Strips deployed squads whose outfit template was deleted or exceeds the settlement budget.
        /// </summary>
        public void ValidateDeployedSquadOutfits()
        {
            foreach (MercenarySquadFC squad in mercenarySquads)
            {
                if (squad.outfit is null || !squads.Contains(squad.outfit))
                {
                    squad.StripSquad();
                    squad.outfit = null;
                }
                else
                {
                    int settlementMilLevel = 0;
                    if (squad.settlement != null)
                        settlementMilLevel = squad.settlement.settlementMilitaryLevel;
                    if (squad.outfit is null || !(squad.outfit.GetEquipmentTotalCost() >
                                                  CalculateSquadBudget(settlementMilLevel)))
                        continue;
                    if (squad.settlement != null)
                    {
                        Messages.Message(
                            "The max allowed equipment cost for the squad assigned to " + squad.settlement.Name +
                            " has been exceeded. Thus, the settlement's squad has been unassigned.",
                            MessageTypeDefOf.RejectInput);
                    }

                    squad.outfit = null;
                    squad.StripSquad();
                }
            }
        }

        /// <summary>
        /// Re-outfits all deployed squads if any template has changed since the last check.
        /// </summary>
        public void PropagateTemplateChanges()
        {
            if (tickChanged >= GETLatestChange) return;
            foreach (var merc in mercenarySquads.Where(merc => merc.outfit != null))
            {
                merc.OutfitSquad(merc.outfit);
            }

            ChangeTick();
            RebuildMercenaryPawnSet();
        }

        public int GETLatestChange
        {
            get { return squads.Select(squadFC => squadFC.getLatestChanged).Prepend(0).Max(); }
        }

        public static double CalculateSquadBudget(int militaryLevel)
        {
            return 1000 + (500.0 * militaryLevel) + (600.0 * militaryLevel * militaryLevel);
        }

        public static double CalculateFireSupportBudget(int militaryLevel)
        {
            return 500 + (500.0 * militaryLevel * militaryLevel);
        }

        // --- Mercenary Healing ---

        private HashSet<Mercenary> injuredMercs;

        /// <summary>
        /// Gradually heal injuries on undeployed mercenary pawns.
        /// Only iterates the tracked injured set for performance.
        /// Per-settlement heal rate is determined by the mercHealRateMultiplier stat.
        /// </summary>
        public void TickMercenaryHealing(int interval)
        {
            FactionFC faction = FactionCache.FactionComp;
            if (faction is null) return;
            
            if (injuredMercs is null) RebuildInjuredMercs();
            if ((injuredMercs?.Count ?? 0) == 0) return;

            float baseHealAmount = FCSettings.mercenaryHealRatePerHour * ((float)interval / (float)GenDate.TicksPerHour);
            if (baseHealAmount <= 0f) return;

            Dictionary<WorldSettlementFC, float> healCache = null;

            List<Mercenary> toRemove = null;
            foreach (Mercenary merc in injuredMercs)
            {
                Pawn pawn = merc.pawn;
                if (pawn is null || pawn.Destroyed || pawn.Dead || pawn.Map != null)
                {
                    if (toRemove is null) toRemove = new List<Mercenary>();
                    toRemove.Add(merc);
                    continue;
                }

                float healAmount = GetSettlementHealAmount(merc, baseHealAmount, faction, ref healCache);
                HealMercenaryTick(pawn, healAmount);
                if (!HasInjuries(pawn))
                {
                    if (toRemove is null) toRemove = new List<Mercenary>();
                    toRemove.Add(merc);
                }
            }
            if (toRemove != null)
            {
                foreach (Mercenary m in toRemove) injuredMercs.Remove(m);
            }
        }

        private float GetSettlementHealAmount(Mercenary merc, float baseHealAmount, FactionFC faction,
            ref Dictionary<WorldSettlementFC, float> cache)
        {
            WorldSettlementFC settlement = merc.settlement ?? merc.squad?.getSettlement;
            if (faction is null || settlement is null) return baseHealAmount;

            if (cache is null) cache = new Dictionary<WorldSettlementFC, float>();
            if (cache.TryGetValue(settlement, out float cached)) return cached;

            double multiplier = faction.GetStatValue(FCStatDefOf.mercHealRateMultiplier, settlement);
            float result = baseHealAmount * (float)multiplier;
            cache[settlement] = result;
            return result;
        }

        /// <summary>
        /// Full scan of all undeployed squads to populate the injured mercs set.
        /// Called lazily on first tick or after load.
        /// </summary>
        private void RebuildInjuredMercs()
        {
            injuredMercs = new HashSet<Mercenary>();
#pragma warning disable 0618 // legacy isDeployed property — equivalent to "squad pawns currently on a player map"
            foreach (MercenarySquadFC squad in mercenarySquads)
            {
                if (squad.isDeployed) continue;
                RegisterSquadInjuries(squad);
            }
#pragma warning restore 0618
        }

        /// <summary>
        /// Register injuries for a single squad's mercs after recall from deployment.
        /// </summary>
        public void RegisterSquadInjuries(MercenarySquadFC squad)
        {
            if (injuredMercs is null) injuredMercs = new HashSet<Mercenary>();
            if (squad.mercenaries is null) return;
            foreach (Mercenary merc in squad.mercenaries)
            {
                if (merc?.pawn is null || merc.pawn.Dead || merc.pawn.Map != null) continue;
                if (HasInjuries(merc.pawn))
                    injuredMercs.Add(merc);
            }
        }

        private static bool HasInjuries(Pawn pawn)
        {
            List<Hediff> hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null) return false;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is Hediff_Injury injury && !injury.IsPermanent()) return true;
            }
            return false;
        }

        private static void HealMercenaryTick(Pawn pawn, float healAmount)
        {
            List<Hediff> hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null) return;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                if (hediffs[i] is Hediff_Injury injury && !injury.IsPermanent())
                {
                    injury.Heal(healAmount);
                    // Only heal one injury at a time
                    break;
                }
            }
        }

        public MercenarySquadFC ReturnSquadFromUnit(Pawn unit)
        {
            foreach (var squad in mercenarySquads)
            {
                foreach (var merc in squad.mercenaries)
                {
                    if (merc?.pawn?.Map != null && merc.pawn == unit)
                        return squad;
                }
                if (squad.animals != null)
                {
                    foreach (var animal in squad.animals)
                    {
                        if (animal?.pawn?.Map != null && animal.pawn == unit)
                            return squad;
                    }
                }
            }

            LogUtil.Message("MercenarySquadFC - ReturnSquadFromUnit - Did not find squad.");
            return null;
        }

        public Mercenary ReturnMercenaryFromUnit(Pawn unit, MercenarySquadFC squad)
        {
            return squad.mercenaries.FirstOrDefault(merc => merc.pawn == unit);
        }

        public IEnumerable<Mercenary> AllMercenaries =>
            mercenarySquads.SelectMany(squad =>
                squad.animals?.Count > 0
                    ? squad.mercenaries.Concat(squad.animals)
                    : squad.mercenaries);

#pragma warning disable 0618 // legacy isDeployed property — preserves original "Deploy op + Engaged" filter semantics
        public IEnumerable<MercenarySquadFC> DeployedSquads =>
            mercenarySquads.Where(squad => squad.isDeployed);
#pragma warning restore 0618

        public IEnumerable<Pawn> AllMercenaryPawns =>
            AllMercenaries.Select(merc => merc.pawn);

        public void ResetSquads()
        {
            squads = new List<MilSquadFC>();
        }

        public void UpdateUnits()
        {
            foreach (MilUnitFC unit in units)
            {
                unit.UpdateEquipmentTotalCost();
            }
        }

        public List<FloatMenuOption> BuildSquadAssignmentOptions(WorldSettlementFC settlement)
        {
            if (squads is null) ResetSquads();

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (MilSquadFC squad in squads)
            {
                MilSquadFC captured = squad;
                options.Add(new FloatMenuOption(
                    squad.name + " - " + "FCCost".Translate() + ": " + squad.GetEquipmentTotalCost(),
                    delegate { AttemptToAssignSquad(settlement, captured); }));
            }

            if (options.Count == 0)
                options.Add(new FloatMenuOption("FCNoSquadAvailable".Translate(), null));

            return options;
        }

        public void AttemptToAssignSquad(WorldSettlementFC settlement, MilSquadFC squad)
        {
            if (settlement.MilitaryComp == null)
            {
                LogUtil.Message($"Attempted to assign a squad to settlement {settlement.Name} with NULL MilitaryComp");
                return;
            }
            if (!SquadAssignmentRegistry.CanAssign(settlement, squad, out string rejectReason))
            {
                Messages.Message(rejectReason, MessageTypeDefOf.RejectInput);
                return;
            }
            if (CalculateSquadBudget(settlement.settlementMilitaryLevel) >=
                squad.GetEquipmentTotalCost())
            {
                if (SquadExists(settlement))
                {
                    settlement.MilitaryComp.militarySquad.OutfitSquad(squad);
                }
                else
                {
                    //create new squad
                    CreateMercenarySquad(settlement);
                    settlement.MilitaryComp.militarySquad.OutfitSquad(squad);
                }

                Messages.Message(squad.name + "'s loadout has been assigned to " + settlement.Name,
                    MessageTypeDefOf.TaskCompletion);
            }
            else
            {
                Messages.Message("FCSquadExceedsMaxCost".Translate(), MessageTypeDefOf.RejectInput);
            }
        }

        public MercenarySquadFC CreateMercenarySquad(WorldSettlementFC settlement, bool isExtra = false)
        {
            if (settlement.MilitaryComp == null)
            {
                LogUtil.Warning($"Attempted to create a mercenary squad for settlement {settlement.Name} with NULL MilitaryComp. Skipping");
                return null;
            }
            MercenarySquadFC squad = MilTemplateFactory.CreateMercSquad();
            squad.InitiateSquad();
            mercenarySquads.Add(squad);
            if (!isExtra)
                settlement.MilitaryComp.militarySquad = FindSquad(squad);
            squad.settlement = settlement;
            squad.isExtraSquad = isExtra;

            if (settlement.MilitaryComp.militarySquad == null)
            {
                LogUtil.Warning("CreateMercenarySquad fail. Found squad is Null");
            }

            RebuildMercenaryPawnSet();
            return FindSquad(squad);
        }

        public MercenarySquadFC FindSquad(MercenarySquadFC squad)
        {
            return mercenarySquads.FirstOrDefault(mercSquad => squad == mercSquad);
        }

        public bool SquadExists(WorldSettlementFC settlement)
        {
            return settlement.MilitaryComp?.militarySquad != null;
        }

        public void ChangeTick()
        {
            tickChanged = Find.TickManager.TicksGame;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref units, "units", LookMode.Deep);
            Scribe_Collections.Look(ref squads, "squads", LookMode.Deep);
            Scribe_Collections.Look(ref mercenarySquads, "mercenarySquads", LookMode.Deep);
            Scribe_Collections.Look(ref fireSupport, "fireSupport", LookMode.Deep);
            Scribe_Collections.Look(ref fireSupportDefs, "fireSupportDefs", LookMode.Deep);
            Scribe_Collections.Look(ref deadPawns, "deadPawns", LookMode.Deep);

            Scribe_Deep.Look(ref blankUnit, "blankUnit");
            Scribe_Values.Look(ref tickChanged, "tickChanged");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RebuildMercenaryPawnSet();
            }
        }
    }
}