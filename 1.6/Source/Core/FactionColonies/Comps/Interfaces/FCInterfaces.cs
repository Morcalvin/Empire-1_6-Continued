using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Defines an interface to let classes specify additional tabs to add to the main tab window.
    /// </summary>
    public interface IMainTabWindowOverview
    {
        void PreOpenWindow(FactionFC faction);
        void OnTabSwitch();
        void DrawOverviewTab(Rect boundingBox);
        void PostCloseWindow();
        string TabName();
    }
    /// <summary>
    /// Defines an interface that WorldObjectComps can implement in order to add a new overview tab to the settlement window.
    /// <para>This must be implemented by a WorldObjectComp. It will not be invoked otherwise.</para>
    /// </summary>
    public interface ISettlementWindowOverview
    {
        void PreOpenWindow(WorldSettlementFC settlement);
        void OnTabSwitch();
        void DrawOverviewTab(Rect boundingBox);
        void PostCloseWindow();
        string OverviewTabName();
    }
    /// <summary>
    /// A simple interface that a WorldObjectComp -- attached to a WorldSettlementFC -- can implement to affect non-resource stats.
    /// <para>Results are cached alongside stat modifiers. Caches are automatically invalidated after all lifecycle
    /// events (building, settlement, military, research, tax hooks). Only call
    /// <c>((WorldSettlementFC)parent).InvalidateStatCache()</c> manually if changing values outside a lifecycle callback.</para>
    /// </summary>
    public interface IStatModifierProvider
    {
        double GetStatModifier(FCStatDef stat);
        string GetStatModifierDesc(FCStatDef stat);
    }
    /// <summary>
    /// A WorldObjectComp interface for contributing dynamic, per-resource production bonuses.
    /// Unlike <see cref="IStatModifierProvider"/> (which operates at the stat level), this operates
    /// directly on <see cref="ResourceFC"/> instances, letting comps target specific resources.
    /// <para>Results are queried during production calculation (lazy-cached by ResourceFC's dirty flags).
    /// Caches are automatically invalidated after all lifecycle events (building, settlement, military,
    /// research, tax hooks). Only call <c>((WorldSettlementFC)parent).InvalidateResourceCaches()</c>
    /// manually if changing values outside a lifecycle callback.</para>
    /// </summary>
    public interface IResourceProductionModifier
    {
        /// <summary>
        /// Returns an additive production bonus for the given resource. Return 0 for no effect.
        /// </summary>
        double GetResourceAdditiveModifier(ResourceFC resource);
        /// <summary>
        /// Returns a multiplicative production modifier for the given resource. Return 1 for no effect.
        /// </summary>
        double GetResourceMultiplierModifier(ResourceFC resource);
        /// <summary>
        /// Returns a description of this comp's additive contribution for the additive tooltip.
        /// Return null or empty if not contributing additively to this resource.
        /// </summary>
        string GetResourceAdditiveDesc(ResourceFC resource);
        /// <summary>
        /// Returns a description of this comp's multiplier contribution for the multiplier tooltip.
        /// Return null or empty if not contributing a multiplier to this resource.
        /// </summary>
        string GetResourceMultiplierDesc(ResourceFC resource);
    }
    /// <summary>
    /// A WorldObjectComp interface for injecting additional tithe budget into a resource.
    /// The injected budget raises the tithe income cap (<see cref="ResourceFC.GetTitheIncome"/>).
    /// In <see cref="ResourceFC.actualIncome"/>, only the portion of tithe actually covered by the
    /// injection is offset, so the settlement is not penalised for externally-sourced goods.
    /// <para>Queried during tithe budget calculation via <see cref="ResourceFC.externalTitheBudget"/>.
    /// Caches are automatically invalidated after all lifecycle events. Call
    /// <c>((WorldSettlementFC)parent).InvalidateStatCache()</c> manually if changing values outside
    /// a lifecycle callback.</para>
    /// </summary>
    public interface ITitheBudgetModifier
    {
        /// <summary>
        /// Returns additional tithe budget (in silver value) for the given resource.
        /// Return 0 for no effect.
        /// </summary>
        double GetExternalTitheBudget(ResourceFC resource);

        /// <summary>
        /// Description text for the tithe budget breakdown tooltip. Return null or empty for no entry.
        /// </summary>
        string GetExternalTitheBudgetDesc(ResourceFC resource);
    }
    /// <summary>
    /// A WorldObjectComp interface for performing initialization that depends on fully-rebuilt
    /// settlement state (stat modifiers, buildings, settlement type). Called from
    /// WorldSettlementFC.ExposeData's PostLoadInit block AFTER stat modifiers are rebuilt.
    /// <para>Comps that need to validate against production values or stat caches at load time
    /// should do that work here, not in PostExposeData (which runs before the settlement
    /// finishes its own PostLoadInit).</para>
    /// </summary>
    public interface ISettlementPostLoadInit
    {
        void PostSettlementLoadInit(WorldSettlementFC settlement);
    }
    /// <summary>
    /// Defines an interface to let classes hook into the tax system.
    /// </summary>
    public interface ITaxTickParticipant
    {
        void PreTaxResolution(FactionFC faction);
        void PostTaxResolution(FactionFC faction);
        /// <summary>
        /// Called at the start of tax collection, after pre-tax preparation (cache invalidation, resource pruning) and SettlementTypeExtension.PreTax.
        /// </summary>
        void PreSettlementCreateTax(WorldSettlementFC settlement);
        /// <summary>
        /// Called at the end of tax collection, after all calculations are complete and SettlementTypeExtension.PostTax.
        /// </summary>
        void PostSettlementCreateTax(WorldSettlementFC settlement, ref int silverAmount, List<Thing> titheThings);
    }
    /// <summary>
    /// Unified lifecycle hook for settlement, building, military, and research events.
    /// Register implementations via <see cref="LifecycleRegistry"/>.
    /// Use <see cref="LifecycleParticipantBase"/> to avoid stubbing unused methods.
    /// </summary>
    public interface ILifecycleParticipant
    {
        void OnSettlementCreated(WorldSettlementFC settlement);
        void OnSettlementRemoved(WorldSettlementFC settlement);
        void OnSettlementUpgraded(WorldSettlementFC settlement, int oldLevel, int newLevel);
        void OnSettlementTypeChanged(WorldSettlementFC settlement, WorldSettlementDef oldDef, WorldSettlementDef newDef);
        void OnBuildingConstructed(WorldSettlementFC settlement, BuildingFCDef building, int slot);
        void OnBuildingDeconstructed(WorldSettlementFC settlement, BuildingFCDef building, int slot);
        /// <summary>Called immediately after a <see cref="MilitaryOperation"/> is created and registered.
        /// Fires for every op (offensive, defensive, deploy). For defensive ops the defending squad
        /// is reachable via <c>op.defender.squad</c> when an Empire settlement is the defender.</summary>
        void OnOperationCreated(MilitaryOperation op);
        /// <summary>Called when an op resolves. Any squads referenced by <c>op.aggressor.squad</c>
        /// or <c>op.defender.squad</c> are freed at this point.</summary>
        void OnOperationResolved(MilitaryOperation op);
        /// <summary>Called after the battle simulation / manual battle has produced a result.</summary>
        void OnBattleResolved(MilitaryOperation op, bool victory, BattleResult result);
        void OnResearchCompleted(ResearchProjectDef project);
        /// <summary>
        /// Called when a mercenary is killed, before the default auto-replacement.
        /// Set <see cref="MercenaryDeathEvent.CancelReplacement"/> to prevent auto-replacement.
        /// </summary>
        void OnMercenaryDeath(MercenaryDeathEvent evt);
        /// <summary>
        /// Called immediately after a fresh <see cref="MercenarySquadFC"/> is hired (silver paid,
        /// squad created from a template, added to <c>mercenarySquads</c>) and before the player
        /// has assigned it to a settlement. <c>squad.settlement</c> is null at this point.
        /// </summary>
        void OnSquadHired(MercenarySquadFC squad);
        /// <summary>
        /// Called when a squad is dismissed by the player. The squad has been removed from
        /// <c>mercenarySquads</c>.
        /// </summary>
        void OnSquadDismissed(MercenarySquadFC squad);
        /// <summary>
        /// Called after a squad's loadout is brought up to its source template via
        /// <see cref="MercenarySquadFC.UpgradeToTemplate"/>. Silver has already been paid.
        /// </summary>
        void OnSquadUpgraded(MercenarySquadFC squad);
    }
    /// <summary>
    /// Snapshot of the data a force modifier needs about a pending or simulated battle.
    /// Built either from a real <see cref="MilitaryOperation"/> at engagement time, or
    /// constructed directly by the squad-attack window for the displayed-power estimate.
    /// Both code paths run modifiers through the same registry; modifiers therefore see the
    /// same shape of input regardless of whether the battle is real.
    /// </summary>
    public class BattleForceContext
    {
        /// <summary>The military job (raid, capture, enslave, ...). May be null in synthetic contexts.</summary>
        public MilitaryJobDef kind;
        /// <summary>The world tile the battle resolves on.</summary>
        public PlanetTile targetTile;
        /// <summary>The world object being attacked (typically a Settlement).</summary>
        public WorldObject targetObject;
        /// <summary>Attacker side: faction, force, squad, homeSettlement.</summary>
        public MilitaryOperationParticipant aggressor;
        /// <summary>Defender side: faction, force, squad, homeSettlement.</summary>
        public MilitaryOperationParticipant defender;
    }

    /// <summary>
    /// Cache-time, faction-level modifier. Mutates the cached <see cref="EnemyPower"/> baseline
    /// after <see cref="WorldComponent_EnemyPower"/> derives it from tech level + ETL +
    /// threat adaptation, and BEFORE any settlement entry mirrors it. Use for faction-wide
    /// effects (e.g. a Diplomacy submod that weakens a faction whose leader is sick).
    /// <para>Pure transformation: read <paramref name="faction"/>, mutate <paramref name="power"/>.
    /// No side effects.</para>
    /// </summary>
    public interface IFactionPowerModifier
    {
        void ModifyFactionPower(Faction faction, EnemyPower power);
    }

    /// <summary>
    /// Cache-time, settlement-level modifier. Runs after a settlement entry is mirrored from
    /// its faction's baseline. Use for settlement-attribute-derived effects — e.g. reading
    /// a settlement's <c>CompViralSpread</c> or <c>RimWarSettlementComp</c> to write a
    /// settlement-specific level. Cached, so the squad-attack window's displayed range and
    /// the actual battle agree.
    /// <para>Pure transformation: read <paramref name="settlement"/>, mutate
    /// <paramref name="power"/>. No side effects.</para>
    /// </summary>
    public interface ISettlementPowerModifier
    {
        void ModifySettlementPower(Settlement settlement, EnemyPower power);
    }

    /// <summary>
    /// Attack-time modifier. Mutates a force snapshot at engagement / display. Use for
    /// battle-context effects that depend on the attacker as well as the defender — terrain,
    /// fortification at the battle tile, traveling fatigue, weather, defensive artillery.
    /// Effects that are properties of the settlement alone belong in
    /// <see cref="ISettlementPowerModifier"/> instead so they cache.
    /// </summary>
    public interface IBattleModifier
    {
        /// <summary>
        /// Pure transformation: read <paramref name="ctx"/> (participants, target, kind) and
        /// mutate <paramref name="force"/>'s <see cref="MilitaryForce.militaryLevel"/>,
        /// <see cref="MilitaryForce.militaryEfficiency"/>, or
        /// <see cref="MilitaryForce.forceRemaining"/>. Must NOT touch any state outside
        /// <paramref name="force"/> — the same modifier may be invoked from the squad-picker UI
        /// for an estimate display, where side effects (logging, history, persistence) would
        /// be incorrect. Op lifecycle hooks are the place for those.
        /// </summary>
        void ModifyForce(BattleForceContext ctx, MilitaryForce force, bool isAttacker);
    }
    /// <summary>
    /// Lets submods adjust how long a defensive auto-resolve battle stays in the Engaged
    /// phase before <see cref="MilitaryOperation.CompleteBattle"/> fires. Called once per op
    /// per auto-resolve, after the base formula and clamp. Register implementations via
    /// <see cref="AutoResolveDurationRegistry"/>.
    /// </summary>
    public interface IAutoResolveDurationProvider
    {
        /// <summary>
        /// Adjust <paramref name="durationTicks"/> in place. Use <paramref name="op"/> participants
        /// and <paramref name="result"/> (notably <c>totalRounds</c> and <c>winner</c>) to shape
        /// the engagement window. The orchestrator clamps the final value to at least 1 tick.
        /// </summary>
        void ModifyDuration(MilitaryOperation op, BattleResult result, ref int durationTicks);
    }
    /// <summary>
    /// Allows submods to veto or filter defense assignments. Called when a settlement
    /// is considered as a defender for another settlement (both manual selection and auto-defend).
    /// Register implementations via <see cref="DefenseValidatorRegistry"/>.
    /// </summary>
    public interface IDefenseValidator
    {
        /// <summary>
        /// Returns true if <paramref name="defender"/> is allowed to defend <paramref name="target"/>.
        /// Return false to exclude it from the defender list or auto-defend selection.
        /// </summary>
        bool CanDefend(WorldSettlementFC defender, WorldSettlementFC target);
    }
    /// <summary>
    /// Allows submods to veto squad assignments. Called before a squad is assigned to a
    /// settlement. Receives the actual <see cref="MercenarySquadFC"/> instance so validators
    /// can read per-squad state (current loadout cost, mercenary count, cooldown, etc.) rather
    /// than just the source template. Register implementations via <see cref="SquadAssignmentRegistry"/>.
    /// </summary>
    public interface ISquadAssignmentValidator
    {
        /// <summary>
        /// Returns true if <paramref name="squad"/> can be assigned to <paramref name="settlement"/>.
        /// If false, <paramref name="reason"/> is shown to the player as a rejection message.
        /// </summary>
        bool CanAssign(WorldSettlementFC settlement, MercenarySquadFC squad, out string reason);
    }
    /// <summary>
    /// Lets submods compose adjustments to a squad's projected combat power
    /// (veterancy bonuses, specialist multipliers, augmentations, etc.). Modifiers
    /// chain: each receives the running <see cref="SquadPower"/> (initially the
    /// base computed from loadout cost + settlement efficiency) and returns the
    /// modified value. Higher <see cref="Priority"/> runs first.
    /// <para>Submods that don't want to apply in a given case should return
    /// <paramref name="currentPower"/> unchanged.</para>
    /// Register via <see cref="SquadPowerRegistry"/>.
    /// </summary>
    public interface ISquadPowerModifier
    {
        /// <summary>Higher priorities run first. Modifiers see the power after all
        /// higher-priority modifiers have run.</summary>
        int Priority { get; }
        /// <summary>Returns the squad's adjusted power. Implementations may consult
        /// <c>squad.outfit</c>, mercs, custom data, etc. Throw-safe: exceptions are
        /// logged and the modifier is skipped (running power is preserved).</summary>
        SquadPower ModifyPower(MercenarySquadFC squad, SquadPower currentPower);
    }
    /// <summary>Squad-projected combat power. <see cref="militaryLevel"/> is on the same
    /// 1-9 scale as <see cref="WorldSettlementFC.settlementMilitaryLevel"/>.
    /// <see cref="militaryEfficiency"/> is a multiplier (typical range 0.5-1.5, dampened
    /// by <see cref="FCSettings.efficiencyDamping"/> in <see cref="SimulateBattleFc"/>).</summary>
    public struct SquadPower
    {
        public double militaryLevel;
        public double militaryEfficiency;
        public SquadPower(double level, double efficiency)
        {
            militaryLevel = level;
            militaryEfficiency = efficiency;
        }
    }
    /// <summary>
    /// Allows submods to add custom validation, display additional costs, and perform
    /// side effects (e.g., resource consumption) when settlements are founded.
    /// Register implementations via <see cref="FoundingValidatorRegistry"/>.
    /// </summary>
    public interface ISettlementFoundingValidator
    {
        /// <summary>
        /// Called during settlement creation validation. Return false with a reason
        /// to prevent the player from founding the settlement.
        /// </summary>
        bool CanFoundSettlement(PlanetTile tile, WorldSettlementDef type, out string reason);

        /// <summary>
        /// Returns additional cost text to display in the settlement creation UI,
        /// below the silver cost (e.g., "100 Food, 50 Lumber from [Settlement]").
        /// Return null if no additional costs to display.
        /// </summary>
        string GetAdditionalCostDescription(PlanetTile tile, WorldSettlementDef type);

        /// <summary>
        /// Called in DoFoundSettlement() after silver payment succeeds.
        /// Consume resources or perform other founding side effects here.
        /// <para>Note that at this point, the settlement has not actually been created yet.</para>
        /// </summary>
        void OnSettlementFounded(PlanetTile tile, WorldSettlementDef type);
    }
    /// <summary>
    /// Allows submods to contribute additive or multiplicative modifiers to the Empire Threat Level (ETL).
    /// Register implementations via <see cref="ThreatScalingRegistry"/>.
    /// </summary>
    public interface IThreatScalingContributor
    {
        /// <summary>
        /// Returns an additive contribution to the ETL (added to the raw score before multiplication).
        /// Return 0 for no effect.
        /// </summary>
        double GetAdditiveContribution(FactionFC faction);

        /// <summary>
        /// Returns a multiplicative contribution to the ETL (multiplied into the final result).
        /// Return 1.0 for no effect.
        /// </summary>
        double GetMultiplicativeContribution(FactionFC faction);
    }

    /// <summary>
    /// Allows submods to intercept and redirect tax delivery events at two points:
    /// when the event is queued (to redirect the destination) and when it fires
    /// (to handle delivery of goods to a non-standard location).
    /// Register implementations via <see cref="TaxDeliveryRegistry"/>.
    /// </summary>
    public interface ITaxDeliveryInterceptor
    {
        /// <summary>
        /// Called when a taxColony event is about to be queued via <see cref="FactionFC.AddEvent"/>,
        /// after goods consolidation. Implementations can mutate <c>context.Event.location</c> and
        /// <c>context.Event.timeTillTrigger</c> to redirect the delivery destination.
        /// Set <c>context.Redirected = true</c> to prevent further interceptors from running.
        /// </summary>
        void OnTaxEventCreated(TaxDeliveryContext context);

        /// <summary>
        /// Called when a taxColony event fires and goods are about to be delivered.
        /// Return true to consume the delivery (this interceptor handled the goods).
        /// Return false to let the next interceptor or default delivery logic handle it.
        /// </summary>
        bool TryDeliverGoods(TaxDeliveryContext context);
    }

    /// <summary>
    /// Defines an interface to let classes intercept and modify silver payments before they are processed.
    /// </summary>
    public interface ISilverPaymentModifier
    {
        /// <summary>
        /// Called before silver is consumed. Modify context.Amount to change how much is charged.
        /// Use the context's Reason and Settlement fields to determine what the payment is for.
        /// </summary>
        void ModifyPayment(SilverPaymentContext context);
    }
    /// <summary>
    /// Allows submods to influence which settlement gets attacked by enemy factions.
    /// Each provider returns a weight multiplier per settlement. The final weight for
    /// settlement selection is: base weight * product of all provider weights.
    /// Register implementations via <see cref="RaidWeightRegistry"/>.
    /// </summary>
    public interface IRaidWeightProvider
    {
        /// <summary>
        /// Returns a weight multiplier for <paramref name="settlement"/> when attacked by <paramref name="attackingFaction"/>.
        /// Return 1.0 for no effect. Return &gt; 1.0 to make the settlement more likely to be targeted.
        /// Return &lt; 1.0 (but &gt; 0) to make it less likely. Return 0 to completely exclude it.
        /// </summary>
        float GetSettlementRaidWeight(WorldSettlementFC settlement, RimWorld.Faction attackingFaction);
    }

    /// <summary>
    /// Allows external mods to register world objects as raid targets for Empire's military system.
    /// Registered targets are included in the attack target pool alongside Empire settlements,
    /// receive the same 24-hour warning, and auto-resolve via <see cref="SimulateBattleFc.FightBattle"/>.
    /// Register implementations via <see cref="RaidTargetRegistry"/>.
    /// </summary>
    public interface IRaidTarget
    {
        /// <summary>The world object this target wraps (for serialization and <see cref="LookTargets"/>).</summary>
        WorldObject WorldObject { get; }
        string Name { get; }
        int Tile { get; }
        /// <summary>Virtual military level used for targeting weight and auto-defend comparison.</summary>
        int MilitaryLevel { get; }
        /// <summary>Set by the attack system to prevent duplicate attacks. Cleared on resolution.</summary>
        bool IsUnderAttack { get; set; }
        void OnRaidWon(BattleResult result);
        void OnRaidLost(BattleResult result);
    }
    /// <summary>
    /// Allows external mods to register world objects as auto-defenders for Empire settlements
    /// (and other <see cref="IRaidTarget"/>s). Defenders create a <see cref="MilitaryForce"/> and
    /// are placed on cooldown after battle resolution.
    /// Register implementations via <see cref="AutoDefenderRegistry"/>.
    /// </summary>
    public interface IAutoDefender
    {
        WorldObject WorldObject { get; }
        int MilitaryLevel { get; }
        /// <summary>Maximum tile distance for auto-defense eligibility.</summary>
        int Range { get; }
        /// <summary>True if the defender is available (enabled, not busy, not packing, etc.).</summary>
        bool CanAutoDefend { get; }
        MilitaryForce CreateDefendingForce();
        void OnDefenseStarted(WorldObject target);
        void OnDefenseComplete(bool won, BattleResult result);
        /// <summary>Called when this defender is replaced by another force (not defeated).</summary>
        void OnDefenseReplaced();
        /// <summary>
        /// Returns pawns to fight in a manual battle, or null to generate pawns from force points.
        /// Implementations should remove pawns from their source before returning them.
        /// </summary>
        List<Pawn> GetDefendingPawns();
        /// <summary>
        /// Called after a manual battle ends to return surviving pawns.
        /// Pawns will already be despawned from the battle map.
        /// </summary>
        void ReturnDefendingPawns(List<Pawn> pawns);
    }
    /// <summary>
    /// Allows external mods to display entries in Empire's military tab alongside settlements.
    /// Entries appear as simplified cards with name, military level, status, and an auto-defend toggle.
    /// Register implementations via <see cref="MilitaryTabRegistry"/>.
    /// </summary>
    public interface IMilitaryTabEntry
    {
        WorldObject WorldObject { get; }
        string Name { get; }
        int MilitaryLevel { get; }
        bool AutoDefend { get; set; }
        bool IsUnderAttack { get; }
        bool IsBusy { get; }
        string StatusLabel { get; }
        Color AccentColor { get; }
    }
    /// <summary>
    /// Allows DefModExtensions on <see cref="BuildingFCDef"/> to contribute additional sections
    /// to the building detail panel in FCBuildingWindow. Sections render between the Modifiers
    /// block and the Settlement Impact block.
    /// <para>Implement on a <see cref="DefModExtension"/> attached to a <see cref="BuildingFCDef"/>.
    /// The window discovers implementors via <c>def.modExtensions.OfType&lt;IBuildingDetailSection&gt;()</c>.</para>
    /// </summary>
    public interface IBuildingDetailSection
    {
        /// <summary>Header label for the section (rendered by the caller in standard style).</summary>
        string SectionLabel { get; }

        /// <summary>
        /// Total height needed for section content (excluding the header). Return 0 to hide the section entirely.
        /// </summary>
        float GetSectionHeight(BuildingFCDef def, float width);

        /// <summary>
        /// Draws section content into <paramref name="contentRect"/>. The header is drawn by the caller;
        /// implementors only draw below it.
        /// </summary>
        void DrawSection(BuildingFCDef def, Rect contentRect);

        /// <summary>
        /// Short description appended to building card text in the left panel and tooltips.
        /// Return null or empty to add nothing.
        /// </summary>
        string GetCardDescription(BuildingFCDef def);
    }
    /// <summary>
    /// Allows submods to add buttons to the settlement window's left panel.
    /// Buttons are drawn uniformly as text buttons between the built-in buttons and the Delete button.
    /// Register implementations via <see cref="SettlementButtonRegistry"/>.
    /// </summary>
    public interface ISettlementWindowButton
    {
        /// <summary>
        /// Returns the translated label to display on the button.
        /// Called every frame, so dynamic labels (e.g., with counts) are supported.
        /// </summary>
        string Label(WorldSettlementFC settlement);

        /// <summary>
        /// Called when the button is clicked. Open windows, show float menus, etc.
        /// </summary>
        void OnClick(WorldSettlementFC settlement);

        /// <summary>
        /// Returns true if the button should be enabled (clickable). Disabled buttons
        /// are drawn grayed out. Called every frame.
        /// </summary>
        bool IsEnabled(WorldSettlementFC settlement);

        /// <summary>
        /// Returns true if the button should be visible at all for this settlement.
        /// Return false to hide the button entirely (it takes no space).
        /// Called every frame.
        /// </summary>
        bool IsVisible(WorldSettlementFC settlement);
    }
    /// <summary>
    /// Allows submods to add sections to the squad inspection window (below the per-pawn rows).
    /// Register implementations via <see cref="SquadInspectionRegistry"/>.
    /// </summary>
    public interface ISquadInspectionSection
    {
        /// <summary>Header label for the section.</summary>
        string SectionLabel { get; }

        /// <summary>Total height needed for the section (excluding the header drawn by the caller).
        /// Return 0 to hide the section entirely.</summary>
        float GetSectionHeight(MercenarySquadFC squad, float width);

        /// <summary>Draws the section content into <paramref name="contentRect"/>. The header is
        /// drawn by the caller — only draw below it.</summary>
        void DrawSection(MercenarySquadFC squad, Rect contentRect);

        /// <summary>Lower values render earlier; ties broken by registration order.</summary>
        int Order { get; }
    }
    /// <summary>
    /// A WorldObjectComp interface for contributing additional upkeep or income to a settlement's
    /// cost breakdown.
    /// <para>Queried during profit recomputation (<see cref="WorldSettlementFC.RecomputeProfit"/>).
    /// Caches are automatically invalidated after all lifecycle events. Call
    /// <c>((WorldSettlementFC)parent).DirtyProfitCache()</c> manually if changing values
    /// outside a lifecycle callback.</para>
    /// </summary>
    public interface IProfitContributor
    {
        /// <summary>
        /// Returns the total upkeep cost (in silver) to add to the settlement's upkeep per tax period.
        /// Return 0 for no effect.
        /// </summary>
        double GetUpkeepContribution();

        /// <summary>
        /// Returns a formatted description line for the upkeep tooltip breakdown.
        /// Return null or empty to add no tooltip line.
        /// </summary>
        string GetUpkeepContributionDesc();

        /// <summary>
        /// Returns additional silver income to add to the settlement's income per tax period.
        /// Return 0 for no effect.
        /// </summary>
        double GetIncomeContribution();

        /// <summary>
        /// Returns a formatted description line for the income tooltip breakdown.
        /// Return null or empty to add no tooltip line.
        /// </summary>
        string GetIncomeContributionDesc();
    }
    /// <summary>
    /// Allows submods to influence the auto-tending of off-map mercenary pawns. Each registered
    /// provider can supply a doctor pawn (whose <c>MedicalTendQuality</c> stat is read by vanilla
    /// <c>TendUtility.DoTend</c>) and/or override the medicine ThingDef chosen by the base mod's
    /// tech-level mapping. Register implementations via <see cref="MercAutoTendRegistry"/>.
    /// </summary>
    public interface IMercAutoTendProvider
    {
        /// <summary>
        /// Returns a pawn to act as the tending doctor, or null to defer to other providers /
        /// the base default (no doctor). The doctor does not need to be spawned — vanilla only
        /// reads the doctor's stats. First non-null return wins; later providers do not run.
        /// </summary>
        Pawn ProvideTendingDoctor(Mercenary patient, WorldSettlementFC settlement);

        /// <summary>
        /// Override the medicine ThingDef. Receives the running choice as <paramref name="currentChoice"/>
        /// (initially the base mod's tech-level pick); return that to defer or any other ThingDef
        /// to override. Return null to force no-medicine tending. Chains across all providers.
        /// </summary>
        ThingDef OverrideTendingMedicine(Mercenary patient, WorldSettlementFC settlement, ThingDef currentChoice);
    }
}
