using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Defensive squad picker — opened during the 24-hour warning window of an incoming
    /// attack. Lists every faction squad as a defender candidate with billet, projected
    /// defender force, travel time to the besieged settlement, and predicted win chance
    /// against the actual incoming attacker. External <see cref="IAutoDefender"/> entries
    /// (VOE outposts, etc.) appear under the squad list. Confirm dispatches to
    /// <see cref="MilitaryOperationsUtil.ChangeDefendingToSquad"/> or
    /// <see cref="MilitaryOperationsUtil.ChangeDefendingToExternalForce"/>.
    /// <para>Card layout, scroll plumbing, sort/filter, and status helpers live on
    /// <see cref="Dialog_SquadPicker"/>.</para>
    /// </summary>
    public class Dialog_DefendSettlement : Dialog_SquadPicker
    {
        private readonly FCEvent evt;
        private readonly MilitaryOperation op;
        private readonly WorldSettlementFC homeSettlement;
        private readonly Faction enemy;
        private readonly MilitaryForce attackerForce;

        /* External-defender selection runs in parallel to `selected` (squad). Confirm dispatches
         * one or the other; OnRowSelected clears whichever wasn't picked. */
        private IAutoDefender selectedExternal;
        private List<ExternalRow> externalRows = new List<ExternalRow>();

        private struct ExternalRow
        {
            public IAutoDefender defender;
            public int distance;
            public double winChance;
            public bool hasForce;
            public double power;
        }

        public Dialog_DefendSettlement(FCEvent evt)
        {
            this.evt = evt;
            this.op = evt?.linkedOperation;
            this.attackerForce = op?.aggressor?.force;
            this.enemy = op?.aggressor?.faction;
            this.homeSettlement = FactionCache.FactionComp?.ReturnSettlementByLocation(evt?.location ?? PlanetTile.Invalid);

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            draggable = true;
        }

        protected override float DrawHeader(Rect inRect)
        {
            // Title banner (full width)
            Rect titleRect = new Rect(0, 0, inRect.width, TitleH);
            Widgets.DrawHighlight(titleRect);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            string titleText = "FCDefenderPickerTitle".Translate(homeSettlement?.Name ?? "?");
            Widgets.Label(new Rect(8f, 0, inRect.width - 16f, TitleH), titleText);

            UIUtil.DrawColoredHorizontalLine(0, TitleH, inRect.width, Color.gray);

            float y = TitleH + 6f;
            float innerX = 8f;
            float innerW = inRect.width - 16f;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            // Attacker faction icon + name (relations color), centered
            Color relationsColor = enemy?.PlayerRelationKind.GetColor() ?? Color.white;
            string factionName = enemy?.Name ?? "?";
            const float IconSize = 22f;
            const float IconGap = 6f;
            const float FactionRowH = 24f;
            Vector2 nameSize = Text.CalcSize(factionName);
            float groupW = (enemy?.def?.FactionIcon != null ? IconSize + IconGap : 0f) + nameSize.x;
            float groupX = (inRect.width - groupW) * 0.5f;

            if (enemy?.def?.FactionIcon != null)
            {
                Color colorBefore = GUI.color;
                GUI.color = enemy.Color;
                GUI.DrawTexture(new Rect(groupX, y + (FactionRowH - IconSize) / 2f, IconSize, IconSize),
                    enemy.def.FactionIcon);
                GUI.color = colorBefore;
                groupX += IconSize + IconGap;
            }

            UIUtil.DrawColoredLabel(new Rect(groupX, y, nameSize.x + 4f, FactionRowH), factionName, relationsColor);
            y += FactionRowH;

            // Incoming attacker force line
            Text.Anchor = TextAnchor.MiddleCenter;
            if (attackerForce is object)
            {
                float lineH = 22f;
                string forceLine = "FCDefenderPickerIncoming".Translate(
                    attackerForce.militaryLevel.ToString("0.#"),
                    attackerForce.militaryEfficiency.ToString("0.##"),
                    Math.Round(attackerForce.forceRemaining).ToString("0"));
                Widgets.Label(new Rect(innerX, y, innerW, lineH), forceLine);
                y += lineH;
            }

            // Current defender summary
            string defenderSummary = BuildCurrentDefenderLine();
            if (!defenderSummary.NullOrEmpty())
            {
                float lineH = 22f;
                Widgets.Label(new Rect(innerX, y, innerW, lineH), defenderSummary);
                y += lineH;
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            return y;
        }

        private string BuildCurrentDefenderLine()
        {
            if (op?.defender is null) return null;
            if (op.externalDefenderSource is object)
            {
                return "FCDefenderPickerCurrentExternal".Translate(op.externalDefenderSource.LabelCap,
                    Math.Round(op.defender.force?.DefensivePower ?? 0).ToString("0"));
            }
            string name = op.defender.squad?.DisplayName
                ?? op.defender.homeSettlement?.Name
                ?? "?";
            return "FCDefenderPickerCurrentDefender".Translate(name,
                Math.Round(op.defender.force?.DefensivePower ?? 0).ToString("0"));
        }

        /* Travel time isn't actionable for defense. Squads dispatched during the warning window
         * are presumed to arrive in time, so we hide the column and free its space for cost. */
        protected override bool ShowTravel => false;

        /* Highlight the squad currently committed to defense. */
        protected override MercenarySquadFC CurrentSquadIndicator => op?.defender?.squad;

        protected override bool CanConfirm() =>
            (selected is object && selected.IsAvailable) || selectedExternal is object;

        protected override void OnRowSelected(MercenarySquadFC squad)
        {
            base.OnRowSelected(squad);
            selectedExternal = null;
        }

        protected override void Confirm()
        {
            if (op is null || evt is null) { Close(); return; }
            if (selectedExternal is object)
            {
                MilitaryOperationsUtil.ChangeDefendingToExternalForce(evt, selectedExternal);
                Close();
                return;
            }
            if (selected is object && selected.IsAvailable)
            {
                MilitaryOperationsUtil.ChangeDefendingToSquad(evt, selected);
                Close();
                return;
            }
        }

        protected override void RebuildRows()
        {
            rows.Clear();
            externalRows.Clear();

            FactionFC fc = FactionCache.FactionComp;
            List<MercenarySquadFC> pool = fc?.military?.mercenarySquads;
            if (pool is null || homeSettlement is null || attackerForce is null)
            {
                rowsDirty = false;
                return;
            }

            int now = Find.TickManager.TicksGame;

            foreach (MercenarySquadFC squad in pool)
            {
                if (squad?.settlement is null) continue;
                bool available = squad.IsAvailable;
                if (availableOnly && !available) continue;

                // Filter by defense validators (e.g. road / range gates) for foreign squads.
                if (squad.settlement != homeSettlement
                    && !DefenseValidatorRegistry.CanDefend(squad.settlement, homeSettlement))
                    continue;

                int travelTicks = TravelUtil.ReturnTicksToArrive(squad.settlement.Tile, homeSettlement.Tile);

                MilitaryForce defenderForce = MilitaryForce.CreateMilitaryForceFromSquad(squad);

                bool hasDefenderForce = defenderForce is object;
                double defenderPower = SquadPowerRegistry.Resolve(squad).militaryLevel;
                double defenderEff = defenderForce?.militaryEfficiency ?? 0;

                if (hasDefenderForce)
                {
                    BattleForceContext rowCtx = new BattleForceContext
                    {
                        kind = op.kind,
                        targetTile = homeSettlement.Tile,
                        targetObject = homeSettlement,
                        aggressor = new MilitaryOperationParticipant { faction = enemy, force = attackerForce },
                        defender = new MilitaryOperationParticipant
                        {
                            faction = FactionCache.PlayerColonyFaction,
                            squad = squad,
                            force = defenderForce,
                            homeSettlement = squad.settlement
                        }
                    };
                    FactionCache.EnemyPower?.ApplyBattleModifiers(rowCtx, defenderForce, isAttacker: false);
                    defenderEff = defenderForce.militaryEfficiency;
                }

                double winChance = hasDefenderForce
                    ? SimulateBattleFc.CalculateDefenderWinChance(attackerForce, defenderForce)
                    : 0;

                string status;
                Color statusColor;
                bool isReady;
                ComputeStatus(squad, now, out status, out statusColor, out isReady);

                int injuredCount = SquadHealthUtil.CountInjuredMercs(squad);
                if (isReady && injuredCount > 0)
                {
                    status = "FCSquadStatusInjured".Translate(injuredCount);
                    statusColor = AccentUtil.MilActiveMission;
                }

                rows.Add(new RowData
                {
                    squad = squad,
                    travelTicks = travelTicks,
                    winChanceMin = winChance,
                    winChanceMax = winChance,
                    ourPower = defenderPower,
                    ourEfficiency = defenderEff,
                    hasOurForce = hasDefenderForce,
                    status = status,
                    statusColor = statusColor,
                    available = available,
                    deploymentCost = squad.DeploymentCost(),
                    injuredCount = injuredCount
                });
            }

            ApplySort();

            // Build external-defender rows for the extra section.
            foreach (IAutoDefender defender in AutoDefenderRegistry.Defenders)
            {
                if (defender?.WorldObject is null) continue;
                if (!defender.CanAutoDefend) continue;
                int distance = Find.WorldGrid.TraversalDistanceBetween(defender.WorldObject.Tile, homeSettlement.Tile);
                if (distance > defender.Range) continue;

                MilitaryForce extForce = defender.CreateDefendingForce();
                double extWc = extForce is object
                    ? SimulateBattleFc.CalculateDefenderWinChance(attackerForce, extForce)
                    : 0;
                externalRows.Add(new ExternalRow
                {
                    defender = defender,
                    distance = distance,
                    winChance = extWc,
                    hasForce = extForce is object,
                    power = defender.MilitaryLevel
                });
            }

            rowsDirty = false;
        }

        protected override float ExtraRowsHeight => externalRows.Count > 0
            ? ExternalSectionHeaderH + externalRows.Count * (CardH + RowGap)
            : 0f;

        private const float ExternalSectionHeaderH = 24f;

        protected override void DrawExtraRows(Rect viewRect, ref float runningY)
        {
            if (externalRows.Count == 0) return;

            // Section header
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Color colorBefore = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect headerRect = new Rect(0f, runningY + 4f, viewRect.width, ExternalSectionHeaderH - 4f);
            Widgets.DrawHighlight(headerRect);
            Widgets.Label(headerRect, "FCDefenderPickerExternal".Translate());
            runningY += ExternalSectionHeaderH;

            for (int i = 0; i < externalRows.Count; i++)
            {
                Rect cardRect = new Rect(0f, runningY, viewRect.width, CardH);
                if (i % 2 == 0) Widgets.DrawHighlight(cardRect);
                DrawExternalCard(cardRect, externalRows[i]);
                runningY += CardH + RowGap;
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
            GUI.color = colorBefore;
        }

        /* External-defender card — same rhythm as squad cards, but the data is from
         * IAutoDefender (no settlement, no travel ticks, no deployment cost). The accent
         * strip is win-chance-driven the same way squad cards are. */
        private void DrawExternalCard(Rect cardRect, ExternalRow row)
        {
            Color winColor = row.hasForce
                ? AccentUtil.GetStatColor((float)(row.winChance * 100), inverted: false)
                : AccentUtil.MilInactive;

            bool isSelected = selectedExternal == row.defender;
            if (isSelected)
            {
                Widgets.DrawHighlightSelected(cardRect);
                Widgets.DrawBoxSolid(cardRect, new Color(winColor.r, winColor.g, winColor.b, 0.10f));
            }
            else if (Mouse.IsOver(cardRect))
            {
                Widgets.DrawHighlight(cardRect);
            }

            Widgets.DrawBoxSolid(new Rect(cardRect.x, cardRect.y, AccentW, cardRect.height), winColor);

            float contentX = cardRect.x + AccentW + 6f;
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Header row: name (left, win-color) + winLbl on the right
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            float headerY = cardRect.y;
            UIUtil.DrawColoredLabel(
                new Rect(contentX, headerY, cardRect.width - contentX - 110f, CardHeaderH),
                row.defender.WorldObject.LabelCap,
                winColor);

            Text.Anchor = TextAnchor.MiddleRight;
            string winLbl = row.hasForce
                ? (string)"FCSquadColWinChance".Translate() + ": " + Math.Round(row.winChance * 100) + "%"
                : (string)"FCSquadColWinChance".Translate() + ": -";
            UIUtil.DrawColoredLabel(new Rect(cardRect.xMax - 110f, headerY, 106f, CardHeaderH), winLbl, winColor);

            // Detail row: power | distance
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float detailY = cardRect.y + CardHeaderH;
            string powLbl = (string)"FCSquadColPower".Translate() + ": " + row.power.ToString("0.#");
            string distLbl = (string)"FCSquadColTravel".Translate() + ": "
                + row.distance + " " + "FCDefenderPickerTiles".Translate();
            UIUtil.DrawColoredLabel(new Rect(contentX, detailY, 200f, CardDetailH), powLbl, Color.white);
            UIUtil.DrawColoredLabel(new Rect(contentX + 210f, detailY, 240f, CardDetailH), distLbl, Color.white);

            if (Widgets.ButtonInvisible(cardRect))
            {
                selected = null;
                selectedExternal = row.defender;
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }
    }
}
