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

        public MercenarySquadFC militarySquad;
        public int artilleryTimer = 0;
        public bool autoDefend = false;
        public int settlementMilitaryLevel;

        /* -*-*-*-*- Legacy load buffers (Phase 6+7) -*-*-*-*-
         * Old saves (pre-Phase-2) carried operation state on the comp directly. After the
         * gut, the canonical state lives on MilitaryOperation in the manager; the readable
         * comp surface (militaryBusy / militaryJob / militaryLocation / militaryEnemy /
         * isUnderAttack) is computed from manager queries below. Battle infrastructure
         * (attackers / defenders / draftedNPCs / activeWaves) lives on BattlefieldContext;
         * the comp's read-only computed properties below proxy through to it. These _legacy*
         * fields are loaded from old save XML during LoadingVars and consumed by
         * <see cref="MilitaryMigrationUtil"/> in PostLoadInit. They are NOT written on save.
         */
        public bool _legacyMilitaryBusy;
        public MilitaryJobDef _legacyMilitaryJob;
        public PlanetTile _legacyMilitaryLocation = PlanetTile.Invalid;
        public Faction _legacyMilitaryEnemy;
        public bool _legacyIsUnderAttack;
        public List<Pawn> _legacyAttackers;
        public List<Pawn> _legacyDefenders;
        public List<Pawn> _legacyDraftedNPCs;
        public List<DefenseWave> _legacyActiveWaves;
        public bool _legacyBattleMapInitialized;
        public int _legacyInitialDefenderCount;

        /* -*-*-*-*- Computed battle state (Phase 7: derived from BattlefieldContext) -*-*-*-*-
         * Read-only proxies onto the per-tile BattlefieldContext owned by the manager.
         * External code that read these fields (UI / VEF compat / WorldSettlementFC / debug)
         * continues to compile and read correctly. Internal mutations migrated to
         * BattlefieldContext.
         */

        private static readonly List<Pawn> _emptyPawnList = new List<Pawn>();
        private static readonly List<DefenseWave> _emptyWaveList = new List<DefenseWave>();

        private BattlefieldContext Battlefield
            => FactionCache.MilitaryManager?.GetBattlefield(WorldSettlement?.Tile ?? PlanetTile.Invalid);

        public List<Pawn> attackers => Battlefield?.attackerPawns ?? _emptyPawnList;
        public List<Pawn> defenders => Battlefield?.defenderPawns ?? _emptyPawnList;
        public List<Pawn> draftedNPCs => Battlefield?.draftedNPCs ?? _emptyPawnList;
        public List<DefenseWave> activeWaves => Battlefield?.activeWaves ?? _emptyWaveList;

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

        // Phase 7: endingBattle / battleMapInitialized / shuttleLandingPending /
        // initialDefenderCount / pendingDeliveryMessage moved to BattlefieldContext.
        // IsPawnTrulyGone / HasPendingPodAttackers are static helpers on BattlefieldContext.

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref militarySquad, "militarySquad");
            Scribe_Values.Look(ref artilleryTimer, "artilleryTimer");
            Scribe_Values.Look(ref autoDefend, "autoDefend");
            Scribe_Values.Look(ref settlementMilitaryLevel, "settlementMilitaryLevel");

            /* Backward compat: load pre-refactor save state into legacy buffers consumed by
             * MilitaryMigrationUtil during PostLoadInit. The canonical state lives on
             * MilitaryOperationManager (ops) and BattlefieldContext (battle pawns/waves).
             * These are NOT written on save — post-refactor saves use the new layout. */
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Values.Look(ref _legacyMilitaryBusy, "militaryBusy", false);
                Scribe_Defs.Look(ref _legacyMilitaryJob, "militaryJob");
                Scribe_Values.Look(ref _legacyMilitaryLocation, "militaryLocation", PlanetTile.Invalid);
                Scribe_References.Look(ref _legacyMilitaryEnemy, "militaryEnemy");
                Scribe_Values.Look(ref _legacyIsUnderAttack, "isUnderAttack", false);
                Scribe_Values.Look(ref _legacyBattleMapInitialized, "battleMapInitialized", false);
                Scribe_Values.Look(ref _legacyInitialDefenderCount, "initialDefenderCount", 0);

                Scribe_Collections.Look(ref _legacyAttackers, "attackers", LookMode.Reference);
                Scribe_Collections.Look(ref _legacyDefenders, "defenders", LookMode.Reference);
                Scribe_Collections.Look(ref _legacyDraftedNPCs, "draftedNPCs", LookMode.Reference);
                Scribe_Collections.Look(ref _legacyActiveWaves, "activeWaves", LookMode.Deep);

                MilitaryForce legacyAttackerForce = null;
                MilitaryForce legacyDefenderForce = null;
                Scribe_Deep.Look(ref legacyAttackerForce, "attackerForce");
                Scribe_Deep.Look(ref legacyDefenderForce, "defenderForce");

                if (_legacyActiveWaves is null) _legacyActiveWaves = new List<DefenseWave>();
                if (_legacyActiveWaves.Count == 0 && (legacyAttackerForce is object || legacyDefenderForce is object))
                {
                    _legacyActiveWaves.Add(new DefenseWave
                    {
                        attackerForce = legacyAttackerForce,
                        defenderForce = legacyDefenderForce,
                        attackerFaction = legacyAttackerForce?.homeFaction
                    });
                }
            }
        }

        public override void Initialize(WorldObjectCompProperties props_l)
        {
            base.Initialize(props_l);
        }

        public override void CompTick()
        {
            base.CompTick();
            Battlefield?.Tick();
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
            if (FCSettings.battleMode == BattleMode.Hybrid && !PlayerCaravanOnSettlementTile())
            {
                return new AcceptanceReport("FCHybridBattleEnabledNoManualFight".Translate());
            }
            return AcceptanceReport.WasAccepted;
        }

        // Phase 7: IsPlayerCaravanOnTile / CountOtherSettlementBattleMaps / ShuttleCaravanDefend /
        // SpawnPawnsAtEdge / RegisterPawnsAsDefenders migrated to BattlefieldContext.
        // CaravanDefend / AddToDefenceFromList are kept as thin wrappers because external
        // callers (VEF Harmony patch, WorldSettlementDefendAction, TransportPodArrivalActionPatch)
        // target them by name on the comp.

        private bool PlayerCaravanOnSettlementTile()
        {
            return Find.WorldObjects.Caravans.Any(c =>
                c.Tile == WorldSettlement.Tile && c.Faction == Faction.OfPlayer);
        }

        public void CaravanDefend(Caravan caravan)
        {
            BattlefieldContext bf = FactionCache.MilitaryManager?.GetOrCreateBattlefield(WorldSettlement.Tile);
            if (bf is null)
            {
                LogUtil.Error($"CaravanDefend: no battlefield for {WorldSettlement?.Name}.");
                return;
            }
            bf.CaravanDefend(caravan);
        }

        public void AddToDefenceFromList(List<Pawn> pawns, int destinationTile)
        {
            AddToDefenceFromList(pawns, destinationTile, assignToLord: true);
        }

        public void AddToDefenceFromList(List<Pawn> pawns, int destinationTile, bool assignToLord)
        {
            BattlefieldContext bf = FactionCache.MilitaryManager?.GetOrCreateBattlefield(new PlanetTile(destinationTile));
            if (bf is null)
            {
                LogUtil.Error($"AddToDefenceFromList: no battlefield for tile {destinationTile}.");
                return;
            }
            bf.AddToDefenceFromList(pawns, destinationTile, assignToLord);
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            if (isUnderAttack)
                foreach (var option in WorldSettlementDefendAction.GetFloatMenuOptions(caravan, WorldSettlement))
                    yield return option;
        }

        // Phase 7: DeleteMap migrated to BattlefieldContext.

        /// <summary>Phase 7: thin delegation to <see cref="BattlefieldContext.StartDefenseFromEvent"/>.
        /// All map generation / wave spawning / auto-resolve branches live on the battlefield now.
        /// External callers (manual Defend gizmo, CaravanDefend, debug actions) keep using this entry
        /// point for source compatibility.</summary>
        public void StartDefence(FCEvent evt, Action after)
        {
            BattlefieldContext bf = FactionCache.MilitaryManager?.GetOrCreateBattlefield(WorldSettlement.Tile);
            if (bf is null)
            {
                LogUtil.Error($"StartDefence: no battlefield available for {WorldSettlement?.Name}.");
                FactionCache.FactionComp?.RemoveEvent(evt);
                return;
            }
            bf.StartDefenseFromEvent(evt, after);
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
            // BattlefieldContext.EndBattle resets battleMapInitialized after this call returns.
        }

        public void ClearAttackState()
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

            // isUnderAttack is computed from manager state. Battle pawn lists / wave state /
            // flags live on BattlefieldContext now — clear them through it.
            BattlefieldContext bf = Battlefield;
            if (bf is object)
            {
                bf.endingBattle = false;
                bf.battleMapInitialized = false;
                bf.shuttleLandingPending = false;
                bf.attackerPawns?.Clear();
                bf.defenderPawns?.Clear();
                bf.draftedNPCs?.Clear();
                bf.activeWaves?.Clear();
            }
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
            int initialCount = Battlefield?.initialDefenderCount ?? 0;
            int battleDeaths = Math.Max(0, initialCount - remaining);
            bool overwhelming = won && remaining >= initialCount;

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

            string deliveryMsg = Battlefield?.pendingDeliveryMessage;
            if (!string.IsNullOrEmpty(deliveryMsg))
            {
                str += "\n\n" + deliveryMsg;
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
            string deliveryMsg = Battlefield?.pendingDeliveryMessage;
            if (!string.IsNullOrEmpty(deliveryMsg))
            {
                text += "\n\n" + deliveryMsg;
            }
            if (Map != null)
            {
                text += "\n\n" + "FCDefenseBattleOverLeaveMap".Translate();
            }
            Find.LetterStack.ReceiveLetter("FCDefenseSuccessful".Translate(),
                text,
                LetterDefOf.PositiveEvent, new LookTargets(WorldSettlement));
        }

        // Phase 7: EndAttack / RemoveAttacker / RemoveDefender migrated to BattlefieldContext.
        // External lord callers (LordJob_HuntColonists / LordJob_DefendColony / LordJob_ColonistsIdle)
        // and a few internal helpers still reference the comp methods by name; these stay as
        // thin delegations.

        public void EndAttack() => Battlefield?.EndAttack();

        public void RemoveAttacker(Pawn downed) => Battlefield?.RemoveAttacker(downed);

        public void RemoveDefender(Pawn defender) => Battlefield?.RemoveDefender(defender);

        public override void PostCaravanFormed(Caravan caravan)
        {
            BattlefieldContext bf = Battlefield;
            foreach (var pawn in caravan.pawns)
            {
                var lord = pawn.GetLord();
                if (lord != null)
                    lord.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
                bf?.defenderPawns?.Remove(pawn);
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
