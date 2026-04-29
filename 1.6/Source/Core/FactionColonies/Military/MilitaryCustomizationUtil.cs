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

            try { ValidateDeployedSquadOutfits(); }
            catch (Exception ex) { LogUtil.Error($"Error in squad reconciliation: {ex}"); }
        }

        /// <summary>
        /// Validates that all unit references in squad templates are still valid.
        /// Replaces invalid refs with blankUnit. Deployed squads are NOT re-outfitted —
        /// gear changes happen only when the player explicitly hires, fills, or upgrades
        /// (strict-manual outfit policy).
        /// </summary>
        public void ValidateTemplateUnits()
        {
            foreach (MilSquadFC squad in squads)
            {
                if (squad?.Units is null) continue;

                for (int count = 0; count < MilSquadFC.MaxSquadSize && count < squad.Units.Count; count++)
                {
                    if (squad.Units[count] != null &&
                        (units.Contains(squad.Units[count]) || squad.Units[count] == blankUnit)) continue;
                    squad.SetUnit(count, blankUnit);
                }
            }
        }

        /// <summary>
        /// Nulls the outfit reference for any deployed squad whose template has been deleted.
        /// Mercenaries and their gear are left untouched — the squad keeps whatever loadout
        /// it had at the moment the player last edited it. Per-merc <see cref="Mercenary.loadout"/>
        /// pool references stay valid; deletion of pool units snapshots into <c>ownedLoadout</c>
        /// via <see cref="DeleteUnit"/>.
        /// </summary>
        public void ValidateDeployedSquadOutfits()
        {
            foreach (MercenarySquadFC squad in mercenarySquads)
            {
                if (squad?.outfit is null) continue;
                if (!squads.Contains(squad.outfit)) squad.outfit = null;
            }
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
            foreach (MercenarySquadFC squad in mercenarySquads)
            {
                if (squad.IsPhysicallyDeployed()) continue;
                RegisterSquadInjuries(squad);
            }
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

        public IEnumerable<MercenarySquadFC> DeployedSquads =>
            mercenarySquads.Where(squad => squad.IsPhysicallyDeployed());

        public IEnumerable<Pawn> AllMercenaryPawns =>
            AllMercenaries.Select(merc => merc.pawn);

        public void ResetSquads()
        {
            squads = new List<MilSquadFC>();
        }

        /// <summary>Removes <paramref name="unit"/> from the units pool. For every merc that
        /// referenced it via <see cref="Mercenary.loadout"/>, snapshots the merc's
        /// <see cref="Mercenary.currentLoadout"/> (the equipped truth) into
        /// <see cref="Mercenary.ownedLoadout"/> as the divergence marker, then nulls the
        /// pool reference. Also replaces template references to the unit with
        /// <see cref="blankUnit"/>.</summary>
        public void DeleteUnit(MilUnitFC unit)
        {
            if (unit is null || unit == blankUnit) return;

            foreach (MercenarySquadFC squad in mercenarySquads)
            {
                if (squad?.mercenaries is null) continue;
                foreach (Mercenary m in squad.mercenaries)
                {
                    if (m is null || m.loadout != unit) continue;
                    if (m.ownedLoadout is null)
                    {
                        // Prefer the equipped-truth snapshot. Fall back to cloning the
                        // about-to-be-deleted pool unit if currentLoadout was never set
                        // (empty slot or unmigrated save).
                        m.ownedLoadout = (m.currentLoadout ?? unit).Clone();
                    }
                    m.loadout = null;
                }
            }

            foreach (MilSquadFC sq in squads)
            {
                if (sq?.Units is null) continue;
                for (int i = 0; i < sq.Units.Count; i++)
                {
                    if (sq.Units[i] == unit) sq.SetUnit(i, blankUnit);
                }
            }

            units.Remove(unit);
        }

        /// <summary>Removes <paramref name="template"/> from the templates pool. Clears
        /// <see cref="MercenarySquadFC.outfit"/> on every mercenary squad that referenced it —
        /// mercs and gear are left untouched (each merc still references its pool unit through
        /// <see cref="Mercenary.loadout"/>). No snapshot is needed; templates don't directly
        /// own gear.</summary>
        public void DeleteTemplate(MilSquadFC template)
        {
            if (template is null) return;
            foreach (MercenarySquadFC squad in mercenarySquads)
            {
                if (squad?.outfit == template) squad.outfit = null;
            }
            squads.Remove(template);
        }

        public void UpdateUnits()
        {
            foreach (MilUnitFC unit in units)
            {
                unit.UpdateEquipmentTotalCost();
            }
        }

        /// <summary>Builds float-menu options for hiring a squad from each available template.
        /// Click handler routes through <see cref="HireSquad"/> + <see cref="AttemptToAssign"/> so
        /// the player ends up with a hired squad attached to <paramref name="settlement"/>.</summary>
        public List<FloatMenuOption> BuildSquadAssignmentOptions(WorldSettlementFC settlement)
        {
            if (squads is null) ResetSquads();

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (MilSquadFC template in squads)
            {
                MilSquadFC captured = template;
                int hireCost = (int)Math.Round(captured.GetEquipmentTotalCost() * FCSettings.squadHireCostMultiplier);
                string label = captured.name + " - " + "FCCost".Translate() + ": " + hireCost;
                options.Add(new FloatMenuOption(label, delegate
                {
                    MercenarySquadFC hired = HireSquad(captured);
                    if (hired is object) AttemptToAssign(hired, settlement);
                }));
            }

            if (options.Count == 0)
                options.Add(new FloatMenuOption("FCNoSquadAvailable".Translate(), null));

            return options;
        }

        /// <summary>Pre-refactor entry point. Creates a fresh hired squad from the template and
        /// attempts to assign it to <paramref name="settlement"/>. Internally identical to
        /// <see cref="HireSquad"/> + <see cref="AttemptToAssign"/>.</summary>
        [System.Obsolete("Use HireSquad(template) + AttemptToAssign(squad, settlement). Will be removed in a follow-up.")]
        public void AttemptToAssignSquad(WorldSettlementFC settlement, MilSquadFC template)
        {
            if (settlement?.MilitaryComp is null)
            {
                LogUtil.Message($"Attempted to assign a squad to settlement {settlement?.Name ?? "null"} with NULL MilitaryComp");
                return;
            }
            MercenarySquadFC hired = HireSquad(template);
            if (hired is object) AttemptToAssign(hired, settlement);
        }

        /// <summary>Hires a fresh squad from <paramref name="template"/>: pays the hire cost,
        /// creates a <see cref="MercenarySquadFC"/> in the unassigned pool (settlement = null),
        /// and outfits it from the template. Returns null if the player can't afford the cost.
        /// </summary>
        public MercenarySquadFC HireSquad(MilSquadFC template)
        {
            if (template is null) return null;
            int cost = (int)Math.Round(template.GetEquipmentTotalCost() * FCSettings.squadHireCostMultiplier);
            if (cost > 0 && PaymentUtil.GetSilver() < cost)
            {
                Messages.Message("FCSquadHireInsufficientSilver".Translate(cost), MessageTypeDefOf.RejectInput, false);
                return null;
            }
            if (cost > 0) PaymentUtil.PaySilver(cost, PaymentUtil.Reason_SquadHire, null);

            MercenarySquadFC squad = MilTemplateFactory.CreateMercSquad();
            squad.outfit = template;
            squad.hireCostPaid = cost;
            squad.hiredAtTick = Find.TickManager.TicksGame;
            template.hiresEverMade++;
            squad.name = template.name + " #" + template.hiresEverMade;
            squad.InitiateSquad();
            mercenarySquads.Add(squad);

            RebuildMercenaryPawnSet();
            LifecycleRegistry.InvokeOnSquadHired(squad);
            Messages.Message("FCSquadHired".Translate(squad.name, cost), MessageTypeDefOf.PositiveEvent);
            return squad;
        }

        /// <summary>Dismisses <paramref name="squad"/>: refunds <see cref="FCSettings.squadDismissalRefundFraction"/>
        /// of <see cref="MercenarySquadFC.hireCostPaid"/>, removes it from <see cref="mercenarySquads"/>,
        /// and fires <see cref="LifecycleRegistry.InvokeOnSquadDismissed"/>. No-op when busy.</summary>
        public bool DismissSquad(MercenarySquadFC squad)
        {
            if (squad is null) return false;
            if (squad.IsBusy)
            {
                Messages.Message("FCCannotDismissBusySquad".Translate(squad.name), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            int refund = (int)Math.Round(squad.hireCostPaid * FCSettings.squadDismissalRefundFraction);
            if (refund > 0)
            {
                PaymentUtil.RefundSilver(refund, PaymentUtil.Reason_SquadDismissalRefund, squad.settlement);
            }
            // Detach from billet so StationedSquads queries see it gone immediately.
            squad.settlement = null;
            squad.autoDefend = false;
            mercenarySquads.Remove(squad);
            RebuildMercenaryPawnSet();
            LifecycleRegistry.InvokeOnSquadDismissed(squad);
            Messages.Message("FCSquadDismissed".Translate(squad.name, refund), MessageTypeDefOf.NeutralEvent);
            return true;
        }

        /// <summary>Assigns <paramref name="squad"/> to <paramref name="settlement"/>'s billet
        /// (target settlement). Runs <see cref="SquadAssignmentRegistry"/> validators (cap, size,
        /// submods) before mutating. No-op when the squad is busy.</summary>
        public bool AttemptToAssign(MercenarySquadFC squad, WorldSettlementFC settlement)
        {
            if (squad is null || settlement is null) return false;
            if (settlement.MilitaryComp is null)
            {
                LogUtil.Warning($"AttemptToAssign: settlement {settlement.Name} has no MilitaryComp.");
                return false;
            }
            if (squad.IsBusy)
            {
                Messages.Message("FCCannotReassignBusySquad".Translate(squad.name), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (!SquadAssignmentRegistry.CanAssign(settlement, squad, out string reason))
            {
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            squad.settlement = settlement;
            Messages.Message("FCSquadAssigned".Translate(squad.name, settlement.Name), MessageTypeDefOf.PositiveEvent);
            return true;
        }

        /// <summary>Removes <paramref name="squad"/>'s billet (returns it to the unassigned pool).
        /// No-op when busy.</summary>
        public bool Unassign(MercenarySquadFC squad)
        {
            if (squad is null) return false;
            if (squad.IsBusy)
            {
                Messages.Message("FCCannotUnassignBusySquad".Translate(squad.name), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            squad.settlement = null;
            return true;
        }

        /// <summary>Internal squad creation factory. Used by HireSquad (settlement: null) and by
        /// CallinExtraForces (settlement: caller, isExtra: true). Does NOT pay any silver — paying
        /// is the caller's responsibility.</summary>
        public MercenarySquadFC CreateMercenarySquad(WorldSettlementFC settlement, bool isExtra = false)
        {
            MercenarySquadFC squad = MilTemplateFactory.CreateMercSquad();
            squad.InitiateSquad();
            mercenarySquads.Add(squad);
            squad.settlement = settlement;
            squad.isExtraSquad = isExtra;

            RebuildMercenaryPawnSet();
            return FindSquad(squad);
        }

        public MercenarySquadFC FindSquad(MercenarySquadFC squad)
        {
            return mercenarySquads.FirstOrDefault(mercSquad => squad == mercSquad);
        }

        public bool SquadExists(WorldSettlementFC settlement)
        {
            return settlement?.PrimaryStationedSquad != null;
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

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RebuildMercenaryPawnSet();
            }
        }
    }
}