using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI.Group;
using Verse.Sound;

namespace FactionColonies
{
    public class WorldObjectCompProperties_SettlementMilitary : WorldObjectCompProperties
    {
        public WorldObjectCompProperties_SettlementMilitary()
        {
            compClass = typeof(WorldObjectComp_SettlementMilitary);
        }
        public override IEnumerable<string> ConfigErrors(WorldObjectDef parentDef)
        {
            foreach (string item in base.ConfigErrors(parentDef))
            {
                yield return item;
            }
            if (!typeof(MapParent).IsAssignableFrom(parentDef.worldObjectClass))
            {
                yield return parentDef.defName + " has WorldObjectCompProperties_SettlementMilitary but it's not MapParent.";
            }
        }
    }

    public class WorldObjectComp_SettlementMilitary : WorldObjectComp, ISettlementPostLoadInit
    {
        private WorldSettlementFC cachedWorldSettlementParent = null;
        public WorldSettlementFC WorldSettlement
        {
            get
            {
                if (cachedWorldSettlementParent != null)
                {
                    return cachedWorldSettlementParent;
                }
                if (parent is WorldSettlementFC ws)
                {
                    cachedWorldSettlementParent = ws;
                }
                else
                {
                    cachedWorldSettlementParent = null;
                    LogUtil.ErrorOnce($"WorldObjectComp_SettlementMilitary has a non-WorldSettlementFC parent: {parent.Label}", 93512108);
                }
                return cachedWorldSettlementParent;
            }
        }
        public Map Map => WorldSettlement.Map;

        /// <summary>Per-wave tracking for multi-wave defense battles. Used by the legacy
        /// <c>comp.StartDefence</c> spawning flow that drives manual battles today.</summary>
        public List<DefenseWave> activeWaves = new List<DefenseWave>();

        public List<Pawn> attackers = new List<Pawn>();
        public List<Pawn> defenders = new List<Pawn>();
        public List<Pawn> draftedNPCs = new List<Pawn>();

        public MercenarySquadFC militarySquad;
        public int artilleryTimer = 0;
        public bool autoDefend = false;
        public int settlementMilitaryLevel;

        /* -*-*-*-*- Legacy load buffers (Phase 6) -*-*-*-*-
         * Old saves (pre-Phase-2) carried operation state on the comp directly. After the
         * gut, the canonical state lives on MilitaryOperation in the manager; the readable
         * comp surface (militaryBusy / militaryJob / militaryLocation / militaryEnemy /
         * isUnderAttack) is computed from manager queries below. These _legacy* fields
         * are loaded from old save XML during LoadingVars and consumed by
         * <see cref="MilitaryMigrationUtil"/> in PostLoadInit. They are NOT written on save.
         */
        public bool _legacyMilitaryBusy;
        public MilitaryJobDef _legacyMilitaryJob;
        public PlanetTile _legacyMilitaryLocation = PlanetTile.Invalid;
        public Faction _legacyMilitaryEnemy;
        public bool _legacyIsUnderAttack;

        /* -*-*-*-*- Computed comp surface (derived from manager) -*-*-*-*- */

        /// <summary>True when this settlement has any active op in which it's the squad-bearer
        /// (offensive aggressor, deploy aggressor, or foreign defender of another settlement's
        /// defensive battle).</summary>
        public bool militaryBusy
        {
            get
            {
                MilitaryOperationManager manager = FactionCache.MilitaryManager;
                if (manager is null) return false;
                IReadOnlyList<MilitaryOperation> ops = manager.GetOpsForSettlement(WorldSettlement);
                for (int i = 0; i < ops.Count; i++)
                {
                    MilitaryOperation op = ops[i];
                    if (op.aggressor?.homeSettlement == WorldSettlement) return true;
                    if (op.defender?.homeSettlement == WorldSettlement
                        && (op.targetObject as WorldSettlementFC) != WorldSettlement) return true;
                }
                return false;
            }
        }

        /// <summary>Current op kind for this settlement: the aggressor op's kind (Raid/Capture/
        /// Enslave/Deploy/Cooldown) or <c>DefendFriendlySettlement</c> if foreign-defending,
        /// else <c>Undefined</c>.</summary>
        public MilitaryJobDef militaryJob
        {
            get
            {
                MilitaryOperation op = FindOwnOp();
                if (op is null) return MilitaryJobDefOf.Undefined;
                if (op.phase == MilitaryOperationPhase.CooldownPending) return MilitaryJobDefOf.Cooldown;
                if (op.aggressor?.homeSettlement == WorldSettlement) return op.kind ?? MilitaryJobDefOf.Undefined;
                // Foreign-defender case
                return MilitaryJobDefOf.DefendFriendlySettlement;
            }
        }

        /// <summary>Target tile of the active op (raid target, deploy map tile, or defended settlement).</summary>
        public PlanetTile militaryLocation
        {
            get
            {
                MilitaryOperation op = FindOwnOp();
                return op?.targetTile ?? PlanetTile.Invalid;
            }
        }

        /// <summary>Enemy faction in the current op (defender's faction for offensive ops,
        /// aggressor's faction for foreign-defender ops).</summary>
        public Faction militaryEnemy
        {
            get
            {
                MilitaryOperation op = FindOwnOp();
                if (op is null) return null;
                if (op.aggressor?.homeSettlement == WorldSettlement) return op.defender?.faction;
                // Foreign-defender: enemy is the attacker
                return op.aggressor?.faction;
            }
        }

        /// <summary>True when this settlement is the target of any active defensive op.</summary>
        public bool isUnderAttack => FactionCache.MilitaryManager?.HasDefenseAt(WorldSettlement) ?? false;

        /// <summary>Aggressor's force in the active defensive battle on this tile, or null.
        /// Reads from <see cref="activeWaves"/> first (legacy mid-battle), then from the
        /// defensive op the manager tracks for this settlement.</summary>
        public MilitaryForce attackerForce
        {
            get
            {
                if (activeWaves != null && activeWaves.Count > 0) return activeWaves[0].attackerForce;
                MilitaryOperation op = FindIncomingDefensiveOp();
                return op?.aggressor?.force;
            }
        }

        /// <summary>Defender's force in the active defensive battle on this tile, or null.</summary>
        public MilitaryForce defenderForce
        {
            get
            {
                if (activeWaves != null && activeWaves.Count > 0) return activeWaves[0].defenderForce;
                MilitaryOperation op = FindIncomingDefensiveOp();
                return op?.defender?.force;
            }
        }

        /// <summary>Returns the first op where this settlement is "the actor" — aggressor of any
        /// op, or foreign defender of someone else's defensive op. Used by computed shadow
        /// surface to derive job / location / enemy.</summary>
        private MilitaryOperation FindOwnOp()
        {
            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            if (manager is null) return null;
            IReadOnlyList<MilitaryOperation> ops = manager.GetOpsForSettlement(WorldSettlement);
            for (int i = 0; i < ops.Count; i++)
            {
                MilitaryOperation op = ops[i];
                if (op.aggressor?.homeSettlement == WorldSettlement) return op;
                if (op.defender?.homeSettlement == WorldSettlement
                    && (op.targetObject as WorldSettlementFC) != WorldSettlement) return op;
            }
            return null;
        }

        /// <summary>Returns the defensive op targeting THIS settlement, or null.</summary>
        private MilitaryOperation FindIncomingDefensiveOp()
        {
            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            if (manager is null) return null;
            IReadOnlyList<MilitaryOperation> ops = manager.GetOpsForSettlement(WorldSettlement);
            for (int i = 0; i < ops.Count; i++)
            {
                MilitaryOperation op = ops[i];
                if (op.IsDefensive && (op.targetObject as WorldSettlementFC) == WorldSettlement) return op;
            }
            return null;
        }

        private bool endingBattle = false;
        private bool battleMapInitialized = false;
        private bool shuttleLandingPending = false;
        private int initialDefenderCount;
        private string pendingDeliveryMessage;

        /* Pod-bound pawns during Skyfaller descent are !Spawned but ParentHolder != null.
         * A pure !Spawned check treats them as lost and triggers false victory. */
        private static bool IsPawnTrulyGone(Pawn p)
        {
            if (p is null) return true;
            if (p.Destroyed) return true;
            if (p.Spawned) return false;
            if (p.ParentHolder is object) return false;
            return true;
        }

        private bool HasPendingPodAttackers()
        {
            foreach (DefenseWave wave in activeWaves)
            {
                if (wave is null || wave.resolved) continue;
                List<Pawn> list = wave.waveAttackers;
                if (list is null) continue;
                foreach (Pawn p in list)
                {
                    if (p is null || p.Destroyed || p.Dead) continue;
                    if (!p.Spawned && p.ParentHolder is object) return true;
                }
            }

            return false;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref attackers, "attackers", LookMode.Reference);
            Scribe_Collections.Look(ref defenders, "defenders", LookMode.Reference);
            Scribe_Collections.Look(ref draftedNPCs, "draftedNPCs", LookMode.Reference);
            Scribe_Collections.Look(ref activeWaves, "activeWaves", LookMode.Deep);
            Scribe_References.Look(ref militarySquad, "militarySquad");
            Scribe_Values.Look(ref artilleryTimer, "artilleryTimer");
            Scribe_Values.Look(ref autoDefend, "autoDefend");
            Scribe_Values.Look(ref settlementMilitaryLevel, "settlementMilitaryLevel");
            Scribe_Values.Look(ref initialDefenderCount, "initialDefenderCount");
            Scribe_Values.Look(ref battleMapInitialized, "battleMapInitialized");

