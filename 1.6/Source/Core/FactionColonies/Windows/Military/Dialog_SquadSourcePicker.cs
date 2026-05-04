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
    /// predicted win chance, and status. Confirm dispatches the chosen squad through
    /// <see cref="MilitaryOperationManager.CreateOffensiveOp"/>.
    /// <para>The same dialog is reused for any "pick a squad" flow (caravan defense routing, etc.)
    /// — instantiate with a custom <c>onConfirm</c> callback and a <c>headerOverride</c>.</para>
    /// </summary>
    public class Dialog_SquadSourcePicker : Window
    {
        public override Vector2 InitialSize => new Vector2(820f, 560f);

        private readonly WorldObject target;
        private readonly MilitaryJobDef job;
        private readonly Faction enemy;
        private readonly Action<MercenarySquadFC> onConfirm;
        private readonly string headerOverride;

        private MercenarySquadFC selected;
        private Vector2 scrollPos;
        private bool availableOnly = true;
        private SortMode sort = SortMode.WinChance;

        /* Defender power: the registry entry plus pre-built min/max bounds with
         * BattleModifierRegistry already applied. Computed once at ctor — stable across
         * window opens, stable within a session. The bounds frame the actual battle force
         * the player will face: the engagement-time roll in MilitaryOperation.BeginEngagement
         * lands somewhere in [defenderForceMin, defenderForceMax]. */
        private EnemySettlementPower defenderPower;
        private MilitaryForce defenderForceMin;
        private MilitaryForce defenderForceMax;

        private List<RowData> rows = new List<RowData>();
        private bool rowsDirty = true;

        /* Card layout constants — mirror HireSquadsWindow so the two squad-listing surfaces share rhythm. */
        private const float Pad         = 4f;
        private const float RowGap      = 2f;
        private const float CardHeaderH = 24f;
        private const float CardDetailH = 22f;
        private const float CardH       = CardHeaderH + CardDetailH;
        private const float AccentW     = 4f;

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

        /// <summary>Standard constructor for offensive-op picking.</summary>
        public Dialog_SquadSourcePicker(WorldObject target, MilitaryJobDef job, Faction enemy)
            : this(target, job, enemy, null, null) { }

        /// <summary>Override constructor: caller supplies their own <paramref name="onConfirm"/>
        /// (e.g. caravan defense). When non-null, the dialog calls it instead of creating an
        /// offensive op via the manager.</summary>
        public Dialog_SquadSourcePicker(WorldObject target, MilitaryJobDef job, Faction enemy,
            Action<MercenarySquadFC> onConfirm, string headerOverride)
        {
            this.target = target;
            this.job = job;
            this.enemy = enemy;
            this.onConfirm = onConfirm;
            this.headerOverride = headerOverride;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;

            BuildDefenderRange();
        }

        /* Pulls the cached EnemySettlementPower for the target settlement, builds min/max
         * MilitaryForce instances at the variance bounds, and runs BattleModifierRegistry
         * on each so the displayed range matches what the engagement-time roll will hit
         * (modulo same-tick cache state). The context's aggressor is left null because
         * defender-side modifiers in the current codebase don't read aggressor info; a
         * future modifier that needs it would also need a per-row recompute path. */
        private void BuildDefenderRange()
        {
            Settlement targetSettlement = target as Settlement;
            if (targetSettlement is null) return;
            defenderPower = FactionCache.EnemyPowerRegistry?.GetOrCompute(targetSettlement);
            if (defenderPower is null || enemy is null) return;

            defenderForceMin = defenderPower.BuildBoundForce(enemy, max: false);
            defenderForceMax = defenderPower.BuildBoundForce(enemy, max: true);

            BattleForceContext ctx = new BattleForceContext
            {
                kind = job,
                targetTile = targetSettlement.Tile,
                targetObject = targetSettlement,
                aggressor = null,
                defender = new MilitaryOperationParticipant { faction = enemy }
            };

            ctx.defender.force = defenderForceMin;
            BattleModifierRegistry.InvokeModifyForce(ctx, defenderForceMin, isAttacker: false);
            ctx.defender.force = defenderForceMax;
            BattleModifierRegistry.InvokeModifyForce(ctx, defenderForceMax, isAttacker: false);
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (rowsDirty) RebuildRows();

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Color colorBefore = GUI.color;

            float headerBottom = DrawHeader(inRect);

            // Filter / sort row
            float toolbarY = headerBottom + 4f;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.CheckboxLabeled(new Rect(0, toolbarY, 160f, 24f),
                "FCSquadPickerAvailableOnly".Translate(), ref availableOnly);
            if (Widgets.ButtonText(new Rect(180f, toolbarY, 200f, 24f),
                "FCSquadPickerSort".Translate() + ": " + SortLabel(sort)))
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

            // Card list
            float listTop = toolbarY + 32f;
            float buttonsHeight = 36f;
            float listHeight = inRect.height - listTop - buttonsHeight - 6f;
            Rect listRect = new Rect(0, listTop, inRect.width, listHeight);
            DrawCardList(listRect);

            // Buttons
            float btnY = inRect.height - buttonsHeight + 2f;
            if (Widgets.ButtonText(new Rect(inRect.width - 320f, btnY, 150f, 32f), "Cancel".Translate()))
            {
                Close();
            }
            bool canConfirm = selected is object && selected.IsAvailable;
            if (!canConfirm) GUI.color = Color.gray;
            if (Widgets.ButtonText(new Rect(inRect.width - 160f, btnY, 150f, 32f), "Confirm".Translate(), true, true, canConfirm))
            {
                Confirm();
            }
            GUI.color = colorBefore;

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        /* Header: banner title, divider, target subhead with faction icon + relations color, defender power line.
           When headerOverride is set (caravan-defense reuse), only the override title is shown — followed by the
           defender power line if an enemy faction was supplied. Returns the y-coordinate just below the header block. */
        private float DrawHeader(Rect inRect)
        {
            // Title banner
            float titleH = 32f;
            Rect titleRect = new Rect(0, 0, inRect.width, titleH);
            Widgets.DrawHighlight(titleRect);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            string titleText = headerOverride.NullOrEmpty()
                ? "FCSquadPickerHeader".Translate(job?.LabelCap ?? "?").ToString()
                : headerOverride;
            Widgets.Label(new Rect(8f, 0, inRect.width - 16f, titleH), titleText);

            // Divider
            float dividerY = titleH;
            UIUtil.DrawColoredHorizontalLine(0, dividerY, inRect.width, new Color(0.5f, 0.5f, 0.5f));

            float y = dividerY + 6f;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            // Target subhead — only in default (non-override) mode
            if (headerOverride.NullOrEmpty())
            {
                float subRowH = 24f;
                float iconSize = 22f;
                float iconX = 8f;
                if (enemy?.def?.FactionIcon != null)
                {
                    GUI.DrawTexture(new Rect(iconX, y + (subRowH - iconSize) / 2f, iconSize, iconSize),
                        enemy.def.FactionIcon);
                }
                float labelX = iconX + iconSize + 6f;
                Color targetColor = enemy is object ? enemy.PlayerRelationKind.GetColor() : Color.white;
                string targetName = target?.LabelCap ?? "?";
                if (enemy is object && enemy.HasName)
                    targetName = targetName + ", " + enemy.Name;
                UIUtil.DrawColoredLabel(new Rect(labelX, y, inRect.width - labelX - 8f, subRowH),
                    "FCSquadPickerTarget".Translate(targetName), targetColor);
                y += subRowH;
            }

            // Defender power line — shown whenever we have defender bounds (i.e. enemy settlement supplied)
            if (defenderForceMin is object && defenderForceMax is object)
            {
                float defRowH = 22f;
                string defenderLine = "FCSquadPickerEstimatedDefender".Translate(
                    defenderForceMin.forceRemaining,
                    defenderForceMax.forceRemaining,
                    defenderForceMin.militaryEfficiency.ToString("0.##"),
                    defenderForceMax.militaryEfficiency.ToString("0.##")).ToString();
                Widgets.Label(new Rect(8f, y, inRect.width - 16f, defRowH), defenderLine);
                y += defRowH;
            }

            return y;
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
            for (int i = 0; i < rows.Count; i++)
            {
                Rect cardRect = new Rect(0f, runningY, scrollRect.width, CardH);
                DrawSquadCard(cardRect, rows[i], now);
                runningY += CardH + RowGap;
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

            string settlementLbl = (string)"FCSquadColBillet".Translate() + ": "
                + (squad.settlement?.Name ?? (string)"FCMilitaryTableSlotEmpty".Translate());
            string powerLbl = (string)"FCSquadColPower".Translate() + ": " + row.attackerPower.ToString("0.0");
            string effLbl = row.hasAttackerForce
                ? (string)"FCSquadColEfficiency".Translate() + ": x" + row.attackerEfficiency.ToString("0.##")
                : (string)"FCSquadColEfficiency".Translate() + ": -";
            string travelLbl = (string)"FCSquadColTravel".Translate() + ": "
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
                int minPct = (int)System.Math.Round(row.winChanceMin * 100);
                int maxPct = (int)System.Math.Round(row.winChanceMax * 100);
                winLbl = (string)"FCSquadColWinChance".Translate() + ": "
                    + (minPct == maxPct ? minPct + "%" : minPct + "-" + maxPct + "%");
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

            if (onConfirm is object)
            {
                try { onConfirm(selected); }
                catch (Exception e) { LogUtil.Error($"Dialog_SquadSourcePicker.onConfirm threw: {e}"); }
                Close();
                return;
            }

            // Default behavior: create offensive op via manager.
            int travel = selected.settlement is object && target is object
                ? TravelUtil.ReturnTicksToArrive(selected.settlement.Tile, target.Tile)
                : 60000;
            MilitaryOperationManager manager = FactionCache.MilitaryManager;
            if (manager is null)
            {
                LogUtil.Error("Dialog_SquadSourcePicker.Confirm: MilitaryManager unavailable.");
                Close();
                return;
            }
            RelationsUtilFC.AttackFaction(enemy);
            manager.CreateOffensiveOp(selected, target, job, enemy, travel);
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
                        kind = job,
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
                    BattleModifierRegistry.InvokeModifyForce(rowCtx, attackerForce, isAttacker: true);
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
