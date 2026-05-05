using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Replaces the per-job float menu of "settlements with usable military" with a richer picker:
    /// every squad in the faction listed as a card with its billet, power, efficiency, travel time,
    /// predicted win chance, and status. The raid type itself is selectable inside the dialog (no
    /// second-level FloatMenu in the gizmo flow). Confirm dispatches the chosen squad through
    /// <see cref="MilitaryOperationManager.CreateOffensiveOp"/>.
    /// <para>The dialog is also reused for "pick a squad" flows that don't need raid-type switching
    /// (caravan defense routing, etc.) — instantiate via the override constructor with a single job,
    /// a custom <c>onConfirm</c> callback, and a <c>headerOverride</c>.</para>
    /// </summary>
    public class Dialog_AttackSettlement : Window
    {
        public override Vector2 InitialSize => new Vector2(820f, 600f);

        private readonly WorldObject target;
        private readonly Faction enemy;
        private readonly List<MilitaryJobDef> validJobs; // null in override mode
        private readonly Action<MercenarySquadFC> onConfirm;
        private readonly string headerOverride;

        /* Drives the picker: dropdown selection in default mode, pinned job in override mode.
           Defender bounds and per-row attacker modifiers all depend on this through
           BattleForceContext.kind, so any change must rebuild both. */
        private MilitaryJobDef currentJob;

        private MercenarySquadFC selected;
        private Vector2 scrollPos;
        private bool availableOnly = true;
        private SortMode sort = SortMode.WinChance;

        /* Defender power: the registry entry plus pre-built min/max bounds with
         * BattleModifierRegistry already applied. Computed at ctor and rebuilt whenever the
         * dropdown picks a new job — stable across redraws within a single job selection.
         * The bounds frame the actual battle force the player will face: the engagement-time
         * roll in MilitaryOperation.BeginEngagement lands somewhere in
         * [defenderForceMin, defenderForceMax]. */
        private EnemyPower defenderPower;
        private MilitaryForce defenderForceMin;
        private MilitaryForce defenderForceMax;

        private List<RowData> rows = new List<RowData>();
        private bool rowsDirty = true;

        private const float margin = 5f;
        private const float smallMargin = 3f;

        /* Card layout constants — mirror HireSquadsWindow so the two squad-listing surfaces share rhythm. */
        private const float Pad         = 4f;
        private const float RowGap      = 2f;
        private const float CardHeaderH = 24f;
        private const float CardDetailH = 22f;
        private const float CardH       = CardHeaderH + CardDetailH;
        private const float AccentW     = 4f;

        /* Header layout constants */
        private const float TitleH       = 32f;
        private const float HeaderColGap = 12f;
        private const float SubHeaderH   = 28f;

        private enum SortMode
        {
            WinChance,
            Travel,
            Power,
            Name
        }

        private struct RowData
        {
            public MercenarySquadFC squad;
            public int travelTicks;
            public double winChanceMin;
            public double winChanceMax;
            public double attackerPower;
            public double attackerEfficiency;
            public bool hasAttackerForce;
            public string status;
            public Color statusColor;
            public bool available;
        }

        /// <summary>Default mode: the dialog presents a switcher across the supplied job list.</summary>
        public Dialog_AttackSettlement(WorldObject target, Faction enemy, List<MilitaryJobDef> jobs)
            : this(target, enemy, jobs, jobs?.FirstOrDefault()) { }

        /// <summary>Default mode with an explicit initial job (must be present in <paramref name="jobs"/>;
        /// falls back to the first entry otherwise).</summary>
        public Dialog_AttackSettlement(WorldObject target, Faction enemy, List<MilitaryJobDef> jobs, MilitaryJobDef initialJob)
        {
            this.target = target;
            this.enemy = enemy;
            this.validJobs = jobs;
            this.onConfirm = null;
            this.headerOverride = null;
            this.currentJob = (initialJob is object && jobs is object && jobs.Contains(initialJob))
                ? initialJob
                : jobs?.FirstOrDefault();

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            draggable = true;

            BuildDefenderRange();
        }

        /// <summary>Override mode (caravan defense and similar): caller pins a single
        /// <paramref name="job"/>, supplies their own <paramref name="onConfirm"/>, and an explicit
        /// header title. The raid-type switcher is suppressed.</summary>
        public Dialog_AttackSettlement(WorldObject target, MilitaryJobDef job, Faction enemy,
            Action<MercenarySquadFC> onConfirm, string headerOverride)
        {
            this.target = target;
            this.enemy = enemy;
            this.validJobs = null;
            this.onConfirm = onConfirm;
            this.headerOverride = headerOverride;
            this.currentJob = job;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;

            BuildDefenderRange();
        }

        /* Resolves the cached defender entry, builds min/max MilitaryForce instances at the
         * variance bounds, and lets the worldcomp run battle modifiers against each so the
         * displayed range matches what the engagement-time roll will hit (modulo same-tick
         * cache state). Probe context's aggressor is left null because settlement-/faction-
         * level modifiers run at cache time; battle modifiers that read aggressor are rare
         * and would need a per-row recompute path. */
        private void BuildDefenderRange()
        {
            defenderPower = null;
            defenderForceMin = null;
            defenderForceMax = null;

            Settlement targetSettlement = target as Settlement;
            if (targetSettlement is null || enemy is null) return;

            WorldComponent_EnemyPower registry = FactionCache.EnemyPower;
            if (registry is null) return;

            defenderPower = registry.GetOrCompute(targetSettlement);

            BattleForceContext probeCtx = new BattleForceContext
            {
                kind = currentJob,
                targetTile = targetSettlement.Tile,
                targetObject = targetSettlement,
                aggressor = null
            };
            var bounds = registry.ResolveDefenderBounds(probeCtx);
            defenderForceMin = bounds.min;
            defenderForceMax = bounds.max;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (rowsDirty) RebuildRows();

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Color colorBefore = GUI.color;

            const float SquadHeaderH = 22f;

            float headerBottom = DrawHeader(inRect);

            // "Select Squad" sub-header
            float subHeaderY = headerBottom + 4f;
            TexLoad.DrawHorizontalPeakGradientLine(0, subHeaderY, inRect.width, Color.gray);
            subHeaderY += 4f;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect squadHeader = new Rect(0, subHeaderY, inRect.width, SquadHeaderH);
            Widgets.DrawHighlight(squadHeader);
            Widgets.Label(squadHeader, "FCSquadPickerSelectSquad".Translate());

            // Filter / sort row
            float toolbarY = subHeaderY + SquadHeaderH + 2f;
            float toolbarHeight = 24f;
            float sortButtonW = 200f;
            float checkboxW = 160f;
            Rect sortButton = new Rect(inRect.xMax - sortButtonW - (margin * 2), toolbarY, sortButtonW, toolbarHeight);
            Rect checkbox = new Rect(sortButton.x - checkboxW - margin, toolbarY, checkboxW, toolbarHeight);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.CheckboxLabeled(checkbox, "FCSquadPickerAvailableOnly".Translate(), ref availableOnly);
            if (Widgets.ButtonText(sortButton, "FCSquadPickerSort".Translate(SortLabel(sort))))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(SortLabel(SortMode.WinChance), () => { sort = SortMode.WinChance; rowsDirty = true; }),
                    new FloatMenuOption(SortLabel(SortMode.Travel),    () => { sort = SortMode.Travel;    rowsDirty = true; }),
                    new FloatMenuOption(SortLabel(SortMode.Power),     () => { sort = SortMode.Power;     rowsDirty = true; }),
                    new FloatMenuOption(SortLabel(SortMode.Name),      () => { sort = SortMode.Name;      rowsDirty = true; })
                };
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            // Card list (framed)
            float listTop = toolbarY + 32f;
            float buttonsHeight = 36f;
            float listHeight = inRect.height - listTop - buttonsHeight - 6f;
            Rect listRect = new Rect(0, listTop, inRect.width, listHeight);
            Widgets.DrawMenuSection(listRect);
            DrawCardList(listRect);

            // Buttons
            float btnY = inRect.height - buttonsHeight + 2f;
            if (Widgets.ButtonText(new Rect(inRect.width - 320f, btnY, 150f, 32f), "Cancel".Translate()))
            {
                Close();
            }
            bool canConfirm = selected is object && selected.IsAvailable && currentJob is object;
            if (!canConfirm) GUI.color = Color.gray;
            if (Widgets.ButtonText(new Rect(inRect.width - 160f, btnY, 150f, 32f), "Confirm".Translate(), true, true, canConfirm))
            {
                Confirm();
            }
            GUI.color = colorBefore;

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        /* Header: full-width title banner, then a two-column body — left = target row + defender
           power line; right = "Operation:" dropdown + description + rewards. In override mode the
           dialog has a pinned job, so the right column is suppressed and the body reverts to the
           single-column layout from before this change. Returns the y just below the body. */
        private float DrawHeader(Rect inRect)
        {
            // Title banner (full width)
            Rect titleRect = new Rect(0, 0, inRect.width, TitleH);
            Widgets.DrawHighlight(titleRect);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            string titleText = headerOverride.NullOrEmpty()
                ? (string)"FCSquadPickerTitle".Translate()
                : headerOverride;
            Widgets.Label(new Rect(8f, 0, inRect.width - 16f, TitleH), titleText);

            // Divider under title
            UIUtil.DrawColoredHorizontalLine(0, TitleH, inRect.width, Color.gray);

            float bodyTop = TitleH + 6f;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            bool twoColumn = headerOverride.NullOrEmpty() && validJobs is object && validJobs.Count > 0;

            if (!twoColumn)
            {
                // Single-column override mode: defender power line only, target row suppressed
                // (the override title already names the engagement context).
                float y = bodyTop;
                if (defenderForceMin is object && defenderForceMax is object)
                {
                    float defRowH = 22f;
                    Widgets.Label(new Rect(8f, y, inRect.width - 16f, defRowH), DefenderLineText());
                    y += defRowH;
                }
                return y;
            }

            // Two-column body
            float colW = (inRect.width - HeaderColGap) * 0.5f;
            Rect leftCol  = new Rect(0,                   bodyTop, colW, 0);
            Rect rightCol = new Rect(colW + HeaderColGap, bodyTop, colW, 0);

            float leftBottom  = DrawHeaderLeftColumn(leftCol);
            float rightBottom = DrawHeaderRightColumn(rightCol);

            // Vertical divider between columns, sized to the taller column
            float bodyBottom = Math.Max(leftBottom, rightBottom);
            Widgets.DrawBoxSolid(
                new Rect(colW + HeaderColGap * 0.5f - 0.5f, bodyTop, 1f, bodyBottom - bodyTop),
                new Color(0.5f, 0.5f, 0.5f));

            return bodyBottom;
        }

        /* Left column: a centered, highlighted target box (Target / faction icon + name /
           settlement name) followed by the defender power line. */
        private float DrawHeaderLeftColumn(Rect col)
        {
            float y = col.y;

            const float TargetFactionRowH = 24f;
            const float TargetSettlementH = 28f;
            const float TargetIconSize = 22f;
            const float TargetIconGap = 6f;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            // Target header row
            Rect targetHeader = new Rect(col.x + margin, y, col.width - (margin * 2), SubHeaderH);
            Rect targetHeaderLabel = new Rect(targetHeader.x + smallMargin, targetHeader.y, targetHeader.width - (smallMargin * 2), targetHeader.height);
            Widgets.Label(targetHeaderLabel, "FCSquadPickerTarget".Translate());
            TexLoad.DrawHorizontalPeakGradientLine(targetHeader.x, targetHeader.yMax, targetHeader.width, Color.gray);
            y += targetHeader.height + margin;

            Color relationsColor = enemy?.PlayerRelationKind.GetColor() ?? Color.white;

            // faction icon (faction color) + faction name (relations color), centered as a unit
            Text.Anchor = TextAnchor.MiddleCenter;
            string factionName = enemy?.Name ?? "?";
            Vector2 nameSize = Text.CalcSize(factionName);
            float groupW = (enemy?.def?.FactionIcon != null ? TargetIconSize + TargetIconGap : 0f) + nameSize.x;
            float groupX = col.x + (col.width - groupW) * 0.5f;

            if (enemy?.def?.FactionIcon != null)
            {
                Color colorBefore = GUI.color;
                GUI.color = enemy.Color;
                GUI.DrawTexture(new Rect(groupX, y + (TargetFactionRowH - TargetIconSize) / 2f,
                    TargetIconSize, TargetIconSize), enemy.def.FactionIcon);
                GUI.color = colorBefore;
                groupX += TargetIconSize + TargetIconGap;
            }

            UIUtil.DrawColoredLabel(new Rect(groupX, y, nameSize.x + 4f, TargetFactionRowH), factionName, relationsColor);
            y += TargetFactionRowH;

            // settlement name, Medium font, centered, relations color
            Text.Font = GameFont.Medium;
            UIUtil.DrawColoredLabel(new Rect(col.x, y, col.width, TargetSettlementH), target?.LabelCap ?? "?", relationsColor);

            Text.Font = GameFont.Small;
            y += TargetSettlementH;

            // Defender power line — sits below the target box
            if (defenderForceMin is object && defenderForceMax is object)
            {
                float defRowH = 22f;
                y += 4f;
                Widgets.Label(new Rect(col.x + 8f, y, col.width - 16f, defRowH), DefenderLineText());
                y += defRowH;
            }

            return y;
        }

        /* Right column: "Operation:" label + dropdown button, then the wrapped description and
           rewards lines. Description comes from Verse Def.description (auto-translated via
           DefInjections); rewards comes from the optional rewardsDescKey. */
        private float DrawHeaderRightColumn(Rect col)
        {
            float y = col.y;
            float innerX = col.x + 8f;
            float innerW = col.width - 16f;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            // Operation header row
            Rect opHeader = new Rect(col.x + margin, y, col.width - (margin * 2), SubHeaderH);
            Rect opHeaderLabel = new Rect(opHeader.x + smallMargin, opHeader.y, opHeader.width - (smallMargin * 2), opHeader.height);
            Widgets.Label(opHeaderLabel, "FCSquadPickerOperation".Translate());
            TexLoad.DrawHorizontalPeakGradientLine(opHeader.x, opHeader.yMax, opHeader.width, Color.gray);
            y += opHeader.height + margin;

            // Operation dropdown row
            float buttonMargin = margin * 3;
            Rect opButton = new Rect(col.x + buttonMargin, y, col.width - (buttonMargin * 2), SubHeaderH - 4f);
            string btnLabel = currentJob is null ? "?" : currentJob.LabelCap.ToString();
            if (Widgets.ButtonText(opButton, btnLabel))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                foreach (MilitaryJobDef job in validJobs)
                {
                    MilitaryJobDef captured = job;
                    opts.Add(new FloatMenuOption(captured.LabelCap, () => SetCurrentJob(captured)));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            y += opButton.height + margin;

            // Description
            Text.Anchor = TextAnchor.UpperLeft;
            string description = currentJob?.description;
            if (!description.NullOrEmpty())
            {
                float h = Text.CalcHeight(description, innerW);
                Widgets.Label(new Rect(innerX, y, innerW, h), description);
                y += h + 4f;
            }

            // Rewards line
            string rewards = currentJob?.rewardsDesc;
            if (!rewards.NullOrEmpty())
            {
                string rewardsLine = (string)"FCSquadPickerRewards".Translate() + ": " + rewards;
                float h = Text.CalcHeight(rewardsLine, innerW);
                Widgets.Label(new Rect(innerX, y, innerW, h), rewardsLine);
                y += h;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            return y;
        }

        /* Switching jobs invalidates defender bounds (BattleForceContext.kind drives them) and
           per-row attacker modifiers (same), so we rebuild both. */
        private void SetCurrentJob(MilitaryJobDef job)
        {
            if (job == currentJob) return;
            currentJob = job;
            BuildDefenderRange();
            rowsDirty = true;
        }

        /* Builds the defender-power summary line, collapsing min/max ranges to a single value
           when min == max (e.g. when defender variance is zero). */
        private string DefenderLineText()
        {
            string forceText = TextUtil.FormatRange(defenderForceMin.forceRemaining, defenderForceMax.forceRemaining, "0");
            string effText = TextUtil.FormatRange(defenderForceMin.militaryEfficiency, defenderForceMax.militaryEfficiency, "0.##");
            return "FCSquadPickerEstimatedDefender".Translate(forceText, effText).ToString();
        }

        private void DrawCardList(Rect listRect)
        {
            if (rows.Count == 0)
            {
                Color colorBefore = GUI.color;
                TextAnchor anchorBefore = Text.Anchor;
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(listRect.x, listRect.y + listRect.height * 0.35f,
                    listRect.width, 40f), "FCHireSquadsEmpty".Translate());
                GUI.color = colorBefore;
                Text.Anchor = anchorBefore;
                return;
            }

            float innerX = listRect.x + Pad;
            float innerW = listRect.width - Pad * 2f;
            Rect viewRect = new Rect(innerX, listRect.y + Pad, innerW, listRect.height - Pad * 2f);
            float totalH = rows.Count * (CardH + RowGap);
            Rect scrollRect = ScrollUtil.BeginScrollView(viewRect, ref scrollPos, totalH);

            int now = Find.TickManager.TicksGame;
            float runningY = 0f;
            bool alternate = false;
            for (int i = 0; i < rows.Count; i++)
            {
                Rect cardRect = new Rect(0f, runningY, scrollRect.width, CardH);
                if (alternate) Widgets.DrawAltRect(cardRect);
                DrawSquadCard(cardRect, rows[i], now);
                runningY += CardH + RowGap;
                alternate = !alternate;
            }
            ScrollUtil.EndScrollView();
        }

        /* Per-squad card. Header row: accent strip, squad name, right-aligned status badge.
           Detail row: Settlement / Power / Eff / Travel / Win-chance cells. The whole card is the
           click target for selection — selected card uses the brighter selected highlight, hovered
           non-selected card uses the standard hover highlight. */
        private void DrawSquadCard(Rect cardRect, RowData row, int now)
        {
            MercenarySquadFC squad = row.squad;

            // Hover / selected highlight (whole card)
            bool isSelected = selected == squad;
            if (isSelected) Widgets.DrawHighlightSelected(cardRect);
            else if (Mouse.IsOver(cardRect)) Widgets.DrawHighlight(cardRect);

            // Accent strip
            Color accent = squad.settlement?.MilitaryComp != null
                ? AccentUtil.GetMilitaryAccent(squad.settlement.MilitaryComp)
                : AccentUtil.MilInactive;
            Widgets.DrawBoxSolid(new Rect(cardRect.x, cardRect.y, AccentW, cardRect.height), accent);

            float contentX = cardRect.x + AccentW + 6f;
            float contentW = cardRect.xMax - contentX - 4f;

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Color colorBefore = GUI.color;

            // Dim the whole card content when squad is unavailable (busy / cooldown / unassigned)
            Color baseTint = row.available ? Color.white : new Color(0.7f, 0.7f, 0.7f);

            /* === HEADER ROW === */
            float headerY = cardRect.y;
            float statusW = 180f;

            // Status badge (right)
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = row.statusColor;
            Widgets.Label(new Rect(cardRect.xMax - statusW - 4f, headerY, statusW, CardHeaderH), row.status);

            // Squad name (left, accent-colored)
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = row.available ? accent : new Color(accent.r * 0.7f, accent.g * 0.7f, accent.b * 0.7f);
            float nameW = contentW - statusW - 6f;
            Widgets.Label(new Rect(contentX, headerY, nameW, CardHeaderH), squad.DisplayName);

            /* === DETAIL ROW === */
            float detailY = cardRect.y + CardHeaderH;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = baseTint;

            float dx = contentX;
            float colSettlement = 220f;
            float colPower      = 95f;
            float colEff        = 90f;
            float colTravel     = 110f;
            float colWin        = Math.Max(0f, contentW - colSettlement - colPower - colEff - colTravel);

            string settlementLbl = "FCSquadColBillet".Translate() + ": "
                + (squad.settlement?.Name ?? "FCMilitaryTableSlotEmpty".Translate());
            string powerLbl = "FCSquadColPower".Translate() + ": " + row.attackerPower.ToString("0.0");
            string effLbl = row.hasAttackerForce
                ? (string)"FCSquadColEfficiency".Translate() + ": x" + row.attackerEfficiency.ToString("0.##")
                : (string)"FCSquadColEfficiency".Translate() + ": -";
            string travelLbl = "FCSquadColTravel".Translate() + ": "
                + (squad.IsAssigned && target is object
                    ? (row.travelTicks / (float)GenDate.TicksPerDay).ToString("0.0") + " d"
                    : "-");
            string winLbl;
            if (!row.available)
            {
                winLbl = (string)"FCSquadColWinChance".Translate() + ": -";
            }
            else
            {
                double minPct = Math.Round(row.winChanceMin * 100);
                double maxPct = Math.Round(row.winChanceMax * 100);
                winLbl = (string)"FCSquadColWinChance".Translate() + ": " + TextUtil.FormatRange(minPct, maxPct, "0") + "%";
            }

            Widgets.Label(new Rect(dx, detailY, colSettlement, CardDetailH), settlementLbl); dx += colSettlement;
            Widgets.Label(new Rect(dx, detailY, colPower,      CardDetailH), powerLbl);      dx += colPower;
            Widgets.Label(new Rect(dx, detailY, colEff,        CardDetailH), effLbl);        dx += colEff;
            Widgets.Label(new Rect(dx, detailY, colTravel,     CardDetailH), travelLbl);     dx += colTravel;
            Widgets.Label(new Rect(dx, detailY, colWin,        CardDetailH), winLbl);

            // Whole-card click → select
            if (Widgets.ButtonInvisible(cardRect))
            {
                selected = squad;
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
            GUI.color = colorBefore;
        }

        private void Confirm()
        {
            if (selected is null) return;
            if (!selected.IsAvailable) return;
            if (currentJob is null) return;

            if (onConfirm is object)
            {
                try { onConfirm(selected); }
                catch (Exception e) { LogUtil.Error($"Dialog_AttackSettlement.onConfirm threw: {e}"); }
                Close();
                return;
            }

            // Default behavior: create offensive op via manager.
            int travel = selected.settlement is object && target is object
                ? TravelUtil.ReturnTicksToArrive(selected.settlement.Tile, target.Tile)
                : GenDate.TicksPerDay;
            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            if (manager is null)
            {
                LogUtil.Error("Dialog_AttackSettlement.Confirm: MilitaryManager unavailable.");
                Close();
                return;
            }
            RelationsUtilFC.AttackFaction(enemy);
            manager.CreateOffensiveOp(selected, target, currentJob, enemy, travel);
            Close();
        }

        private void RebuildRows()
        {
            rows.Clear();
            FactionFC fc = FactionCache.FactionComp;
            List<MercenarySquadFC> pool = fc?.militaryCustomizationUtil?.mercenarySquads;
            if (pool is null) { rowsDirty = false; return; }

            int now = Find.TickManager.TicksGame;
            foreach (MercenarySquadFC squad in pool)
            {
                if (squad is null) continue;
                bool available = squad.IsAvailable;
                if (availableOnly && !available) continue;

                int travelTicks = 0;
                if (squad.settlement is object && target is object)
                    travelTicks = TravelUtil.ReturnTicksToArrive(squad.settlement.Tile, target.Tile);

                // Attacker force is computed once per row so we can show power+efficiency on busy/cooldown
                // squads too (they're still informative to compare). Win chance only meaningful when ready.
                MilitaryForce attackerForce = MilitaryForce.CreateMilitaryForceFromSquad(squad, isAttacking: true);
                double attackerPower = SquadPowerRegistry.Resolve(squad).militaryLevel;
                double attackerEfficiency = attackerForce?.militaryEfficiency ?? 0;
                bool hasAttackerForce = attackerForce is object;

                /* Apply attacker-side modifiers per-row so the win chance reflects how the
                 * specific squad would actually perform after BattleModifierRegistry runs at
                 * engagement. Defender-side bounds were already pre-modified once at ctor. */
                if (hasAttackerForce && target is object)
                {
                    BattleForceContext rowCtx = new BattleForceContext
                    {
                        kind = currentJob,
                        targetTile = target.Tile,
                        targetObject = target,
                        aggressor = new MilitaryOperationParticipant
                        {
                            faction = FactionCache.PlayerColonyFaction,
                            squad = squad,
                            force = attackerForce,
                            homeSettlement = squad.settlement
                        },
                        defender = new MilitaryOperationParticipant { faction = enemy }
                    };
                    FactionCache.EnemyPower?.ApplyBattleModifiers(rowCtx, attackerForce, isAttacker: true);
                    attackerEfficiency = attackerForce.militaryEfficiency;
                }

                /* Win chance against MAX defender = lower bound; against MIN defender = upper bound.
                 * (Stronger defender => lower attacker win chance, and vice versa.) */
                double winChanceMin = 0;
                double winChanceMax = 0;
                if (available && hasAttackerForce && defenderForceMin is object && defenderForceMax is object)
                {
                    winChanceMin = SimulateBattleFc.CalculateAttackerWinChance(attackerForce, defenderForceMax);
                    winChanceMax = SimulateBattleFc.CalculateAttackerWinChance(attackerForce, defenderForceMin);
                }

                string status;
                Color statusColor;
                ComputeStatus(squad, now, out status, out statusColor);

                rows.Add(new RowData
                {
                    squad = squad,
                    travelTicks = travelTicks,
                    winChanceMin = winChanceMin,
                    winChanceMax = winChanceMax,
                    attackerPower = attackerPower,
                    attackerEfficiency = attackerEfficiency,
                    hasAttackerForce = hasAttackerForce,
                    status = status,
                    statusColor = statusColor,
                    available = available
                });
            }

            switch (sort)
            {
                /* Sort by midpoint so a row with a wider but higher-on-average range still
                 * outranks a narrower lower-average row. */
                case SortMode.WinChance: rows = rows.OrderByDescending(r => (r.winChanceMin + r.winChanceMax) * 0.5).ToList(); break;
                case SortMode.Travel:    rows = rows.OrderBy(r => r.travelTicks).ToList(); break;
                case SortMode.Power:     rows = rows.OrderByDescending(r => r.attackerPower).ToList(); break;
                case SortMode.Name:      rows = rows.OrderBy(r => r.squad.DisplayName).ToList(); break;
            }
            rowsDirty = false;
        }

        /* Mirrors HireSquadsWindow.ComputeStatus / ColorForStatus — kept local to avoid promoting a
           9-line helper to public surface for one caller. */
        private static void ComputeStatus(MercenarySquadFC squad, int now, out string status, out Color color)
        {
            if (!squad.IsAssigned)
            {
                status = "FCSquadStatusUnassigned".Translate();
                color = AccentUtil.MilInactive;
                return;
            }
            MilitaryOperation op = squad.Operation;
            if (op is object && op.kind != MilitaryJobDefOf.Cooldown && op.phase != MilitaryOperationPhase.CooldownPending)
            {
                int ticksLeft = Math.Max(0, op.nextPhaseTick - now);
                string opLabel = op.kind?.label ?? "?";
                status = "FCSquadStatusBusyOp".Translate(opLabel,
                    (ticksLeft / (float)GenDate.TicksPerDay).ToString("0.0"));
                color = AccentUtil.MilActiveMission;
                return;
            }
            if (squad.nextAvailableTick > now)
            {
                int ticksLeft = squad.nextAvailableTick - now;
                status = "FCSquadStatusCooldown".Translate(
                    (ticksLeft / (float)GenDate.TicksPerDay).ToString("0.0"));
                color = AccentUtil.MilCooldown;
                return;
            }
            status = "FCSquadStatusReady".Translate();
            color = AccentUtil.MilReady;
        }

        private static string SortLabel(SortMode mode)
        {
            switch (mode)
            {
                case SortMode.WinChance: return "FCSquadColWinChance".Translate();
                case SortMode.Travel:    return "FCSquadColTravel".Translate();
                case SortMode.Power:     return "FCSquadColPower".Translate();
                case SortMode.Name:      return "FCSquadColName".Translate();
            }
            return "?";
        }
    }
}
