using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Settlement-picker dialog used by the HireSquadsWindow's per-row "Reassign" action.
    /// Lists every Empire settlement with an indicator showing remaining cap room, and the
    /// option to "Unassign" (return the squad to the pool). On confirm calls
    /// <see cref="MilitaryCustomizationUtil.AttemptToAssign"/> or <see cref="MilitaryCustomizationUtil.Unassign"/>.
    /// </summary>
    public class Dialog_SquadAssignment : Window
    {
        public override Vector2 InitialSize => new Vector2(480f, 460f);

        private readonly MercenarySquadFC squad;
        private Vector2 scroll;

        public Dialog_SquadAssignment(MercenarySquadFC squad)
        {
            this.squad = squad;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 30f),
                "FCDialogSquadAssignmentHeader".Translate(squad?.name ?? "(?)"));
            Text.Font = GameFont.Small;

            float listTop = 36f;
            float btnH = 36f;
            float listH = inRect.height - listTop - btnH - 8f;
            Rect listRect = new Rect(0, listTop, inRect.width, listH);

            DrawList(listRect);

            float btnY = inRect.height - btnH;
            if (Widgets.ButtonText(new Rect(inRect.width - 160f, btnY, 150f, 32f), "Close".Translate()))
                Close();
        }

        private void DrawList(Rect rect)
        {
            FactionFC fc = FactionCache.FactionComp;
            MilitaryCustomizationUtil util = fc?.militaryCustomizationUtil;
            if (fc is null || util is null) return;

            float rowH = 32f;
            int rowCount = (fc.settlements?.Count ?? 0) + 1; // +1 for unassign row
            float viewH = rowCount * rowH;
            Rect viewRect = new Rect(0, 0, rect.width - 16f, viewH);
            Widgets.BeginScrollView(rect, ref scroll, viewRect);

            int row = 0;

            // Unassign row
            Rect unassignRect = new Rect(0, row * rowH, viewRect.width, rowH);
            if (row % 2 == 0) Widgets.DrawHighlight(unassignRect);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(unassignRect.x + 8f, unassignRect.y, unassignRect.width - 100f, rowH),
                "FCDialogSquadAssignmentUnassign".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonInvisible(unassignRect))
            {
                util.Unassign(squad);
                Close();
            }
            row++;

            if (fc.settlements is object)
            {
                foreach (WorldSettlementFC s in fc.settlements)
                {
                    if (s is null) continue;
                    Rect rowRect = new Rect(0, row * rowH, viewRect.width, rowH);
                    if (row % 2 == 0) Widgets.DrawHighlight(rowRect);

                    int stationed = s.StationedSquads.Count;
                    int cap = s.SquadCap;
                    bool isHere = squad?.settlement == s;
                    bool atCap = !isHere && stationed >= cap;

                    Color colorBefore = GUI.color;
                    if (atCap) GUI.color = new Color(0.6f, 0.6f, 0.6f);
                    else if (isHere) GUI.color = new Color(0.6f, 0.9f, 0.6f);

                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y, rowRect.width - 100f, rowH),
                        s.Name ?? "?");
                    Text.Anchor = TextAnchor.MiddleRight;
                    Widgets.Label(new Rect(rowRect.xMax - 90f, rowRect.y, 80f, rowH),
                        stationed + " / " + cap);
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = colorBefore;

                    if (!atCap && Widgets.ButtonInvisible(rowRect))
                    {
                        if (util.AttemptToAssign(squad, s)) Close();
                    }
                    row++;
                }
            }

            Widgets.EndScrollView();
        }
    }
}
