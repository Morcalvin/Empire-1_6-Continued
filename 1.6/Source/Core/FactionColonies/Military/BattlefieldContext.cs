using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace FactionColonies
{
    /// <summary>
    /// Per-tile shared object that owns the battle map, pawn lists, lords, and wave state for
    /// every <see cref="MilitaryOperation"/> targeting a single tile.
    /// <para>One <c>BattlefieldContext</c> exists per active battle tile, regardless of how many
    /// concurrent ops it serves. When two ops target the same tile (e.g. two attackers on one
    /// settlement) they share the map: the second op's pawns spawn into the existing map and
    /// either join the existing attacker lord or get a fresh one if the previous lord was
    /// destroyed.</para>
    /// <para>Lifecycle: a context outlives any single op. After the last op detaches, if the
    /// player is still on the map the context enters <see cref="awaitingPlayerExit"/>; a new
    /// op arriving in that state re-engages by calling <see cref="TryReengage"/>. The context
    /// is destroyed once <see cref="activeOps"/> is empty AND the player has left.</para>
    /// </summary>
    public class BattlefieldContext : IExposable
    {
        public PlanetTile tile = PlanetTile.Invalid;

        /// <summary>The battle map. Lazy: created when the first op transitions to Engaged on this tile.</summary>
        public Map map;

        /// <summary>Lord for the attacking side. May be null between waves; re-created when needed.</summary>
        public Lord attackerLord;

        /// <summary>Lord for the defending side. May be null between waves; re-created when needed.</summary>
        public Lord defenderLord;

        /// <summary>Ops currently using this battlefield. The context cannot be destroyed while non-empty.</summary>
        public List<MilitaryOperation> activeOps = new List<MilitaryOperation>();

        /// <summary>True when the last op has resolved but the player is still on-map.
        /// A new op arriving in this state triggers <see cref="TryReengage"/>.</summary>
        public bool awaitingPlayerExit;

        /* -*-*-*-*- Battle state -*-*-*-*-
         * Flat aggregations of all on-map pawns at this tile, summed across every active op.
         * Per-op pawn ownership lives on op.aggressor.pawns / op.defender.pawns; these flat lists
         * are kept for cheap iteration in Tick / RemoveAttacker / RemoveDefender / DeleteMap.
         */

        public List<Pawn> attackerPawns = new List<Pawn>();
        public List<Pawn> defenderPawns = new List<Pawn>();
        public List<Pawn> draftedNPCs = new List<Pawn>();

        public bool battleMapInitialized;
        public bool endingBattle;
        public bool shuttleLandingPending;
        public int initialDefenderCount;
        public string pendingDeliveryMessage;

        /* Scribe scratch buffers for Lord cross-refs (RimWorld's Scribe_Collections needs
         * working lists during load). Pawn lists use LookMode.Reference. */

        public BattlefieldContext() { }

        public BattlefieldContext(PlanetTile tile)
        {
            this.tile = tile;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref tile, "tile", PlanetTile.Invalid);
            Scribe_References.Look(ref map, "map");
            Scribe_References.Look(ref attackerLord, "attackerLord");
            Scribe_References.Look(ref defenderLord, "defenderLord");
            Scribe_Collections.Look(ref activeOps, "activeOps", LookMode.Reference);
            Scribe_Values.Look(ref awaitingPlayerExit, "awaitingPlayerExit", false);

            Scribe_Collections.Look(ref attackerPawns, "attackerPawns", LookMode.Reference);
            Scribe_Collections.Look(ref defenderPawns, "defenderPawns", LookMode.Reference);
            Scribe_Collections.Look(ref draftedNPCs, "draftedNPCs", LookMode.Reference);
            Scribe_Values.Look(ref battleMapInitialized, "battleMapInitialized", false);
            Scribe_Values.Look(ref endingBattle, "endingBattle", false);
            Scribe_Values.Look(ref shuttleLandingPending, "shuttleLandingPending", false);
            Scribe_Values.Look(ref initialDefenderCount, "initialDefenderCount", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (activeOps is null) activeOps = new List<MilitaryOperation>();
                if (attackerPawns is null) attackerPawns = new List<Pawn>();
                if (defenderPawns is null) defenderPawns = new List<Pawn>();
                if (draftedNPCs is null) draftedNPCs = new List<Pawn>();
            }
        }

        /* -*-*-*-*- Lookups -*-*-*-*- */

        /// <summary>The Empire settlement at this battlefield's tile, or null for non-Empire raid targets.</summary>
        public WorldSettlementFC ParentSettlement => Find.WorldObjects.WorldObjectAt<WorldSettlementFC>(tile);

        /// <summary>The Empire settlement's military comp, or null.</summary>
        public WorldObjectComp_SettlementMilitary ParentMilitaryComp => ParentSettlement?.MilitaryComp;

        /* -*-*-*-*- Tick -*-*-*-*-
         * Per-tick battlefield housekeeping: orphan flag clearing, stale pawn cleanup, untracked
         * player pawn capture, stuck-battle detection. Called once per game tick by the comp.
         */

        public void Tick()
        {
            WorldSettlementFC settlement = ParentSettlement;
            if (settlement is null) return;

            bool isUnderAttack = FactionCache.MilitaryManager?.HasDefenseAt(settlement) ?? false;
            if (!isUnderAttack) return;
            if (endingBattle) return;

            int ticks = Find.TickManager.TicksGame;

            // Periodic orphan flag clearing: isUnderAttack is true but no map / no combatants
            // and no warning event in queue.
            if (ticks % 2500 == 0 && map is null && attackerPawns.Count == 0 && defenderPawns.Count == 0)
            {
                FCEvent evt = MilitaryUtilFC.ReturnMilitaryEventByLocation(settlement.Tile);
                if (evt is null)
                {
                    LogUtil.Warning($"Clearing orphaned isUnderAttack flag on {settlement.Name} " +
                        $"(no matching settlementBeingAttacked event in queue).");
                    ParentMilitaryComp?.ClearAttackState();
                    return;
                }
            }

            if (ticks % 250 != 0) return;
            if (map == null) return;

            // Clean stale references: null (save/load), destroyed, or despawned-without-holder.
            // Pod-bound pawns in a descending Skyfaller are !Spawned but ParentHolder != null;
            // they stay tracked until the pod opens.
            attackerPawns.RemoveAll(IsPawnTrulyGone);
            defenderPawns.RemoveAll(IsPawnTrulyGone);

            // Detect untracked player pawns on the battle map (e.g. shuttle-delivered pawns
            // that spawned via the Unload job after the ArrivePatch fired).
            Lord battleLord = null;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Dead || pawn.Downed) continue;
                if (!defenderPawns.Contains(pawn))
                {
                    LogUtil.Warning($"Registering untracked player pawn {pawn.LabelShort} with defense at {settlement.Name}");
                    defenderPawns.Add(pawn);
                    initialDefenderCount++;
                }
                if (pawn.GetLord() is null)
                {
                    if (battleLord is null)
                        battleLord = defenderPawns.FirstOrDefault(d => d.GetLord() != null)?.GetLord();
                    if (battleLord != null && !battleLord.ownedPawns.Contains(pawn))
                        battleLord.AddPawn(pawn);
                }
            }

            // Don't declare stuck if attackers are still inbound in drop pods.
            bool attackersGone = attackerPawns.Count == 0 && !HasPendingPodAttackers();
            if (attackersGone || defenderPawns.Count == 0)
            {
                LogUtil.Warning($"Stuck battle detected at {settlement.Name}, forcing resolution.");
                endingBattle = true;
                LongEventHandler.QueueLongEvent(EndAttack,
                    "EndingAttack", false, error =>
                    {
                        DelayedErrorWindowRequest.Add("FCErrorEndingAttack".Translate(),
                            "FCErrorEndingAttackDescription".Translate());
                        LogUtil.Error(error.Message);
                    });
            }
        }

        /* -*-*-*-*- Static helpers -*-*-*-*- */

        /// <summary>Pod-bound pawns during Skyfaller descent are <c>!Spawned</c> but
        /// <c>ParentHolder != null</c>. A pure <c>!Spawned</c> check treats them as lost and
        /// triggers false victory.</summary>
        public static bool IsPawnTrulyGone(Pawn p)
        {
            if (p is null) return true;
            if (p.Destroyed) return true;
            if (p.Spawned) return false;
            if (p.ParentHolder is object) return false;
            return true;
        }

        public static IntVec3 FindNearEdgeCell(Map map)
        {
            bool BaseValidator(IntVec3 x)
            {
                return x.Standable(map) && !x.Fogged(map);
            }

            var hostFaction = map.ParentFaction;
            if (CellFinder.TryFindRandomEdgeCellWith(x =>
            {
                if (!BaseValidator(x))
                    return false;
                if (hostFaction != null && map.reachability.CanReachFactionBase(x, hostFaction))
                    return true;
                return hostFaction == null && map.reachability.CanReachBiggestMapEdgeDistrict(x);
            }, map, CellFinder.EdgeRoadChance_Neutral, out var result))
                return CellFinder.RandomClosewalkCellNear(result, map, 5);
            if (CellFinder.TryFindRandomEdgeCellWith(BaseValidator, map, CellFinder.EdgeRoadChance_Neutral, out result))
                return CellFinder.RandomClosewalkCellNear(result, map, 5);
            LogUtil.Warning("Could not find any valid edge cell.");
            return CellFinder.RandomCell(map);
        }

        private static PawnsArrivalModeDef ResolveRaidArriveMode(IncidentParms parms)
        {
            return parms.raidStrategy.arriveModes
                .Where(mode => mode.Worker.CanUseWith(parms))
                .TryRandomElementByWeight(mode => mode.Worker.GetSelectionWeight(parms), out PawnsArrivalModeDef output)
                ? output
                : PawnsArrivalModeDefOf.EdgeWalkIn;
        }

        public bool HasPendingPodAttackers()
        {
            if (activeOps is null) return false;
            foreach (MilitaryOperation op in activeOps)
            {
                List<Pawn> list = op?.aggressor?.pawns;
                if (list is null) continue;
                foreach (Pawn p in list)
                {
                    if (p is null || p.Destroyed || p.Dead) continue;
                    if (!p.Spawned && p.ParentHolder is object) return true;
                }
            }
            return false;
        }

        /* -*-*-*-*- Op lifecycle (Join/Detach) -*-*-*-*- */

        /// <summary>Register <paramref name="op"/> on this battlefield. Idempotent. Clears
        /// <see cref="awaitingPlayerExit"/>. Pawn / lord spawning is the caller's responsibility
        /// (typically via <see cref="StartDefense"/> or <see cref="SpawnParticipantOnMap"/>).</summary>
        public void Join(MilitaryOperation op)
        {
            if (op is null) return;
            if (activeOps is null) activeOps = new List<MilitaryOperation>();
            if (!activeOps.Contains(op)) activeOps.Add(op);
            awaitingPlayerExit = false;
        }

        /// <summary>Remove <paramref name="op"/> from this battlefield. If no ops remain and the
        /// player is still on the map, transitions into <see cref="awaitingPlayerExit"/>;
        /// otherwise the context is removed from the manager.</summary>
        public void Detach(MilitaryOperation op)
        {
            if (op is null) return;
            activeOps?.Remove(op);

            if (activeOps == null || activeOps.Count == 0)
            {
                bool playerOnMap = map is object && map.mapPawns?.FreeColonistsSpawnedCount > 0;
                if (playerOnMap)
                {
                    awaitingPlayerExit = true;
                }
                else
                {
                    awaitingPlayerExit = false;
                    FactionCache.MilitaryManager?.RemoveBattlefield(tile);
                }
            }
        }

        /// <summary>Spawns an op's aggressor or defender pawns onto this battlefield and attaches
        /// them to the appropriate lord. Public op-aware entry point for handlers / submods that
        /// want to inject reinforcements mid-battle.</summary>
        public void SpawnParticipantOnMap(MilitaryOperation op, ParticipantSide side)
        {
            if (op is null || map is null) return;

            if (side == ParticipantSide.Defender)
            {
                // Squad-based defender (foreign settlement reinforcement).
                if (op.defender?.squad is object)
                {
                    SpawnReinforcementsForOp(op);
                    return;
                }
                // No squad — generate fallback pawns from force points using the same path used
                // for the initial defending settlement's spawn.
                if (op.defender?.force is null) return;
                GenerateFriendlies(op);
                return;
            }

            // Aggressor side: hostile faction raid spawn.
            SpawnAttackersForOp(op);
        }

        /// <summary>Called when a new op joins while <see cref="awaitingPlayerExit"/> is true.
        /// Spawns fresh attacker pawns/lord on the existing map; if defender pawns survived,
        /// re-recruits them into a fresh defender lord.</summary>
        public void TryReengage(MilitaryOperation op)
        {
            if (op is null || map is null) return;

            awaitingPlayerExit = false;
            battleMapInitialized = true;
            endingBattle = false;

            // Re-recruit any surviving Empire defenders from idle lords back into a defense lord
            RecruitIdleDefenders();

            // Spawn fresh attackers for the new op
            SpawnAttackersForOp(op);
        }

        /* -*-*-*-*- Map generation -*-*-*-*- */

        /// <summary>Get-or-create the battle map at this tile. Idempotent.</summary>
        public Map GenerateMap()
        {
            if (map is object) return map;
            WorldSettlementFC settlement = ParentSettlement;
            if (settlement is null)
            {
                LogUtil.Error($"BattlefieldContext.GenerateMap: no Empire settlement at tile {tile}; cannot generate map.");
                return null;
            }
            int size = 70 + settlement.settlementLevel * 10;
            map = MapGenerator.GenerateMap(
                new IntVec3(size, 1, size),
                settlement, settlement.MapGeneratorDef, settlement.ExtraGenStepDefs);
            return map;
        }

        /* -*-*-*-*- Battle entry: StartDefense -*-*-*-*-
         * Called from op.OnEventFired's defensive branch (or via comp.StartDefence's facade) once
         * the warning event fires. Drives the three paths: add to existing battle, auto-resolve,
         * or fresh battle. Reads forces/factions from the op rather than the FCEvent.
         */

        public void StartDefense(MilitaryOperation op, Action after = null)
        {
            if (op is null) return;

            // Drop the op's warning event from the queue if it's still there. Op.OnEventFired
            // already strips it from sourceEvents; this removes it from the FactionFC event list.
            FactionFC factionFC = FactionCache.FactionComp;
            if (factionFC is object)
            {
                foreach (FCEvent srcEvt in op.sourceEvents)
                {
                    if (srcEvt is null) continue;
                    if (srcEvt.def == FCEventDefOf.settlementBeingAttacked)
                        factionFC.RemoveEvent(srcEvt);
                }
            }

            WorldSettlementFC settlement = ParentSettlement;
            if (settlement is null)
            {
                LogUtil.Error($"StartDefense: no Empire settlement at tile {tile}.");
                return;
            }

            bool isUnderAttack = FactionCache.MilitaryManager?.HasDefenseAt(settlement) ?? false;

            // Path 1: existing battle map active — add a new attacker group.
            if (battleMapInitialized && map is object && isUnderAttack)
            {
                LongEventHandler.QueueLongEvent(() =>
                {
                    SpawnAttackersForOp(op);
                    SpawnReinforcementsForOp(op);

                    string enemyName = op.aggressor?.faction?.Name ?? "Unknown";
                    Find.LetterStack.ReceiveLetter(
                        "FCNewWaveArrived".Translate(settlement.Name),
                        "FCNewWaveArrivedDesc".Translate(settlement.Name, enemyName),
                        LetterDefOf.ThreatBig,
                        new LookTargets(settlement));
                    after?.Invoke();
                }, "GeneratingMap", false, null);
                return;
            }

            // Auto-resolve check
            bool shouldAutoResolve = false;
            if (FCSettings.battleMode == BattleMode.Auto || !settlement.settlementDef.supportsManualBattle)
            {
                shouldAutoResolve = true;
            }
            else if (FCSettings.battleMode == BattleMode.Hybrid)
            {
                shouldAutoResolve = !battleMapInitialized && !IsPlayerCaravanOnTile();
            }
            if (!shouldAutoResolve && FCSettings.maxConcurrentBattleMaps > 0
                && CountOtherSettlementBattleMaps() >= FCSettings.maxConcurrentBattleMaps)
                shouldAutoResolve = true;

            if (shouldAutoResolve)
            {
                MilitaryForce defForce = op.defender?.force;
                MilitaryForce atkForce = op.aggressor?.force;
                if (defForce is null || atkForce is null)
                {
                    LogUtil.Warning($"StartDefense: missing force(s) for {settlement.Name} on auto-resolve path.");
                    EndBattle(false, 0, null);
                    return;
                }
                initialDefenderCount = (int)defForce.forceRemaining;
                BattleResult battleResult = SimulateBattleFc.FightBattle(atkForce, defForce);
                EndBattle(battleResult.DefenderVictory, (int)defForce.forceRemaining, battleResult);
                return;
            }

            // Path 2: post-battle map exists, player still on it — re-engage.
            if (map is object && !isUnderAttack)
            {
                battleMapInitialized = true;

                LongEventHandler.QueueLongEvent(() =>
                {
                    RecruitIdleDefenders();
                    SpawnAttackersForOp(op);
                    SpawnReinforcementsForOp(op);
                    Find.TickManager.Notify_GeneratedPotentiallyHostileMap();

                    string enemyName = op.aggressor?.faction?.Name ?? "Unknown";
                    Find.LetterStack.ReceiveLetter(
                        "FCManualBattleStarted".Translate(settlement.Name),
                        "FCManualBattleStartedDesc".Translate(settlement.Name, enemyName),
                        LetterDefOf.ThreatBig,
                        new LookTargets(settlement));
                    after?.Invoke();
                }, "GeneratingMap", false, null);
                return;
            }

            // Path 3: fresh battle — generate map.
            if (op.defender?.force is null)
            {
                LogUtil.Warning($"StartDefense: defender force is null for {settlement.Name}, settlement loses by default.");
                EndBattle(false, 0, null);
                return;
            }

            LongEventHandler.QueueLongEvent(() =>
            {
                if (map == null) GenerateMap();
                ZoomIntoTile(op);
                SetupAttack(op);
                after?.Invoke();
            }, "GeneratingMap", false, GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);
        }

        private void SetupAttack(MilitaryOperation op)
        {
            // ZoomIntoTile may have aborted via EndBattle (null force); don't spawn attackers
            // into a cleaned-up state.
            WorldSettlementFC settlement = ParentSettlement;
            if (settlement is null) return;
            bool isUnderAttack = FactionCache.MilitaryManager?.HasDefenseAt(settlement) ?? false;
            if (!isUnderAttack) return;

            if (map is null)
            {
                LogUtil.Error($"SetupAttack: {settlement.Name} has no map. Resetting battle state.");
                EndBattle(false, 0, null);
                return;
            }

            if (op?.aggressor?.force is null || op.aggressor.faction is null)
            {
                LogUtil.Error($"SetupAttack: Missing attacking force or faction for {settlement.Name}. Resetting battle state.");
                EndBattle(false, 0, null);
                return;
            }

            SpawnAttackersForOp(op);
        }

        /* -*-*-*-*- Spawning -*-*-*-*- */

        /// <summary>Spawns attackers for a single op onto the current map. Adds them to the op's
        /// <c>aggressor.pawns</c> list and to the context's flat <see cref="attackerPawns"/>
        /// aggregation. Updates <see cref="attackerLord"/> to a new <see cref="LordJob_HuntColonists"/>
        /// for the spawned group.</summary>
        private void SpawnAttackersForOp(MilitaryOperation op)
        {
            if (map is null || op?.aggressor?.force is null) return;

            IncidentParms parms = new IncidentParms
            {
                target = map,
                faction = op.aggressor.faction,
                generateFightersOnly = true,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidNeverFleeIndividual = true
            };
            parms.points = Math.Max(
                IncidentWorker_Raid.AdjustedRaidPoints(
                    (float)op.aggressor.force.forceRemaining * 175,
                    PawnsArrivalModeDefOf.EdgeWalkIn, parms.raidStrategy,
                    parms.faction, PawnGroupKindDefOf.Combat,
                    parms.target),
                300f);
            parms.raidArrivalMode = ResolveRaidArriveMode(parms) ?? PawnsArrivalModeDefOf.EdgeWalkIn;
            parms.raidArrivalMode.Worker.TryResolveRaidSpawnCenter(parms);

            List<Pawn> newAttackers = PawnGroupMakerUtility.GeneratePawns(
                IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                    PawnGroupKindDefOf.Combat, parms, true)).ToList();
            if (!newAttackers.Any())
            {
                LogUtil.Error("Got no pawns spawning attackers for op id=" + op.id + " from parms " + parms);
                if (!attackerPawns.Any())
                    LongEventHandler.QueueLongEvent(EndAttack, "EndingAttack", false, null);
                return;
            }

            double attackerEfficiency = op.aggressor.force.militaryEfficiency;
            foreach (Pawn attacker in newAttackers)
            {
                MilitaryEfficiencyUtil.ApplyCombatEfficiencyHediff(attacker, attackerEfficiency);
            }

            parms.raidArrivalMode.Worker.Arrive(newAttackers, parms);

            op.aggressor.pawns.AddRange(newAttackers);
            attackerPawns.AddRange(newAttackers);

            WorldSettlementFC settlement = ParentSettlement;
            attackerLord = LordMaker.MakeNewLord(
                parms.faction,
                new LordJob_HuntColonists(settlement, parms.raidArrivalMode != PawnsArrivalModeDefOf.CenterDrop),
                map, newAttackers);
            op.aggressor.lord = attackerLord;
        }

        /// <summary>Spawns reinforcement defenders for an op if a foreign defending settlement's
        /// squad is available. Does NOT generate random pawns if the squad is deployed. Adds the
        /// spawned pawns to the op's <c>defender.pawns</c> and to the flat <see cref="defenderPawns"/>
        /// aggregation; rolls them into the existing defender lord if present, otherwise creates one.</summary>
        private void SpawnReinforcementsForOp(MilitaryOperation op)
        {
            if (map is null || op?.defender?.force is null) return;

            WorldSettlementFC ourSettlement = ParentSettlement;

            var homeComp = op.defender.force.homeSettlement?.MilitaryComp;
            if (homeComp is null) return;
            // Don't spawn reinforcements from the home settlement defending itself — already handled
            if (op.defender.force.homeSettlement == ourSettlement) return;

            bool hasSquad = homeComp.militarySquad != null
                && homeComp.militarySquad.outfit != null
                && homeComp.militarySquad.mercenaries.Any();
            if (!hasSquad) return;
            if (homeComp.militarySquad.IsPhysicallyDeployed()) return;

            var squad = homeComp.militarySquad;
            squad.CheckInitialization();
            squad.OutfitSquad(squad.outfit);
            squad.UpdateSquadStats(op.defender.force.homeSettlement.settlementMilitaryLevel);
            squad.ResetNeeds();

            double efficiency = op.defender.force.militaryEfficiency;
            foreach (Pawn merc in squad.AllEquippedMercenaryPawns)
            {
                MilitaryEfficiencyUtil.ShiftPawnGearQuality(merc, efficiency);
                MilitaryEfficiencyUtil.ApplyCombatEfficiencyHediff(merc, efficiency);
            }

            List<Pawn> reinforcements = squad.AllEquippedMercenaryPawns.ToList();
            Lord defenseLord = defenderPawns.Count > 0 ? defenderPawns[0].GetLord() : null;
            var spawnedReinforcements = new List<Pawn>();

            foreach (Pawn friendly in reinforcements)
            {
                if (friendly.IsWildMan()) continue;
                friendly.ApplyIdeologyRitualWounds();

                IntVec3 loc;
                if (!CellFinder.TryFindRandomCellNear(map.Center, map, 20, c => c.Standable(map), out loc))
                    loc = map.Center;

                try
                {
                    GenSpawn.Spawn(friendly, loc, map, new Rot4());
                    friendly.drafter = new Pawn_DraftController(friendly);
                    map.mapPawns.RegisterPawn(friendly);
                    spawnedReinforcements.Add(friendly);
                }
                catch (Exception e)
                {
                    LogUtil.Warning($"Failed to spawn reinforcement {friendly.LabelShort}: {e}");
                }
            }

            if (spawnedReinforcements.Any())
            {
                if (defenseLord is object)
                {
                    foreach (Pawn pawn in spawnedReinforcements)
                        defenseLord.AddPawn(pawn);
                }
                else
                {
                    defenderLord = LordMaker.MakeNewLord(FactionCache.PlayerColonyFaction,
                        new LordJob_DefendColony(ourSettlement, new Dictionary<Pawn, Pawn>()),
                        map, spawnedReinforcements);
                }

                op.defender.pawns.AddRange(spawnedReinforcements);
                defenderPawns.AddRange(spawnedReinforcements);
                op.defender.lord = defenderPawns.Count > 0 ? defenderPawns[0].GetLord() : op.defender.lord;
                initialDefenderCount += spawnedReinforcements.Count;
            }
        }

        /// <summary>Re-recruits surviving Empire NPC defenders from their idle lord into a new
        /// defense lord. Used when reusing a post-battle map for a new attack (Path 2 + reengage).</summary>
        private void RecruitIdleDefenders()
        {
            if (map is null) return;
            WorldSettlementFC settlement = ParentSettlement;
            if (settlement is null) return;
            Faction empireFaction = FactionCache.PlayerColonyFaction;
            var idleDefenders = new List<Pawn>();

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Dead || pawn.Downed) continue;
                if (pawn.Faction != empireFaction && pawn.Faction != Faction.OfPlayer) continue;
                if (pawn.IsPrisonerOfColony) continue;
                idleDefenders.Add(pawn);
            }

            foreach (Pawn pawn in idleDefenders)
            {
                Lord lord = pawn.GetLord();
                if (lord != null)
                    lord.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
            }

            if (idleDefenders.Any())
            {
                defenderLord = LordMaker.MakeNewLord(empireFaction,
                    new LordJob_DefendColony(settlement, new Dictionary<Pawn, Pawn>()),
                    map, idleDefenders);
                defenderPawns.AddRange(idleDefenders);
                initialDefenderCount = defenderPawns.Count;
            }
        }

        private void ZoomIntoTile(MilitaryOperation op)
        {
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            if (battleMapInitialized) return;

            WorldSettlementFC settlement = ParentSettlement;
            if (settlement is null || map is null) return;

            if (op?.defender?.force is null)
            {
                LogUtil.Warning($"Aborting defense for {settlement.Name}, null defending force. Resetting battle state.");
                EndBattle(false, 0, null);
                return;
            }

            battleMapInitialized = true;

            map.fogGrid.ClearAllFog();

            // Remove pawns spawned by KCSG/VBGE that don't belong to Empire or the player.
            Faction empireFaction = FactionCache.PlayerColonyFaction;
            List<Pawn> toRemove = new List<Pawn>();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!pawn.RaceProps.Humanlike) continue;
                if (pawn.Faction == empireFaction) continue;
                if (pawn.Faction == Faction.OfPlayer) continue;
                toRemove.Add(pawn);
            }
            foreach (Pawn pawn in toRemove) pawn.Destroy();
            if (toRemove.Count > 0)
                LogUtil.Message($"Cleaned up {toRemove.Count} unrelated pawns from {settlement.Name}");

            GenerateFriendlies(op);
            RecruitMapInhabitants();
            Find.TickManager.Notify_GeneratedPotentiallyHostileMap();

            string enemyName = op.aggressor?.force?.homeFaction?.Name ?? op.aggressor?.faction?.Name ?? "Unknown";
            GlobalTargetInfo jumpTarget = defenderPawns.Any()
                ? new GlobalTargetInfo(defenderPawns[0])
                : new GlobalTargetInfo(new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), map);
            Find.LetterStack.ReceiveLetter(
                "FCManualBattleStarted".Translate(settlement.Name),
                "FCManualBattleStartedDesc".Translate(settlement.Name, enemyName),
                LetterDefOf.ThreatBig,
                new LookTargets(jumpTarget));
        }

        private void GenerateFriendlies(MilitaryOperation op)
        {
            WorldSettlementFC settlement = ParentSettlement;
            if (settlement is null || map is null) return;
            MilitaryForce force = op?.defender?.force;
            if (force is null) return;

            var points = Math.Max((float)(force.forceRemaining * 100), 50f);
            List<Pawn> friendlies = null;
            var riders = new Dictionary<Pawn, Pawn>();

            // Try external defender pawns (VOE outposts, etc.)
            if (force.homeSettlement == null && op.externalDefenderSource != null)
            {
                IAutoDefender extDefender = AutoDefenderRegistry.FindByWorldObject(op.externalDefenderSource);
                friendlies = extDefender?.GetDefendingPawns();
            }

            if (friendlies == null || friendlies.Count == 0)
            {
                var homeComp = force.homeSettlement?.MilitaryComp;
                bool hasSquad = homeComp?.militarySquad != null
                    && homeComp.militarySquad.outfit != null
                    && homeComp.militarySquad.mercenaries.Any();
                bool squadDeployed = hasSquad && homeComp.militarySquad.IsPhysicallyDeployed();
                bool squadAvailable = hasSquad && !squadDeployed
                    && (homeComp.militaryJob == null
                        || homeComp.militaryJob == MilitaryJobDefOf.Undefined
                        || homeComp.militaryJob == MilitaryJobDefOf.DefendFriendlySettlement);

                if (squadAvailable)
                {
                    var squad = force.homeSettlement.MilitaryComp.militarySquad;
                    squad.CheckInitialization();
                    squad.OutfitSquad(squad.outfit);
                    squad.UpdateSquadStats(force.homeSettlement.settlementMilitaryLevel);
                    squad.ResetNeeds();

                    double efficiency = force.militaryEfficiency;
                    foreach (Pawn merc in squad.AllEquippedMercenaryPawns)
                    {
                        MilitaryEfficiencyUtil.ShiftPawnGearQuality(merc, efficiency);
                        MilitaryEfficiencyUtil.ApplyCombatEfficiencyHediff(merc, efficiency);
                    }

                    friendlies = squad.AllEquippedMercenaryPawns.ToList();

                    foreach (var animal in squad.animals)
                    {
                        if (animal.handler?.pawn is object)
                            riders.Add(animal.handler.pawn, animal.pawn);
                    }
                }
                else if (!hasSquad)
                {
                    var parms = new IncidentParms
                    {
                        target = map,
                        faction = FactionCache.PlayerColonyFaction,
                        generateFightersOnly = true,
                        raidStrategy = RaidStrategyDefOf.ImmediateAttackFriendly
                    };
                    parms.points = IncidentWorker_Raid.AdjustedRaidPoints(points,
                        PawnsArrivalModeDefOf.EdgeWalkIn, parms.raidStrategy,
                        parms.faction, PawnGroupKindDefOf.Combat,
                        parms.target);
                    friendlies = PawnGroupMakerUtility.GeneratePawns(
                        IncidentParmsUtility.GetDefaultPawnGroupMakerParms(
                            PawnGroupKindDefOf.Combat, parms, true)).ToList();
                    if (!friendlies.Any()) LogUtil.Error("Got no pawns spawning raid from parms " + parms);

                    double efficiency = force.militaryEfficiency;
                    foreach (Pawn defender in friendlies)
                    {
                        MilitaryEfficiencyUtil.ApplyCombatEfficiencyHediff(defender, efficiency);
                    }
                }
                // else: squad exists but is physically deployed — settlement fights with inhabitants only.
            }

            void tryFindLoc(out IntVec3 loc, Pawn friendly)
            {
                var min = (70 + settlement.settlementLevel * 10) / 2 - 5 - 5 * settlement.settlementLevel;
                var size = 10 + settlement.settlementLevel * 10;
                CellFinder.TryFindRandomCellInsideWith(new CellRect(min, min, size, size),
                    testing => testing.Standable(map) && map.reachability.CanReachMapEdge(testing,
                        TraverseParms.For(TraverseMode.PassDoors)), out loc);
                if (loc.x == -1000)
                {
                    LogUtil.Message("Failed with " + friendly + ", " + loc);
                    CellFinder.TryFindRandomCellNear(new IntVec3(min + 10 + settlement.settlementLevel, 1,
                            min + 10 + settlement.settlementLevel), map, 75,
                        testing => testing.Standable(map), out loc);
                }
            }

            if (friendlies is null) friendlies = new List<Pawn>();

            var spawnedFriendlies = new List<Pawn>();
            foreach (var friendly in friendlies)
            {
                if (friendly.IsWildMan()) continue;
                friendly.ApplyIdeologyRitualWounds();

                IntVec3 loc;
                if (friendly.AnimalOrWildMan())
                {
                    Pawn rider = null;
                    if (riders.Count > 0)
                    {
                        var pair = riders.FirstOrDefault(p => p.Value.thingIDNumber == friendly.thingIDNumber);
                        rider = pair.Key;
                    }

                    if (rider is object)
                    {
                        CellFinder.TryFindRandomCellInsideWith(new CellRect((int)rider.DrawPos.x - 5,
                                (int)rider.DrawPos.z - 5, 10, 10),
                            testing => testing.Standable(map) && map.reachability.CanReachMapEdge(testing,
                                TraverseParms.For(TraverseMode.PassDoors)), out loc);
                    }
                    else
                    {
                        LogUtil.Warning($"Defender animal {friendly.LabelShort} ({friendly.thingIDNumber}) has no rider pair; placing as a standalone defender.");
                        tryFindLoc(out loc, friendly);
                    }
                }
                else
                {
                    tryFindLoc(out loc, friendly);
                }

                try
                {
                    GenSpawn.Spawn(friendly, loc, map, new Rot4());
                    friendly.drafter = new Pawn_DraftController(friendly);
                    map.mapPawns.RegisterPawn(friendly);
                    spawnedFriendlies.Add(friendly);
                }
                catch (Exception e)
                {
                    LogUtil.Warning($"Failed to spawn defender {friendly.LabelShort} (likely a mod conflict): {e}");
                }
            }

            defenderLord = LordMaker.MakeNewLord(FactionCache.PlayerColonyFaction, new LordJob_DefendColony(settlement, riders), map, spawnedFriendlies);

            defenderPawns = spawnedFriendlies;
            initialDefenderCount = defenderPawns.Count;
            op.defender.lord = defenderLord;

            // Track external defender pawns on the op's defender participant so EndAttack can
            // return them to the auto-defender on battle resolution.
            if (force.homeSettlement == null && op.externalDefenderSource != null)
            {
                op.defender.pawns.AddRange(spawnedFriendlies);
            }
        }

        private void RecruitMapInhabitants()
        {
            if (map == null || !defenderPawns.Any()) return;
            WorldSettlementFC settlement = ParentSettlement;
            if (settlement is null) return;

            Lord defenseLord = defenderPawns[0].GetLord();
            if (defenseLord == null) return;

            Faction empireFaction = FactionCache.PlayerColonyFaction;
            var inhabitants = new List<Pawn>();

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (defenderPawns.Contains(pawn)) continue;
                if (!pawn.RaceProps.Humanlike) continue;
                if (pawn.Downed || pawn.Dead) continue;
                if (pawn.Faction != empireFaction) continue;
                if (pawn.IsPrisonerOfColony) continue;
                inhabitants.Add(pawn);
            }

            int targetCount = (int)settlement.workers;

            while (inhabitants.Count > targetCount)
            {
                Pawn excess = inhabitants[inhabitants.Count - 1];
                inhabitants.RemoveAt(inhabitants.Count - 1);
                if (excess.Spawned) excess.Destroy();
            }

            int spawnAttempts = 0;
            while (inhabitants.Count < targetCount && spawnAttempts < targetCount * 2)
            {
                spawnAttempts++;
                Pawn civilian = PawnGenerator.GeneratePawn(FCPawnGenerator.CivilianRequest());
                IntVec3 loc;
                if (!CellFinder.TryFindRandomCellNear(map.Center, map, 15, c => c.Standable(map), out loc))
                    loc = map.Center;
                try
                {
                    GenSpawn.Spawn(civilian, loc, map);
                    inhabitants.Add(civilian);
                }
                catch (Exception e)
                {
                    LogUtil.Warning($"Failed to spawn civilian (likely a mod conflict): {e}");
                }
            }

            for (int i = 0; i < inhabitants.Count; i++)
            {
                if (i % 8 != 0)
                    inhabitants[i].equipment.DestroyAllEquipment();
            }

            foreach (Pawn inhabitant in inhabitants)
            {
                Lord existingLord = inhabitant.GetLord();
                if (existingLord != null)
                    existingLord.Notify_PawnLost(inhabitant, PawnLostCondition.LeftVoluntarily);

                defenseLord.AddPawn(inhabitant);
                defenderPawns.Add(inhabitant);
            }

            if (inhabitants.Count > 0)
            {
                initialDefenderCount = defenderPawns.Count;
                LogUtil.Message($"Added {inhabitants.Count} settlement inhabitants to defenders at {settlement.Name}");
            }
        }

        /* -*-*-*-*- Battle resolution -*-*-*-*- */

        public void EndBattle(bool won, int remaining, BattleResult battleResult = null)
        {
            // Settlement-side effects (happiness/loyalty/buildings) and op.CompleteBattle
            // dispatch are still owned by comp.EndBattle since they're settlement-specific.
            // BattlefieldContext just clears its own state after the comp finishes.
            WorldObjectComp_SettlementMilitary comp = ParentMilitaryComp;
            if (comp is object)
            {
                comp.EndBattle(won, remaining, battleResult);
            }
            battleMapInitialized = false;
        }

        public void EndAttack()
        {
            bool won = defenderPawns.Any();
            int remaining = defenderPawns.Count;

            // Return external defender pawns per op before map cleanup destroys them.
            if (activeOps is object)
            {
                foreach (MilitaryOperation op in activeOps)
                {
                    if (op?.externalDefenderSource is null) continue;
                    IAutoDefender extDefender = AutoDefenderRegistry.FindByWorldObject(op.externalDefenderSource);
                    if (extDefender is null) continue;

                    List<Pawn> survivingPawns = new List<Pawn>();
                    foreach (Pawn pawn in op.defender.pawns)
                    {
                        if (pawn != null && !pawn.Dead && !pawn.Destroyed)
                        {
                            if (pawn.Spawned) pawn.DeSpawn();
                            survivingPawns.Add(pawn);
                            defenderPawns.Remove(pawn);
                        }
                    }
                    extDefender.ReturnDefendingPawns(survivingPawns);
                }
            }

            // Strip combat efficiency hediffs from surviving defenders
            foreach (Pawn defender in defenderPawns)
            {
                if (defender != null && !defender.Dead && !defender.Destroyed)
                    MilitaryEfficiencyUtil.RemoveCombatEfficiencyHediff(defender);
            }

            DeleteMap(won);
            EndBattle(won, remaining);

            defenderPawns.Clear();
            attackerPawns.Clear();
            endingBattle = false;
            pendingDeliveryMessage = null;
        }

        public void RemoveAttacker(Pawn downed)
        {
            attackerPawns.Remove(downed);
            attackerPawns.RemoveAll(IsPawnTrulyGone);

            // Drop the pawn from whichever op it belonged to. Pawns are uniquely owned by one op's
            // aggressor list, so a single Remove is sufficient.
            if (activeOps is object)
            {
                foreach (MilitaryOperation op in activeOps)
                {
                    if (op?.aggressor?.pawns is null) continue;
                    if (op.aggressor.pawns.Remove(downed)) break;
                }
            }

            attackerPawns.RemoveAll(IsPawnTrulyGone);
            bool anyUnderAttack = FactionCache.MilitaryManager?.HasDefenseAt(ParentSettlement) ?? false;
            if (attackerPawns.Any() || HasPendingPodAttackers() || endingBattle || !anyUnderAttack) return;

            endingBattle = true;
            LongEventHandler.QueueLongEvent(EndAttack, "EndingAttack", false, error =>
            {
                DelayedErrorWindowRequest.Add("FCErrorEndingAttack".Translate(),
                    "FCErrorEndingAttackDescription".Translate());
                LogUtil.Error(error.Message);
            });
        }

        public void RemoveDefender(Pawn defender)
        {
            defenderPawns.Remove(defender);
            defenderPawns.RemoveAll(IsPawnTrulyGone);
            bool anyUnderAttack = FactionCache.MilitaryManager?.HasDefenseAt(ParentSettlement) ?? false;
            if (defenderPawns.Any() || endingBattle || !anyUnderAttack) return;

            endingBattle = true;
            LongEventHandler.QueueLongEvent(EndAttack, "EndingAttack", false, error =>
            {
                DelayedErrorWindowRequest.Add("FCErrorEndingAttack".Translate(),
                    "FCErrorEndingAttackDescription".Translate());
                LogUtil.Error(error.Message);
            });
        }

        public void DeleteMap(bool won = true)
        {
            if (map is null) return;
            WorldSettlementFC settlement = ParentSettlement;
            if (settlement is null) return;

            // Restore faction on Empire defenders the player drafted during battle.
            Faction empireFaction = FactionCache.PlayerColonyFaction;
            foreach (Pawn npc in draftedNPCs)
            {
                if (npc is null || npc.Dead || npc.Destroyed) continue;
                if (npc.Faction == Faction.OfPlayer)
                    npc.SetFaction(empireFaction);
            }
            draftedNPCs.Clear();

            // Check for player pawns on the map
            List<Pawn> playerPawns = new List<Pawn>();
            bool anyMobile = false;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                playerPawns.Add(pawn);
                if (!pawn.Downed) anyMobile = true;
            }

            if (anyMobile || shuttleLandingPending)
            {
                // Mobile player pawns (or shuttles) exist — keep the map alive.
                foreach (var lord in map.lordManager.lords.ListFullCopy())
                    map.lordManager.RemoveLord(lord);

                List<Pawn> empireDefenders = new List<Pawn>();
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    if (pawn.Faction == empireFaction && !pawn.Dead && !pawn.Downed)
                        empireDefenders.Add(pawn);

                foreach (Pawn pawn in empireDefenders)
                    pawn.jobs.StopAll();

                if (empireDefenders.Any())
                    LordMaker.MakeNewLord(empireFaction, new LordJob_ColonistsIdle(settlement), map, empireDefenders);

                return;
            }

            // Immediate path — remove all lords before cleanup
            foreach (var lord in map.lordManager.lords.ListFullCopy())
                map.lordManager.RemoveLord(lord);

            if (playerPawns.Count > 0)
            {
                foreach (Pawn pawn in playerPawns)
                    if (pawn.Spawned) pawn.DeSpawn();

                foreach (Pawn pawn in playerPawns)
                    if (!pawn.Dead)
                    {
                        int iterations = 0;
                        while (pawn.health.HasHediffsNeedingTend())
                        {
                            iterations++;
                            if (iterations > 10000)
                            {
                                LogUtil.Error("BattlefieldContext.DeleteMap: Too many tend iterations.");
                                break;
                            }
                            TendUtility.DoTend(null, pawn, null);
                        }
                    }

                string eventText = won
                    ? DeliveryEvent.ShuttleEventInjuredString
                    : DeliveryEvent.ShuttleEventInjuredLostString;
                int travelTicks = TravelUtil.ReturnTicksToArrive(settlement.Tile, Find.AnyPlayerHomeMap.Tile);
                if (!won) travelTicks += GenDate.TicksPerDay;

                var goods = new List<Thing>(playerPawns.Count);
                foreach (Pawn pawn in playerPawns) goods.Add(pawn);

                var eventParams = new FCEvent
                {
                    location = Find.AnyPlayerHomeMap.Tile,
                    source = settlement.Tile,
                    goods = goods,
                    customDescription = eventText,
                    timeTillTrigger = Find.TickManager.TicksGame + travelTicks
                };
                DeliveryEvent.CreateDeliveryEvent(eventParams);
                string travelDays = ((float)travelTicks / GenDate.TicksPerDay).ToString("0.#");
                pendingDeliveryMessage = "FCInjuredCaravanMembersReturning".Translate(playerPawns.Count, travelDays);
            }

            CameraJumper.TryJump(settlement.Tile);
            Current.Game.CurrentMap = Find.AnyPlayerHomeMap;
            Current.Game.DeinitAndRemoveMap(map, false);
            map = null;
        }

        /* -*-*-*-*- Caravan defend / external pawn injection -*-*-*-*- */

        public void AddToDefenceFromList(List<Pawn> pawns, int destinationTile)
        {
            AddToDefenceFromList(pawns, destinationTile, assignToLord: true);
        }

        /// <summary>Registers pawns with the defense system. When <paramref name="assignToLord"/>
        /// is false, pawns are added to defenders but not to the battle lord (used for VEF
        /// vehicles that have their own job systems).</summary>
        public void AddToDefenceFromList(List<Pawn> pawns, int destinationTile, bool assignToLord)
        {
            if (pawns.NullOrEmpty())
            {
                LogUtil.Error("Tried to add an empty list of pawns to a battlefield");
                return;
            }

            // If battle is already in progress, register directly.
            bool isUnderAttack = FactionCache.MilitaryManager?.HasDefenseAt(ParentSettlement) ?? false;
            if (isUnderAttack && map != null)
            {
                RegisterPawnsAsDefenders(pawns, assignToLord);
                return;
            }

            // Battle hasn't started yet — start it via the op linked to the warning event.
            FCEvent warning = MilitaryUtilFC.ReturnMilitaryEventByLocation(new PlanetTile(destinationTile));
            if (warning is null)
            {
                LogUtil.Warning("AddToDefenceFromList: no settlementBeingAttacked event at destination tile.");
                return;
            }

            MilitaryOperation op = FactionCache.MilitaryManager?.GetOp(warning.linkedOperationId);
            if (op is null)
            {
                LogUtil.Warning($"AddToDefenceFromList: warning event at tile {destinationTile} has no linked op.");
                return;
            }

            StartDefense(op, () => RegisterPawnsAsDefenders(pawns, assignToLord));
        }

        public void RegisterPawnsAsDefenders(List<Pawn> pawns, bool assignToLord)
        {
            WorldSettlementFC settlement = ParentSettlement;
            if (assignToLord)
            {
                Lord existingLord = defenderPawns.Any() ? defenderPawns[0].GetLord() : null;
                if (existingLord != null)
                {
                    foreach (var pawn in pawns)
                    {
                        if (!defenderPawns.Contains(pawn) && !existingLord.ownedPawns.Contains(pawn))
                            existingLord.AddPawn(pawn);
                    }
                }
                else if (map != null && settlement is object)
                {
                    var lordless = new List<Pawn>();
                    foreach (var pawn in pawns)
                    {
                        if (!defenderPawns.Contains(pawn) && pawn.GetLord() is null)
                            lordless.Add(pawn);
                    }
                    if (lordless.Any())
                        LordMaker.MakeNewLord(FactionCache.PlayerColonyFaction,
                            new LordJob_ColonistsIdle(settlement), map, lordless);
                }
            }

            foreach (var pawn in pawns)
            {
                if (!defenderPawns.Contains(pawn))
                {
                    defenderPawns.Add(pawn);
                    initialDefenderCount++;
                }
            }
        }

        public void CaravanDefend(Caravan caravan)
        {
            var pawns = caravan.pawns.InnerListForReading.ListFullCopy();

            // Check for shuttle in caravan (Odyssey DLC passenger shuttle)
            var shuttle = caravan.Shuttle;
            if (shuttle != null && map != null)
            {
                ShuttleCaravanDefend(caravan, pawns, shuttle);
                return;
            }

            // Standard flow — spawn pawns at map edge
            RegisterPawnsAsDefenders(pawns, assignToLord: true);
            if (!caravan.Destroyed) caravan.Destroy();
            SpawnPawnsAtEdge(pawns);
        }

        private void ShuttleCaravanDefend(Caravan caravan, List<Pawn> pawns, Building_PassengerShuttle shuttle)
        {
            Pawn owner = CaravanInventoryUtility.GetOwnerOf(caravan, shuttle);
            owner?.inventory.innerContainer.Remove(shuttle);

            RegisterPawnsAsDefenders(pawns, assignToLord: false);
            if (!caravan.Destroyed) caravan.Destroy();

            CompShuttle compShuttle = shuttle.TryGetComp<CompShuttle>();
            TransportShipDef shipDef = compShuttle?.Props?.shipDef ?? TransportShipDefOf.Ship_Shuttle;
            TransportShip transportShip = TransportShipMaker.MakeTransportShip(shipDef, pawns, shuttle);

            shuttleLandingPending = true;

            var battleMap = map;
            var settlement = ParentSettlement;
            var shuttleDef = shuttle.def;
            Rot4 shuttleRotation = shuttleDef.defaultPlacingRot;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                Current.Game.CurrentMap = battleMap;
                CameraJumper.TryJump(new IntVec3(battleMap.Size.x / 2, 0, battleMap.Size.z / 2), battleMap);

                var targetParams = new TargetingParameters
                {
                    canTargetLocations = true,
                    canTargetSelf = false,
                    canTargetPawns = false,
                    canTargetFires = false,
                    canTargetBuildings = false,
                    canTargetItems = false
                };

                bool landed = false;
                Find.Targeter.BeginTargeting(targetParams,
                    delegate(LocalTargetInfo target)
                    {
                        landed = true;
                        shuttleLandingPending = false;
                        shuttle.Rotation = shuttleRotation;
                        transportShip.ArriveAt(target.Cell, settlement);
                        transportShip.AddJobs(ShipJobDefOf.Unload, ShipJobDefOf.WaitForever);
                    },
                    delegate(LocalTargetInfo target)
                    {
                        RoyalTitlePermitWorker_CallShuttle.DrawShuttleGhost(target, battleMap, shuttleDef, shuttleRotation);
                    },
                    delegate(LocalTargetInfo target)
                    {
                        return RoyalTitlePermitWorker_CallShuttle.ShuttleCanLandHere(target, battleMap, shuttleDef, shuttleRotation);
                    },
                    null,
                    delegate
                    {
                        if (landed) return;
                        shuttleLandingPending = false;
                        if (!Find.Maps.Contains(battleMap)) return;
                        IntVec3 fallback = DropCellFinder.GetBestShuttleLandingSpot(battleMap, Faction.OfPlayer);
                        transportShip.ArriveAt(fallback, settlement);
                        transportShip.AddJobs(ShipJobDefOf.Unload, ShipJobDefOf.WaitForever);
                    },
                    null, true, null,
                    delegate(LocalTargetInfo target)
                    {
                        if (!shuttleDef.rotatable) return;
                        if (KeyBindingDefOf.Designator_RotateRight.KeyDownEvent)
                            shuttleRotation = shuttleRotation.Rotated(RotationDirection.Clockwise);
                        if (KeyBindingDefOf.Designator_RotateLeft.KeyDownEvent)
                            shuttleRotation = shuttleRotation.Rotated(RotationDirection.Counterclockwise);
                    });
            });
        }

        private void SpawnPawnsAtEdge(List<Pawn> pawns)
        {
            if (map is null) return;
            var enterCell = FindNearEdgeCell(map);
            foreach (var pawn in pawns)
            {
                var loc = CellFinder.RandomSpawnCellForPawnNear(enterCell, map);
                GenSpawn.Spawn(pawn, loc, map, Rot4.Random);
            }
        }

        /* -*-*-*-*- Helpers -*-*-*-*- */

        private bool IsPlayerCaravanOnTile()
        {
            return Find.WorldObjects.Caravans.Any(c =>
                c.Tile == tile && c.Faction == Faction.OfPlayer);
        }

        private static int CountOtherSettlementBattleMaps()
        {
            int count = 0;
            FactionFC faction = FactionCache.FactionComp;
            if (faction is null) return 0;
            foreach (WorldSettlementFC settlement in faction.settlements)
            {
                if (settlement.Map != null)
                {
                    bool underAttack = FactionCache.MilitaryManager?.HasDefenseAt(settlement) ?? false;
                    if (underAttack) count++;
                }
            }
            return count;
        }
    }
}
