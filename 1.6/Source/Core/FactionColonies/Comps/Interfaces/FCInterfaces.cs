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
        /// Note: fires for every op, including defensive ops where no squad has been deployed.</summary>
        void OnOperationCreated(MilitaryOperation op);
        /// <summary>Called when an op resolves and its squad (if any) is freed.</summary>
        void OnOperationResolved(MilitaryOperation op);
        /// <summary>Called after the battle simulation / manual battle has produced a result.</summary>
        void OnBattleResolved(MilitaryOperation op, bool victory, BattleResult result);
        void OnResearchCompleted(ResearchProjectDef project);
        /// <summary>
        /// Called when a mercenary is killed, before the default auto-replacement.
        /// Set <see cref="MercenaryDeathEvent.CancelReplacement"/> to prevent auto-replacement.
        /// </summary>
        void OnMercenaryDeath(MercenaryDeathEvent evt);
    }
    /// <summary>
    /// Defines an interface to let classes modify military forces before a battle is resolved.
    /// </summary>
    public interface IBattleModifier
    {
        /// <summary>
        /// Called before the battle loop begins. Modify the force's militaryLevel, militaryEfficiency,
        /// or forceRemaining to affect the outcome. Receives the operation context so modifiers can
        /// read participants, target, and phase rather than just the force snapshot.
        /// </summary>
        void ModifyForce(MilitaryOperation op, MilitaryForce force, bool isAttacker);
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
    /// Allows submods to veto squad assignments. Called before a squad loadout is
    /// assigned to a settlement. Register implementations via <see cref="SquadAssignmentRegistry"/>.
    /// </summary>
    public interface ISquadAssignmentValidator
    {
        /// <summary>
        /// Returns true if <paramref name="squad"/> can be assigned to <paramref name="settlement"/>.
        /// If false, <paramref name="reason"/> is shown to the player as a rejection message.
        /// </summary>
        bool CanAssign(WorldSettlementFC settlement, MilSquadFC squad, out string reason);
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
}
