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
    /// Replaces the per-job float menu of "settlements with usable military" with a richer table:
    /// every squad in the faction listed with its billet, travel time, predicted win chance, and
    /// status. Confirm dispatches the chosen squad through <see cref="MilitaryOperationManager.CreateOffensiveOp"/>.
    /// <para>The same dialog is reused for any "pick a squad" flow (caravan defense routing, etc.)
    /// — instantiate with a custom <c>onConfirm</c> callback.</para>
    /// </summary>
    public class Dialog_SquadSourcePicker : Window
    {
        public override Vector2 InitialSize => new Vector2(720f, 540f);

        private readonly WorldObject target;
        private readonly MilitaryJobDef job;
        private readonly Faction enemy;
        private readonly Action<MercenarySquadFC> onConfirm;
        private readonly string headerOverride;

        private MercenarySquadFC selected;
        private Vector2 scrollPos;
        private bool availableOnly = true;
        private SortMode sort = SortMode.WinChance;
        private MilitaryForce estimatedDefenderForce;
        private List<RowData> rows = new List<RowData>();
        private bool rowsDirty = true;

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
            public double winChance;
            public string status;
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

            if (enemy is object)
            {
                estimatedDefenderForce = MilitaryForce.CreateMilitaryForceFromFaction(enemy, false);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (rowsDirty) RebuildRows();

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Header
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            string header = headerOverride.NullOrEmpty()
                ? "FCSquadPickerHeader".Translate(job?.LabelCap ?? "?", target?.LabelCap ?? "?", enemy?.Name ?? "").ToString()
                : headerOverride;
            Widgets.Label(new Rect(0, 0, inRect.width, 30f), header);

            Text.Font = GameFont.Small;
            string defenderLine = "FCSquadPickerEstimatedDefender".Translate(
                estimatedDefenderForce?.forceRemaining ?? 0,
                (estimatedDefenderForce?.militaryEfficiency ?? 0).ToString("0.##")).ToString();
            Widgets.Label(new Rect(0, 32f, inRect.width, 22f), defenderLine);

            // Filter / sort row
            float toolbarY = 60f;
            Widgets.CheckboxLabeled(new Rect(0, toolbarY, 160f, 24f), "FCSquadPickerAvailableOnly".Translate(),
                ref availableOnly);
            if (Widgets.ButtonText(new Rect(180f, toolbarY, 200f, 24f), "FCSquadPickerSort".Translate() + ": " + SortLabel(sort)))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(SortLabel(SortMode.WinChance), () => { sort = SortMode.WinChance; rowsDirty = true; }),
                    new FloatMenuOption(SortLabel(SortMode.Travel), () => { sort = SortMode.Travel; rowsDirty = true; }),
                    new FloatMenuOption(SortLabel(SortMode.Power), () => { sort = SortMode.Power; rowsDirty = true; }),
                    new FloatMenuOption(SortLabel(SortMode.Name), () => { sort = SortMode.Name; rowsDirty = true; })
                };
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            // Table
            float tableTop = toolbarY + 32f;
            float buttonsHeight = 36f;
            float tableHeight = inRect.height - tableTop - buttonsHeight - 6f;
            Rect tableRect = new Rect(0, tableTop, inRect.width, tableHeight);
            DrawTable(tableRect);

            // Buttons
            float btnY = inRect.height - buttonsHeight + 2f;
            if (Widgets.ButtonText(new Rect(inRect.width - 320f, btnY, 150f, 32f), "Cancel".Translate()))
            {
                Close();
            }
            bool canConfirm = selected is object && selected.IsAvailable;
            Color colorBefore = GUI.color;
            if (!canConfirm) GUI.color = Color.gray;
            if (Widgets.ButtonText(new Rect(inRect.width - 160f, btnY, 150f, 32f), "Confirm".Translate(), true, true, canConfirm))
            {
                Confirm();
            }
            GUI.color = colorBefore;

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        private void DrawTable(Rect outRect)
        {
            // Header row
            float headerH = 24f;
            Rect headerRect = new Rect(outRect.x, outRect.y, outRect.width, headerH);
            Widgets.DrawHighlight(headerRect);
            DrawColumns(headerRect, isHeader: true,
                squadName: "FCSquadColName".Translate(),
                billet: "FCSquadColBillet".Translate(),
                travel: "FCSquadColTravel".Translate(),
                winChance: "FCSquadColWinChance".Translate(),
                status: "FCSquadColStatus".Translate());

            // Rows
            Rect listRect = new Rect(outRect.x, outRect.y + headerH, outRect.width, outRect.height - headerH);
            float rowH = 28f;
            float viewHeight = rows.Count * rowH;
            Rect viewRect = new Rect(0, 0, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref scrollPos, viewRect);
            for (int i = 0; i < rows.Count; i++)
            {
                RowData row = rows[i];
                Rect rowRect = new Rect(0, i * rowH, viewRect.width, rowH);
                if (selected == row.squad) Widgets.DrawHighlightSelected(rowRect);
                else if (i % 2 == 0) Widgets.DrawHighlight(rowRect);

                Color rowColor = row.available ? Color.white : new Color(0.7f, 0.7f, 0.7f);
                Color colorBefore = GUI.color;
                GUI.color = rowColor;

                string winChanceText = row.available
                    ? (row.winChance * 100).ToString("0") + "%"
                    : "-";
                string travelText = row.squad.IsAssigned
                    ? (row.travelTicks / (float)GenDate.TicksPerDay).ToString("0.0") + " d"
                    : "-";
                DrawColumns(rowRect, isHeader: false,
                    squadName: row.squad.DisplayName,
                    billet: row.squad.settlement?.Name ?? "(unassigned)",
                    travel: travelText,
                    winChance: winChanceText,
                    status: row.status);

                GUI.color = colorBefore;

                if (Widgets.ButtonInvisible(rowRect))
                {
                    selected = row.squad;
                }
            }
            Widgets.EndScrollView();
        }

        private static void DrawColumns(Rect rect, bool isHeader, string squadName, string billet, string travel, string winChance, string status)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Text.Font = isHeader ? GameFont.Tiny : GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            float x = rect.x + 6f;
            Widgets.Label(new Rect(x, rect.y, 160f, rect.height), squadName); x += 160f;
            Widgets.Label(new Rect(x, rect.y, 140f, rect.height), billet); x += 140f;
            Widgets.Label(new Rect(x, rect.y, 80f, rect.height), travel); x += 80f;
            Widgets.Label(new Rect(x, rect.y, 90f, rect.height), winChance); x += 90f;
            Widgets.Label(new Rect(x, rect.y, rect.xMax - x - 6f, rect.height), status);

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
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

                double winChance = 0;
                if (available && estimatedDefenderForce is object)
                {
                    MilitaryForce attackerForce = MilitaryForce.CreateMilitaryForceFromSquad(squad, isAttacking: true);
                    if (attackerForce is object)
                        winChance = SimulateBattleFc.CalculateAttackerWinChance(attackerForce, estimatedDefenderForce);
                }

                string status;
                MilitaryOperation op = squad.Operation;
                if (!squad.IsAssigned) status = "FCSquadStatusUnassigned".Translate();
                else if (op is object && op.kind != MilitaryJobDefOf.Cooldown && op.phase != MilitaryOperationPhase.CooldownPending)
                {
                    int ticksLeft = Math.Max(0, op.nextPhaseTick - now);
                    string opLabel = op.kind?.label ?? "?";
                    status = "FCSquadStatusBusyOp".Translate(opLabel, (ticksLeft / (float)GenDate.TicksPerDay).ToString("0.0"));
                }
                else if (squad.nextAvailableTick > now)
                {
                    int ticksLeft = squad.nextAvailableTick - now;
                    status = "FCSquadStatusCooldown".Translate((ticksLeft / (float)GenDate.TicksPerDay).ToString("0.0"));
                }
                else status = "FCSquadStatusReady".Translate();

                rows.Add(new RowData
                {
                    squad = squad,
                    travelTicks = travelTicks,
                    winChance = winChance,
                    status = status,
                    available = available
                });
            }

            switch (sort)
            {
                case SortMode.WinChance: rows = rows.OrderByDescending(r => r.winChance).ToList(); break;
                case SortMode.Travel: rows = rows.OrderBy(r => r.travelTicks).ToList(); break;
                case SortMode.Power: rows = rows.OrderByDescending(r => r.squad.outfit?.UpdateEquipmentTotalCost() ?? 0).ToList(); break;
                case SortMode.Name: rows = rows.OrderBy(r => r.squad.DisplayName).ToList(); break;
            }
            rowsDirty = false;
        }

        private static string SortLabel(SortMode mode)
        {
            switch (mode)
            {
                case SortMode.WinChance: return "FCSquadColWinChance".Translate();
                case SortMode.Travel: return "FCSquadColTravel".Translate();
                case SortMode.Power: return "FCSquadColPower".Translate();
                case SortMode.Name: return "FCSquadColName".Translate();
            }
            return "?";
        }
    }
}
