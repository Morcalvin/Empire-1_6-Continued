using FactionColonies.util;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace FactionColonies
{
    public static class MilitaryUtil
    {

        /// <summary>
        /// Internal method used to spawn a <paramref name="settlement"/>'s squad for military deployment
        /// </summary>
        /// <param name="settlement"></param>
        /// <param name="squad"></param>
        /// <param name="dropPosition"></param>
        /// <param name="DropPod"></param>
        private static void SpawnSquad(WorldSettlementFC settlement, MercenarySquadFC squad, IntVec3 dropPosition, bool DropPod)
        {
            if (settlement.MilitaryComp == null)
            {
                LogUtil.Warning($"SpawnSquad called on settlement {settlement.Name} with no MilitaryComp. Aborting.");
                return;
            }

            Map currentMap = Find.CurrentMap;
            if (currentMap is null)
            {
                LogUtil.Warning("SpawnSquad: Find.CurrentMap is null. Cannot deploy squad.");
                return;
            }

            IncidentParms parms = new IncidentParms
            {
                target = currentMap,
                faction = FactionCache.PlayerColonyFaction,
                podOpenDelay = 140,
                points = 999,
                raidArrivalModeForQuickMilitaryAid = true,
                raidNeverFleeIndividual = true,
                //raidForceOneIncap = true,
                raidArrivalMode = PawnsArrivalModeDefOf.CenterDrop,
                raidStrategy = RaidStrategyDefOf.ImmediateAttackFriendly
            };

            List<Pawn> equippedPawns = squad.AllEquippedMercenaryPawns.ToList();

            if (DropPod)
            {
                parms.spawnCenter = dropPosition;
                PawnsArrivalModeWorkerUtility.DropInDropPodsNearSpawnCenter(parms, equippedPawns);
            }
            else
            {
                PawnsArrivalModeWorker_EdgeWalkIn worker = new PawnsArrivalModeWorker_EdgeWalkIn();
                if (RCellFinder.TryFindClosestEdgeCellTo(dropPosition, currentMap, out parms.spawnCenter))
                {
                    parms.spawnRotation = Rot4.FromAngleFlat((currentMap.Center - parms.spawnCenter).AngleFlat);
                }
                else
                {
                    // dropPosition is unreachable from any map edge (e.g. fully sealed off) — fall back to vanilla random edge cell.
                    worker.TryResolveRaidSpawnCenter(parms);
                }
                worker.Arrive(equippedPawns, parms);
            }

            equippedPawns.ForEach(pawn => pawn.ApplyIdeologyRitualWounds());
            squad.orderLocation = dropPosition;
            Find.LetterStack.ReceiveLetter("FCDeploymentSuccessLabel".Translate(), "FCDeploymentSuccessDesc".Translate(settlement.Name, currentMap.Parent.LabelCap), LetterDefOf.NeutralEvent, new LookTargets(equippedPawns));

            // Deploy is a manager op: CreateDeployOp registers the squad, sets phase=Engaged,
            // and fires LifecycleRegistry.InvokeOnOperationCreated. Squad-first: pass the squad,
            // not the settlement; the op's home settlement is read from squad.settlement.
            FactionCache.MilitaryManager?.CreateDeployOp(squad, currentMap.Tile);

            LordMaker.MakeNewLord(FactionCache.PlayerColonyFaction, new LordJob_DeployMilitary(dropPosition, squad), currentMap, equippedPawns);
        }

        /// <summary>
        /// Deploys a <paramref name="settlement"/>'s main force, takes silver if there is an <paramref name="overrideSquad"/>
        /// </summary>
        /// <param name="settlement"></param>
        /// <param name="DropPod"></param>
        /// <param name="overrideSquad"></param>
        public static void CallinAlliedForces(WorldSettlementFC settlement, bool DropPod, MercenarySquadFC overrideSquad = null)
        {
            MercenarySquadFC squad = overrideSquad
                ?? settlement?.FirstAvailableStationedSquad
                ?? settlement?.PrimaryStationedSquad;

            if (Find.CurrentMap.Parent is WorldSettlementFC)
            {
                // I think this case might be obsolete now that SettlementFC has been phased out. Need to double-check
                Messages.Message("FCMilitaryTriedDeployingToSettlementFC".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            if (squad == null)
            {
                LogUtil.Warning($"Attempted to call in allied forces for settlement {settlement.Name} with NULL MilitaryComp. Skipping");
                return;
            }

            squad.CheckInitialization();
            squad.UpdateSquadStats(settlement.settlementMilitaryLevel);
            squad.ResetNeeds();

            IntVec3 dropPosition;
            DebugTool tool = new DebugTool("FCSelectDeploymentPosition".Translate(), delegate
            {
                dropPosition = UI.MouseCell();
                Map curMap = Find.CurrentMap;

                if (!dropPosition.InBounds(curMap))
                {
                    Messages.Message("FCSelectedPosOutOfBounds".Translate(), MessageTypeDefOf.RejectInput);
                    return;
                }
                if (dropPosition.CloseToEdge(curMap, 10))
                {
                    Messages.Message("FCSelectedPosTooCloseToEdge".Translate(), MessageTypeDefOf.RejectInput);
                    return;
                }

                if (overrideSquad != null) PaymentUtil.PaySilver((int)Math.Round((squad?.outfit?.UpdateEquipmentTotalCost() ?? 0) * .2), PaymentUtil.Reason_SquadDeployment, settlement);
                SpawnSquad(settlement, squad, dropPosition, DropPod);
                DebugTools.curTool = null;
            });
            DebugTools.curTool = tool;

            //UI.UIToMapPosition(UI.MousePositionOnUI).ToIntVec3();
        }

        /// <summary>
        /// Deploys the secondary military of the empire from a <paramref name="settlement"/> 
        /// </summary>
        /// <param name="settlement"></param>
        /// <param name="DropPod"></param>
        public static void CallinExtraForces(WorldSettlementFC settlement, bool DropPod)
        {
            MercenarySquadFC squad = FactionCache.FactionComp.militaryCustomizationUtil.CreateMercenarySquad(settlement, true);
            if (squad == null) return;
            // Copy the outfit from the settlement's primary stationed squad (any squad with an
            // outfit will do — we just need a template to clone the gear from).
            MilSquadFC mainOutfit = settlement?.PrimaryStationedSquad?.outfit;
            if (mainOutfit != null) squad.OutfitSquad(mainOutfit);
            CallinAlliedForces(settlement, DropPod, squad);
        }
        public static void FireSupport(WorldSettlementFC settlement, MilitaryFireSupport support)
        {
            DebugTool tool = null;
            IntVec3 DropPosition;
            tool = new DebugTool("FCFireSupportSelectPosition".Translate(), delegate
            {
                float cost = support.ReturnTotalCost();
                if (DebugSettings.godMode || PaymentUtil.GetSilver() > cost)
                {
                    if (!DebugSettings.godMode)
                        PaymentUtil.PaySilver((int)Math.Round(cost), PaymentUtil.Reason_FireSupport, settlement);
                    DropPosition = UI.MouseCell();
                    IntVec3 spawnCenter = DropPosition;
                    Map map = Find.CurrentMap;
                    //Make new list
                    List<ThingDef> projectiles = new List<ThingDef>();
                    projectiles.AddRange(support.projectiles);
                    MilitaryFireSupport fireSupport = new MilitaryFireSupport("fireSupport", map, spawnCenter,
                        projectiles.Count() * 15, 600, support.accuracy, projectiles, settlement.Tile);
                    FactionCache.FactionComp.militaryCustomizationUtil.fireSupport.Add(fireSupport);

                    Messages.Message("FCFireSupportNameWillBeFiredOnPosition".Translate(support.name), MessageTypeDefOf.ThreatSmall);
                    if (settlement.MilitaryComp != null)
                        settlement.MilitaryComp.artilleryTimer = Find.TickManager.TicksGame + (DebugSettings.godMode ? 1 : 60000);
                }
                else
                {
                    Messages.Message("FCFireSupportNoSilver".Translate(), MessageTypeDefOf.RejectInput);
                }


                DebugTools.curTool = null;
            }, delegate { GenDraw.DrawRadiusRing(UI.MouseCell(), support.accuracy, Color.red); });
            DebugTools.curTool = tool;
        }

        /* Default variance bounds used by EnemySettlementPower for per-battle RNG.
         * DefaultLevelVariance preserves today's +/-2 spread. EfficiencyVariance is 0
         * because legacy code didn't randomize efficiency at all. */
        public const double DefaultLevelVariance = 2.0;
        public const double DefaultEfficiencyVariance = 0.0;

        /// <summary>
        /// Rolls a single offset within +/-<paramref name="variance"/> for use when sampling a
        /// battle force from a cached <see cref="EnemySettlementPower"/> baseline. Uniform
        /// distribution; replaces the prior curve-weighted <c>RandomAttackModifier</c> whose
        /// weighting was a no-op once the input clamped to the curve's first point.
        /// </summary>
        public static double RollVarianceOffset(double variance)
        {
            if (variance <= 0) return 0;
            return Rand.Range((float)-variance, (float)variance);
        }

        /// <summary>
        /// Resolves a defender <see cref="MilitaryForce"/> for an offensive operation by
        /// consulting <see cref="WorldComponent_EnemySettlementPower"/> for the cached
        /// per-settlement baseline and rolling within its variance. Falls back to
        /// <see cref="MilitaryForce.CreateMilitaryForceFromFaction"/> when no settlement is
        /// associated with the op. Used by <see cref="MilitaryOperation.BeginEngagement"/>
        /// and by job-handler auto-resolve fallbacks so the same path produces the defender
        /// in every scenario.
        /// </summary>
        public static MilitaryForce SampleDefenderForceForOp(MilitaryOperation op)
        {
            if (op is null || op.defender?.faction is null) return null;

            Settlement target = op.targetObject as Settlement;
            if (target is null && op.targetTile.Valid)
                target = Find.WorldObjects.SettlementAt(op.targetTile);

            EnemySettlementPower power = FactionCache.EnemyPowerRegistry?.GetOrCompute(target);
            return power?.SampleBattleForce(op.defender.faction)
                ?? MilitaryForce.CreateMilitaryForceFromFaction(op.defender.faction, false);
        }

        /// <summary>
        /// Computes a faction's deterministic baseline military level and efficiency.
        /// Mirrors the non-random part of <see cref="MilitaryForce.CreateMilitaryForceFromFaction"/>:
        /// tech-level lookup, Insect override, Empire Threat Level scaling, and storyteller
        /// threat adaptation. The +/-variance roll is intentionally omitted so this can be cached
        /// and surfaced as a stable display value.
        /// </summary>
        public static void ComputeFactionBaselinePower(Faction faction, FactionFC factionComp,
                                                      out double level, out double efficiency)
        {
            level = 1;
            efficiency = 1;
            if (faction is null || faction.def is null) return;

            MilitaryForce.GetMilitaryLevelAndEfficiencyFromTechLevel(faction.def.techLevel, out level, out efficiency);

            if (faction.def.defName == "Insect")
            {
                level = 4;
                efficiency = 1.2;
            }

            if (factionComp is object)
            {
                level *= ThreatScalingUtil.ComputeEmpireThreatLevel(factionComp);
                if (factionComp.threatAdaptation is object)
                {
                    level *= factionComp.threatAdaptation.ThreatFactor;
                }
            }
        }
    }
}
