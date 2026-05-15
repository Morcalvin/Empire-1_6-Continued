using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using FactionColonies.util;

namespace FactionColonies
{
    /// <summary>
    /// Mercenary pawn generation. Three-tier xenotype/race fallback for combatants
    /// and a simple animal generator. Handles biotech custom-xenotype lookups and
    /// security-guard auto-assignment for non-violent xenotypes.
    /// </summary>
    public static class MercenaryPawnFactory
    {
        /// <summary>Generates an animal pawn for the slot and writes squad/settlement back-refs
        /// onto <paramref name="merc"/>. Caller is responsible for adding the merc to the
        /// squad's <c>animals</c> list (or to a parent merc's <c>animal</c> field).</summary>
        public static void CreateNewAnimal(MercenarySquadFC squad, ref Mercenary merc, PawnKindDef race)
        {
            Pawn newPawn = PawnGenerator.GeneratePawn(FCPawnGenerator.AnimalRequest(race));

            merc.squad = squad;
            merc.settlement = squad?.settlement;
            merc.pawn = newPawn;
        }

        /// <summary>
        /// If the mercenary's xenotype is non-violent and has security guards configured,
        /// auto-assigns a random guard animal from the xenotype's SecurityGuardList.
        /// Currently unreferenced; kept here for resurrection rather than re-extraction.
        /// </summary>
        public static void TryAssignSecurityGuard(MercenarySquadFC squad, Mercenary merc)
        {
            if (squad is null || merc?.pawn?.genes == null) return;
            // Don't overwrite a manually-assigned animal
            if (merc.animal != null) return;

            FactionFC factionFc = FactionCache.FactionComp;
            if (factionFc?.xenotypeFilter == null) return;

            XenotypeFilter xenoFilter = factionFc.xenotypeFilter;
            List<PawnKindDef> guardOptions = null;

            XenotypeDef mercXenotype = merc.pawn.genes.Xenotype;
            if (mercXenotype != null && FactionCache.XenotypeIsNonViolent(mercXenotype))
            {
                guardOptions = xenoFilter.GetSecurityGuardsForXenotype(mercXenotype);
            }
            else if (merc.pawn.genes.CustomXenotype != null)
            {
                string customName = merc.pawn.genes.CustomXenotype.name;
                if (FactionCache.CustomXenotypeIsNonViolent(customName))
                {
                    guardOptions = xenoFilter.GetSecurityGuardsForCustomXenotype(customName);
                }
            }

            if (guardOptions != null && guardOptions.Any())
            {
                PawnKindDef guardKind = guardOptions.RandomElement();
                Mercenary guardAnimal = new Mercenary(true);
                CreateNewAnimal(squad, ref guardAnimal, guardKind);
                guardAnimal.handler = merc;
                merc.animal = guardAnimal;
                squad.animals.Add(guardAnimal);
            }
        }

