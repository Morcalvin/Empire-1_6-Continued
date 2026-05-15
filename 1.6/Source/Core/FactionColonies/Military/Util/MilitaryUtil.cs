using FactionColonies.util;
using LudeonTK;
using RimWorld;
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
        /* Silver deploy-cost for a given equipment value, applying the configured
         * FCSettings.squadDeploymentCostPercentage. Single source of truth for the
         * deploy-cost formula — used by the live squad deploy bill, the settlement
         * max-deploy-cost badge, the design-window deploy preview, and the
         * over-budget assignment rejection message. */
        public static int CalculateDeploymentCost(double squadEquipmentCost)
        {
            return (int)Math.Round(squadEquipmentCost * FCSettings.squadDeploymentCostPercentage);
        }

        /// <summary>
        /// Internal method used to spawn a <paramref name="settlement"/>'s squad for military deployment
        /// </summary>
        /// <param name="settlement"></param>
        /// <param name="squad"></param>
        /// <param name="dropPosition"></param>
        /// <param name="DropPod"></param>
        /// <param name="bill">The deployment-cost bill created for this deployment, or
        /// <c>null</c> if none was created (zero cost / godMode). Controls whether the
        /// deployment letter mentions the cost and payment deadline.</param>
        private static void SpawnSquad(WorldSettlementFC settlement, MercenarySquadFC squad, IntVec3 dropPosition, bool DropPod, BillFC bill)
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

            // SpawnableMercenaryPawns filters downed pawns out — they stay at base to recover.
            List<Pawn> equippedPawns = squad.SpawnableMercenaryPawns.ToList();

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
            // Squad-first: name the deployed squad and its home settlement. When a deployment-cost
            // bill was created, also surface the cost and payment deadline; otherwise omit that
            // sentence (no bill when cost is 0% or godMode is on).
            string deploymentDesc = bill is object
                ? "FCDeploymentSuccessDesc".Translate(squad.DisplayName, squad.settlement?.Name, currentMap.Parent.LabelCap, SquadCostCalculator.DeploymentCost(squad), FCSettings.deploymentBillLifespan_days)
                : "FCDeploymentSuccessDescNoBill".Translate(squad.DisplayName, squad.settlement?.Name, currentMap.Parent.LabelCap);
            Find.LetterStack.ReceiveLetter("FCDeploymentSuccessLabel".Translate(), deploymentDesc, LetterDefOf.NeutralEvent, new LookTargets(equippedPawns));

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

            if (squad == null)
            {
                LogUtil.Warning($"Attempted to call in allied forces for settlement {settlement.Name} with NULL MilitaryComp. Skipping");
                return;
            }

            squad.CheckInitialization();
            squad.UpdateSquadStats(settlement.settlementMilitaryLevel);
            SquadHealthUtil.ResetNeeds(squad);

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

                BillFC deploymentBill = PaymentUtil.CreateDeploymentCostBill(squad);
                SpawnSquad(settlement, squad, dropPosition, DropPod, deploymentBill);
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
            if (mainOutfit != null) squad.Equipment.OutfitSquad(mainOutfit);
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

        /// <summary>
        /// Rolls a single offset within +/-<paramref name="variance"/> for use when sampling a
        /// battle force from a cached <see cref="EnemyPower"/> baseline. Uniform distribution;
        /// replaces the prior curve-weighted <c>RandomAttackModifier</c> whose weighting was a
        /// no-op once the input clamped to the curve's first point.
        /// </summary>
        public static double RollVarianceOffset(double variance)
        {
            if (variance <= 0) return 0;
            return Rand.Range((float)-variance, (float)variance);
        }

        /// <summary>
        /// Returns the bare tech-level baseline (level, efficiency, variances) from
        /// <see cref="EnemyPowerTechDef"/>.
        /// </summary>
        public static void GetTechLevelBaseline(TechLevel tl,
            out double level, out double efficiency,
            out double levelVariance, out double efficiencyVariance)
        {
            EnemyPowerTechDef d = FactionCache.EnemyPower?.GetTechDef(tl);
            if (d is null)
            {
                /* Pre-world / test path: no WorldComponent yet. Read DefDatabase directly. */
                foreach (EnemyPowerTechDef candidate in DefDatabase<EnemyPowerTechDef>.AllDefsListForReading)
                {
                    if (candidate.techLevel == tl) { d = candidate; break; }
                }
            }
            if (d is object)
            {
                level = d.level;
                efficiency = d.efficiency;
                levelVariance = d.levelVariance;
                efficiencyVariance = d.efficiencyVariance;
                return;
            }
            level = 1; efficiency = 1; levelVariance = 2; efficiencyVariance = 0;
        }

        /// <summary>
        /// Convenience overload returning only level and efficiency from the tech-level baseline.
        /// </summary>
        public static void GetTechLevelBaseline(TechLevel tl, out double level, out double efficiency)
        {
            GetTechLevelBaseline(tl, out level, out efficiency, out _, out _);
        }

        public static BattleViewerSide ResolvePlayerSide(MilitaryOperation op)
        {
            if (op is null) return BattleViewerSide.Neither;
            if (op.IsOffensive) return BattleViewerSide.Attacker;
            if (op.IsDefensive) return BattleViewerSide.Defender;
            return BattleViewerSide.Neither;
        }

        /// <summary>Runs <see cref="SquadUpgradeUtil.UpgradeToTemplate"/>, first prompting for
        /// confirmation when the re-template would fire (destroy) a pawn the player has
        /// personalized via the per-pawn loadout editor. Shared by every Upgrade-All entry
        /// point (squad inspection, military tab, settlement squad menu) so the warning is
        /// consistent. Window-layer helper — the model never opens dialogs itself.</summary>
        public static void ConfirmAndUpgradeAll(MercenarySquadFC squad)
        {
            if (squad is null) return;
            if (SquadUpgradeUtil.UpgradeWouldFirePersonalized(squad))
            {
                MercenarySquadFC captured = squad;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "FCSquadUpgradeFirePersonalizedConfirm".Translate(),
                    delegate { SquadUpgradeUtil.UpgradeToTemplate(captured); }));
                return;
            }
            SquadUpgradeUtil.UpgradeToTemplate(squad);
        }
    }
}