            /* Backward compat: load pre-Phase-2 op state (militaryJob/militaryLocation/etc.)
             * and pre-Phase-2 attacker/defender forces into legacy buffers consumed by
             * MilitaryMigrationUtil during PostLoadInit. These are NOT written on save —
             * post-refactor saves carry the canonical state on MilitaryOperationManager. */
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Values.Look(ref _legacyMilitaryBusy, "militaryBusy", false);
                Scribe_Defs.Look(ref _legacyMilitaryJob, "militaryJob");
                Scribe_Values.Look(ref _legacyMilitaryLocation, "militaryLocation", PlanetTile.Invalid);
                Scribe_References.Look(ref _legacyMilitaryEnemy, "militaryEnemy");
                Scribe_Values.Look(ref _legacyIsUnderAttack, "isUnderAttack", false);

                MilitaryForce legacyAttackerForce = null;
                MilitaryForce legacyDefenderForce = null;
                Scribe_Deep.Look(ref legacyAttackerForce, "attackerForce");
                Scribe_Deep.Look(ref legacyDefenderForce, "defenderForce");

                if (activeWaves is null) activeWaves = new List<DefenseWave>();
                if (activeWaves.Count == 0 && (legacyAttackerForce is object || legacyDefenderForce is object))
                {
                    activeWaves.Add(new DefenseWave
                    {
                        attackerForce = legacyAttackerForce,
                        defenderForce = legacyDefenderForce,
                        attackerFaction = legacyAttackerForce?.homeFaction
                    });
                }
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                EnsureCollectionsNonNull();
        }

        /* LookMode.Reference collections silently drop unresolved entries and may leave
         * the list null if the XML element was absent (e.g. pre-field saves). */
        private void EnsureCollectionsNonNull()
        {
            if (activeWaves is null) activeWaves = new List<DefenseWave>();
            if (attackers is null) attackers = new List<Pawn>();
            if (defenders is null) defenders = new List<Pawn>();
            if (draftedNPCs is null) draftedNPCs = new List<Pawn>();
        }

        public override void Initialize(WorldObjectCompProperties props_l)
        {
            base.Initialize(props_l);

            attackers = new List<Pawn>();
            defenders = new List<Pawn>();
            draftedNPCs = new List<Pawn>();
            activeWaves = new List<DefenseWave>();
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!isUnderAttack) return;
            if (endingBattle) return;

            if (isUnderAttack && !endingBattle && Find.TickManager.TicksGame % 2500 == 0
                && Map is null && attackers.Count == 0 && defenders.Count == 0)
            {
                // Events are removed from the queue before battle starts (see FCEventMaker.ProcessEvents),
                // so an orphaned flag is only "stuck" if the battle also isn't in progress; i.e. no map loaded
                // and no active combatants.
                FCEvent evt = MilitaryUtilFC.ReturnMilitaryEventByLocation(WorldSettlement.Tile);
                if (evt is null)
                {
                    LogUtil.Warning($"Clearing orphaned isUnderAttack flag on {WorldSettlement.Name} " +
                        $"(no matching settlementBeingAttacked event in queue).");
                    ClearAttackState();
                    return;
                }
            }

            if (Find.TickManager.TicksGame % 250 != 0) return;
            if (Map == null) return;

            // Clean stale references: null (save/load), destroyed, or despawned-without-holder
            // (e.g. pawn joined an existing caravan without PostCaravanFormed firing).
            // Pod-bound pawns in a descending Skyfaller are !Spawned but ParentHolder != null;
            //   they stay tracked until the pod opens.
            attackers.RemoveAll(IsPawnTrulyGone);
            defenders.RemoveAll(IsPawnTrulyGone);