        public static void CreateNewPawn(MercenarySquadFC squad, ref Mercenary merc, PawnKindDef race, XenotypeDef _xenotype, string _customXenotypeName = null)
        {
            XenotypeDef xenotypeChoice = _xenotype;
            PawnKindDef raceChoice = race;
            FactionFC factionFc = FactionCache.FactionComp;

            if (race == null || factionFc.xenotypeFilter.GetRaceWeight(raceChoice.race) <= 0)
            {
                raceChoice = PawnKindTemplateUtil.GetFighterForRace(ThingDefOf.Human);
            }

            // Try to generate pawn with the requested kind
            Pawn newPawn = null;
            try
            {
                PawnGenerationRequest request;
                if (_customXenotypeName != null)
                {
                    CustomXenotype custom = null;
                    FactionCache.CustomXenotypesDecoder?.TryGetValue(_customXenotypeName, out custom);

                    if (custom != null)
                        request = FCPawnGenerator.WorkerOrMilitaryRequest(raceChoice, custom);
                    else
                    {
                        LogUtil.Warning($"Custom xenotype '{_customXenotypeName}' not found, falling back to Baseliner");
                        request = FCPawnGenerator.WorkerOrMilitaryRequest(raceChoice, XenotypeDefOf.Baseliner);
                    }
                }
                else
                {
                    request = FCPawnGenerator.WorkerOrMilitaryRequest(raceChoice, xenotypeChoice);
                }
                newPawn = FCPawnGenerator.GenerateWithForcedXenotype(request);

                // Set faction after generation (since we generate without faction to avoid xenotype forcing)
                if (newPawn != null && newPawn.Faction == null)
                {
                    var empireFaction = FactionCache.PlayerColonyFaction;
                    if (empireFaction != null)
                    {
                        newPawn.SetFaction(empireFaction);
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.Warning($"Failed to generate pawn with kind {raceChoice?.defName}: {ex.Message}");
            }

            // Fallback 1: Try with Baseliner xenotype and NO faction (avoids faction xenotype forcing)
            if (newPawn == null)
            {
                LogUtil.Warning($"Pawn generation failed for {raceChoice?.defName}. Trying Baseliner fallback without faction.");
                try
                {
                    var simpleRequest = new PawnGenerationRequest(
                        kind: PawnKindDefOf.Colonist,
                        faction: null, // NO faction - this prevents faction xenotype forcing
                        context: PawnGenerationContext.NonPlayer,
                        tile: -1,
                        forceGenerateNewPawn: false,
                        allowDead: false,
                        allowDowned: false,
                        canGeneratePawnRelations: false, // No relations for factionless pawns
                        mustBeCapableOfViolence: true,
                        colonistRelationChanceFactor: 0,
                        forceAddFreeWarmLayerIfNeeded: false,
                        allowGay: true,
                        allowFood: true,
                        allowAddictions: false,
                        forcedXenotype: XenotypeDefOf.Baseliner // Force Baseliner - guaranteed violence capable
                    );
                    newPawn = PawnGenerator.GeneratePawn(simpleRequest);

                    // Set the faction after generation
                    if (newPawn != null)
                    {
                        var empireFaction = FactionCache.PlayerColonyFaction;
                        if (empireFaction != null)
                        {
                            newPawn.SetFaction(empireFaction);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.Warning($"Baseliner fallback also failed: {ex.Message}");
                }
            }

            // Fallback 2: Absolute minimal request - no faction, no xenotype, no violence requirement
            if (newPawn == null)
            {
                LogUtil.Warning("All standard generation failed. Trying minimal fallback.");
                try
                {
                    var fallbackRequest = new PawnGenerationRequest(
                        kind: PawnKindDefOf.Colonist,
                        faction: null, // NO faction
                        context: PawnGenerationContext.NonPlayer,
                        tile: -1,
                        forceGenerateNewPawn: false,
                        allowDead: false,
                        allowDowned: false,
                        canGeneratePawnRelations: false,
                        mustBeCapableOfViolence: false, // Allow non-violent as absolute last resort
                        colonistRelationChanceFactor: 0,
                        forceAddFreeWarmLayerIfNeeded: false,
                        allowGay: true,
                        allowFood: true,
                        allowAddictions: false
                    );
                    newPawn = PawnGenerator.GeneratePawn(fallbackRequest);

                    // Set the faction after generation
                    if (newPawn != null)
                    {
                        var empireFaction = FactionCache.PlayerColonyFaction;
                        if (empireFaction != null)
                        {
                            newPawn.SetFaction(empireFaction);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.Error($"Critical - all pawn generation attempts failed: {ex.Message}");
                }
            }

            // Final check - if still null, we cannot proceed
            if (newPawn == null)
            {
                LogUtil.Error("Critical error - could not generate any pawn for mercenary squad. Skipping this mercenary.");
                return;
            }

            if (newPawn.kindDef == null)
            {
                newPawn.kindDef = raceChoice ?? PawnKindDefOf.Colonist;
                LogUtil.Warning($"MercenaryPawnFactory.CreateNewPawn: detected null kindDef, setting to default");
            }

            newPawn.apparel?.DestroyAll();
            newPawn.equipment?.DestroyAllEquipment();
            merc.squad = squad;
            merc.settlement = squad?.settlement;
            merc.pawn = newPawn;
        }
    }
}
