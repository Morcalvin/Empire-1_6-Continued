using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI.Group;

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

        public int artilleryTimer = 0;
        public int settlementMilitaryLevel;

        /// <summary>Legacy 1:1 settlement-to-squad accessor. Squads now live on the faction-wide
        /// pool and reference their billet via <see cref="MercenarySquadFC.settlement"/>.
        /// New code should iterate <see cref="WorldSettlementFC.StationedSquads"/>; this shim
        /// returns the first stationed squad for cross-mod source compatibility.</summary>
        [System.Obsolete("Use WorldSettlementFC.StationedSquads. This shim returns the primary stationed squad for back-compat.")]
        public MercenarySquadFC militarySquad
        {
            get
            {
                List<MercenarySquadFC> stationed = WorldSettlement?.StationedSquads;
                if (stationed is null || stationed.Count == 0) return null;
                return stationed[0];
            }
            set
            {
                // Legacy 1:1 setter: "this is THE squad now". Translate to the canonical
                // squad-first model by detaching any existing stationed squads, then
                // attaching the new one.
                if (WorldSettlement is null) return;
                if (value is null)
                {
                    // Snapshot the list — assigning settlement = null mutates StationedSquads.
                    List<MercenarySquadFC> stationed = new List<MercenarySquadFC>(WorldSettlement.StationedSquads);
                    foreach (MercenarySquadFC s in stationed)
                    {
                        if (s is object) s.settlement = null;
                    }
                    return;
                }
                if (value.settlement == WorldSettlement) return;
                List<MercenarySquadFC> existing = new List<MercenarySquadFC>(WorldSettlement.StationedSquads);
                foreach (MercenarySquadFC s in existing)
                {
                    if (s is object && s != value) s.settlement = null;
                }
                value.settlement = WorldSettlement;
            }
        }

        /// <summary>Legacy per-settlement auto-defend flag. Auto-defend now lives on the squad
        /// (<see cref="MercenarySquadFC.autoDefend"/>) so a settlement with multiple squads can
        /// opt some in and some out. The shim returns true when any stationed squad has
        /// <c>autoDefend</c> set; the setter applies the flag to all stationed squads.</summary>
        [System.Obsolete("Use MercenarySquadFC.autoDefend. This shim aggregates across stationed squads for back-compat.")]
        public bool autoDefend
        {
            get
            {
                List<MercenarySquadFC> stationed = WorldSettlement?.StationedSquads;
                if (stationed is null) return false;
                for (int i = 0; i < stationed.Count; i++)
                {
                    if (stationed[i] != null && stationed[i].autoDefend) return true;
                }
                return false;
            }
            set
            {
                List<MercenarySquadFC> stationed = WorldSettlement?.StationedSquads;
                if (stationed is null) return;
                for (int i = 0; i < stationed.Count; i++)
                {
                    if (stationed[i] != null) stationed[i].autoDefend = value;
                }
            }
        }

        // -*-*-*-*- Squad-first migration buffers -*-*-*-*-
        // Pre-refactor saves wrote militarySquad/autoDefend on the comp itself. After this
        // refactor those fields live on MercenarySquadFC (squad.settlement / squad.autoDefend).
        // On load we capture the legacy values into [Unsaved] buffers; MilitaryMigrationUtil
        // drains them in PostLoadInit. Never written on save.
        [Unsaved] public MercenarySquadFC _legacyMilitarySquad;
        [Unsaved] public bool _legacyAutoDefend;

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
            Scribe_Values.Look(ref artilleryTimer, "artilleryTimer");
            Scribe_Values.Look(ref settlementMilitaryLevel, "settlementMilitaryLevel");

            /* Backward compat: load pre-refactor save state into legacy buffers consumed by
             * MilitaryMigrationUtil during PostLoadInit. The canonical state lives on
             * MilitaryOperationManager (ops) and BattlefieldContext (battle pawns).
             * These are NOT written on save — post-refactor saves use the new layout. */
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Squad-first refactor: capture pre-refactor militarySquad/autoDefend on the comp
                // into [Unsaved] buffers. Drained in PostLoadInit by MigrateLegacyComp_MilitarySquad
                // which writes squad.settlement = this and squad.autoDefend = legacy value.
                Scribe_References.Look(ref _legacyMilitarySquad, "militarySquad");
                Scribe_Values.Look(ref _legacyAutoDefend, "autoDefend", false);

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
            return new Command_Action
            {
                defaultLabel = "FCDefendSettlement".Translate(),
                defaultDesc = "",
                icon = TexLoad.iconCustomize,
                action = delegate { Find.WindowStack.Add(new Dialog_DefendSettlement(evt)); }
            };
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
            MilitaryOperation op = evt.linkedOperation;
            if (op is null)
            {
                LogUtil.Error($"StartDefence: warning event for {WorldSettlement?.Name} has no linked op.");
                FactionCache.FactionComp?.RemoveEvent(evt);
                return;
            }
            BattlefieldContext bf = manager.GetOrCreateBattlefield(WorldSettlement.Tile);
            bf.StartDefense(op, after);
        }


        public void EndBattle(bool won, int remaining, BattleResult battleResult = null)
        {
            var faction = FactionCache.FactionComp;

            // Reset before per-op dispatch so the fallback emitter at the end of this method can
            // detect whether any handler successfully sent a result letter.
            DefensiveBattleEffects.letterEmitted = false;

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

            // Defense-in-depth: the per-op pipeline has multiple silent-skip points (empty
            // opsAtTile, ops in non-Engaged phase, ApplyResult throwing before ReceiveLetter,
            // etc.). End-of-battle must always produce a letter — emit a fallback if no handler
            // managed to send one. Body text deliberately calls out the abnormal path so user
            // bug reports are unambiguous.
            if (!DefensiveBattleEffects.letterEmitted)
            {
                string title = (won ? "FCDefenseSuccessful" : "FCDefenseFailure").Translate();
                string body = (won ? "FCDefenseSuccessfulFallback" : "FCDefenseFailureFallback")
                    .Translate(WorldSettlement?.Name ?? "");
                Find.LetterStack.ReceiveLetter(title, body,
                    won ? LetterDefOf.PositiveEvent : LetterDefOf.Death,
                    new LookTargets(WorldSettlement));
                LogUtil.Warning($"WorldSettlementFC.EndBattle: emitted fallback letter (won={won}) " +
                    $"because no per-op handler sent a result letter at tile {WorldSettlement?.Tile}.");
            }

            _ = remaining; // legacy parameter retained for source compat with callers.
            _ = faction;
        }

        public void ClearAttackState()
        {
            // Foreign-defender squad-injury bookkeeping already ran inside op.CompleteBattle
            // (which registers both aggressor and defender squads). No need to re-fire it here.
            // isUnderAttack is computed from manager state — battle pawn lists and flags live on
            // BattlefieldContext, so clear them through it.
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

        // Battle pawn-list mutations live on BattlefieldContext. These shims exist because lord
        // jobs (LordJob_HuntColonists / LordJob_DefendColony / LordJob_ColonistsIdle) call them
        // by name on the settlement comp; treat them as load-bearing public API.
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

        /// <summary>Squad-first entry point. Routes <paramref name="squad"/> through the manager
        /// to launch a handler-driven offensive op.</summary>
        public void SendMilitary(MercenarySquadFC squad, PlanetTile location, MilitaryJobDef job, int timeToFinish, Faction enemy)
        {
            if (squad is null)
            {
                LogUtil.Warning("SendMilitary: null squad parameter; aborting.");
                return;
            }
            if (IsTargetOccupied(location)) return;

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
                PaymentUtil.CreateDeploymentCostBill(squad);
                manager.CreateOffensiveOp(squad, target, job, enemy, timeToFinish);
                return;
            }
        }

        /// <summary>Pre-refactor entry point. Resolves the settlement's primary stationed squad
        /// (via the obsolete <see cref="militarySquad"/> shim) and forwards. Will be removed in a
        /// follow-up — callers should pick a specific squad via <see cref="WorldSettlementFC.StationedSquads"/>
        /// or the new source-picker dialog.</summary>
        [System.Obsolete("Pass an explicit MercenarySquadFC squad. Resolves to the primary stationed squad as a fallback.")]
        public void SendMilitary(PlanetTile location, MilitaryJobDef job, int timeToFinish, Faction enemy)
        {
            MercenarySquadFC squad = WorldSettlement?.StationedSquads.FirstOrDefault();
            if (squad is null)
            {
                Messages.Message("FCNoSquadAssigned".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            SendMilitary(squad, location, job, timeToFinish, enemy);
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

            // Register injuries across every stationed squad so injured pawns get healed.
            MilitaryCustomizationUtil util = FactionCache.FactionComp?.militaryCustomizationUtil;
            if (util != null)
            {
                List<MercenarySquadFC> stationed = WorldSettlement?.StationedSquads;
                if (stationed != null)
                {
                    for (int i = 0; i < stationed.Count; i++)
                    {
                        if (stationed[i] != null) util.RegisterSquadInjuries(stationed[i]);
                    }
                }
            }

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
            List<MercenarySquadFC> stationed = WorldSettlement?.StationedSquads;
            if (stationed is null || stationed.Count == 0)
            {
                Messages.Message("FCNoSquadAssigned".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            // Valid if any stationed squad has an outfit and equipped mercs ready to deploy.
            bool anyHasOutfit = false;
            for (int i = 0; i < stationed.Count; i++)
            {
                MercenarySquadFC s = stationed[i];
                if (s is null) continue;
                s.CheckInitialization();
                if (s.outfit is null) continue;
                anyHasOutfit = true;
                if (s.EquippedMercenaries.Any()) return true;
            }

            Messages.Message((anyHasOutfit ? "FCNoSquadEquipped" : "FCNoSquadLoadoutAssigned").Translate(),
                MessageTypeDefOf.RejectInput);
            return false;
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