            // Detect untracked player pawns on the battle map (e.g. shuttle-delivered pawns
            // that spawned via the Unload job after the ArrivePatch fired).
            // Also assigns lordless defenders to the battle lord (e.g. pawns that just
            // unloaded from a shuttle after being registered with assignToLord: false).
            var map = Map;
            Lord battleLord = null;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Dead || pawn.Downed) continue;
                if (!defenders.Contains(pawn))
                {
                    LogUtil.Warning($"Registering untracked player pawn {pawn.LabelShort} with defense at {WorldSettlement.Name}");
                    defenders.Add(pawn);
                    initialDefenderCount++;
                }
                if (pawn.GetLord() is null)
                {
                    if (battleLord is null)
                        battleLord = defenders.FirstOrDefault(d => d.GetLord() != null)?.GetLord();
                    if (battleLord != null && !battleLord.ownedPawns.Contains(pawn))
                        battleLord.AddPawn(pawn);
                }
            }

            // Don't declare stuck if attackers are still inbound in drop pods.
            bool attackersGone = attackers.Count == 0 && !HasPendingPodAttackers();
            if (attackersGone || defenders.Count == 0)
            {
                LogUtil.Warning($"Stuck battle detected at {WorldSettlement.Name}, forcing resolution.");
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

        private static string FoundSettlementString(WorldSettlementFC settlement, string winChanceText = null, bool isCurrentDefender = false)
        {
            string s = settlement.Name + " " + "FCShortMilitary".Translate() + " " + settlement.settlementMilitaryLevel;
            if (!winChanceText.NullOrEmpty())
                s += " - Victory: " + winChanceText + "%";
            if (isCurrentDefender)
                s += " - [" + "FCCurrentDefender".Translate() + "]";
            else
                s += " - " + "FCAvailable".Translate() + ": " + (settlement.MilitaryComp?.IsMilitaryBusySilent() != true).ToString();
            return s;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            if (isUnderAttack)
            {
                yield return DefendColonyAction();
            }
            if (isUnderAttack)
            {
                // Show change-defender gizmos for each pending (not yet fired) attack event
                IReadOnlyList<FCEvent> pendingEvents = MilitaryUtilFC.ReturnMilitaryEventsByLocation(WorldSettlement.Tile);
                for (int i = 0; i < pendingEvents.Count; i++)
                {
                    yield return ChangeDefenderAction(pendingEvents[i]);
                }
            }
        }

        private Command DefendColonyAction()
        {
            Command_Action defendColony = new Command_Action
            {
                defaultLabel = "FCDefendColony".Translate(),
                defaultDesc = "FCDefendColonyDesc".Translate(),
                icon = TexLoad.iconMilitary,
                action = delegate
                {
                    StartDefence(MilitaryUtilFC.ReturnMilitaryEventByLocation(WorldSettlement.Tile), () => { });
                }
            };
            /* If auto-battle is enabled, then disable the button. We leave it visible, though, so that the player knows that this is an option if
             * they change their settings. */
            AcceptanceReport canUse = CanDoManualFight();
            if (!canUse.Accepted)
            {
                defendColony.Disable(canUse.Reason);
            }

            return defendColony;
        }

        private Command ChangeDefenderAction(FCEvent evt)
        {
            Command_Action changeDefender = new Command_Action
            {
                defaultLabel = "FCDefendSettlement".Translate(),
                defaultDesc = "",
                icon = TexLoad.iconCustomize,
                action = delegate
                {
                    if (evt.militaryForceDefending == null || evt.militaryForceDefending.homeSettlement == null)
                    {
                        LogUtil.Warning($"ChangeDefenderAction: militaryForceDefending or its homeSettlement is null for event at {evt.location}");
                        ChangeDefendingForceAction(evt);
                        return;
                    }

                    double winChance = SimulateBattleFc.CalculateDefenderWinChance(evt.militaryForceAttacking, evt.militaryForceDefending);
                    var list = new List<FloatMenuOption>()
                    {
                        new FloatMenuOption("FCSettlementDefendingInformation".Translate(evt.militaryForceDefending.homeSettlement.Name,
                                                                                       evt.militaryForceDefending.DefensivePower,
                                                                                       (winChance * 100).ToString("F0")),
                                            null, MenuOptionPriority.High),
                        new FloatMenuOption("FCChangeDefendingForce".Translate(), () => ChangeDefendingForceAction(evt))
                    };

                    var floatMenu = new FloatMenu(list)
                    {
                        vanishIfMouseDistant = true
                    };
                    Find.WindowStack.Add(floatMenu);
                }
            };

            return changeDefender;
        }

        private void ChangeDefendingForceAction(FCEvent evt)
        {
            var faction = FactionCache.FactionComp;
            MilitaryForce attackForce = evt.militaryForceAttacking;
            WorldSettlementFC currentDefender = evt.militaryForceDefending?.homeSettlement;

            // "Reset to Home Settlement" option with win chance
            MilitaryForce homeForce = MilitaryForce.CreateMilitaryForceFromSettlement(WorldSettlement);
            double homeWinChance = SimulateBattleFc.CalculateDefenderWinChance(attackForce, homeForce);
            var settlementList = new List<FloatMenuOption>
            {
                new FloatMenuOption
                (
                    "FCResetToHomeSettlement".Translate(settlementMilitaryLevel, (homeWinChance * 100).ToString("F0")),
                    delegate { MilitaryUtilFC.ChangeDefendingMilitaryForce(evt, WorldSettlement); },
                    MenuOptionPriority.High
                )
            };

            // Other Empire settlements with win chance per option
            foreach (WorldSettlementFC foundSettlement in faction.settlements)
            {
                if (foundSettlement == WorldSettlement) continue;
                if (foundSettlement.MilitaryComp?.IsMilitaryValid() != true) continue;
                if (!DefenseValidatorRegistry.CanDefend(foundSettlement, WorldSettlement)) continue;

                MilitaryForce tmpHome = MilitaryForce.CreateMilitaryForceFromSettlement(WorldSettlement, true);
                MilitaryForce hypothetical = MilitaryForce.CreateMilitaryForceFromSettlement(foundSettlement, homeDefendingForce: tmpHome);
                double wc = SimulateBattleFc.CalculateDefenderWinChance(attackForce, hypothetical);
                string wcText = (wc * 100).ToString("F0");

                WorldSettlementFC s = foundSettlement;
                settlementList.Add(new FloatMenuOption(
                    FoundSettlementString(s, wcText, s == currentDefender),
                    delegate
                    {
                        if (s.MilitaryComp?.IsMilitaryBusy() != true)
                            MilitaryUtilFC.ChangeDefendingMilitaryForce(evt, s);
                    }
                ));
            }

            // Add external auto-defenders (VOE outposts, etc.)
            foreach (IAutoDefender defender in AutoDefenderRegistry.Defenders)
            {
                if (!defender.CanAutoDefend) continue;
                if (evt.externalDefenderSource != null && evt.externalDefenderSource == defender.WorldObject) continue;
                int distance = Find.WorldGrid.TraversalDistanceBetween(defender.WorldObject.Tile, WorldSettlement.Tile);
                if (distance > defender.Range) continue;

                IAutoDefender d = defender;
                MilitaryForce extForce = d.CreateDefendingForce();
                double extWc = SimulateBattleFc.CalculateDefenderWinChance(attackForce, extForce);
                settlementList.Add(new FloatMenuOption(
                    d.WorldObject.LabelCap + " (" + "FCMilitaryLevel".Translate() + " " + d.MilitaryLevel
                        + " - Victory: " + (extWc * 100).ToString("F0") + "%)",
                    delegate { MilitaryUtilFC.ChangeDefendingToExternalForce(evt, d); }
                ));
            }

            if (settlementList.Count == 0)
                settlementList.Add(new FloatMenuOption("FCNoValidMilitaries".Translate(), null));

            var floatMenu2 = new FloatMenu(settlementList)
            {
                vanishIfMouseDistant = true
            };
            Find.WindowStack.Add(floatMenu2);
        }

        public override IEnumerable<Gizmo> GetCaravanGizmos(Caravan caravan)
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            if (isUnderAttack)
            {
                yield return DefendColonyCaravan(caravan);
            }
        }

        private Command DefendColonyCaravan(Caravan caravan)
        {
            Command_Action defendColonyCaravan = new Command_Action
            {
                defaultLabel = "FCDefendColony".Translate(),
                defaultDesc = "FCDefendColonyDesc".Translate(),
                icon = TexLoad.iconMilitary,
                action = () =>
                {
                    StartDefence(MilitaryUtilFC.ReturnMilitaryEventByLocation(WorldSettlement.Tile), () => CaravanDefend(caravan));
                }
            };
            /* If auto-battle is enabled, then disable the button. We leave it visible, though, so that the player knows that this is an option if
             * they change their settings. (Once manual fighting becomes an option the player can use, at least) */
            AcceptanceReport canUse = CanDoManualFight();
            if (!canUse.Accepted)
            {
                defendColonyCaravan.Disable(canUse.Reason);
            }

            return defendColonyCaravan;
        }

        private AcceptanceReport CanDoManualFight()
        {
            if (!WorldSettlement.settlementDef.supportsManualBattle)
            {
                return new AcceptanceReport("FCSettlementTypeNoManualBattle".Translate());
            }
            if (FCSettings.battleMode == BattleMode.Auto)
            {
                return new AcceptanceReport("FCAutoBattleEnabledNoManualFight".Translate());
            }
            if (FCSettings.battleMode == BattleMode.Hybrid && !IsPlayerCaravanOnTile())
            {
                return new AcceptanceReport("FCHybridBattleEnabledNoManualFight".Translate());
            }
            return AcceptanceReport.WasAccepted;
        }

        private bool IsPlayerCaravanOnTile()
        {
            return Find.WorldObjects.Caravans.Any(c =>
                c.Tile == WorldSettlement.Tile &&
                c.Faction == Faction.OfPlayer);
        }

        private int CountOtherSettlementBattleMaps()
        {
            int count = 0;
            foreach (WorldSettlementFC settlement in FactionCache.FactionComp?.settlements ?? Enumerable.Empty<WorldSettlementFC>())
            {
                if (settlement == WorldSettlement) continue;
                if (settlement.Map != null && settlement.MilitaryComp?.isUnderAttack == true) count++;
            }
            return count;
        }

        public void CaravanDefend(Caravan caravan)
        {
            var pawns = caravan.pawns.InnerListForReading.ListFullCopy();

            // Check for shuttle in caravan (Odyssey DLC passenger shuttle)
            var shuttle = caravan.Shuttle;
            if (shuttle != null && Map != null)
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
            // Extract shuttle from pawn inventory before destroying caravan
            Pawn owner = CaravanInventoryUtility.GetOwnerOf(caravan, shuttle);
            owner?.inventory.innerContainer.Remove(shuttle);

            // Register pawns as defenders (for win/loss counting) but don't assign to a lord
            // since they're still inside the shuttle and not spawned on the map yet.
            RegisterPawnsAsDefenders(pawns, assignToLord: false);
            if (!caravan.Destroyed) caravan.Destroy();

            // Build TransportShip with pawns loaded inside
            CompShuttle compShuttle = shuttle.TryGetComp<CompShuttle>();
            TransportShipDef shipDef = compShuttle?.Props?.shipDef ?? TransportShipDefOf.Ship_Shuttle;
            TransportShip transportShip = TransportShipMaker.MakeTransportShip(shipDef, pawns, shuttle);

            // Prevent DeleteMap from removing the map while shuttle is in flight
            shuttleLandingPending = true;

            // Defer targeting to the next CompTick — UI can't render during LongEvents.
            var map = Map;
            var settlement = WorldSettlement;
            var shuttleDef = shuttle.def;
            Rot4 shuttleRotation = shuttleDef.defaultPlacingRot;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                // Force camera to the battle map so the player sees where to land
                Current.Game.CurrentMap = map;
                CameraJumper.TryJump(new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), map);

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
                        RoyalTitlePermitWorker_CallShuttle.DrawShuttleGhost(target, map, shuttleDef, shuttleRotation);
                    },
                    delegate(LocalTargetInfo target)
                    {
                        return RoyalTitlePermitWorker_CallShuttle.ShuttleCanLandHere(target, map, shuttleDef, shuttleRotation);
                    },
                    null,
                    delegate
                    {
                        if (landed) return;
                        shuttleLandingPending = false;
                        // Player cancelled targeting — auto-land at best spot
                        if (!Find.Maps.Contains(map)) return;
                        IntVec3 fallback = DropCellFinder.GetBestShuttleLandingSpot(map, Faction.OfPlayer);
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
            var enterCell = FindNearEdgeCell(Map);
            foreach (var pawn in pawns)
            {
                var loc = CellFinder.RandomSpawnCellForPawnNear(enterCell, Map);
                GenSpawn.Spawn(pawn, loc, Map, Rot4.Random);
            }
        }

        public void AddToDefenceFromList(List<Pawn> pawns, int destinationTile)
        {
            AddToDefenceFromList(pawns, destinationTile, assignToLord: true);
        }

        /// <summary>
        /// Registers pawns with the defense system (defenders list + battle lord).
        /// When <paramref name="assignToLord"/> is false, pawns are added to defenders
        /// but not to the battle lord. This is needed for entities like Vehicle Framework
        /// vehicles that have their own job systems and conflict with lord duty assignments.
        /// </summary>
        public void AddToDefenceFromList(List<Pawn> pawns, int destinationTile, bool assignToLord)
        {
            if (pawns.NullOrEmpty())
            {
                LogUtil.Error("Tried to add an empty list of pawns to an FCEvent");
                return;
            }

            // If battle is already in progress (map loaded), register directly.
            // Avoids a redundant StartDefence call that would fail to find the event
            // (already consumed) and queue a second LongEvent causing cascade errors.
            if (isUnderAttack && Map != null)
            {
                RegisterPawnsAsDefenders(pawns, assignToLord);
                return;
            }

            StartDefence(
                MilitaryUtilFC.ReturnMilitaryEventByLocation(destinationTile), () =>
                {
                    RegisterPawnsAsDefenders(pawns, assignToLord);
                });
        }

        private void RegisterPawnsAsDefenders(List<Pawn> pawns, bool assignToLord)
        {
            if (assignToLord)
            {
                Lord existingLord = defenders.Any() ? defenders[0].GetLord() : null;
                if (existingLord != null)
                {
                    foreach (var pawn in pawns)
                    {
                        if (!defenders.Contains(pawn) && !existingLord.ownedPawns.Contains(pawn))
                            existingLord.AddPawn(pawn);
                    }
                }
                else if (Map != null)
                {
                    var lordless = new List<Pawn>();
                    foreach (var pawn in pawns)
                    {
                        if (!defenders.Contains(pawn) && pawn.GetLord() is null)
                            lordless.Add(pawn);
                    }
                    if (lordless.Any())
                        LordMaker.MakeNewLord(FactionCache.PlayerColonyFaction,
                            new LordJob_ColonistsIdle(WorldSettlement), Map, lordless);
                }
            }

            foreach (var pawn in pawns)
            {
                if (!defenders.Contains(pawn))
                {
                    defenders.Add(pawn);
                    initialDefenderCount++;
                }
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            if (isUnderAttack)
                foreach (var option in WorldSettlementDefendAction.GetFloatMenuOptions(caravan, WorldSettlement))
                    yield return option;
        }

        private void DeleteMap(bool won = true)
        {
            var map = Map;
            if (map is null) return;

            // Restore faction on Empire defenders the player drafted during battle.
            // After this, any remaining Faction.OfPlayer pawns are real player colonists.
            Faction empireFaction = FactionCache.PlayerColonyFaction;
            foreach (Pawn npc in draftedNPCs)
            {
                if (npc is null || npc.Dead || npc.Destroyed) continue;
                if (npc.Faction == Faction.OfPlayer)
                    npc.SetFaction(empireFaction);
            }
            draftedNPCs.Clear();

            // Check for player pawns on the map (spawned as Faction.OfPlayer free colonists)
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
                // ShouldRemoveMapNow checks AnyPawnBlockingMapRemoval and will
                // auto-remove the map once all player pawns/shuttles have left.
                // Notify_MyMapAboutToBeRemoved handles Empire pawn cleanup at that point.

                // Remove battle lords, then re-assign Empire defenders to an idle lord
                // so they hold position instead of wandering to the map edge.
                foreach (var lord in map.lordManager.lords.ListFullCopy())
                    map.lordManager.RemoveLord(lord);

                List<Pawn> empireDefenders = new List<Pawn>();
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    if (pawn.Faction == empireFaction && !pawn.Dead && !pawn.Downed)
                        empireDefenders.Add(pawn);

                // Stop stale jobs that survived lord cleanup (e.g. Goto with exitMapOnArrival).
                // Lord.Cleanup only interrupts jobs where EndPawnJobOnCleanup returns true;
                // the rest keep executing and can walk pawns off the map.
                foreach (Pawn pawn in empireDefenders)
                    pawn.jobs.StopAll();

                if (empireDefenders.Any())
                    LordMaker.MakeNewLord(empireFaction, new LordJob_ColonistsIdle(WorldSettlement), map, empireDefenders);

                return;
            }

            // Immediate path — remove all lords before cleanup
            foreach (var lord in map.lordManager.lords.ListFullCopy())
                map.lordManager.RemoveLord(lord);

            if (playerPawns.Count > 0)
            {
                // All player pawns are downed — deliver them home via event, then remove map
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
                                LogUtil.Error("SettlementMilitary.DeleteMap: Too many tend iterations.");
                                break;
                            }
                            TendUtility.DoTend(null, pawn, null);
                        }
                    }

                string eventText = won
                    ? DeliveryEvent.ShuttleEventInjuredString
                    : DeliveryEvent.ShuttleEventInjuredLostString;
                int travelTicks = TravelUtil.ReturnTicksToArrive(WorldSettlement.Tile, Find.AnyPlayerHomeMap.Tile);
                if (!won) travelTicks += GenDate.TicksPerDay;

                var goods = new List<Thing>(playerPawns.Count);
                foreach (Pawn pawn in playerPawns) goods.Add(pawn);

                var eventParams = new FCEvent
                {
                    location = Find.AnyPlayerHomeMap.Tile,
                    source = WorldSettlement.Tile,
                    goods = goods,
                    customDescription = eventText,
                    timeTillTrigger = Find.TickManager.TicksGame + travelTicks
                };
                DeliveryEvent.CreateDeliveryEvent(eventParams);
                string travelDays = ((float)travelTicks / GenDate.TicksPerDay).ToString("0.#");
                pendingDeliveryMessage = "FCInjuredCaravanMembersReturning".Translate(playerPawns.Count, travelDays);
            }

            // No player pawns (or all downed and delivered) — immediate map removal.
            // Notify_MyMapAboutToBeRemoved handles Empire pawn cleanup.
            CameraJumper.TryJump(WorldSettlement.Tile);
            Current.Game.CurrentMap = Find.AnyPlayerHomeMap;
            Current.Game.DeinitAndRemoveMap(map, false);
        }

        public void StartDefence(FCEvent evt, Action after)
        {
            // Consume the event from the faction queue exactly once, regardless of entry point
            // (manual Defend button, caravan defend, or timer-driven ProcessEvents). Prevents
            // ProcessEvents from re-triggering a second defense after this one resolves.
            FactionCache.FactionComp?.RemoveEvent(evt);

            // Path 1: Add wave to active battle
            if (battleMapInitialized && Map is object && isUnderAttack)
            {
                var wave = new DefenseWave(evt, evt.militaryForceAttacking, evt.militaryForceDefending, evt.militaryForceAttackingFaction);
                activeWaves.Add(wave);

                LongEventHandler.QueueLongEvent(() =>
                {
                    SpawnWaveAttackers(wave);
                    GenerateWaveReinforcements(wave);

                    string enemyName = wave.attackerFaction?.Name ?? "Unknown";
                    Find.LetterStack.ReceiveLetter(
                        "FCNewWaveArrived".Translate(WorldSettlement.Name),
                        "FCNewWaveArrivedDesc".Translate(WorldSettlement.Name, enemyName),
                        LetterDefOf.ThreatBig,
                        new LookTargets(WorldSettlement));
                    after.Invoke();
                }, "GeneratingMap", false, null);
                return;
            }

            // Determine auto-resolve
            bool shouldAutoResolve = false;
            if (FCSettings.battleMode == BattleMode.Auto || !WorldSettlement.settlementDef.supportsManualBattle)
            {
                shouldAutoResolve = true;
            }
            else if (FCSettings.battleMode == BattleMode.Hybrid)
            {
                shouldAutoResolve = !battleMapInitialized && !IsPlayerCaravanOnTile();
            }

            // Concurrent battle limit check
            if (!shouldAutoResolve && FCSettings.maxConcurrentBattleMaps > 0
                && CountOtherSettlementBattleMaps() >= FCSettings.maxConcurrentBattleMaps)
                shouldAutoResolve = true;

            if (shouldAutoResolve)
            {
                initialDefenderCount = (int)evt.militaryForceDefending.forceRemaining;
                BattleResult battleResult = SimulateBattleFc.FightBattle(evt.militaryForceAttacking, evt.militaryForceDefending);
                EndBattle(battleResult.DefenderVictory, (int)evt.militaryForceDefending.forceRemaining, battleResult);
                return;
            }

            // Path 2: Reuse post-battle map (player still on it)
            if (Map is object && !isUnderAttack)
            {
                // isUnderAttack is computed from manager state; the active defensive op
                // already drives it. Just flag battleMapInitialized for legacy bookkeeping.
                battleMapInitialized = true;

                var wave = new DefenseWave(evt, evt.militaryForceAttacking, evt.militaryForceDefending, evt.militaryForceAttackingFaction);
                activeWaves.Add(wave);

                LongEventHandler.QueueLongEvent(() =>
                {
                    // Re-recruit surviving Empire NPC defenders from their idle lord
                    RecruitIdleDefenders();
                    SpawnWaveAttackers(wave);
                    GenerateWaveReinforcements(wave);
                    Find.TickManager.Notify_GeneratedPotentiallyHostileMap();

                    string enemyName = wave.attackerFaction?.Name ?? "Unknown";
                    Find.LetterStack.ReceiveLetter(
                        "FCManualBattleStarted".Translate(WorldSettlement.Name),
                        "FCManualBattleStartedDesc".Translate(WorldSettlement.Name, enemyName),
                        LetterDefOf.ThreatBig,
                        new LookTargets(WorldSettlement));
                    after.Invoke();
                }, "GeneratingMap", false, null);
                return;
            }

            // Path 3: Fresh battle — generate map
            if (evt.militaryForceDefending is null)
            {
                LogUtil.Warning($"StartDefence: defenderForce is null for {WorldSettlement.Name}, settlement loses by default.");
                EndBattle(false, 0, null);
                return;
            }

            var initialWave = new DefenseWave(evt, evt.militaryForceAttacking, evt.militaryForceDefending, evt.militaryForceAttackingFaction);
            activeWaves.Add(initialWave);

            LongEventHandler.QueueLongEvent(() =>
            {
                if (Map == null)
                    MapGenerator.GenerateMap(new IntVec3(70 + WorldSettlement.settlementLevel * 10, 1, 70 + WorldSettlement.settlementLevel * 10),
                                             WorldSettlement, WorldSettlement.MapGeneratorDef, WorldSettlement.ExtraGenStepDefs);

                ZoomIntoTile(evt);
                SetupAttack(evt);
                after.Invoke();
            },
                "GeneratingMap", false, GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);
        }

        private void SetupAttack(FCEvent temp)
        {
            // ZoomIntoTile may have aborted via EndBattle (null event / null force); don't
            // spawn attackers into a cleaned-up state.
            if (!isUnderAttack) return;

            if (Map is null)
            {
                LogUtil.Error($"SetupAttack: {WorldSettlement.Name} has no map. Resetting battle state.");
                EndBattle(false, 0, null);
                return;
            }

            if (temp.militaryForceAttacking is null || temp.militaryForceAttackingFaction is null)
            {
                LogUtil.Error($"SetupAttack: Missing attacking force or faction for {WorldSettlement.Name}. Resetting battle state.");
                EndBattle(false, 0, null);
                return;
            }

            // Find the wave that was created in StartDefence (the last one added)
            DefenseWave wave = activeWaves.Count > 0 ? activeWaves[activeWaves.Count - 1] : null;
            if (wave is null)
            {
                // Shouldn't happen — StartDefence always adds a wave before calling SetupAttack
                wave = new DefenseWave(temp, temp.militaryForceAttacking, temp.militaryForceDefending, temp.militaryForceAttackingFaction);
                activeWaves.Add(wave);
            }

            SpawnWaveAttackers(wave);
        }

        /// <summary>
        /// Spawns attackers for a single wave onto the current map.
        /// Adds them to both the wave's <c>waveAttackers</c> and the comp's flat <c>attackers</c> list.
        /// </summary>
        private void SpawnWaveAttackers(DefenseWave wave)
        {
            if (Map is null || wave.attackerForce is null) return;

            IncidentParms parms = new IncidentParms
            {
                target = Map,
                faction = wave.attackerFaction,
                generateFightersOnly = true,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidNeverFleeIndividual = true
            };
            parms.points = Math.Max(
                IncidentWorker_Raid.AdjustedRaidPoints(
                    (float)wave.attackerForce.forceRemaining * 175,
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
                LogUtil.Error("Got no pawns spawning wave attackers from parms " + parms);
                if (!attackers.Any())
                    LongEventHandler.QueueLongEvent(EndAttack, "EndingAttack", false, null);
                return;
            }

            double attackerEfficiency = wave.attackerForce.militaryEfficiency;
            foreach (Pawn attacker in newAttackers)
            {
                MilitaryEfficiencyUtil.ApplyCombatEfficiencyHediff(attacker, attackerEfficiency);
            }

            parms.raidArrivalMode.Worker.Arrive(newAttackers, parms);

            wave.waveAttackers.AddRange(newAttackers);
            attackers.AddRange(newAttackers);
            LordMaker.MakeNewLord(
                parms.faction,
                new LordJob_HuntColonists(WorldSettlement, parms.raidArrivalMode != PawnsArrivalModeDefOf.CenterDrop),
                Map, newAttackers);
        }

        /// <summary>
        /// Spawns reinforcement defenders for a wave if a foreign defending settlement's squad is available.
        /// Does NOT generate random pawns if the squad is deployed — the settlement fights with existing defenders.
        /// </summary>
        private void GenerateWaveReinforcements(DefenseWave wave)
        {
            if (Map is null || wave.defenderForce is null) return;

            var homeComp = wave.defenderForce.homeSettlement?.MilitaryComp;
            if (homeComp is null) return;
            // Don't spawn reinforcements from the home settlement defending itself — already handled
            if (wave.defenderForce.homeSettlement == WorldSettlement) return;

            bool hasSquad = homeComp.militarySquad != null
                && homeComp.militarySquad.outfit != null
                && homeComp.militarySquad.mercenaries.Any();
            if (!hasSquad) return;
            if (homeComp.militarySquad.IsPhysicallyDeployed()) return;

            var squad = homeComp.militarySquad;
            squad.CheckInitialization();
            squad.OutfitSquad(squad.outfit);
            squad.UpdateSquadStats(wave.defenderForce.homeSettlement.settlementMilitaryLevel);
            squad.ResetNeeds();

            double efficiency = wave.defenderForce.militaryEfficiency;
            foreach (Pawn merc in squad.AllEquippedMercenaryPawns)
            {
                MilitaryEfficiencyUtil.ShiftPawnGearQuality(merc, efficiency);
                MilitaryEfficiencyUtil.ApplyCombatEfficiencyHediff(merc, efficiency);
            }

            List<Pawn> reinforcements = squad.AllEquippedMercenaryPawns.ToList();
            Lord defenseLord = defenders.Count > 0 ? defenders[0].GetLord() : null;
            var spawnedReinforcements = new List<Pawn>();

            foreach (Pawn friendly in reinforcements)
            {
                if (friendly.IsWildMan()) continue;
                friendly.ApplyIdeologyRitualWounds();

                IntVec3 loc;
                if (!CellFinder.TryFindRandomCellNear(Map.Center, Map, 20, c => c.Standable(Map), out loc))
                    loc = Map.Center;

                try
                {
                    GenSpawn.Spawn(friendly, loc, Map, new Rot4());
                    friendly.drafter = new Pawn_DraftController(friendly);
                    Map.mapPawns.RegisterPawn(friendly);
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
                    LordMaker.MakeNewLord(FactionCache.PlayerColonyFaction,
                        new LordJob_DefendColony(WorldSettlement, new Dictionary<Pawn, Pawn>()),
                        Map, spawnedReinforcements);
                }

                wave.waveDefenders.AddRange(spawnedReinforcements);
                defenders.AddRange(spawnedReinforcements);
                initialDefenderCount += spawnedReinforcements.Count;
            }
        }

        /// <summary>
        /// Re-recruits surviving Empire NPC defenders from their idle lord into a new defense lord.
        /// Used when reusing a post-battle map for a new attack (Path 2).
        /// </summary>
        private void RecruitIdleDefenders()
        {
            if (Map is null) return;
            Faction empireFaction = FactionCache.PlayerColonyFaction;
            var idleDefenders = new List<Pawn>();

            foreach (Pawn pawn in Map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Dead || pawn.Downed) continue;
                if (pawn.Faction != empireFaction && pawn.Faction != Faction.OfPlayer) continue;
                if (pawn.IsPrisonerOfColony) continue;
                idleDefenders.Add(pawn);
            }

            // Remove from any existing idle lords
            foreach (Pawn pawn in idleDefenders)
            {
                Lord lord = pawn.GetLord();
                if (lord != null)
                    lord.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
            }

            if (idleDefenders.Any())
            {
                LordMaker.MakeNewLord(empireFaction,
                    new LordJob_DefendColony(WorldSettlement, new Dictionary<Pawn, Pawn>()),
                    Map, idleDefenders);
                defenders.AddRange(idleDefenders);
                initialDefenderCount = defenders.Count;
            }
        }

        private static PawnsArrivalModeDef ResolveRaidArriveMode(IncidentParms parms)
        {
            return parms.raidStrategy.arriveModes
                .Where(mode => mode.Worker.CanUseWith(parms))
                .TryRandomElementByWeight(mode => mode.Worker.GetSelectionWeight(parms), out PawnsArrivalModeDef output)
                ? output
                : PawnsArrivalModeDefOf.EdgeWalkIn;
        }

        private void ZoomIntoTile(FCEvent evt)
        {
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            if (!battleMapInitialized)
            {
                if (evt == null)
                {
                    LogUtil.Warning("Aborting defense, null FCEvent! Resetting battle state.");
                    EndBattle(false, 0, null);
                    return;
                }

                var force = MilitaryUtilFC.ReturnDefendingMilitaryForce(evt);
                if (force == null)
                {
                    LogUtil.Warning($"Aborting defense for {WorldSettlement?.Name}, null defending force. Resetting battle state.");
                    EndBattle(false, 0, null);
                    return;
                }

                battleMapInitialized = true;

                // Foreign defender's busy state is now derived from the manager op's
                // defender.homeSettlement; no shadow write needed here.

                Map.fogGrid.ClearAllFog();

                // Remove pawns spawned by KCSG/VBGE that don't belong to Empire or the player.
                // KCSG's SymbolResolver_Settlement spawns generic faction pawns during map gen
                // that conflict with Empire's lord-based military system.
                Faction empireFaction = FactionCache.PlayerColonyFaction;
                List<Pawn> toRemove = new List<Pawn>();
                foreach (Pawn pawn in Map.mapPawns.AllPawnsSpawned)
                {
                    if (!pawn.RaceProps.Humanlike) continue;
                    if (pawn.Faction == empireFaction) continue;
                    if (pawn.Faction == Faction.OfPlayer) continue;
                    toRemove.Add(pawn);
                }
                foreach (Pawn pawn in toRemove)
                {
                    pawn.Destroy();
                }
                if (toRemove.Count > 0)
                    LogUtil.Message($"Cleaned up {toRemove.Count} unrelated pawns from {WorldSettlement.Name}");

                GenerateFriendlies(force, evt);
                RecruitMapInhabitants();
                Find.TickManager.Notify_GeneratedPotentiallyHostileMap();

                string enemyName = attackerForce?.homeFaction?.Name ?? "Unknown";
                GlobalTargetInfo jumpTarget = defenders.Any()
                    ? new GlobalTargetInfo(defenders[0])
                    : new GlobalTargetInfo(new IntVec3(Map.Size.x / 2, 0, Map.Size.z / 2), Map);
                Find.LetterStack.ReceiveLetter(
                    "FCManualBattleStarted".Translate(WorldSettlement.Name),
                    "FCManualBattleStartedDesc".Translate(WorldSettlement.Name, enemyName),
                    LetterDefOf.ThreatBig,
                    new LookTargets(jumpTarget));
            }
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

        private void GenerateFriendlies(MilitaryForce force, FCEvent battleEvent = null)
        {
            var points = Math.Max((float)(force.forceRemaining * 100), 50f);
            List<Pawn> friendlies = null;
            var riders = new Dictionary<Pawn, Pawn>();

            // Try external defender pawns (VOE outposts, etc.)
            if (force.homeSettlement == null && battleEvent?.externalDefenderSource != null)
            {
                IAutoDefender extDefender = AutoDefenderRegistry.FindByWorldObject(
                    battleEvent.externalDefenderSource);
                friendlies = extDefender?.GetDefendingPawns();
            }

            if (friendlies != null && friendlies.Count > 0)
            {
                // External defender provided real pawns — skip squad/random generation
            }
            else
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
                    // No squad configured — generate random pawns as fallback
                    var parms = new IncidentParms
                    {
                        target = Map,
                        faction = FactionCache.PlayerColonyFaction,
                        generateFightersOnly = true,
                        raidStrategy = RaidStrategyDefOf.ImmediateAttackFriendly
                    };
                    parms.points = IncidentWorker_Raid.AdjustedRaidPoints(points,
                        PawnsArrivalModeDefOf.EdgeWalkIn, parms.raidStrategy,
                        parms.faction, PawnGroupKindDefOf.Combat,
                        parms.target // new required parameter
                    );
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
                // else: squad exists but is physically deployed — no defenders from this source.
                // Settlement fights with inhabitants only (RecruitMapInhabitants).
            } // end else (no external defender pawns)

            void tryFindLoc(out IntVec3 loc, Pawn friendly)
            {
                var min = (70 + WorldSettlement.settlementLevel * 10) / 2 - 5 - 5 * WorldSettlement.settlementLevel;
                var size = 10 + WorldSettlement.settlementLevel * 10;
                CellFinder.TryFindRandomCellInsideWith(new CellRect(min, min, size, size),
                    testing => testing.Standable(Map) && Map.reachability.CanReachMapEdge(testing,
                        TraverseParms.For(TraverseMode.PassDoors)), out loc);
                if (loc.x == -1000)
                {
                    LogUtil.Message("Failed with " + friendly + ", " + loc);
                    CellFinder.TryFindRandomCellNear(new IntVec3(min + 10 + WorldSettlement.settlementLevel, 1,
                            min + 10 + WorldSettlement.settlementLevel), Map, 75,
                        testing => testing.Standable(Map), out loc);
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
                            testing => testing.Standable(Map) && Map.reachability.CanReachMapEdge(testing,
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
                    GenSpawn.Spawn(friendly, loc, Map, new Rot4());
                    friendly.drafter = new Pawn_DraftController(friendly);
                    Map.mapPawns.RegisterPawn(friendly);
                    spawnedFriendlies.Add(friendly);
                }
                catch (Exception e)
                {
                    LogUtil.Warning($"Failed to spawn defender {friendly.LabelShort} (likely a mod conflict): {e}");
                }
            }

            LordMaker.MakeNewLord(FactionCache.PlayerColonyFaction, new LordJob_DefendColony(WorldSettlement, riders), Map, spawnedFriendlies);

            defenders = spawnedFriendlies;
            initialDefenderCount = defenders.Count;

            // Track external defender pawns on the arriving wave so EndAttack can return them
            // via IAutoDefender.ReturnDefendingPawns.
            if (force.homeSettlement == null && battleEvent?.externalDefenderSource != null)
            {
                DefenseWave wave = activeWaves.FirstOrDefault(w => w.sourceEvent == battleEvent);
                if (wave is null)
                {
                    wave = activeWaves.FirstOrDefault();
                    if (wave is object)
                        LogUtil.Warning($"GenerateFriendlies: no wave matched battleEvent for external defender at {WorldSettlement.Name}; falling back to first active wave.");
                    else
                        LogUtil.Error($"GenerateFriendlies: no active waves to track external defender pawns at {WorldSettlement.Name}; outpost will lose its pawns at battle end.");
                }
                wave?.waveDefenders.AddRange(spawnedFriendlies);
            }
        }

        private void RecruitMapInhabitants()
        {
            if (Map == null || !defenders.Any()) return;

            Lord defenseLord = defenders[0].GetLord();
            if (defenseLord == null) return;

            Faction empireFaction = FactionCache.PlayerColonyFaction;
            var inhabitants = new List<Pawn>();

            foreach (Pawn pawn in Map.mapPawns.AllPawnsSpawned)
            {
                if (defenders.Contains(pawn)) continue;
                if (!pawn.RaceProps.Humanlike) continue;
                if (pawn.Downed || pawn.Dead) continue;
                if (pawn.Faction != empireFaction) continue;
                if (pawn.IsPrisonerOfColony) continue;
                inhabitants.Add(pawn);
            }

            int targetCount = (int)WorldSettlement.workers;

            // Remove excess civilians
            while (inhabitants.Count > targetCount)
            {
                Pawn excess = inhabitants[inhabitants.Count - 1];
                inhabitants.RemoveAt(inhabitants.Count - 1);
                if (excess.Spawned) excess.Destroy();
            }

            // Spawn additional civilians if needed
            int spawnAttempts = 0;
            while (inhabitants.Count < targetCount && spawnAttempts < targetCount * 2)
            {
                spawnAttempts++;
                Pawn civilian = PawnGenerator.GeneratePawn(FCPawnGenerator.CivilianRequest());
                IntVec3 loc;
                if (!CellFinder.TryFindRandomCellNear(Map.Center, Map, 15, c => c.Standable(Map), out loc))
                    loc = Map.Center;
                try
                {
                    GenSpawn.Spawn(civilian, loc, Map);
                    inhabitants.Add(civilian);
                }
                catch (Exception e)
                {
                    LogUtil.Warning($"Failed to spawn civilian (likely a mod conflict): {e}");
                }
            }

            // Strip weapons from most civilians so they are visually distinct from guards (~12% keep weapons)
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
                defenders.Add(inhabitant);
            }

            if (inhabitants.Count > 0)
            {
                initialDefenderCount = defenders.Count;
                LogUtil.Message($"Added {inhabitants.Count} settlement inhabitants to defenders at {WorldSettlement.Name}");
            }
        }

        public void EndBattle(bool won, int remaining, BattleResult battleResult = null)
        {
            var faction = FactionCache.FactionComp;

            LogUtil.Message("WorldSettlementFC.EndBattle: Handling combat resolution...");
            try
            {
                if (won)
                {
                    WinBattle(faction);
                }
                else
                {
                    LoseBattle(faction);
                }

                // Phase 2: walk defensive ops at this tile, fire CompleteBattle on each.
                // Each op fires its own LifecycleRegistry.OnBattleResolved, schedules its own
                // cooldown event linked to itself, and (via FactionFC's hook) updates the
                // foreign defender's comp shadow to Cooldown. This replaces the legacy per-wave
                // CooldownMilitary loop.
                MilitaryOperationManager manager = FactionCache.MilitaryManager;
                if (manager is object)
                {
                    BattleResult resultForOps = battleResult ?? new BattleResult
                    {
                        winner = won ? BattleWinner.Defender : BattleWinner.Attacker
                    };
                    var opsAtTile = manager.GetOpsAt(WorldSettlement.Tile);
                    if (opsAtTile.Count > 0)
                    {
                        // Snapshot to avoid enumeration mutation if CompleteBattle unregisters.
                        var snapshot = new List<MilitaryOperation>(opsAtTile);
                        foreach (MilitaryOperation op in snapshot)
                        {
                            if (op is null) continue;
                            if (!op.IsDefensive) continue;
                            if (op.phase == MilitaryOperationPhase.CooldownPending
                                || op.phase == MilitaryOperationPhase.Resolved) continue;
                            try { op.CompleteBattle(resultForOps); }
                            catch (Exception innerEx)
                            {
                                LogUtil.Error($"EndBattle: op id={op.id} threw in CompleteBattle: {innerEx}");
                            }
                        }
                    }
                    else
                    {
                        // Legacy fallback: no ops registered (loaded mid-battle from pre-refactor
                        // save). Run the old per-wave settlement cooldown so existing flow works.
                        LogUtil.Message("WorldSettlementFC.EndBattle: No manager ops at tile; falling back to legacy CooldownMilitary.");
                        CooldownMilitary(remaining, won);
                    }
                }
                else
                {
                    CooldownMilitary(remaining, won);
                }
            }
            catch (Exception e)
            {
                LogUtil.Error($"Encountered an error while trying to resolve combat in Empire{Environment.NewLine}{e}");
            }
            // isUnderAttack is computed from manager state; the op completing already drove it.
            battleMapInitialized = false;
        }

        private void ClearAttackState()
        {
            // Foreign defender's commitment is owned by the manager op now; ReturnMilitary on
            // the comp is a no-op for op-driven flow. The legacy ReturnMilitary call is kept
            // for back-compat with pre-refactor save data without linkedOperationId.
            if (defenderForce?.homeSettlement is object
                && defenderForce.homeSettlement != WorldSettlement)
            {
#pragma warning disable 0618
                defenderForce.homeSettlement.MilitaryComp?.ReturnMilitary(false);
#pragma warning restore 0618
            }

            // isUnderAttack is computed from manager state.
            endingBattle = false;
            battleMapInitialized = false;
            shuttleLandingPending = false;
            attackers?.Clear();
            defenders?.Clear();
            draftedNPCs?.Clear();
            activeWaves?.Clear();
        }

        public void PostSettlementLoadInit(WorldSettlementFC settlement)
        {
            if (isUnderAttack
                && MilitaryUtilFC.ReturnMilitaryEventByLocation(settlement.Tile) is null
                && !attackers.Any() && !defenders.Any())
            {
                // Save taken mid-battle: event was removed from the queue but combatants are still
                // scribed. Leave the battle state alone (EndBattle will clear naturally on resolve).
                LogUtil.Warning($"Repairing stuck isUnderAttack flag on {settlement.Name} during load " +
                    $"(no matching settlementBeingAttacked event).");
                ClearAttackState();
            }

            // Orphan-DefendFriendlySettlement repair: catches deploys whose target was cleaned up
            // without notifying us (any code path that bypasses ClearAttackState's notify hook).
            if (militaryBusy
                && militaryJob == MilitaryJobDefOf.DefendFriendlySettlement
                && IsStaleDeploy())
            {
                LogUtil.Warning($"Clearing orphaned DefendFriendlySettlement on {settlement.Name} during load.");
                ReturnMilitary(false);
            }
        }

        // Shared by load-time and debug-force paths. Caller has already verified
        // militaryJob == DefendFriendlySettlement.
        public bool IsStaleDeploy()
        {
            // Tile-less deploy: SendMilitary sets job + location together, so this is broken state.
            if (militaryLocation == PlanetTile.Invalid) return true;
            
            // Active warning event for the target; defense is actually in progress.
            if (MilitaryUtilFC.ReturnMilitaryEventByLocation(militaryLocation) is object) return false;
            
            // Target world object is gone (settlement destroyed, outpost despawned, etc.): stale.
            var targetComp = Find.WorldObjects.WorldObjectAt<WorldSettlementFC>(militaryLocation)?.MilitaryComp;
            if (targetComp is null) return true;
            
            // Target exists, no event, not under attack: stale.
            return !targetComp.isUnderAttack;
        }

        private void CooldownMilitary(int remaining, bool won)
        {
            // Track which settlements we've already processed (a settlement may defend multiple waves)
            var processedSettlements = new HashSet<WorldSettlementFC>();
            int battleDeaths = Math.Max(0, initialDefenderCount - remaining);
            bool overwhelming = won && remaining >= initialDefenderCount;

            foreach (DefenseWave wave in activeWaves)
            {
                MilitaryForce waveDefForce = wave.defenderForce;

                // External auto-defender (no homeSettlement)
                if (waveDefForce is null || waveDefForce.homeSettlement is null)
                {
                    if (wave.externalDefenderSource is object)
                    {
                        IAutoDefender extDefender = AutoDefenderRegistry.FindByWorldObject(wave.externalDefenderSource);
                        extDefender?.OnDefenseComplete(won, null);
                    }
                    continue;
                }

                WorldSettlementFC defHome = waveDefForce.homeSettlement;
                if (processedSettlements.Contains(defHome)) continue;
                processedSettlements.Add(defHome);

                var homeComp = defHome.MilitaryComp;
                if (homeComp is null) continue;

                // If defending settlement is itself under attack, defer cooldown — its own battle will handle it
                if (defHome != WorldSettlement && homeComp.isUnderAttack) continue;
                // If squad is still physically deployed on another map, defer
                if (defHome != WorldSettlement && homeComp.militarySquad?.IsPhysicallyDeployed() == true) continue;

                // If squad was busy with a non-defense job (raid, capture, etc.), generated pawns were used — skip
                if (homeComp.militaryJob != null
                    && homeComp.militaryJob != MilitaryJobDefOf.Undefined
                    && homeComp.militaryJob != MilitaryJobDefOf.DefendFriendlySettlement)
                {
                    continue;
                }

                if (overwhelming)
                {
                    homeComp.ReturnMilitary(true);
                }
                else
                {
                    homeComp.CooldownMilitaryFinal(battleDeaths);
                }
            }

            if (overwhelming && processedSettlements.Count > 0)
            {
                Find.LetterStack.ReceiveLetter("FCOverwhelmingVictory".Translate(), "FCOverwhelmingVictoryDesc".Translate(), LetterDefOf.PositiveEvent);
            }
        }

        private void LoseBattle(FactionFC faction)
        {
            faction.threatAdaptation.Notify_BattleLost();

            var happinessLostMultiplier = WorldSettlement.GetStatValue(FCStatDefOf.happinessLostMultiplier);
            var loyaltyLostMultiplier = WorldSettlement.GetStatValue(FCStatDefOf.loyaltyLostMultiplier);

            var (prosperityLoss, happinessLoss, loyaltyLoss) = SettlementFormulas.CalculateBattleLossPenalties(happinessLostMultiplier, loyaltyLostMultiplier);
            prosperityLoss *= faction.GetStatValue(FCStatDefOf.battleProsperityLossMultiplier);
            happinessLoss *= faction.GetStatValue(FCStatDefOf.battleHappinessLossMultiplier);
            loyaltyLoss *= faction.GetStatValue(FCStatDefOf.battleLoyaltyLossMultiplier);
            var canDestroyBuildings = !faction.AnyPolicyPreventsBuildingDestruction();

            // buildingDestructionChance stat scales the survival threshold:
            // stat=1.0 -> threshold 7 (36% destruction, default)
            // stat<1.0 -> higher threshold (less destruction)
            // stat>1.0 -> lower threshold (more destruction)
            double destructionStat = faction.GetStatValue(FCStatDefOf.buildingDestructionChance);
            int deconstructChance = Math.Max(0, Math.Min(11, (int)Math.Round(11 - 4 * destructionStat)));

            WorldSettlement.prosperity -= prosperityLoss;
            WorldSettlement.happiness -= happinessLoss;
            WorldSettlement.loyalty -= loyaltyLoss;

            string str = "FCDefenseFailureFull".Translate(WorldSettlement.Name);

            // Penalty summary
            str += "\n\n" + "FCDefenseFailurePenaltiesHeader".Translate();

            int displayProsperity = (int)Math.Round(prosperityLoss);
            int displayHappiness = (int)Math.Round(happinessLoss);
            int displayLoyalty = (int)Math.Round(loyaltyLoss);

            if (displayProsperity > 0)
            {
                str += "\n  - " + "FCDefenseFailureProsperityLoss".Translate(displayProsperity);
            }
            if (displayHappiness > 0)
            {
                str += "\n  - " + "FCDefenseFailureHappinessLoss".Translate(displayHappiness);
            }
            if (displayLoyalty > 0)
            {
                str += "\n  - " + "FCDefenseFailureLoyaltyLoss".Translate(displayLoyalty);
            }

            if (canDestroyBuildings && WorldSettlement?.BuildingsComp != null)
            {
                // Collect candidate slots for demolition
                List<int> candidates = new List<int>();
                for (var k = 0; k < 4; k++)
                {
                    var deconstructRoll = new IntRange(0, 10).RandomInRange;
                    if (deconstructRoll < deconstructChance ||
                        !WorldSettlement.BuildingsComp.BuildingSlotIsBuilding(k))
                    {
                        continue;
                    }
                    candidates.Add(k);
                }

                // Sort so buildings that depend on other buildings are demolished first
                candidates.Sort((a, b) =>
                {
                    BuildingFCDef defA = WorldSettlement.BuildingsComp.GetBuildingInSlot(a);
                    BuildingFCDef defB = WorldSettlement.BuildingsComp.GetBuildingInSlot(b);
                    bool aRequiresB = FactionCache.SatisfiesAnyRequirement(defB, defA.requiredBuildings);
                    bool bRequiresA = FactionCache.SatisfiesAnyRequirement(defA, defB.requiredBuildings);
                    if (aRequiresB) return -1; // a depends on b, demolish a first
                    if (bRequiresA) return 1;  // b depends on a, demolish b first
                    // Buildings with any requirements go before those without
                    int aReqCount = defA.requiredBuildings?.Count ?? 0;
                    int bReqCount = defB.requiredBuildings?.Count ?? 0;
                    return bReqCount.CompareTo(aReqCount);
                });

                foreach (int k in candidates)
                {
                    str += "\n  - " + "FCBuildingDestroyedInRaid".Translate(WorldSettlement.BuildingsComp.BuildingLabel(k));
                    WorldSettlement.DeconstructBuilding(k);
                }
            }

            if (!canDestroyBuildings)
            {
                str += "\n  - " + "FCDefenseFailureBuildingsProtected".Translate();
            }

            // level remover checker — uses same destruction stat scaling
            if (WorldSettlement?.settlementLevel > 1 && canDestroyBuildings)
            {
                var num = new IntRange(0, 10).RandomInRange;
                if (num >= deconstructChance)
                {
                    str += "\n  - " + "FCSettlementDeleveledRaid".Translate();
                    WorldSettlement.DelevelSettlement();
                }
            }

            if (!string.IsNullOrEmpty(pendingDeliveryMessage))
            {
                str += "\n\n" + pendingDeliveryMessage;
            }
            if (Map != null)
            {
                str += "\n\n" + "FCDefenseBattleOverLeaveMap".Translate();
            }
            Find.LetterStack.ReceiveLetter("FCDefenseFailure".Translate(), str, LetterDefOf.Death,
                new LookTargets(WorldSettlement));
        }

        private void WinBattle(FactionFC faction)
        {
            faction.AddExperienceToFactionLevel(5f);
            faction.threatAdaptation.Notify_BattleWon();
            string text = "FCDefenseSuccessfulFull".Translate(WorldSettlement.Name);
            if (!string.IsNullOrEmpty(pendingDeliveryMessage))
            {
                text += "\n\n" + pendingDeliveryMessage;
            }
            if (Map != null)
            {
                text += "\n\n" + "FCDefenseBattleOverLeaveMap".Translate();
            }
            Find.LetterStack.ReceiveLetter("FCDefenseSuccessful".Translate(),
                text,
                LetterDefOf.PositiveEvent, new LookTargets(WorldSettlement));
        }

        public void EndAttack()
        {
            bool won = defenders.Any();
            int remaining = defenders.Count;

            // Return external defender pawns per wave before map cleanup destroys them
            foreach (DefenseWave wave in activeWaves)
            {
                if (wave.externalDefenderSource is null) continue;
                IAutoDefender extDefender = AutoDefenderRegistry.FindByWorldObject(wave.externalDefenderSource);
                if (extDefender is null) continue;

                List<Pawn> survivingPawns = new List<Pawn>();
                foreach (Pawn pawn in wave.waveDefenders)
                {
                    if (pawn != null && !pawn.Dead && !pawn.Destroyed)
                    {
                        if (pawn.Spawned) pawn.DeSpawn();
                        survivingPawns.Add(pawn);
                        defenders.Remove(pawn);
                    }
                }
                extDefender.ReturnDefendingPawns(survivingPawns);
            }

            // Strip combat efficiency hediffs from surviving defenders (squad mercs persist between battles)
            foreach (Pawn defender in defenders)
            {
                if (defender != null && !defender.Dead && !defender.Destroyed)
                    MilitaryEfficiencyUtil.RemoveCombatEfficiencyHediff(defender);
            }

            DeleteMap(won);
            EndBattle(won, remaining);

            defenders.Clear();
            attackers.Clear();
            activeWaves.Clear();
            endingBattle = false;
            pendingDeliveryMessage = null;
        }

        public void RemoveAttacker(Pawn downed)
        {
            attackers.Remove(downed);
            attackers.RemoveAll(IsPawnTrulyGone);

            // Remove from the specific wave's attacker list; mark wave resolved if empty
            foreach (DefenseWave wave in activeWaves)
            {
                if (wave.resolved) continue;
                if (wave.waveAttackers.Remove(downed) && !wave.waveAttackers.Any())
                {
                    wave.resolved = true;
                }
            }

            attackers.RemoveAll(IsPawnTrulyGone);
            // Guard: don't declare victory while any wave still has pod-bound attackers inbound.
            if (attackers.Any() || HasPendingPodAttackers() || endingBattle || !isUnderAttack) return;

            endingBattle = true;
            LongEventHandler.QueueLongEvent(EndAttack,
                "EndingAttack", false, error =>
                {
                    DelayedErrorWindowRequest.Add("FCErrorEndingAttack".Translate(),
                        "FCErrorEndingAttackDescription".Translate());
                    LogUtil.Error(error.Message);
                });
        }

        public void RemoveDefender(Pawn defender)
        {
            defenders.Remove(defender);
            defenders.RemoveAll(IsPawnTrulyGone);
            if (defenders.Any() || endingBattle || !isUnderAttack) return;

            endingBattle = true;
            LongEventHandler.QueueLongEvent(EndAttack,
                "EndingAttack", false, error =>
                {
                    DelayedErrorWindowRequest.Add("FCErrorEndingAttack".Translate(),
                        "FCErrorEndingAttackDescription".Translate());
                    LogUtil.Error(error.Message);
                });
        }

        public override void PostCaravanFormed(Caravan caravan)
        {
            foreach (var pawn in caravan.pawns)
            {
                var lord = pawn.GetLord();
                if (lord != null)
                    lord.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
                defenders.Remove(pawn);
            }

            if (Map is object)
                foreach (var pawn in caravan.pawns)
                    Map.reservationManager.ReleaseAllClaimedBy(pawn);

            base.PostCaravanFormed(caravan);
        }

        public void SendMilitary(PlanetTile location, MilitaryJobDef job, int timeToFinish, Faction enemy)
        {
            if (IsMilitaryBusy() || IsTargetOccupied(location)) return;

            // Phase 2: jobs with a MilitaryJobHandler (Raid / Capture / Enslave + submod handlers)
            // route through MilitaryOperationManager. The op fires arrival / cooldown FCEvents
            // linked back to itself; FCEventMaker dispatches them to op.OnEventFired which drives
            // the auto-resolve / manual battle / cooldown / resolve chain. FactionFC's
            // ILifecycleParticipantWithOp hook keeps the comp's shadow fields (militaryBusy,
            // militaryJob, militaryLocation, militaryEnemy) in sync for legacy readers (UI gizmos,
            // compat patches, debug actions).
            if (job?.Handler is object)
            {
                MilitaryOperationManager manager = FactionCache.MilitaryManager;
                if (manager is null)
                {
                    LogUtil.Error("SendMilitary: MilitaryManager unavailable; aborting offensive op.");
                    return;
                }
                WorldObject target = ResolveTargetWorldObject(location);
                if (target is null)
                {
                    LogUtil.Warning($"SendMilitary: no world object found at tile {location}; aborting.");
                    return;
                }
                manager.CreateOffensiveOp(WorldSettlement, target, job, enemy, timeToFinish);
                return;
            }

            // Handler-less state jobs (Deploy / DefendFriendlySettlement) used to mark the comp
            // here as "squad committed". After Phase 6 the comp shadow surface is computed from
            // manager state, so these writes are not possible — and not necessary because the
            // canonical squad commitments live on MilitaryOperation:
            //   - Deploy: MilitaryUtil.SpawnSquad creates a Deploy op via Manager.CreateDeployOp.
            //   - DefendFriendlySettlement: the foreign defender's commitment lives on the
            //     defensive op's defender.homeSettlement (set inside Manager.CreateDefensiveOp's
            //     auto-defender selection or via MilitaryUtilFC.ChangeDefendingMilitaryForce).
            // External callers (e.g. Empire-VOE OutpostDefenderGizmo) that still call
            // SendMilitary(DefendFriendlySettlement, ...) directly without going through the
            // manager will get a no-op shadow update; the foreign defender is silently lost
            // from the manager view. New code should call ChangeDefendingMilitaryForce instead.
            if (job.occupiesTarget) FactionCache.FactionComp.AddMilitaryTarget(location);
#pragma warning disable 0618 // legacy lifecycle hook fires for external callers that bypass the manager
            LifecycleRegistry.InvokeOnSquadDeployed(WorldSettlement, job);
#pragma warning restore 0618
        }

        /// <summary>
        /// Look up the WorldObject at <paramref name="tile"/> in priority order: Empire settlement,
        /// any other Settlement (raid target), or any registered <see cref="IRaidTarget"/>'s
        /// world object. Returns null if nothing matches.
        /// </summary>
        private static WorldObject ResolveTargetWorldObject(PlanetTile tile)
        {
            WorldObject target = Find.WorldObjects.WorldObjectAt<WorldSettlementFC>(tile);
            if (target is object) return target;
            target = Find.WorldObjects.SettlementAt(tile);
            if (target is object) return target;
            foreach (IRaidTarget rt in RaidTargetRegistry.Targets)
            {
                if (rt?.WorldObject is object && rt.Tile == tile.tileId) return rt.WorldObject;
            }
            return null;
        }

        public Settlement ReturnMilitaryTarget()
        {
            return !militaryLocation.Valid ? null : Find.WorldObjects.SettlementAt(militaryLocation);
        }

        public void ProcessMilitaryEvent()
        {
            if (militaryJob is null || militaryJob == MilitaryJobDefOf.Undefined || militaryJob == MilitaryJobDefOf.Cooldown)
            {
                LogUtil.Warning($"ProcessMilitaryEvent: {WorldSettlement.Name} has no active operation (job={militaryJob?.defName ?? "null"}). Skipping.");
                return;
            }

            FactionFC faction = FactionCache.FactionComp;
            if (faction.HasMilitaryTarget(militaryLocation))
            {
                faction.RemoveMilitaryTarget(militaryLocation);
            }

            BattleResult result = null;
            MilitaryJobDef resolvedJob = militaryJob;

            if (militaryJob.Handler != null)
            {
                if (militaryJob.Handler.ResolvesManually)
                {
                    militaryJob.Handler.OnManualResolve(this);
                    return; // Handler owns cooldown timing and lifecycle notification
                }
                result = militaryJob.Handler.OnResolved(this);
            }

            bool victory = result != null && result.AttackerVictory;
#pragma warning disable 0618 // legacy ProcessMilitaryEvent path; only fires for pre-refactor save data without linkedOperationId
            LifecycleRegistry.InvokeOnBattleResolved(WorldSettlement, resolvedJob, victory, result);
#pragma warning restore 0618
            CooldownMilitaryFinal();
        }

        public void ReturnMilitary(bool alert)
        {
            // After Phase 6 the comp shadow surface is computed from manager state — there are
            // no fields to clear here. ReturnMilitary now only fires the legacy lifecycle hook
            // (for pre-refactor save data without linkedOperationId) and registers squad
            // injuries. The squad's actual op resolution happens via op.Resolve through the
            // manager's normal flow.
            if (!militaryBusy) return; // No active op — nothing to do

#pragma warning disable 0618 // legacy ReturnMilitary path; only fires for pre-refactor save data
            LifecycleRegistry.InvokeOnSquadRecalled(WorldSettlement);
#pragma warning restore 0618

            if (militarySquad != null)
                FactionCache.FactionComp?.militaryCustomizationUtil?.RegisterSquadInjuries(militarySquad);

            if (alert)
            {
                Find.LetterStack.ReceiveLetter("Military Cooldown", "FCMilitaryCooldown".Translate(WorldSettlement.Name),
                    LetterDefOf.PositiveEvent);
            }
        }

        public void CooldownMilitaryFinal(int battleDeaths = 0)
        {
            FactionFC faction = FactionCache.FactionComp;

            // Prevent duplicate cooldown events for the same settlement
            if (faction.HasEventWithDefAndLocation(FCEventDefOf.cooldownMilitary, WorldSettlement.Tile))
            {
                LogUtil.Warning($"CooldownMilitaryFinal: cooldownMilitary event already exists for {WorldSettlement.Name}. Skipping duplicate.");
                return;
            }

            int cooldown = GenDate.TicksPerDay * 3;
            cooldown += (int)faction.GetStatValue(FCStatDefOf.militaryCooldownOffset);
            if (militaryJob != null && militaryJob.cooldownStatDef != null)
                cooldown += (int)faction.GetStatValue(militaryJob.cooldownStatDef);

            // Dead pawn cooldown: use battleDeaths for defense, squad.dead for deployment
            int deaths = battleDeaths;
            if (deaths == 0 && militaryJob != null && militaryJob.deadPawnCooldown
                && FCSettings.deadPawnsIncreaseMilitaryCooldown)
            {
                deaths = militarySquad != null ? militarySquad.dead : 0;
            }
            if (deaths > 0 && FCSettings.deadPawnsIncreaseMilitaryCooldown)
            {
                int deadMultiplier = 10000 + (int)faction.GetStatValue(FCStatDefOf.deadPawnCooldownOffset);
                cooldown += deaths * deadMultiplier;
            }
            cooldown = Math.Max(cooldown, 0);
            if (DebugSettings.godMode) cooldown = 1;

            // Comp shadow surface is computed from manager state. The cooldown event being
            // scheduled below is the canonical record of the cooldown phase; computed
            // properties (militaryJob / militaryLocation / militaryBusy / militaryEnemy)
            // reflect it indirectly via the op's CooldownPending phase.

            FCEvent tmp = FCEventMaker.MakeEvent(FCEventDefOf.cooldownMilitary);
            tmp.hasCustomDescription = true;
            tmp.timeTillTrigger = Find.TickManager.TicksGame + cooldown;
            tmp.location = WorldSettlement.Tile;
            tmp.customDescription = "FCMilitaryForcesReorganizing".Translate(WorldSettlement.Name); // + 
            FactionCache.FactionComp.AddEvent(tmp);
        }

        public bool IsMilitaryBusy(bool silent = false)
        {
            if (militaryBusy && !silent)
            {
                Messages.Message("FCMilitaryAlreadyAssigned".Translate(), MessageTypeDefOf.RejectInput);
            }

            return militaryBusy;
        }

        public bool IsMilitarySquadValid()
        {
            if (militarySquad != null)
            {
                militarySquad.CheckInitialization();
                if (militarySquad.outfit != null)
                {
                    if (militarySquad.EquippedMercenaries.Any())
                    {
                        return true;
                    }

                    Messages.Message("FCNoSquadEquipped".Translate(),
                        MessageTypeDefOf.RejectInput);
                    return false;
                }

                Messages.Message("FCNoSquadLoadoutAssigned".Translate(),
                    MessageTypeDefOf.RejectInput);
                return false;
            }

            Messages.Message("FCNoSquadAssigned".Translate(), MessageTypeDefOf.RejectInput);
            return false;
        }

        public bool IsMilitarySquadValidSilent()
        {
            return !(militarySquad is null);
        }

        public bool IsMilitaryBusySilent()
        {
            return militaryBusy;
        }

        public bool IsMilitaryValid()
        {
            return settlementMilitaryLevel > 0;
        }

        public bool IsTargetOccupied(PlanetTile location)
        {
            if (FactionCache.FactionComp.HasMilitaryTarget(location))
            {
                Messages.Message("FCTargetAlreadyBeingAttacked".Translate(), MessageTypeDefOf.RejectInput);
                return true;
            }

            return false;
        }
    }
}
