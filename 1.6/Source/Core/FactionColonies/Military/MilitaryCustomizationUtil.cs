using RimWorld;
using RimWorld.Planet;
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

        // Pawn -> Mercenary index of off-map mercs with active injuries. Single source of truth;
        // keyed by pawn so StatPart_EmpireMercHealRate can resolve in O(1) during stat queries.
        private Dictionary<Pawn, Mercenary> injuredMercsByPawn;

        /// <summary>
        /// Gradually heal injuries on undeployed mercenary pawns by delegating to vanilla
        /// <see cref="Pawn_HealthTracker.HealthTickInterval"/>. Heal rate is multiplied via
        /// <see cref="StatPart_EmpireMercHealRate"/> on the InjuryHealingFactor stat, which
        /// composes the user slider and per-settlement mercHealRateMultiplier into the boost.
        /// Auto-tending runs first so wounds heal at the tended rate when applicable.
        /// </summary>
        public void TickMercenaryHealing(int interval)
        {
            if (injuredMercsByPawn is null) RebuildInjuredMercs();
            if (injuredMercsByPawn.Count == 0) return;

            List<Pawn> toRemove = null;
            foreach (KeyValuePair<Pawn, Mercenary> kvp in injuredMercsByPawn)
            {
                Pawn pawn = kvp.Key;
                Mercenary merc = kvp.Value;

                // Permanent removal — pawn is gone, or merc no longer holds this pawn (was reassigned).
                if (pawn is null || pawn.Destroyed || pawn.Dead || merc?.pawn != pawn)
                {
                    if (toRemove is null) toRemove = new List<Pawn>();
                    toRemove.Add(pawn);
                    continue;
                }

                // On-map (currently deployed) — skip this tick but stay tracked so healing
                // resumes automatically once the pawn returns to base.
                if (pawn.Map != null) continue;

                WorldSettlementFC settlement = merc.settlement ?? merc.squad?.getSettlement;
                try
                {
                    // Vanilla HealthTickInterval gates its heal branch on !food.Starving. Off-map
                    // mercs don't tick their needs, so the food need is frozen at whatever value
                    // it had at despawn — possibly Starving. Top it up; mercs at base are
                    // abstracted as eating in the mess hall.
                    Need_Food food = pawn.needs?.food;
                    if (food != null) food.CurLevel = food.MaxLevel;

                    if (pawn.health.HasHediffsNeedingTend())
                        MercTendingUtil.TendOnce(merc, settlement);

                    pawn.health.HealthTickInterval(interval);
                }
                catch (Exception e)
                {
                    LogUtil.Error($"Exception in mercenary heal tick for {pawn.LabelShortCap}: {e}");
                }

                // Vanilla cleanup pruned dead hediffs during the tick; if no injuries remain, drop tracking.
                if (pawn.Dead || pawn.Destroyed || !HasInjuries(pawn))
                {
                    if (toRemove is null) toRemove = new List<Pawn>();
                    toRemove.Add(pawn);
                }
            }
            if (toRemove != null)
            {
                foreach (Pawn p in toRemove) injuredMercsByPawn.Remove(p);
            }
        }

        /// <summary>
        /// Full scan of all undeployed squads to populate the injured mercs index.
        /// Called lazily on first tick or after load.
        /// </summary>
        private void RebuildInjuredMercs()
        {
            injuredMercsByPawn = new Dictionary<Pawn, Mercenary>();
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
            if (injuredMercsByPawn is null) injuredMercsByPawn = new Dictionary<Pawn, Mercenary>();
            if (squad.mercenaries is null) return;
            // Register regardless of current spawn state. TickMercenaryHealing decides whether
            // to actually heal each tick, so on-map pawns stay tracked and resume healing on
            // next despawn without needing a manual re-register.
            foreach (Mercenary merc in squad.mercenaries)
            {
                if (merc?.pawn is null || merc.pawn.Dead || merc.pawn.Destroyed) continue;
                if (HasInjuries(merc.pawn))
                    injuredMercsByPawn[merc.pawn] = merc;
            }
        }

        /// <summary>
        /// Returns the registered <see cref="Mercenary"/> for <paramref name="pawn"/>, or null
        /// if the pawn isn't currently tracked for healing. Used by
        /// <see cref="StatPart_EmpireMercHealRate"/> to decide whether to apply the boost.
        /// </summary>
        public Mercenary GetRegisteredInjuredMerc(Pawn pawn)
        {
            if (pawn is null || injuredMercsByPawn is null) return null;
            return injuredMercsByPawn.TryGetValue(pawn, out Mercenary merc) ? merc : null;
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

        /// <summary>Squads currently participating in an Engaged Deploy op. Distinct from
        /// <see cref="DeployedSquads"/>: that one walks pawn-on-map state (false during the
        /// drop-pod fall window before pods open). This one walks the manager's op state, so
        /// the squad is included from the moment <c>CreateDeployOp</c> registers it through
        /// final lord-cleanup. Use this for UI that should remain coherent across pod fall.</summary>
        public IEnumerable<MercenarySquadFC> SquadsInDeployOp
        {
            get
            {
                if (FactionCache.MilitaryManager is null) yield break;
                foreach (MercenarySquadFC squad in mercenarySquads)
                {
                    MilitaryOperation op = squad?.Operation;
                    if (op is object
                        && op.kind == MilitaryJobDefOf.Deploy
                        && op.phase == MilitaryOperationPhase.Engaged)
                    {
                        yield return squad;
                    }
                }
            }
        }

        /// <summary>True if <paramref name="squad"/> is currently in an Engaged Deploy op.</summary>
        public static bool IsInDeployOp(MercenarySquadFC squad)
        {
            if (squad is null) return false;
            MilitaryOperation op = squad.Operation;
            return op is object
                && op.kind == MilitaryJobDefOf.Deploy
                && op.phase == MilitaryOperationPhase.Engaged;
        }

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
            squad.hiredAtTick = Find.TickManager.TicksGame;
            template.hiresEverMade++;
            squad.SetName(template.name + " #" + template.hiresEverMade);
            squad.InitiateSquad();
            mercenarySquads.Add(squad);

            RebuildMercenaryPawnSet();
            LifecycleRegistry.InvokeOnSquadHired(squad);
            Messages.Message("FCSquadHired".Translate(squad.DisplayName, cost), MessageTypeDefOf.PositiveEvent);
            return squad;
        }

        /// <summary>Dismisses <paramref name="squad"/>: removes it from <see cref="mercenarySquads"/>
        /// and fires <see cref="LifecycleRegistry.InvokeOnSquadDismissed"/>. No silver is returned.
        /// No-op when busy.</summary>
        public bool DismissSquad(MercenarySquadFC squad)
        {
            if (squad is null) return false;
            if (squad.IsBusy)
            {
                Messages.Message("FCCannotDismissBusySquad".Translate(squad.DisplayName), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            // Detach from billet so StationedSquads queries see it gone immediately.
            squad.settlement = null;
            squad.autoDefend = false;
            mercenarySquads.Remove(squad);
            RebuildMercenaryPawnSet();
            LifecycleRegistry.InvokeOnSquadDismissed(squad);
            Messages.Message("FCSquadDismissed".Translate(squad.DisplayName), MessageTypeDefOf.NeutralEvent);
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
                Messages.Message("FCCannotReassignBusySquad".Translate(squad.DisplayName), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (!SquadAssignmentRegistry.CanAssign(settlement, squad, out string reason))
            {
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            Assign(squad, settlement, true);
            Messages.Message("FCSquadAssigned".Translate(squad.DisplayName, settlement.Name), MessageTypeDefOf.PositiveEvent);
            return true;
        }

        /// <summary>Unassigns <paramref name="displace"/> from its current settlement
        /// (only if it's currently at <paramref name="target"/>), then assigns
        /// <paramref name="incoming"/> to <paramref name="target"/>. Both squads must be non-busy.
        /// Used by the Change-button picker to displace a slot's existing occupant when the target
        /// is at squad cap. Returns true only if both legs succeed.</summary>
        public bool AttemptToSwap(MercenarySquadFC incoming, WorldSettlementFC target,
            MercenarySquadFC displace)
        {
            if (incoming is null || target is null) return false;
            if (incoming == displace) return true; // same squad in slot — no-op
            if (incoming.IsBusy)
            {
                Messages.Message("FCSquadSwapBusyReject".Translate(incoming.DisplayName), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (displace is object && displace.IsBusy)
            {
                Messages.Message("FCSquadSwapBusyReject".Translate(displace.DisplayName), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            // Detach the displaced squad first so the cap validator sees the slot as free.
            if (displace is object && displace.settlement == target)
            {
                Unassign(displace, true);
            }

            if (!AttemptToAssign(incoming, target))
            {
                // Best-effort restore: AttemptToAssign already logged its rejection message.
                if (displace is object) Assign(displace, target, true);
                return false;
            }
            return true;
        }

        /// <summary>Removes <paramref name="squad"/>'s billet (returns it to the unassigned pool).
        /// No-op when busy.</summary>
        public bool Unassign(MercenarySquadFC squad, bool silent = false)
        {
            if (squad is null) return false;
            if (squad.IsBusy)
            {
                if (!silent)
                    Messages.Message("FCCannotUnassignBusySquad".Translate(squad.DisplayName), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            squad.settlement = null;
            return true;
        }
        /// <summary>
        /// Actually assigns the squad to the settlement. Meant to be handle any special on-assign processing
        /// that might be added in the future, but for now, it literally just sets the squad's settlement.
        /// </summary>
        /// <param name="squad"></param>
        /// <param name="settlement"></param>
        /// <param name="silent"></param>
        /// <returns></returns>
        public bool Assign(MercenarySquadFC squad, WorldSettlementFC settlement, bool silent = false)
        {
            if (squad is null || settlement is null) return false;

            squad.settlement = settlement;
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