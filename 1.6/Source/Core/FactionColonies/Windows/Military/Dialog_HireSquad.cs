using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Template-picker dialog for the "Hire & assign here" flow on the settlement window's
    /// military tab. Lists every <see cref="MilSquadFC"/> template with hire cost and an
    /// affordability marker; on confirm hires the chosen template and assigns the resulting
    /// squad to <c>targetSettlement</c>.
    /// </summary>
    public class Dialog_HireSquad : Window
    {
        public override Vector2 InitialSize => new Vector2(560f, 480f);

        private readonly WorldSettlementFC targetSettlement;
        private readonly MilitaryFC mfc;
        private MilSquadFC selected;
        private Vector2 scroll;
        private bool affordableOnly = false;

        public Dialog_HireSquad(WorldSettlementFC targetSettlement)
        {
            this.targetSettlement = targetSettlement;
            this.mfc = FactionCache.FactionComp?.military;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 30f),
                "FCDialogHireSquadHeader".Translate(targetSettlement?.Name ?? "?"));
            Text.Font = GameFont.Small;

            Widgets.CheckboxLabeled(new Rect(0, 36f, 200f, 24f),
                "FCDialogHireSquadAffordableOnly".Translate(), ref affordableOnly);

            float silver = PaymentUtil.GetSilver();
            Widgets.Label(new Rect(220f, 36f, 320f, 24f),
                "FCDialogHireSquadSilver".Translate((int)silver));

            float listTop = 70f;
            float btnH = 36f;
            float listH = inRect.height - listTop - btnH - 8f;
            Rect listRect = new Rect(0, listTop, inRect.width, listH);

            DrawList(listRect);

            float btnY = inRect.height - btnH;
            if (Widgets.ButtonText(new Rect(inRect.width - 320f, btnY, 150f, 32f), "Cancel".Translate()))
                Close();

            bool canConfirm = selected is object;
            int hireCost = selected is object
                ? (int)Math.Round(selected.GetEquipmentTotalCost() * FCSettings.squadHireCostMultiplier)
                : 0;
            bool affordable = silver >= hireCost;
            Color colorBefore = GUI.color;
            if (!canConfirm || !affordable) GUI.color = Color.gray;
            string label = canConfirm
                ? (string)"FCDialogHireSquadConfirmCost".Translate(hireCost)
                : (string)"FCDialogHireSquadConfirm".Translate();
            if (Widgets.ButtonText(new Rect(inRect.width - 160f, btnY, 150f, 32f), label, true, true, canConfirm && affordable))
            {
                MercenarySquadFC hired = mfc?.HireSquad(selected);
                if (hired is object && targetSettlement is object) mfc.AttemptToAssign(hired, targetSettlement);
                Close();
            }
            GUI.color = colorBefore;
        }

        private void DrawList(Rect rect)
        {
            List<MilSquadFC> templates = mfc?.squads ?? new List<MilSquadFC>();
            float silver = PaymentUtil.GetSilver();

            float rowH = 32f;
            float viewH = templates.Count * rowH;
            Rect viewRect = new Rect(0, 0, rect.width - 16f, viewH);
            Widgets.BeginScrollView(rect, ref scroll, viewRect);
            for (int i = 0; i < templates.Count; i++)
            {
                MilSquadFC t = templates[i];
                if (t is null) continue;
                int cost = (int)Math.Round(t.GetEquipmentTotalCost() * FCSettings.squadHireCostMultiplier);
                bool affordable = silver >= cost;
                if (affordableOnly && !affordable) continue;

                Rect rowRect = new Rect(0, i * rowH, viewRect.width, rowH);
                if (selected == t) Widgets.DrawHighlightSelected(rowRect);
                else if (i % 2 == 0) Widgets.DrawHighlight(rowRect);

                Color colorBefore = GUI.color;
                if (!affordable) GUI.color = new Color(0.7f, 0.4f, 0.4f);

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y, rowRect.width - 130f, rowH), t.name ?? "(?)");
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(rowRect.xMax - 130f, rowRect.y, 120f, rowH),
                    "$" + cost);
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = colorBefore;

                if (Widgets.ButtonInvisible(rowRect)) selected = t;
            }
            Widgets.EndScrollView();
        }
    }
}
