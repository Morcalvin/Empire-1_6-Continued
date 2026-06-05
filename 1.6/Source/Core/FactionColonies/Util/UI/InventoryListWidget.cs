using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /* Shared carried-inventory panel used by DesignUnitsWindow (per-template) and
     * Dialog_PawnLoadout (per-pawn). Reads from displayUnit; routes mutations through
     * opts.getEditTarget so the per-pawn editor can defer cloning a squad template into
     * ownedLoadout. The 70% carry-weight cap is enforced inside MilUnitFC.AddInventory /
     * SetInventoryCount, not here. */
    public static class InventoryListWidget
    {
        public struct Options
        {
            public bool canEdit;
            public bool showHeaderButtons;
            public Func<MilUnitFC> getEditTarget;
        }

        private const float headerHeight = 25f;
        private const float rowHeight = 28f;
        private const float removeButtonSize = 20f;
        private const float IconSize = 24f;

        public static void Draw(Rect rect, MilUnitFC displayUnit, ref Vector2 scrollPos, Options opts)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Header: title + mass usage
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, headerHeight);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(headerRect, "fcTabInventory".Translate());

            float btnY = rect.y + headerHeight + 3f;

            // Mass usage line
            if (displayUnit != null)
            {
                float cur = displayUnit.CurrentInventoryMass;
                float cap = displayUnit.CarryCapacity;
                Rect massRect = new Rect(rect.x, btnY, rect.width, headerHeight);
                Color colorBefore = GUI.color;
                if (cur > cap + 0.0001f) GUI.color = Color.red;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(massRect, "fcInventoryMass".Translate(cur.ToString("F1"), cap.ToString("F1")));
                GUI.color = colorBefore;

                // Add button (right side of the mass line)
                if (opts.canEdit && opts.showHeaderButtons)
                {
                    float addW = 100f;
                    Rect addBtnRect = new Rect(rect.xMax - addW, btnY, addW, headerHeight);
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    if (Widgets.ButtonText(addBtnRect, "fcAddInventoryItem".Translate()))
                        OpenInventoryPicker(displayUnit, opts);
                }
            }

            // List
            Rect listOutRect = new Rect(rect.x, btnY + headerHeight + 2f, rect.width, rect.height - (2 * (headerHeight + 2f)));

            List<SavedThing> items = displayUnit?.inventory ?? new List<SavedThing>();
            float viewHeight = items.Count * rowHeight;
            Rect scrollViewRect = ScrollUtil.BeginScrollView(listOutRect, ref scrollPos, viewHeight);

            for (int i = 0; i < items.Count; i++)
            {
                SavedThing item = items[i];
                if (item.thing == null) continue;
                int index = i;
                Rect row = new Rect(scrollViewRect.x, scrollViewRect.y + i * rowHeight, scrollViewRect.width, rowHeight);
                if (i % 2 == 0) Widgets.DrawHighlight(row);

                // Icon
                Rect iconRect = new Rect(row.x + 2f, row.y + 2f, IconSize, IconSize);
                Widgets.ThingIcon(iconRect, item.thing, item.stuff);

                // Info button
                const float infoBtnSize = 24f;
                Rect infoRect = new Rect(iconRect.xMax + 2f, row.y + (rowHeight - infoBtnSize) / 2f, infoBtnSize, infoBtnSize);
                Widgets.InfoCardButton(infoRect.x, infoRect.y, item.thing, item.stuff);

                // Remove button (far right)
                Rect removeRect = Rect.zero;
                if (opts.canEdit)
                {
                    removeRect = new Rect(row.xMax - removeButtonSize - 2f, row.y + (rowHeight - removeButtonSize) / 2f, removeButtonSize, removeButtonSize);
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    if (Widgets.ButtonText(removeRect, "X"))
                    {
                        MilUnitFC target = opts.getEditTarget?.Invoke();
                        if (target != null) target.RemoveInventory(index);
                    }
                }

                // Count controls: [ - ] N [ + ]
                float rightEdge = opts.canEdit ? removeRect.x - 4f : row.xMax - 4f;
                float countAreaW = opts.canEdit ? 86f : 30f;
                Rect countArea = new Rect(rightEdge - countAreaW, row.y, countAreaW, rowHeight);
                Text.Font = GameFont.Tiny;
                if (opts.canEdit)
                {
                    Rect minus = new Rect(countArea.x, countArea.y + 4f, 20f, rowHeight - 8f);
                    if (Widgets.ButtonText(minus, "-"))
                    {
                        MilUnitFC target = opts.getEditTarget?.Invoke();
                        if (target != null) target.SetInventoryCount(index, Mathf.Max(0, item.count - 1));
                    }
                    Rect num = new Rect(countArea.x + 22f, countArea.y, 40f, rowHeight);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(num, item.count.ToString());
                    Rect plus = new Rect(countArea.x + 64f, countArea.y + 4f, 20f, rowHeight - 8f);
                    if (Widgets.ButtonText(plus, "+"))
                    {
                        MilUnitFC target = opts.getEditTarget?.Invoke();
                        if (target != null) target.SetInventoryCount(index, item.count + 1);
                    }
                }
                else
                {
                    Text.Anchor = TextAnchor.MiddleRight;
                    Widgets.Label(countArea, "x" + item.count);
                }

                // Mass + value
                float massVal = item.thing.GetStatValueAbstract(StatDefOf.Mass, item.stuff) * Mathf.Max(1, item.count);
                Rect valueRect = new Rect(countArea.x - 100f, row.y, 96f, rowHeight);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(valueRect, massVal.ToString("F1") + " kg  $" + item.MarketValue.ToString("F0"));

                // Label
                Rect labelRect = new Rect(infoRect.xMax + 4f, row.y, valueRect.x - infoRect.xMax - 8f, rowHeight);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                string label = item.stuff != null
                    ? (string)(item.thing.LabelCap + " (" + item.stuff.LabelCap + ")")
                    : item.thing.LabelCap.ToString();
                Widgets.Label(labelRect, label);
            }

            ScrollUtil.EndScrollView();

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        private static void OpenInventoryPicker(MilUnitFC displayUnit, Options opts)
        {
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefs
                .Where(t => t.category == ThingCategory.Item
                    && t.EverHaulable
                    && !t.IsCorpse
                    && t.BaseMarketValue > 0f)
                .OrderBy(t => t.label)
                .ToList();

            Find.WindowStack.Add(new FCWindow_ItemStuffPicker(
                defs,
                onConfirm: null,
                titleKey: "fcPickInventoryItem",
                showCount: true,
                initialCount: 1,
                onConfirmWithCount: (item, stuff, count) =>
                {
                    MilUnitFC target = opts.getEditTarget?.Invoke();
                    if (target != null) target.AddInventory(item, stuff, count);
                }
            ));
        }
    }
}
