using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI.Group;
using Verse.Sound;

// Comp legacy load buffers reference [Obsolete] DefenseWave (drained on PostLoadInit by
// MilitaryMigrationUtil). The runtime surface is computed properties backed by the manager;
// no other obsolete consumption lives here. File-level pragma scopes the silence to the
// load-buffer block.
#pragma warning disable 0618

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

        /* -*-*-*-*- Legacy load buffers -*-*-*-*-
         * Old saves carried operation state on the comp directly. The canonical state now lives
         * on MilitaryOperation in the manager; the comp's militaryBusy / militaryJob /
         * militaryLocation / militaryEnemy / isUnderAttack are computed properties that read
         * from the manager (defined below). Battle infrastructure (attackers / defenders /
         * draftedNPCs) lives on BattlefieldContext. These _legacy* fields are loaded from old
         * save XML during LoadingVars and consumed once by <see cref="MilitaryMigrationUtil"/>
         * in PostLoadInit. They are NOT written on save.
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

        /* -*-*-*-*- Computed battle state (derived from BattlefieldContext) -*-*-*-*-
         * Read-only proxies onto the per-tile BattlefieldContext owned by the manager.
         * External code that read these fields (UI / VEF compat / WorldSettlementFC / debug)
         * continues to compile and read correctly. Internal mutations live on BattlefieldContext.
         */

        private static readonly List<Pawn> _emptyPawnList = new List<Pawn>();

        private BattlefieldContext Battlefield
            => FactionCache.MilitaryManager?.GetBattlefield(WorldSettlement?.Tile ?? PlanetTile.Invalid);

        public IEnumerable<Pawn> attackers => Battlefield?.attackerPawns ?? Enumerable.Empty<Pawn>();
        public IEnumerable<Pawn> defenders => Battlefield?.defenderPawns ?? Enumerable.Empty<Pawn>();
        public List<Pawn> draftedNPCs => Battlefield?.draftedNPCs ?? _emptyPawnList;

        /* -*-*-*-*- Computed properties (derived from manager state) -*-*-*-*- */

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

        /// <summary>Aggressor's force in the active defensive battle on this tile, or null.</summary>
        public MilitaryForce attackerForce => FindIncomingDefensiveOp()?.aggressor?.force;

        /// <summary>Defender's force in the active defensive battle on this tile, or null.</summary>
        public MilitaryForce defenderForce => FindIncomingDefensiveOp()?.defender?.force;

        /// <summary>Returns the first op where this settlement is "the actor" — aggressor of any
        /// op, or foreign defender of someone else's defensive op. Used by the computed
        /// militaryJob / militaryLocation / militaryEnemy properties.</summary>
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

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref militarySquad, "militarySquad");
            Scribe_Values.Look(ref artilleryTimer, "artilleryTimer");
            Scribe_Values.Look(ref autoDefend, "autoDefend");
            Scribe_Values.Look(ref settlementMilitaryLevel, "settlementMilitaryLevel");

            /* Backward compat: load pre-refactor save state into legacy buffers consumed by
             * MilitaryMigrationUtil during PostLoadInit. The canonical state lives on
             * MilitaryOperationManager (ops) and BattlefieldContext (battle pawns).
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
                    MilitaryOperation op = FactionCache.MilitaryManager?.GetOp(evt.linkedOperationId);
                    if (op?.defender?.force == null || op.defender.force.homeSettlement == null)
                    {
                        LogUtil.Warning($"ChangeDefenderAction: op or defender force missing for event at {evt.location}");
                        ChangeDefendingForceAction(evt);
                        return;
                    }

                    double winChance = SimulateBattleFc.CalculateDefenderWinChance(op.aggressor.force, op.defender.force);
                    var list = new List<FloatMenuOption>()
                    {
                        new FloatMenuOption("FCSettlementDefendingInformation".Translate(op.defender.force.homeSettlement.Name,
                                                                                       op.defender.force.DefensivePower,
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
            MilitaryOperation op = FactionCache.MilitaryManager?.GetOp(evt.linkedOperationId);
            MilitaryForce attackForce = op?.aggressor?.force;
            WorldSettlementFC currentDefender = op?.defender?.homeSettlement;
            if (attackForce is null) return;

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
            WorldObject currentExternalSource = op?.externalDefenderSource;
            foreach (IAutoDefender defender in AutoDefenderRegistry.Defenders)
            {
                if (!defender.CanAutoDefend) continue;
                if (currentExternalSource != null && currentExternalSource == defender.WorldObject) continue;
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

        // CaravanDefend / AddToDefenceFromList are thin wrappers around BattlefieldContext;
        // external callers (VEF Harmony patch, WorldSettlementDefendAction,
        // TransportPodArrivalActionPatch) target them by name on the comp.

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

        /// <summary>Thin delegation to <see cref="BattlefieldContext.StartDefense"/>. Looks up the op
        /// linked to <paramref name="evt"/> and routes the start-defense flow through it.
        /// External callers (manual Defend gizmo, CaravanDefend, debug actions) keep using this
        /// entry point for source compatibility.</summary>
        public void StartDefence(FCEvent evt, Action after)
        {
            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            if (manager is null)
            {
                LogUtil.Error($"StartDefence: no MilitaryManager available for {WorldSettlement?.Name}.");
                FactionCache.FactionComp?.RemoveEvent(evt);
                return;
            }
            MilitaryOperation op = manager.GetOp(evt.linkedOperationId);
            if (op is null)
            {
                LogUtil.Error($"StartDefence: warning event for {WorldSettlement?.Name} has no linked op (id={evt.linkedOperationId}).");
                FactionCache.FactionComp?.RemoveEvent(evt);
                return;
            }
            BattlefieldContext bf = manager.GetOrCreateBattlefield(WorldSettlement.Tile);
            bf.StartDefense(op, after);
        }


        public void EndBattle(bool won, int remaining, BattleResult battleResult = null)
        {
            var faction = FactionCache.FactionComp;

            LogUtil.Message("WorldSettlementFC.EndBattle: Handling combat resolution...");

            // Op completion runs first so manager state catches up before any side effect
            // queries it. Each op fires its own LifecycleRegistry.OnBattleResolved and schedules
            // its own cooldown event linked back to itself.
            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            if (manager is object)
            {
                // Manual-battle path constructs the BattleResult here from on-map pawn counts;
                // the auto-resolve path arrives with battleResult already populated by
                // SimulateBattleFc.FightBattle. Either way, op.CompleteBattle uses
                // result.defenderRemainingForce vs defenderInitialForce to detect overwhelming
                // victory (>= all defenders survived) for the FCOverwhelmingVictory letter +
                // foreign-defender cooldown skip.
                BattleResult resultForOps = battleResult ?? new BattleResult
                {
                    winner = won ? BattleWinner.Defender : BattleWinner.Attacker,
                    defenderInitialForce = Battlefield?.initialDefenderCount ?? remaining,
                    defenderRemainingForce = remaining
                };
                var opsAtTile = manager.GetOpsAt(WorldSettlement.Tile);
                if (opsAtTile.Count == 0)
                {
                    LogUtil.Warning($"WorldSettlementFC.EndBattle: no manager ops at tile {WorldSettlement.Tile}; battle resolution dropped.");
                }
                else
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
            }

            // Settlement-side effects (letters, building destruction, stat changes) now run inside
            // op.CompleteBattle via MilitaryJobHandler_Defend.ApplyResult — once per op. Multi-op
            // battles apply one full penalty set per concurrent attacker, treating each op as a
            // logically distinct attack on the settlement.
            // isUnderAttack is computed from manager state; the op completing already drove it.
            // BattlefieldContext.EndBattle resets battleMapInitialized after this call returns.
            _ = won;       // outcome consumed inside op.CompleteBattle's handler dispatch.
            _ = remaining; // legacy parameter retained for source compat with callers.
            _ = faction;
        }

        public void ClearAttackState()
        {
            // Foreign defender that supplied the defending force still has residual squad-injury
            // bookkeeping on its home comp; let it run that on the foreign side.
            if (defenderForce?.homeSettlement is object
                && defenderForce.homeSettlement != WorldSettlement)
            {
                defenderForce.homeSettlement.MilitaryComp?.ReturnMilitary(false);
            }

            // isUnderAttack is computed from manager state. Battle pawn lists and flags live on
            // BattlefieldContext now — clear them through it.
            BattlefieldContext bf = Battlefield;
            if (bf is object)
            {
                bf.endingBattle = false;
                bf.battleMapInitialized = false;
                bf.shuttleLandingPending = false;
                bf.draftedNPCs?.Clear();
                bf.ClearAllOpPawns();
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

        // WinBattle / LoseBattle moved to MilitaryJobHandler_Defend.ApplyResult →
        // DefensiveBattleEffects.ApplyWin / ApplyLoss. Settlement-side outcome handling now runs
        // inside op.CompleteBattle (per-op) so listeners observe post-effect state and the comp
        // doesn't own this anymore.

        // EndAttack / RemoveAttacker / RemoveDefender live on BattlefieldContext. External lord
        // callers (LordJob_HuntColonists / LordJob_DefendColony / LordJob_ColonistsIdle) and a few
        // internal helpers still reference the comp methods by name; these stay as thin delegations.

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
                // Routes through bf.RemoveDefender so per-op pawn lists stay in sync.
                bf?.RemoveDefender(pawn);
            }

            if (Map is object)
                foreach (var pawn in caravan.pawns)
                    Map.reservationManager.ReleaseAllClaimedBy(pawn);

            base.PostCaravanFormed(caravan);
        }

        public void SendMilitary(PlanetTile location, MilitaryJobDef job, int timeToFinish, Faction enemy)
        {
            if (IsMilitaryBusy() || IsTargetOccupied(location)) return;

            // Jobs with a MilitaryJobHandler (Raid / Capture / Enslave + submod handlers) route
            // through MilitaryOperationManager. The op fires arrival / cooldown FCEvents linked
            // back to itself; FCEventMaker dispatches them to op.OnEventFired which drives the
            // auto-resolve / manual battle / cooldown / resolve chain. The comp's surface fields
            // (militaryBusy / militaryJob / etc.) are computed properties reading from the manager.
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

            // Handler-less state jobs (Deploy / DefendFriendlySettlement) — the canonical squad
            // commitment lives on MilitaryOperation. Deploy ops are created by MilitaryUtil.SpawnSquad
            // via Manager.CreateDeployOp; foreign defender ops via Manager.CreateDefensiveOp
            // auto-defender selection or MilitaryUtilFC.ChangeDefendingMilitaryForce. External
            // callers reaching this branch are no-ops — manager state covers occupancy.
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

        /// <summary>
        /// Registers squad injuries and optionally shows the player a "military cooldown" letter.
        /// Called by debug actions, settlement-removal sweep, and load-time stale-deploy repair.
        /// Most release / cooldown work now lives on <see cref="MilitaryOperation"/> (Resolve
        /// fires lifecycle hooks and unregisters); this method only handles the residual squad-
        /// injury bookkeeping.
        /// </summary>
        public void ReturnMilitary(bool alert)
        {
            if (!militaryBusy) return; // No active op — nothing to do

            if (militarySquad != null)
                FactionCache.FactionComp?.militaryCustomizationUtil?.RegisterSquadInjuries(militarySquad);

            if (alert)
            {
                Find.LetterStack.ReceiveLetter("Military Cooldown", "FCMilitaryCooldown".Translate(WorldSettlement.Name),
                    LetterDefOf.PositiveEvent);
            }
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
            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            IReadOnlyList<MilitaryOperation> opsAtTile = manager?.GetOpsAt(location);
            if (opsAtTile is object && opsAtTile.Count > 0)
            {
                Messages.Message("FCTargetAlreadyBeingAttacked".Translate(), MessageTypeDefOf.RejectInput);
                return true;
            }

            return false;
        }
    }
}
