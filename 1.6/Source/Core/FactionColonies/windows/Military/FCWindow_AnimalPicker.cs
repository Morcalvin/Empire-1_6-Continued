using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public class FCWindow_AnimalPicker : Window
    {
        private readonly Action<PawnKindDef> onConfirm;
        private readonly Action onUnequip;
        private PawnKindDef selectedDef;
        private string searchTerm = "";
        private Vector2 scrollPos;

        private const float RowHeight = 30f;
        private const float SearchBarHeight = 28f;
        private const float ButtonHeight = 35f;
        private const float margin = 5f;

        public override Vector2 InitialSize => new Vector2(450f, 550f);

        /* Direct-mutation constructor: kept for callers that just want the picker
         * to set unit.animal and tick. Used by DesignUnitsWindow. */
        public FCWindow_AnimalPicker(MilUnitFC unit)
            : this(unit?.animal,
                  picked =>
                  {
                      if (unit is null) return;
                      unit.animal = picked;
                      unit.ChangeTick();
                  },
                  () =>
                  {
                      if (unit is null) return;
                      unit.animal = null;
                      unit.ChangeTick();
                  })
        {
        }

        /* Callback-based constructor: lets callers (e.g. Dialog_PawnLoadout) defer
         * mutation so opening the picker without confirming is a no-op. */
        public FCWindow_AnimalPicker(PawnKindDef initialAnimal, Action<PawnKindDef> onConfirm, Action onUnequip)
        {
            selectedDef = initialAnimal;
            this.onConfirm = onConfirm;
            this.onUnequip = onUnequip;
            draggable = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Title
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "fcPickAnimal".Translate());

            // Search bar
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect searchRect = new Rect(0, 40f, inRect.width, SearchBarHeight);
            searchTerm = Widgets.TextField(searchRect, searchTerm);

            // Build animal list
            List<PawnKindDef> animals = FactionCache.AllAnimalKindDefs
                .OrderBy(a => a.label ?? a.defName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Filter by search
            if (!string.IsNullOrEmpty(searchTerm))
                animals = animals.Where(a => (a.label ?? a.defName).IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            // Scroll view
            float listTop = searchRect.yMax + margin;
            float listHeight = inRect.height - listTop - ButtonHeight - 15f;
            Rect scrollOutRect = new Rect(0, listTop, inRect.width, listHeight);
            Widgets.DrawMenuSection(scrollOutRect);

            float viewHeight = animals.Count * RowHeight;
            Rect scrollViewRect = ScrollUtil.BeginScrollView(scrollOutRect, ref scrollPos, viewHeight);

            for (int i = 0; i < animals.Count; i++)
            {
                PawnKindDef animal = animals[i];
                Rect row = new Rect(0, i * RowHeight, scrollViewRect.width, RowHeight);

                if (animal == selectedDef)
                    Widgets.DrawHighlightSelected(row);
                else if (i % 2 == 0)
                    Widgets.DrawHighlight(row);

                // Row layout: Icon | Info | Label | Cost
                Rect iconRect = new Rect(row.x + margin, row.y, RowHeight, RowHeight);
                Rect infoRect = new Rect(iconRect.xMax, row.y + 2, RowHeight - 4, RowHeight - 4);
                Rect costRect = new Rect(row.xMax - margin - 70f, row.y, 60f, RowHeight);
                Rect labelRect = new Rect(infoRect.xMax + margin, row.y,
                    costRect.x - infoRect.xMax - (margin * 2), RowHeight);

                Widgets.ThingIcon(iconRect, animal.race);

                Widgets.InfoCardButton(infoRect, animal.race);

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, animal.LabelCap);

                Text.Anchor = TextAnchor.MiddleRight;
                double cost = Math.Floor(animal.race.BaseMarketValue * FCSettings.militaryAnimalCostMultiplier);
                Widgets.Label(costRect, "$" + cost.ToString("F0"));

                if (Widgets.ButtonInvisible(row))
                {
                    selectedDef = animal;
                }
            }

            if (animals.Count == 0)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(scrollOutRect, "fcNoAnimalsAvailable".Translate());
            }

            ScrollUtil.EndScrollView();

            // Bottom buttons
            float buttonWidth = 120f;
            Rect buttonBar = new Rect(0, inRect.height - ButtonHeight - 5f, inRect.width, ButtonHeight);

            // Unequip (left)
            Rect unequipRect = new Rect(buttonBar.x, buttonBar.y, buttonWidth, buttonBar.height);
            if (Widgets.ButtonText(unequipRect, "FCUnitActionUnequipThing".Translate()))
            {
                if (onUnequip != null) onUnequip();
                Close();
            }

            // Cancel (right)
            Rect cancelRect = new Rect(buttonBar.xMax - buttonWidth, buttonBar.y, buttonWidth, buttonBar.height);
            if (Widgets.ButtonText(cancelRect, "CancelButton".Translate()))
            {
                Close();
            }

            // Confirm (left of cancel)
            bool canConfirm = selectedDef != null;
            Rect confirmRect = new Rect(cancelRect.x - buttonWidth - 10f, buttonBar.y, buttonWidth, buttonBar.height);
            if (Widgets.ButtonText(confirmRect, "FCConfirm".Translate(), active: canConfirm))
            {
                if (canConfirm)
                {
                    if (onConfirm != null) onConfirm(selectedDef);
                    Close();
                }
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }
    }
}
