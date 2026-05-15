using FactionColonies.util;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace FactionColonies
{
    public static class DebugUtil
    {

        [DebugAction("Empire", "Force auto-resolve round now", allowedGameStates = AllowedGameStates.Playing)]
        private static void ForceAutoResolveRoundNow()
        {
            MilitaryOperationManager mgr = FactionCache.MilitaryManager;
            if (mgr is null)
            {
                LogUtil.MessageForce("No MilitaryOperationManager available.");
                return;
            }
            int bumped = 0;
            foreach (MilitaryOperation op in mgr.active)
            {
                if (op is null) continue;
                if (op.phase != MilitaryOperationPhase.Engaged) continue;
                FCEvent evt = op.sourceEvents.FirstOrDefault(e => e is object && e.def == FCEventDefOf.autoResolveBattleRound);
                if (evt is object)
                {
                    evt.timeTillTrigger = Find.TickManager.TicksGame + 1;
                    bumped++;
                }
            }
            LogUtil.MessageForce($"Bumped {bumped} autoResolveBattleRound event(s) to fire next tick.");
        }

        [DebugAction("Empire", "View Events and ticks till", allowedGameStates = AllowedGameStates.Playing)]
        private static void ViewEventsAndLog()
        {
            foreach (FCEvent e in FactionCache.FactionComp.Events)
            {
                LogUtil.MessageForce(e.def.defName + " with cooldown: " + (e.timeTillTrigger - Find.TickManager.TicksGame));
            }
        }

        [DebugAction("Empire", "Increment Time 5 Days", allowedGameStates = AllowedGameStates.Playing)]
        private static void IncrementTimeFiveDays()
        {
            LogUtil.MessageForce("Debug - Increment Time 5 Days");
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 300000);
        }

        [DebugAction("Empire", "Increment Time 1 Year", allowedGameStates = AllowedGameStates.Playing)]
        private static void IncrementTimeOneYear()
        {
            LogUtil.MessageForce("Debug - Increment Time 1 Year");
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + GenDate.TicksPerYear);
        }

        [DebugAction("Empire", "Kill & Regen Leader", allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugKillAndRegenLeader()
        {
            Faction faction = FactionCache.PlayerColonyFaction;
            if (faction == null)
            {
                LogUtil.MessageForce("No Empire faction found.");
                return;
            }
            Pawn oldLeader = faction.leader;
            if (oldLeader != null)
            {
                LogUtil.MessageForce($"Killing leader: {oldLeader.Name} ({oldLeader.ThingID}), " +
                                     $"title: {faction.LeaderTitle}, " +
                                     $"ideo: {oldLeader.Ideo?.name ?? "none"}");
                oldLeader.Kill(null);
            }
            else
            {
                LogUtil.MessageForce("No current leader. Generating new one.");
            }
            ColonyUtil.CreatePlayerFactionLeader(faction);
            if (faction.leader != null)
            {
                LogUtil.MessageForce($"New leader: {faction.leader.Name} ({faction.leader.ThingID}), " +
                                     $"title: {faction.LeaderTitle}, " +
                                     $"pawnKind: {faction.leader.kindDef?.defName ?? "null"}, " +
                                     $"ideo: {faction.leader.Ideo?.name ?? "none"}");
            }
        }

        [DebugAction("Empire", "Print Races", allowedGameStates = AllowedGameStates.Playing)]
        private static void PrintRaces()
        {
            FactionCache.PlayerColonyFaction.def.pawnGroupMakers.ForEach(maker =>
            {
                LogUtil.MessageForce("Traders: " + maker.traders.Count);
                foreach (PawnGenOption option in maker.options)
                {
                    LogUtil.MessageForce("Race: " + option.kind.race.defName + ", " + option.kind.defName + ", " +
                                option.kind.isFighter + ", " + option.kind.trader + " for " + maker.kindDef);
                }
            });
        }

        [DebugAction("Empire", "Send Pawn To Settlement", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SendPawnToSettlement()
        {
            List<Pawn> selected = Find.Selector.SelectedPawns;
            if (!selected.Any())
            {
                Messages.Message("No prisoner selected!", MessageTypeDefOf.RejectInput);
                return;
            }
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (WorldSettlementFC settlement in FactionCache.FactionComp.settlements)
            {
                list.Add(new DebugMenuOption(
                    $"{settlement.Name} - Level: {settlement.settlementLevel} - Prisoners: {settlement.prisonerList.Count()}",
                    DebugMenuOptionMode.Action, delegate
                    {
                        foreach (Pawn pawn in selected)
                        {
                            TravelUtil.SendPrisoner(pawn, settlement);

                            foreach (var bed in Find.Maps.Where(map => map.IsPlayerHome).SelectMany(map =>
                                map.listerBuildings.allBuildingsColonist).OfType<Building_Bed>())
                            {
                                if (!Enumerable.Any(bed.OwnersForReading, found => found == pawn)) continue;
                                bed.ForPrisoners = false;
                                bed.ForPrisoners = true;
                            }
                        }
                    }));
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        [DebugAction("Empire", "Clear faction traits and policies", allowedGameStates = AllowedGameStates.Playing)]
        private static void ClearFactionTraitsAndPolicies()
        {
            FactionFC faction = FactionCache.FactionComp;
            if (faction == null) return;

            for (int i = 0; i < faction.factionTraits.Count; i++)
            {
                if (faction.factionTraits[i]?.behavior != null)
                {
                    try { faction.factionTraits[i].behavior.OnRemoved(faction); }
                    catch (Exception e) { LogUtil.Error($"FCPolicyBehavior.OnRemoved error: {e}"); }
                }
                faction.factionTraits[i] = new FCPolicy(FCPolicyDefOf.empty);
            }

            faction.RemoveAllPolicies(faction.policies);
            faction.RebuildBehaviorCache();

            LogUtil.Message("Cleared faction traits and policies.");
        }

        [DebugAction("Empire", "Reset All Military Squad Assignments", allowedGameStates = AllowedGameStates.Playing)]
        private static void ResetAllMilitarySquads()
        {
            LogUtil.MessageForce("Debug - Reset All Military Squad Assignments");
            MilitaryCustomizationUtil util = FactionCache.FactionComp.militaryCustomizationUtil;
            var allMercs = util.AllMercenaries.ToList();
            for (int i = allMercs.Count - 1; i >= 0; i--)
            {
                if (allMercs[i].squad.Deployment.HasLord)
                {
                    allMercs[i].squad.Deployment.Map.lordManager.RemoveLord(allMercs[i].squad.Deployment.Lord);
                }

                allMercs[i].pawn.Destroy();
                allMercs[i].squad.mercenaries.Remove(allMercs[i]);
            }

            for (int k = util.mercenarySquads.Count() - 1; k >= 0; k--)
            {
                MercenarySquadFC squad = util.mercenarySquads[k];
                if (squad?.settlement != null) squad.settlement = null;
                util.mercenarySquads.RemoveAt(k);
            }


            util.CheckMilitaryUtilForErrors();
        }


        [DebugAction("Empire", "Make Random Event", allowedGameStates = AllowedGameStates.Playing)]
        private static void MakeRandomEvent()
        {
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (FCEventDef evtDef in DefDatabase<FCEventDef>.AllDefsListForReading)
            {
                if (evtDef.isRandomEvent)
                    list.Add(new DebugMenuOption(evtDef.label, DebugMenuOptionMode.Action, delegate
                    {
                        LogUtil.MessageForce("Debug - Make Random Event - " + evtDef.label);
                        FCEvent evt = FCEventMaker.MakeRandomEvent(evtDef, null);
                        if (evt == null)
                        {
                            if (!evtDef.activateAtStart)
                                LogUtil.Warning("Debug - Event returned null: " + evtDef.defName);
                            return;
                        }

                        if (!evtDef.activateAtStart)
                        {
                            FactionCache.FactionComp.AddEvent(evt);
                        }

                        string settlementString = evt.settlementTraitLocations.Join((settlement) => $" {settlement.Name}", "\n");
                        if (!settlementString.NullOrEmpty())
                            Find.LetterStack.ReceiveLetter("Random Event", $"{evt.def.desc}\n{"FCEventAffectingSettlements".Translate()}\n{settlementString}", LetterDefOf.NeutralEvent);
                        else
                            Find.LetterStack.ReceiveLetter("Random Event", evt.def.desc, LetterDefOf.NeutralEvent);
                    }
                    ));
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        [DebugAction("Empire", "Proc MilitaryTimeDue", allowedGameStates = AllowedGameStates.Playing)]
        private static void ProcMilitaryTimeDue()
        {
            LogUtil.MessageForce("Debug - Proc MilitaryTimeDue");
            FactionCache.FactionComp.militaryTimeDue = Find.TickManager.TicksGame + 1;
        }

        [DebugAction("Empire", "Attack Player Settlement", allowedGameStates = AllowedGameStates.Playing)]
        private static void AttackPlayerSettlement()
        {
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (WorldSettlementFC settlement in FactionCache.FactionComp.settlements)
            {
                list.Add(new DebugMenuOption(settlement.Name, DebugMenuOptionMode.Action, delegate
                {
                    Faction enemyFaction = Find.FactionManager.RandomEnemyFaction();
                    if (enemyFaction == null)
                    {
                        Messages.Message("No enemy faction found.", MessageTypeDefOf.RejectInput);
                        return;
                    }

                    List<DebugMenuOption> levelList = new List<DebugMenuOption>();
                    for (int level = 1; level <= 10; level++)
                    {
                        int chosenLevel = level;
                        levelList.Add(new DebugMenuOption($"Level {chosenLevel}", DebugMenuOptionMode.Action, delegate
                        {
                            MilitaryUtil.GetTechLevelBaseline(enemyFaction.def.techLevel, out double _, out double efficiency);
                            MilitaryForce attackingForce = new MilitaryForce(chosenLevel, efficiency, null, enemyFaction);
                            LogUtil.MessageForce($"Debug - Attack Player Settlement - {settlement.Name} (level {chosenLevel}, efficiency {efficiency})");
                            if (!MilitaryUtilFC.AttackPlayerSettlement(attackingForce, settlement, enemyFaction))
                            {
                                Messages.Message($"Debug attack on {settlement.Name} failed (no MilitaryComp or MilitaryManager).", MessageTypeDefOf.RejectInput);
                            }
                        }));
                    }
                    Find.WindowStack.Add(new Dialog_DebugOptionListLister(levelList));
                }
                ));
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        [DebugAction("Empire", "Instant Attack Player Settlement", allowedGameStates = AllowedGameStates.Playing)]
        private static void InstantAttackPlayerSettlement()
        {
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (WorldSettlementFC settlement in FactionCache.FactionComp.settlements)
            {
                list.Add(new DebugMenuOption(settlement.Name, DebugMenuOptionMode.Action, delegate
                {
                    Faction enemyFaction = Find.FactionManager.RandomEnemyFaction();
                    if (enemyFaction == null)
                    {
                        Messages.Message("No enemy faction found.", MessageTypeDefOf.RejectInput);
                        return;
                    }

                    List<DebugMenuOption> levelList = new List<DebugMenuOption>();
                    for (int level = 1; level <= 10; level++)
                    {
                        int chosenLevel = level;
                        levelList.Add(new DebugMenuOption($"Level {chosenLevel}", DebugMenuOptionMode.Action, delegate
                        {
                            MilitaryUtil.GetTechLevelBaseline(enemyFaction.def.techLevel, out double _, out double efficiency);
                            MilitaryForce attackingForce = new MilitaryForce(chosenLevel, efficiency, null, enemyFaction);
                            LogUtil.MessageForce($"Debug - Instant Attack Player Settlement - {settlement.Name} (level {chosenLevel}, efficiency {efficiency})");
                            if (settlement.MilitaryComp is null || FactionCache.MilitaryManager is null)
                            {
                                Messages.Message($"Debug attack on {settlement.Name} failed (no MilitaryComp or MilitaryManager).", MessageTypeDefOf.RejectInput);
                                return;
                            }

                            // Call the manager directly so we get the freshly-created op handle. Going via
                            // MilitaryUtilFC.AttackPlayerSettlement would force a tile-wide event lookup, which
                            // returns the FIRST settlementBeingAttacked at this tile — that can be an older,
                            // already-stacked attack rather than the one we just queued.
                            MilitaryOperation op = FactionCache.MilitaryManager.CreateDefensiveOp(settlement, attackingForce, enemyFaction);
                            if (op is null) return;

                            FCEvent attackEvt = null;
                            for (int i = op.sourceEvents.Count - 1; i >= 0; i--)
                            {
                                if (op.sourceEvents[i]?.def == FCEventDefOf.settlementBeingAttacked)
                                {
                                    attackEvt = op.sourceEvents[i];
                                    break;
                                }
                            }
                            if (attackEvt != null)
                            {
                                attackEvt.timeTillTrigger = Find.TickManager.TicksGame + 1;
                            }
                        }));
                    }
                    Find.WindowStack.Add(new Dialog_DebugOptionListLister(levelList));
                }
                ));
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        [DebugAction("Empire", "Force Attack + Event Same Tick", allowedGameStates = AllowedGameStates.Playing)]
        private static void ForceAttackAndEventSameTick()
        {
            FactionFC faction = FactionCache.FactionComp;
            IReadOnlyList<FCEvent> attackEvents = faction.GetEventsByDef(FCEventDefOf.settlementBeingAttacked);
            FCEvent attackEvt = attackEvents.Count > 0 ? attackEvents[0] : null;
            if (attackEvt == null)
            {
                LogUtil.MessageForce("Debug - No pending settlementBeingAttacked event. Use 'Attack Player Settlement' first.");
                return;
            }

            if (FCSettings.disableRandomEvents)
            {
                LogUtil.MessageForce("Debug - Warning: random events are disabled in settings. Random event will not fire.");
            }

            int nextDayBoundary = ((Find.TickManager.TicksGame / GenDate.TicksPerDay) + 1) * GenDate.TicksPerDay;
            attackEvt.timeTillTrigger = nextDayBoundary;
            faction.randomEventLastAdded = FCSettings.maxDaysTillRandomEvent + 1;
            Find.TickManager.DebugSetTicksGame(nextDayBoundary - 1);
            LogUtil.MessageForce($"Debug - Attack timer and random event aligned to tick {nextDayBoundary}. Unpause to trigger both on the same tick.");
        }

        [DebugAction("Empire", "Change Settlement Defending Force", allowedGameStates = AllowedGameStates.Playing)]
        private static void ChangeAttackPlayerSettlementMilitaryForce()
        {
            FactionFC worldcomp = FactionCache.FactionComp;
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (FCEvent evt in worldcomp.Events)
            {
                if (evt.def == FCEventDefOf.settlementBeingAttacked)
                {
                    list.Add(new DebugMenuOption(
                        worldcomp.ReturnSettlementByLocation(evt.location)?.Name ?? "Unknown",
                        DebugMenuOptionMode.Action, delegate
                        {
                            //when event is selected, select defending force to replace it with

                            List<DebugMenuOption> list2 = new List<DebugMenuOption>();
                            MilitaryOperation defOp = evt.linkedOperation;
                            WorldObject currentDefenderTarget = defOp?.targetObject;
                            WorldSettlementFC currentDefenderHome = defOp?.defender?.homeSettlement;
                            foreach (WorldSettlementFC settlement in worldcomp.settlements)
                            {
                                if (settlement.MilitaryComp == null || !settlement.MilitaryComp.IsMilitaryValid()) continue;
                                if (currentDefenderTarget is object && settlement.Name == currentDefenderTarget.Label) continue;
                                list2.Add(new DebugMenuOption(
                                    settlement.Name + " - " + settlement.settlementMilitaryLevel + " - Busy: " +
                                    settlement.MilitaryComp.militaryBusy, DebugMenuOptionMode.Action, delegate
                                    {
                                        if (settlement.MilitaryComp.IsMilitaryBusy() == false)
                                        {
                                            LogUtil.MessageForce($"Debug - Change Player Settlement - {currentDefenderHome?.Name ?? "Unknown"} to {settlement.Name}");
                                            MilitaryUtilFC.ChangeDefendingMilitaryForce(evt, settlement);
                                        }
                                    }
                                ));
                            }

                            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list2));
                        }
                    ));
                }
            }

            if (list.Any())
            {
                Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
            }
        }

        [DebugAction("Empire", "Upgrade Player Settlement", allowedGameStates = AllowedGameStates.Playing)]
        private static void UpgradePlayerSettlementx1() => UpgradePlayerSettlement();

        [DebugAction("Empire", "Upgrade Player Settlement x5", allowedGameStates = AllowedGameStates.Playing)]
        private static void UpgradePlayerSettlementx5() => UpgradePlayerSettlement(5);

        private static void UpgradePlayerSettlement(int times = 1)
        {
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (WorldSettlementFC settlement in FactionCache.FactionComp.settlements)
            {
                list.Add(new DebugMenuOption(settlement.Name, DebugMenuOptionMode.Action, delegate
                {
                    if (times > 0)
                    {
                        LogUtil.MessageForce("Debug - Upgrade Player Settlement x" + times + "- " + settlement.Name);
                    }
                    else
                    {
                        LogUtil.MessageForce("Debug - Downgrade Player Settlement x" + times + "- " + settlement.Name);
                    }
                    settlement.UpgradeSettlement(times);
                }
                ));
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        [DebugAction("Empire", "Flag Road Queue Update", allowedGameStates = AllowedGameStates.Playing)]
        private static void FlagRoadQueueUpdate()
        {
            LogUtil.MessageForce("Debug - Flag Road Queue Update");
            FactionCache.FactionComp.roadBuilder.FlagUpdateRoadQueues();
        }

        [DebugAction("Empire", "De-Level Player Settlement", allowedGameStates = AllowedGameStates.Playing)]
        private static void DelevelPlayerSettlement() => UpgradePlayerSettlement(-1);

        [DebugAction("Empire", "Reset All Military Squads", allowedGameStates = AllowedGameStates.Playing)]
        private static void ResetMilitarySquads()
        {
            LogUtil.MessageForce("Debug - Reset All Military Squads");
            MilitaryCustomizationUtil util = FactionCache.FactionComp.militaryCustomizationUtil;

            for (int i = util.mercenarySquads.Count - 1; i >= 0; i--)
            {
                MercenarySquadFC squad = util.mercenarySquads[i];
                if (squad.Deployment.HasLord)
                {
                    squad.Deployment.Map?.lordManager.RemoveLord(squad.Deployment.Lord);
                }

                foreach (Mercenary merc in squad.mercenaries.Concat(squad.animals).ToList())
                {
                    if (merc?.pawn != null && !merc.pawn.Destroyed)
                        merc.pawn.Destroy();
                }

                squad.settlement = null;

                util.mercenarySquads.RemoveAt(i);
            }

            foreach (WorldSettlementFC settlement in FactionCache.FactionComp.settlements)
            {
                settlement.MilitaryComp?.ReturnMilitary(false);
            }

            util.CheckMilitaryUtilForErrors();
        }

        [DebugAction("Empire", "Reset All Squad Cooldowns", allowedGameStates = AllowedGameStates.Playing)]
        private static void ResetAllSquadCooldowns()
        {
            LogUtil.MessageForce("Debug - Reset All Squad Cooldowns");
            FactionFC faction = FactionCache.FactionComp;

            /* Squad-first model: a squad's cooldown is a cooldownMilitary ("traveling") FCEvent
             * linked to a MilitaryOperation in CooldownPending phase, plus the per-squad
             * nextAvailableTick gate. Clear all three layers. */

            // 1. Resolve every op stuck in a return-trip cooldown. Snapshot first — Resolve()
            //    unregisters the op, which mutates the manager's active list.
            int opsResolved = 0;
            MilitaryOperationManager mgr = FactionCache.MilitaryManager;
            if (mgr is object)
            {
                List<MilitaryOperation> snapshot = new List<MilitaryOperation>(mgr.active);
                foreach (MilitaryOperation op in snapshot)
                {
                    if (op is null || op.phase != MilitaryOperationPhase.CooldownPending) continue;
                    op.Resolve();
                    opsResolved++;
                }
            }

            // 2. Sweep all cooldownMilitary ("traveling") events from the faction queue,
            //    including any orphans whose linked op is already gone.
            int eventsCleared = faction.RemoveEventsWhere(e => e.def == FCEventDefOf.cooldownMilitary);

            // 3. Clear the per-squad cooldown gate so squads are immediately available.
            int squadsCleared = 0;
            List<MercenarySquadFC> pool = faction.militaryCustomizationUtil?.mercenarySquads;
            if (pool is object)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    MercenarySquadFC squad = pool[i];
                    if (squad is null || squad.nextAvailableTick <= Find.TickManager.TicksGame) continue;
                    squad.nextAvailableTick = Find.TickManager.TicksGame;
                    squadsCleared++;
                }
            }

            LogUtil.MessageForce($"Debug - Resolved {opsResolved} cooldown op(s), cleared " +
                $"{eventsCleared} traveling event(s), reset {squadsCleared} squad cooldown(s)");
        }

        [DebugAction("Empire", "Clear Old Bills", allowedGameStates = AllowedGameStates.Playing)]
        private static void ClearOldBills()
        {
            FactionCache.FactionComp.OldBills = new List<BillFC>();
        }

        [DebugAction("Empire", "Clear All Events", allowedGameStates = AllowedGameStates.Playing)]
        private static void ClearAllEvents()
        {
            FactionCache.FactionComp?.eventManager?.Clear();
        }

        [DebugAction("Empire", "Clear All Bills", allowedGameStates = AllowedGameStates.Playing)]
        private static void ClearAllBills()
        {
            FactionCache.FactionComp.Bills = new List<BillFC>();
        }

        [DebugAction("Empire", "Place 500 Silver", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PlaceSilverFC() => SilverPlacer(500);

        [DebugAction("Empire", "Place 50000 Silver", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PlaceALotOfSilverFC() => SilverPlacer(50000);

        private static void SilverPlacer(int amount)
        {
            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = amount;
            GenPlace.TryPlaceThing(silver, UI.MouseCell(), Find.CurrentMap, ThingPlaceMode.Near);
        }

        private static void CallInAlliedForcesSelect()
        {
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (WorldSettlementFC settlement in FactionCache.FactionComp.settlements)
            {
                MercenarySquadFC squad = settlement.PrimaryStationedSquad;
                if (squad != null)
                {
                    list.Add(new DebugMenuOption(settlement.Name, DebugMenuOptionMode.Action, delegate
                    {
                        IncidentParms parms = new IncidentParms();
                        parms.target = Find.CurrentMap;
                        parms.faction = FactionCache.PlayerColonyFaction;
                        parms.podOpenDelay = 140;
                        parms.points = 999;
                        parms.raidArrivalModeForQuickMilitaryAid = true;
                        parms.raidNeverFleeIndividual = true;
                        parms.raidArrivalMode = PawnsArrivalModeDefOf.CenterDrop;
                        parms.raidStrategy = RaidStrategyDefOf.ImmediateAttackFriendly;

                        squad.CheckInitialization();
                        squad.UpdateSquadStats(settlement.settlementMilitaryLevel);

                        DebugTools.curTool = new DebugTool("Select Drop Position", delegate
                        {
                            IntVec3 dropPosition = UI.MouseCell();
                            parms.spawnCenter = dropPosition;

                            squad.Deployment.OrderLocation = dropPosition;

                            var debugEquippedPawns = squad.AllEquippedMercenaryPawns.ToList();
                            PawnsArrivalModeWorkerUtility.DropInDropPodsNearSpawnCenter(parms, debugEquippedPawns);
                            debugEquippedPawns.ForEach(pawn => pawn.ApplyIdeologyRitualWounds());
                            // Register the deploy op so squad.Deployment.IsPhysicallyDeployed reflects the state.
                            // Squad-first: pass the squad, not the settlement.
                            FactionCache.MilitaryManager?.CreateDeployOp(squad, Find.CurrentMap.Tile);
                            DebugTools.curTool = null;
                        });
                    }));
                }
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }


        [DebugAction("Empire", "Call In Allied Forces", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CallInAlliedForcesDebug() => CallInAlliedForcesSelect();


        [DebugAction("Empire", "Level Up Faction", allowedGameStates = AllowedGameStates.Playing)]
        private static void LevelUpFaction()
        {
            FactionFC faction = FactionCache.FactionComp;
            faction.AddExperienceToFactionLevel(faction.factionXPGoal);
        }

        // ============================
        // Helper
        // ============================

        private static void WithSettlementChoice(Action<WorldSettlementFC> callback)
        {
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (WorldSettlementFC settlement in FactionCache.FactionComp.settlements)
            {
                WorldSettlementFC local = settlement;
                list.Add(new DebugMenuOption(
                    $"{local.Name} (Lv{local.settlementLevel})",
                    DebugMenuOptionMode.Action, () => callback(local)));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        private static void WithSquadChoice(Action<MercenarySquadFC> callback)
        {
            List<MercenarySquadFC> pool = FactionCache.FactionComp?.militaryCustomizationUtil?.mercenarySquads;
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            if (pool is object)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    MercenarySquadFC squad = pool[i];
                    if (squad is null) continue;
                    MercenarySquadFC local = squad;
                    string where = local.settlement?.Name ?? "(unassigned)";
                    list.Add(new DebugMenuOption(
                        $"{local.DisplayName} @ {where} - {SquadHealthUtil.CountInjuredMercs(local)} injured merc(s)",
                        DebugMenuOptionMode.Action, () => callback(local)));
                }
            }
            if (list.Count == 0)
            {
                LogUtil.MessageForce("Debug - No squads available");
                return;
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        // ============================
        // Settlement Debug Actions
        // ============================

        [DebugAction("Empire", "Instant Build Building", allowedGameStates = AllowedGameStates.Playing)]
        private static void InstantBuildBuilding()
        {
            WithSettlementChoice(settlement =>
            {
                var comp = settlement.BuildingsComp;
                int slotCount = comp.NumBuildingSlots;
                List<DebugMenuOption> slots = new List<DebugMenuOption>();
                for (int i = 0; i < slotCount; i++)
                {
                    int localSlot = i;
                    BuildingFCDef current = comp.Buildings[i].def;
                    string slotLabel = $"Slot {i}: {current.label ?? current.defName}";
                    slots.Add(new DebugMenuOption(slotLabel, DebugMenuOptionMode.Action, () =>
                    {
                        List<DebugMenuOption> buildingOptions = new List<DebugMenuOption>();
                        foreach (BuildingFCDef bDef in DefDatabase<BuildingFCDef>.AllDefsListForReading)
                        {
                            if (bDef == BuildingFCDefOf.Empty || bDef == BuildingFCDefOf.Construction) continue;
                            BuildingFCDef localDef = bDef;
                            buildingOptions.Add(new DebugMenuOption(localDef.label ?? localDef.defName, DebugMenuOptionMode.Action, () =>
                            {
                                comp.ConstructBuilding(localDef, localSlot);
                                LogUtil.MessageForce($"Debug - Instant built {localDef.defName} in slot {localSlot} at {settlement.Name}");
                                Messages.Message($"Debug: Built {localDef.label ?? localDef.defName} in {settlement.Name}", MessageTypeDefOf.PositiveEvent, false);
                            }));
                        }
                        Find.WindowStack.Add(new Dialog_DebugOptionListLister(buildingOptions));
                    }));
                }
                Find.WindowStack.Add(new Dialog_DebugOptionListLister(slots));
            });
        }

        [DebugAction("Empire", "Log Settlement Stats", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogSettlementStats()
        {
            foreach (WorldSettlementFC s in FactionCache.FactionComp.settlements)
            {
                LogUtil.MessageForce($"[{s.Name}] Lv{s.settlementLevel} | Happy:{s.happiness:F0} Loyal:{s.loyalty:F0} Unrest:{s.unrest:F0} Prosper:{s.prosperity:F0} | Workers:{s.workers}/{s.workersMax} Prisoners:{s.prisonerList.Count}");
            }
        }

        [DebugAction("Empire", "Set Settlement Stat", allowedGameStates = AllowedGameStates.Playing)]
        private static void SetSettlementStat()
        {
            WithSettlementChoice(settlement =>
            {
                List<DebugMenuOption> stats = new List<DebugMenuOption>();
                string[] statNames = { "happiness", "loyalty", "unrest", "prosperity" };
                foreach (string stat in statNames)
                {
                    string localStat = stat;
                    stats.Add(new DebugMenuOption(localStat, DebugMenuOptionMode.Action, () =>
                    {
                        List<DebugMenuOption> values = new List<DebugMenuOption>();
                        foreach (int val in new[] { 0, 25, 50, 75, 100 })
                        {
                            int localVal = val;
                            values.Add(new DebugMenuOption(localVal.ToString(), DebugMenuOptionMode.Action, () =>
                            {
                                switch (localStat)
                                {
                                    case "happiness": settlement.happiness = localVal; break;
                                    case "loyalty": settlement.loyalty = localVal; break;
                                    case "unrest": settlement.unrest = localVal; break;
                                    case "prosperity": settlement.prosperity = localVal; break;
                                }
                                LogUtil.MessageForce($"Debug - Set {settlement.Name} {localStat} = {localVal}");
                            }));
                        }
                        Find.WindowStack.Add(new Dialog_DebugOptionListLister(values));
                    }));
                }
                Find.WindowStack.Add(new Dialog_DebugOptionListLister(stats));
            });
        }

        [DebugAction("Empire", "Create Settlement (Instant)", allowedGameStates = AllowedGameStates.PlayingOnWorld)]
        private static void CreateSettlementInstant()
        {
            List<WorldSettlementDef> defs = DefDatabase<WorldSettlementDef>.AllDefsListForReading;
            if (defs.Count == 1)
            {
                StartTilePickerForSettlement(defs[0]);
                return;
            }

            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (WorldSettlementDef def in defs)
            {
                WorldSettlementDef localDef = def;
                string layerLabel = localDef.planetLayers.Count > 0
                    ? localDef.planetLayers[0].defName
                    : "Surface";
                list.Add(new DebugMenuOption($"{localDef.LabelCap} [{layerLabel}]", DebugMenuOptionMode.Action,
                    () => StartTilePickerForSettlement(localDef)));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        private static void StartTilePickerForSettlement(WorldSettlementDef def)
        {
            FactionFC faction = FactionCache.FactionComp;
            if (faction is null)
            {
                LogUtil.MessageForce("Debug - FactionFC WorldComponent is null, cannot create settlement.");
                return;
            }

            faction.layersForTilePicker = def.planetLayers;

            Find.TilePicker.StartTargeting_NewTemp(
                delegate (PlanetTile tile)
                {
                    StringBuilder reason = new StringBuilder();
                    if (!WorldTileChecker.IsValidTileForNewSettlement(tile, def, reason))
                    {
                        Messages.Message($"Cannot settle here: {reason}", MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    if (faction.CheckSettlementCaravansList(tile))
                    {
                        Messages.Message("A settlement caravan is already heading to this tile.", MessageTypeDefOf.RejectInput);
                        return false;
                    }
                    return true;
                },
                delegate (PlanetTile tile)
                {
                    PlanetTile settlementTile = def.GetTileForSettlement(tile);
                    LogUtil.MessageForce($"Debug - Create Settlement (Instant) at tile {settlementTile.Tile} with type {def.defName}");
                    ColonyUtil.CreatePlayerColonySettlement(settlementTile, def);
                    faction.layersForTilePicker = null;
                },
                allowEscape: true,
                noTileChosen: delegate
                {
                    faction.layersForTilePicker = null;
                },
                title: $"Select tile for {def.LabelCap}",
                showRandomButton: false,
                showNextButton: true,
                canCancel: true
            );
        }

        [DebugAction("Empire", "Create 10 Random Settlements", allowedGameStates = AllowedGameStates.Playing)]
        private static void CreateTenRandomSettlements()
        {
            FactionFC faction = FactionCache.FactionComp;
            if (faction is null)
            {
                LogUtil.MessageForce("Debug - FactionFC WorldComponent is null, cannot create settlements.");
                return;
            }

            WorldSettlementDef def = WorldSettlementDefOf.WorldSettlementDef_Surface;
            int created = 0;
            int maxAttempts = 500;

            for (int attempts = 0; attempts < maxAttempts && created < 10; attempts++)
            {
                PlanetTile tile = TileFinder.RandomSettlementTileFor(Find.WorldGrid.Surface, FactionCache.PlayerColonyFaction);
                if (tile == -1) continue;
                if (!WorldTileChecker.IsValidTileForNewSettlement(tile, def)) continue;

                ColonyUtil.CreatePlayerColonySettlement(tile, def);
                created++;
            }

            LogUtil.MessageForce($"Debug - Created {created}/10 random settlements.");
        }

        [DebugAction("Empire", "Remove Player Settlement", allowedGameStates = AllowedGameStates.Playing)]
        private static void RemovePlayerSettlement()
        {
            WithSettlementChoice(settlement =>
            {
                LogUtil.MessageForce($"Debug - Remove Player Settlement - {settlement.Name}");
                ColonyUtil.RemovePlayerSettlement(settlement);
            });
        }

        [DebugAction("Empire", "Add Stat Modifier", allowedGameStates = AllowedGameStates.Playing)]
        private static void AddStatModifier()
        {
            WithSettlementChoice(settlement =>
            {
                List<DebugMenuOption> list = new List<DebugMenuOption>();
                foreach (FCStatDef stat in DefDatabase<FCStatDef>.AllDefsListForReading)
                {
                    FCStatDef localStat = stat;
                    list.Add(new DebugMenuOption(localStat.defName, DebugMenuOptionMode.Action, () =>
                    {
                        List<DebugMenuOption> values = new List<DebugMenuOption>();
                        double[] options = localStat.aggregation == FCStatAggregation.Additive
                            ? new double[] { -10, -5, -1, 1, 5, 10 }
                            : new double[] { 0.5, 0.75, 1.25, 1.5, 2.0 };
                        foreach (double val in options)
                        {
                            double localVal = val;
                            string label = localStat.aggregation == FCStatAggregation.Additive
                                ? (localVal > 0 ? $"+{localVal}" : $"{localVal}")
                                : $"x{localVal}";
                            values.Add(new DebugMenuOption(label, DebugMenuOptionMode.Action, () =>
                            {
                                settlement.AddStatModifiers(
                                    new List<FCStatModifier> { new FCStatModifier { stat = localStat, value = localVal } },
                                    "debug", "Debug: " + localStat.defName);
                                LogUtil.MessageForce($"Debug - Added stat {localStat.defName} = {localVal} to {settlement.Name}");
                            }));
                        }
                        Find.WindowStack.Add(new Dialog_DebugOptionListLister(values));
                    }));
                }
                Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
            });
        }

        [DebugAction("Empire", "Clear Debug Stat Modifiers", allowedGameStates = AllowedGameStates.Playing)]
        private static void ClearDebugStatModifiers()
        {
            WithSettlementChoice(settlement =>
            {
                settlement.RemoveStatModifiersBySource("debug");
                LogUtil.MessageForce($"Debug - Cleared debug stat modifiers from {settlement.Name}");
            });
        }

        [DebugAction("Empire", "Log All Stat Values", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogAllStatValues()
        {
            WithSettlementChoice(settlement =>
            {
                FactionFC faction = FactionCache.FactionComp;
                LogUtil.MessageForce($"--- Stat Values for {settlement.Name} ---");
                int defaultCount = 0;
                foreach (FCStatDef stat in DefDatabase<FCStatDef>.AllDefsListForReading)
                {
                    if (stat.appliesToSettlements)
                    {
                        double final = faction.GetStatValue(stat, settlement);
                        double settlementPart = settlement.GetSettlementStatValue(stat);
                        double factionPart = faction.GetFactionStatValue(stat);
                        if (Math.Abs(final - stat.IdentityValue) < 0.001
                            && Math.Abs(settlementPart - stat.IdentityValue) < 0.001
                            && Math.Abs(factionPart - stat.IdentityValue) < 0.001)
                        {
                            defaultCount++;
                            continue;
                        }
                        string agg = stat.aggregation == FCStatAggregation.Additive ? "Add" : "Mult";
                        LogUtil.MessageForce($"  {stat.defName}: Final={final:F2} | Settlement={settlementPart:F2} | Faction={factionPart:F2} ({agg})");
                    }
                    else
                    {
                        double val = faction.GetFactionStatValue(stat);
                        if (Math.Abs(val - stat.IdentityValue) < 0.001)
                        {
                            defaultCount++;
                            continue;
                        }
                        LogUtil.MessageForce($"  {stat.defName}: {val:F2} (faction-only)");
                    }
                }
                LogUtil.MessageForce($"  ({defaultCount} stats at default value)");
            });
        }

        [DebugAction("Empire", "Log Stat Breakdown", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogStatBreakdown()
        {
            WithSettlementChoice(settlement =>
            {
                List<DebugMenuOption> list = new List<DebugMenuOption>();
                foreach (FCStatDef stat in DefDatabase<FCStatDef>.AllDefsListForReading)
                {
                    FCStatDef localStat = stat;
                    list.Add(new DebugMenuOption(localStat.defName, DebugMenuOptionMode.Action, () =>
                    {
                        FactionFC faction = FactionCache.FactionComp;
                        string agg = localStat.aggregation == FCStatAggregation.Additive ? "Additive" : "Multiplicative";
                        LogUtil.MessageForce($"--- Stat Breakdown: {localStat.defName} ({agg}, default={localStat.IdentityValue}) ---");

                        // Settlement-level modifiers
                        foreach (FCStatModifier mod in settlement.StatModifiers)
                        {
                            if (mod.stat == localStat)
                                LogUtil.MessageForce($"  Settlement modifier: {mod.value:F2}");
                        }

                        // IStatModifierProvider comps
                        foreach (WorldObjectComp comp in settlement.AllComps)
                        {
                            if (comp is IStatModifierProvider provider)
                            {
                                double compVal = provider.GetStatModifier(localStat);
                                if (Math.Abs(compVal - (localStat.aggregation == FCStatAggregation.Additive ? 0 : 1)) > 0.001)
                                    LogUtil.MessageForce($"  Comp ({comp.GetType().Name}): {compVal:F2}");
                            }
                        }
                        LogUtil.MessageForce($"  Settlement partial = {settlement.GetSettlementStatValue(localStat):F2}");

                        // Faction-level (policies + traits)
                        foreach (FCPolicy p in faction.policies)
                        {
                            if (p?.def == null) continue;
                            foreach (FCStatModifier mod in p.def.statModifiers)
                            {
                                if (mod.stat == localStat)
                                    LogUtil.MessageForce($"  Policy ({p.def.defName}): {mod.value:F2}");
                            }
                        }
                        foreach (FCPolicy p in faction.factionTraits)
                        {
                            if (p?.def == null || p.def == FCPolicyDefOf.empty) continue;
                            foreach (FCStatModifier mod in p.def.statModifiers)
                            {
                                if (mod.stat == localStat)
                                    LogUtil.MessageForce($"  Trait ({p.def.defName}): {mod.value:F2}");
                            }
                        }
                        LogUtil.MessageForce($"  Faction partial = {faction.GetFactionStatValue(localStat):F2}");

                        // Behavior contributions
                        foreach (FCPolicyBehavior b in faction.cachedBehaviors)
                        {
                            string desc = b.GetStatDescription(localStat, settlement);
                            if (!desc.NullOrEmpty())
                                LogUtil.MessageForce($"  Behavior ({b.GetType().Name}): {desc.TrimEnd()}");
                        }

                        double final = faction.GetStatValue(localStat, settlement);
                        LogUtil.MessageForce($"  FINAL = {final:F2}");
                    }));
                }
                Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
            });
        }

        [DebugAction("Empire", "Log Faction Stats", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogFactionStatValues()
        {
            FactionFC faction = FactionCache.FactionComp;
            if (faction == null) return;

            LogUtil.MessageForce("--- Faction-Level Stat Values ---");
            int defaultCount = 0;
            foreach (FCStatDef stat in DefDatabase<FCStatDef>.AllDefsListForReading)
            {
                double val = faction.GetFactionStatValue(stat);
                if (Math.Abs(val - stat.IdentityValue) < 0.001)
                {
                    defaultCount++;
                    continue;
                }
                string agg = stat.aggregation == FCStatAggregation.Additive ? "Add" : "Mult";
                LogUtil.MessageForce($"  {stat.defName} = {val:F2} ({agg}, default={stat.IdentityValue})");
            }
            LogUtil.MessageForce($"  ({defaultCount} stats at default value)");
        }

        // ============================
        // Military Debug Actions
        // ============================

        [DebugAction("Empire", "Log Military Status", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogMilitaryStatus()
        {
            foreach (WorldSettlementFC s in FactionCache.FactionComp.settlements)
            {
                var comp = s.MilitaryComp;
                if (comp == null)
                {
                    LogUtil.MessageForce($"[{s.Name}] MilitaryComp: null");
                    continue;
                }
                List<MercenarySquadFC> stationed = s.StationedSquads;
                string squadInfo;
                if (stationed.Count == 0)
                {
                    squadInfo = "No squad";
                }
                else
                {
                    int deployedCount = 0;
                    for (int i = 0; i < stationed.Count; i++)
                    {
                        if (stationed[i] != null && stationed[i].Deployment.IsPhysicallyDeployed()) deployedCount++;
                    }
                    squadInfo = $"Squads:{stationed.Count} Deployed:{deployedCount} Job:{comp.militaryJob}";
                }
                LogUtil.MessageForce($"[{s.Name}] MilLv:{s.settlementMilitaryLevel} Busy:{comp.militaryBusy} | {squadInfo}");
            }
        }

        [DebugAction("Empire", "Force Return Settlement Military", allowedGameStates = AllowedGameStates.Playing)]
        private static void ForceReturnSettlementMilitary()
        {
            WithSettlementChoice(settlement =>
            {
                if (settlement.MilitaryComp != null)
                {
                    settlement.MilitaryComp.ReturnMilitary(true);
                    LogUtil.MessageForce($"Debug - Force returned military for {settlement.Name}");
                }
                else
                {
                    LogUtil.MessageForce($"Debug - {settlement.Name} has no MilitaryComp");
                }
            });
        }

        [DebugAction("Empire", "Clear Orphaned Deployments", allowedGameStates = AllowedGameStates.Playing)]
        private static void ForceCheckOrphanedDeploys()
        {
            int cleared = 0;
            foreach (WorldSettlementFC settlement in FactionCache.FactionComp.settlements)
            {
                var comp = settlement.MilitaryComp;
                if (comp is null) continue;
                if (!comp.militaryBusy || comp.militaryJob != MilitaryJobDefOf.DefendFriendlySettlement) continue;
                if (comp.IsStaleDeploy())
                {
                    comp.ReturnMilitary(false);
                    cleared++;
                    LogUtil.MessageForce($"  Cleared stale DefendFriendlySettlement on {settlement.Name}");
                }
            }
            LogUtil.MessageForce($"Clear Orphaned Deployments: cleared {cleared}.");
        }

        [DebugAction("Empire", "Run Military Error Check", allowedGameStates = AllowedGameStates.Playing)]
        private static void RunMilitaryErrorCheck()
        {
            LogUtil.MessageForce("Debug - Running military error check");
            FactionCache.FactionComp.militaryCustomizationUtil.CheckMilitaryUtilForErrors();
            LogUtil.MessageForce("Debug - Military error check complete");
        }

        [DebugAction("Empire", "Heal Squad to Full", allowedGameStates = AllowedGameStates.Playing)]
        private static void HealSquadToFull()
        {
            WithSquadChoice(squad =>
            {
                int healed = 0;
                IEnumerable<Mercenary> all = (squad.mercenaries ?? Enumerable.Empty<Mercenary>())
                    .Concat(squad.animals ?? Enumerable.Empty<Mercenary>());
                foreach (Mercenary merc in all)
                {
                    // Skip empty slots and dead/destroyed pawns — there is no revival system,
                    // dead mercs are already empty slots. HealPawn does merc.pawn.health.Reset().
                    if (merc?.pawn is null || merc.pawn.Dead || merc.pawn.Destroyed) continue;
                    SquadHealthUtil.HealPawn(merc);
                    healed++;
                }
                LogUtil.MessageForce($"Debug - Healed {healed} pawn(s) in squad {squad.DisplayName}");
            });
        }

        // ============================
        // Faction / Economy Debug Actions
        // ============================

        [DebugAction("Empire", "Log Faction Status", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogFactionStatus()
        {
            FactionFC f = FactionCache.FactionComp;
            LogUtil.MessageForce($"Faction Lv{f.factionLevel} | XP:{f.factionXPCurrent:F0}/{f.factionXPGoal:F0}");
            LogUtil.MessageForce($"Settlements:{f.settlements.Count} | Income:{f.income:F0} Upkeep:{f.upkeep:F0} Profit:{f.profit:F0}");
            LogUtil.MessageForce($"TaxDue:{f.taxTimeDue - Find.TickManager.TicksGame} ticks | MilDue:{f.militaryTimeDue - Find.TickManager.TicksGame} ticks");
            LogUtil.MessageForce($"AvgHappy:{f.averageHappiness:F0} AvgLoyal:{f.averageLoyalty:F0} AvgUnrest:{f.averageUnrest:F0} AvgProsper:{f.averageProsperity:F0}");
            LogUtil.MessageForce($"Policies:{f.policies.Count} | Traits:{f.factionTraits.Count} | ResearchPool:{f.GetResourcePoolValue(ResourceTypeDefOf.RTD_Research):F0}");
            if (f.factionTraits.Any())
            {
                LogUtil.MessageForce($"Trait list: {f.factionTraits.Select(t => t.def?.defName).ToCommaList()}");
            }
            if (f.policies.Any())
            {
                LogUtil.MessageForce($"Policy list: {f.policies.Select(t => t.def?.defName).ToCommaList()}");
            }
        }

        [DebugAction("Empire", "Add Faction XP", allowedGameStates = AllowedGameStates.Playing)]
        private static void AddFactionXP()
        {
            FactionFC faction = FactionCache.FactionComp;
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (int amount in new[] { 100, 500, 1000 })
            {
                int localAmount = amount;
                list.Add(new DebugMenuOption($"+{localAmount} XP", DebugMenuOptionMode.Action, () =>
                {
                    faction.AddExperienceToFactionLevel(localAmount);
                    LogUtil.MessageForce($"Debug - Added {localAmount} XP (now {faction.factionXPCurrent:F0}/{faction.factionXPGoal:F0})");
                }));
            }
            float remaining = faction.factionXPGoal - faction.factionXPCurrent;
            list.Add(new DebugMenuOption($"+{remaining:F0} XP (to next level)", DebugMenuOptionMode.Action, () =>
            {
                faction.AddExperienceToFactionLevel(remaining);
                LogUtil.MessageForce($"Debug - Added {remaining:F0} XP to reach next level");
            }));
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        [DebugAction("Empire", "Proc Tax Due", allowedGameStates = AllowedGameStates.Playing)]
        private static void ProcTaxDue()
        {
            LogUtil.MessageForce("Debug - Proc TaxTimeDue");
            FactionCache.FactionComp.taxTimeDue = Find.TickManager.TicksGame + 1;
        }

        [DebugAction("Empire", "Log Resource Production", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogResourceProduction()
        {
            WithSettlementChoice(settlement =>
            {
                LogUtil.MessageForce($"--- Resources for {settlement.Name} ---");
                foreach (ResourceFC r in settlement.Resources)
                {
                    LogUtil.MessageForce($"  {r.def.defName}: Workers:{r.assignedWorkers} Base:{r.productionBase:F2} Mult:{r.productionMult:F2} Production:{r.production:F2} Income:{r.actualIncome:F2}");
                }
            });
        }

        // ============================
        // Events Debug Actions
        // ============================

        [DebugAction("Empire", "Force Trigger Event", allowedGameStates = AllowedGameStates.Playing)]
        private static void ForceTriggerEvent()
        {
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (FCEvent evt in FactionCache.FactionComp.Events)
            {
                FCEvent localEvt = evt;
                int ticksLeft = localEvt.timeTillTrigger - Find.TickManager.TicksGame;
                list.Add(new DebugMenuOption(
                    $"{localEvt.def.defName} (in {ticksLeft} ticks)",
                    DebugMenuOptionMode.Action, () =>
                    {
                        localEvt.timeTillTrigger = Find.TickManager.TicksGame + 1;
                        LogUtil.MessageForce($"Debug - Force triggering {localEvt.def.defName}");
                    }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        // ============================
        // Road Debug Actions
        // ============================

        [DebugAction("Empire", "Build Road Segment Now", allowedGameStates = AllowedGameStates.Playing)]
        private static void BuildRoadSegmentNow()
        {
            var rb = FactionCache.FactionComp.roadBuilder;
            if (rb.roadDef == null)
            {
                LogUtil.MessageForce("Debug - No road research completed yet");
                return;
            }
            if (rb.roadQueue == null)
            {
                LogUtil.MessageForce("Debug - No road queue exists");
                return;
            }
            rb.roadQueue.nextRoadTick = Find.TickManager.TicksGame;
            rb.roadQueue.ProcessOnePath();
            bool built = rb.roadQueue.BuildRoadSegments();
            LogUtil.MessageForce($"Debug - Build Road Segment Now: {(built ? "segment built" : "no segment to build")}");
        }

        [DebugAction("Empire", "Build 10 Road Segments", allowedGameStates = AllowedGameStates.Playing)]
        private static void BuildTenRoadSegments()
        {
            var rb = FactionCache.FactionComp.roadBuilder;
            if (rb.roadDef is null)
            {
                LogUtil.MessageForce("Debug - No road research completed yet");
                return;
            }
            if (rb.roadQueue is null)
            {
                LogUtil.MessageForce("Debug - No road queue exists");
                return;
            }

            int built = 0;
            for (int i = 0; i < 10; i++)
            {
                rb.roadQueue.nextRoadTick = Find.TickManager.TicksGame;
                rb.roadQueue.ProcessOnePath();
                if (!rb.roadQueue.BuildRoadSegments()) break;
                built++;
            }
            LogUtil.MessageForce($"Debug - Built {built}/10 road segments.");
        }

        // ============================
        // Policy Debug Actions
        // ============================

        [DebugAction("Empire", "Enact Policy (Debug)", allowedGameStates = AllowedGameStates.Playing)]
        private static void EnactPolicyDebug()
        {
            FactionFC faction = FactionCache.FactionComp;
            if (faction == null) return;

            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (FCPolicyDef def in DefDatabase<FCPolicyDef>.AllDefsListForReading)
            {
                if (def == FCPolicyDefOf.empty) continue;
                if (def.category != FCPolicyCategory.Core) continue;
                FCPolicyDef local = def;
                string status = faction.policies.Any(p => p.def == local) ? " [ACTIVE]" : "";
                list.Add(new DebugMenuOption($"{local.defName}{status}", DebugMenuOptionMode.Action, () =>
                {
                    var policy = new FCPolicy(local);
                    faction.policies.Add(policy);
                    faction.RebuildBehaviorCache();
                    LogUtil.MessageForce($"Debug - Enacted policy: {local.defName} (behavior: {(policy.behavior != null ? policy.behavior.GetType().Name : "none")})");
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        [DebugAction("Empire", "Enact Trait (Debug)", allowedGameStates = AllowedGameStates.Playing)]
        private static void EnactTraitDebug()
        {
            FactionFC faction = FactionCache.FactionComp;
            if (faction == null) return;

            List<DebugMenuOption> traitList = new List<DebugMenuOption>();
            foreach (FCPolicyDef def in DefDatabase<FCPolicyDef>.AllDefsListForReading)
            {
                if (def == FCPolicyDefOf.empty) continue;
                if (def.category != FCPolicyCategory.Trait) continue;
                FCPolicyDef local = def;
                traitList.Add(new DebugMenuOption(local.defName, DebugMenuOptionMode.Action, () =>
                {
                    List<DebugMenuOption> slotList = new List<DebugMenuOption>();
                    for (int i = 0; i < faction.factionTraits.Count; i++)
                    {
                        int slot = i;
                        string current = faction.factionTraits[slot]?.def?.defName ?? "empty";
                        slotList.Add(new DebugMenuOption($"Slot {slot} [{current}]", DebugMenuOptionMode.Action, () =>
                        {
                            if (faction.factionTraits[slot]?.behavior != null)
                            {
                                try { faction.factionTraits[slot].behavior.OnRemoved(faction); }
                                catch (Exception e) { LogUtil.Error($"OnRemoved error: {e}"); }
                            }
                            var trait = new FCPolicy(local);
                            faction.factionTraits[slot] = trait;
                            faction.RebuildBehaviorCache();
                            LogUtil.MessageForce($"Debug - Set trait slot {slot} to: {local.defName}");
                        }));
                    }
                    Find.WindowStack.Add(new Dialog_DebugOptionListLister(slotList));
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(traitList));
        }

        [DebugAction("Empire", "Instantly Enact Edict", allowedGameStates = AllowedGameStates.Playing)]
        private static void InstantlyEnactEdict()
        {
            FactionFC faction = FactionCache.FactionComp;
            if (faction == null) return;

            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (FCPolicyDef def in DefDatabase<FCPolicyDef>.AllDefsListForReading)
            {
                if (!def.IsEdict) continue;
                FCPolicyDef local = def;
                FCPolicy existing;
                faction.edicts.TryGetValue(local.category, out existing);
                string status = (existing != null && existing.def == local) ? " [ACTIVE]" : "";
                list.Add(new DebugMenuOption($"[{local.category}] {local.LabelCap}{status}", DebugMenuOptionMode.Action, () =>
                {
                    faction.EnactEdict(local);
                    FCPolicy edict;
                    if (faction.edicts.TryGetValue(local.category, out edict))
                    {
                        edict.timeEnacted = Find.TickManager.TicksGame - local.enactDuration;
                        faction.InvalidateFactionStatCache();
                        faction.DirtyFactionProfitCache();
                    }
                    LogUtil.MessageForce($"Debug - Instantly enacted edict: {local.defName}");
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        [DebugAction("Empire", "Log Policy Behavior State", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogPolicyBehaviorState()
        {
            FactionFC faction = FactionCache.FactionComp;
            if (faction == null) return;

            LogUtil.MessageForce("=== Policy Behavior State ===");

            foreach (FCPolicy p in faction.policies)
            {
                if (p?.behavior == null)
                {
                    LogUtil.MessageForce($"[Policy] {p?.def?.defName ?? "null"}: no behavior");
                    continue;
                }
                LogBehaviorState("Policy", p);
            }

            for (int i = 0; i < faction.factionTraits.Count; i++)
            {
                FCPolicy p = faction.factionTraits[i];
                if (p?.def == null || p.def == FCPolicyDefOf.empty) continue;
                if (p.behavior == null)
                {
                    LogUtil.MessageForce($"[Trait {i}] {p.def.defName}: no behavior");
                    continue;
                }
                LogBehaviorState($"Trait {i}", p);
            }
        }

        private static void LogBehaviorState(string prefix, FCPolicy p)
        {
            string behaviorType = p.behavior.GetType().Name;

            if (p.behavior is FCPolicyBehavior_Militaristic mil)
            {
                LogUtil.MessageForce($"[{prefix}] {p.def.defName} ({behaviorType}): extraSquadCooldown Ready={mil.DebugCooldownReady()} Days={mil.DebugCooldownDays():F1}");
            }
            else if (p.behavior is FCPolicyBehavior_Pacifist pac)
            {
                LogUtil.MessageForce($"[{prefix}] {p.def.defName} ({behaviorType}): diplomatCooldown Ready={pac.DebugCooldownReady()} Days={pac.DebugCooldownDays():F1}");
            }
            else if (p.behavior is FCPolicyBehavior_Feudal feu)
            {
                LogUtil.MessageForce($"[{prefix}] {p.def.defName} ({behaviorType}): mercenaryCooldown Ready={feu.DebugCooldownReady()} Days={feu.DebugCooldownDays():F1}");
            }
            else if (p.behavior is FCPolicyBehavior_Egalitarian egal)
            {
                LogUtil.MessageForce($"[{prefix}] {p.def.defName} ({behaviorType}): taxBreaks={egal.DebugTaxBreakCount()} active={egal.DebugActiveTaxBreakCount()}");
            }
            else if (p.behavior is FCPolicyBehavior_Mercantile merc)
            {
                int ticksUntil = merc.DebugNextCaravanTick() - Find.TickManager.TicksGame;
                float daysUntil = ticksUntil / (float)GenDate.TicksPerDay;
                LogUtil.MessageForce($"[{prefix}] {p.def.defName} ({behaviorType}): nextCaravan in {daysUntil:F1} days ({ticksUntil} ticks)");
            }
            else
            {
                LogUtil.MessageForce($"[{prefix}] {p.def.defName} ({behaviorType}): (no inspectable state)");
            }
        }

        [DebugAction("Empire", "Force Policy Cooldowns Ready", allowedGameStates = AllowedGameStates.Playing)]
        private static void ForcePolicyCooldownsReady()
        {
            FactionFC faction = FactionCache.FactionComp;
            if (faction == null) return;

            int count = 0;
            foreach (FCPolicyBehavior b in faction.cachedBehaviors)
            {
                if (b is FCPolicyBehavior_Militaristic mil) { mil.DebugResetCooldown(); count++; }
                else if (b is FCPolicyBehavior_Pacifist pac) { pac.DebugResetCooldown(); count++; }
                else if (b is FCPolicyBehavior_Feudal feu) { feu.DebugResetCooldown(); count++; }
                else if (b is FCPolicyBehavior_Mercantile merc) { merc.DebugResetNextCaravan(); count++; }
            }
            LogUtil.MessageForce($"Debug - Reset {count} policy cooldowns to ready");
        }

        [DebugAction("Empire", "Trigger Policy Hook", allowedGameStates = AllowedGameStates.Playing)]
        private static void TriggerPolicyHook()
        {
            FactionFC faction = FactionCache.FactionComp;
            if (faction == null) return;

            List<DebugMenuOption> hookList = new List<DebugMenuOption>();

            hookList.Add(new DebugMenuOption("OnSettlementCreated", DebugMenuOptionMode.Action, () =>
            {
                WithSettlementChoice(settlement =>
                {
                    faction.ForEachBehavior(b => b.OnSettlementCreated(faction, settlement));
                    LogUtil.MessageForce($"Debug - Triggered OnSettlementCreated on {settlement.Name}");
                });
            }));

            hookList.Add(new DebugMenuOption("OnSettlementRemoved", DebugMenuOptionMode.Action, () =>
            {
                WithSettlementChoice(settlement =>
                {
                    faction.ForEachBehavior(b => b.OnSettlementRemoved(faction, settlement));
                    LogUtil.MessageForce($"Debug - Triggered OnSettlementRemoved on {settlement.Name}");
                });
            }));

            hookList.Add(new DebugMenuOption("OnSquadDeployed", DebugMenuOptionMode.Action, () =>
            {
                WithSettlementChoice(settlement =>
                {
                    // Debug trigger has no real op — behaviors that inspect op must null-check.
                    faction.ForEachBehavior(b => b.OnSquadDeployed(faction, null, settlement, false));
                    LogUtil.MessageForce($"Debug - Triggered OnSquadDeployed on {settlement.Name}");
                });
            }));

            hookList.Add(new DebugMenuOption("OnSquadRecalled", DebugMenuOptionMode.Action, () =>
            {
                WithSettlementChoice(settlement =>
                {
                    faction.ForEachBehavior(b => b.OnSquadRecalled(faction, null, settlement));
                    LogUtil.MessageForce($"Debug - Triggered OnSquadRecalled on {settlement.Name}");
                });
            }));

            hookList.Add(new DebugMenuOption("OnTaxCollected", DebugMenuOptionMode.Action, () =>
            {
                WithSettlementChoice(settlement =>
                {
                    faction.ForEachBehavior(b => b.OnTaxCollected(faction, settlement));
                    LogUtil.MessageForce($"Debug - Triggered OnTaxCollected on {settlement.Name}");
                });
            }));

            hookList.Add(new DebugMenuOption("OnSettlementCostPaid", DebugMenuOptionMode.Action, () =>
            {
                faction.ForEachBehavior(b => b.OnSettlementCostPaid(faction));
                LogUtil.MessageForce("Debug - Triggered OnSettlementCostPaid");
            }));

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(hookList));
        }

        // ============================
        // Road Debug Actions
        // ============================

        [DebugAction("Empire", "Log Road Builder Status", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogRoadBuilderStatus()
        {
            var rb = FactionCache.FactionComp.roadBuilder;
            LogUtil.MessageForce($"Road Builder: Enabled:{rb.roadBuildingEnabled} RoadDef:{rb.roadDef?.defName ?? "null"} DaysBetweenTicks:{rb.daysBetweenTicks}");
            if (rb.roadQueue != null)
            {
                var rq = rb.roadQueue;
                LogUtil.MessageForce($"Road Queue: NextTick:{rq.nextRoadTick - Find.TickManager.TicksGame} ticks | FromTiles:{rq.lastFromTileCount} ToTiles:{rq.lastToTileCount} Paths:{rq.roadPaths.Count} NeedsUpdate:{rq.shouldUpdateSettlementsToProcess}");
            }
            else
            {
                LogUtil.MessageForce("Road Queue: null");
            }
        }

        [DebugAction("Empire", "Validate Settlement Caravan List", allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugValidateSettlementCaravanList()
        {
            LogUtil.MessageForce($"Validating settlement caravan list...");
            FactionCache.FactionComp?.ValidateSettlementCaravansList();
        }

        [DebugAction("Empire", "Fire Support (Pick Source)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FireSupportPickSource()
        {
            MilitaryCustomizationUtil util = FactionCache.FactionComp.militaryCustomizationUtil;
            if (util.fireSupportDefs == null || !util.fireSupportDefs.Any())
            {
                Messages.Message("No fire support definitions configured.", MessageTypeDefOf.RejectInput);
                return;
            }

            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (MilitaryFireSupport support in util.fireSupportDefs)
            {
                if (support.projectiles == null || !support.projectiles.Any()) continue;
                MilitaryFireSupport localSupport = support;
                list.Add(new DebugMenuOption(
                    $"{localSupport.name} ({localSupport.projectiles.Count} shells, acc {localSupport.accuracy})",
                    DebugMenuOptionMode.Action, () =>
                    {
                        DebugTools.curTool = new DebugTool("Select target location", () =>
                        {
                            IntVec3 targetLocation = UI.MouseCell();
                            DebugTools.curTool = new DebugTool("Select source (edge) location", () =>
                            {
                                IntVec3 sourceLocation = UI.MouseCell();
                                Map map = Find.CurrentMap;

                                List<ThingDef> projectiles = new List<ThingDef>();
                                projectiles.AddRange(localSupport.projectiles);

                                MilitaryFireSupport fireSupport = new MilitaryFireSupport(
                                    "fireSupport", map, targetLocation,
                                    projectiles.Count * 15, 600, localSupport.accuracy, projectiles);
                                fireSupport.sourceLocation = sourceLocation;
                                util.fireSupport.Add(fireSupport);

                                float dist = sourceLocation.DistanceTo(targetLocation);
                                LogUtil.MessageForce($"Debug - Fire Support '{localSupport.name}' " +
                                    $"from ({sourceLocation.x},{sourceLocation.z}) " +
                                    $"to ({targetLocation.x},{targetLocation.z}) " +
                                    $"distance: {dist:F1} cells");

                                DebugTools.curTool = null;
                            });
                        });
                    }));
            }

            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }

        [DebugAction("Empire", "Force Restock Settlement Trader", allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugForceRestockSettlementTrader()
        {
            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (WorldSettlementFC settlement in FactionCache.FactionComp.settlements)
            {
                options.Add(new DebugMenuOption(settlement.Name, DebugMenuOptionMode.Action, () =>
                {
                    settlement.trader?.TryDestroyStock();
                    LogUtil.MessageForce($"Destroyed trader stock for {settlement.Name}. Will regenerate on next trade access.");
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }
    }
}
