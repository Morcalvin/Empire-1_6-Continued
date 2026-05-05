using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace FactionColonies
{
    public static class UIUtil
    {
        // Table rendering constants
        private const float TableRowHeight = 22f;
        private static readonly Color TableAltRowColor = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color TableLineColor = new Color(1f, 1f, 1f, 0.15f);
        private static readonly Color TableHeaderBgColor = new Color(1f, 1f, 1f, 0.1f);
        private static readonly Color TableHeaderTextColor = new Color(0.85f, 0.85f, 0.85f);

        public static Color Dim(Color c, float mag = 0.7f) => new Color(c.r * mag, c.g * mag, c.b * mag, c.a);

        public static void DrawProgressBar(Rect rect, float progress)
        {
            DrawProgressBarColors(rect, progress, Color.black, Color.cyan);
        }
        public static void DrawProgressBarColors(Rect rect, float progress, Color background, Color bar)
        {
            Rect baseRect = new Rect(rect.x, rect.y, rect.width, rect.height);
            Rect progressRect = new Rect(rect.x, rect.y, rect.width * progress, rect.height);
            Widgets.DrawBoxSolid(baseRect, background);
            Widgets.DrawBoxSolid(progressRect, bar);
        }

        public static void DrawPawnPortrait(Rect rect, Pawn pawn, float cameraZoom = 1f)
        {
            RenderTexture portrait = PortraitsCache.Get(pawn, new Vector2(rect.width, rect.height), Rot4.South, cameraZoom: cameraZoom);
            GUI.DrawTexture(rect, portrait);
        }

        public static bool ButtonFlat(Rect rect, string label, Color? labelColor = null,
            bool disabled = false, bool highlighted = false, Color? baseColor = null)
        {
            return ButtonFlatIcon(rect, label, null, labelColor, disabled, highlighted, baseColor);
        }

        public static bool ButtonFlatIcon(Rect rect, string label, Texture2D icon = null,
            Color? labelColor = null, bool disabled = false, bool highlighted = false,
            Color? baseColor = null)
        {
            bool hovered = !disabled && Mouse.IsOver(rect);
            if (baseColor.HasValue)
            {
                Color c = baseColor.Value;
                float mult = hovered ? (highlighted ? 1.3f : 1.6f) : (highlighted ? 0.7f : 1.0f);
                Widgets.DrawBoxSolid(rect, new Color(
                    Mathf.Clamp01(c.r * mult),
                    Mathf.Clamp01(c.g * mult),
                    Mathf.Clamp01(c.b * mult)));
            }
            else
            {
                float normalBg = highlighted ? 0.15f : 0.22f;
                float hoverBg = highlighted ? 0.28f : 0.35f;
                float bg = hovered ? hoverBg : normalBg;
                Widgets.DrawBoxSolid(rect, new Color(bg, bg, bg));
            }

            float iconSpace = 0f;
            if (icon != null)
            {
                GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + (rect.height - 16f) / 2f, 16f, 16f), icon);
                iconSpace = 20f;
            }

            TextAnchor prevAnchor = Text.Anchor;
            bool prevWordWrap = Text.WordWrap;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = false;
            Color prevColor = GUI.color;
            GUI.color = disabled ? Color.gray : (labelColor ?? Color.white);
            Widgets.Label(new Rect(rect.x + iconSpace, rect.y, rect.width - iconSpace, rect.height), label);
            GUI.color = prevColor;
            Text.Anchor = prevAnchor;
            Text.WordWrap = prevWordWrap;

            if (!disabled && Widgets.ButtonInvisible(rect))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            return false;
        }

        public static void DrawTabDecoratorHorizontalTop(Rect tab, Rect boundingBox, Color color)
        {
            DrawTabDecoratorHorizontalTop(tab, boundingBox.x, boundingBox.xMax, color);
        }
        public static void DrawTabDecoratorHorizontalTop(Rect tab, float leftx, float rightx, Color color)
        {
            Color origColor = GUI.color;
            GUI.color = color;
            Widgets.DrawLineHorizontal(leftx, tab.yMax, tab.x - leftx);
            Widgets.DrawLineVertical(tab.x, tab.y, tab.height);
            Widgets.DrawLineHorizontal(tab.x, tab.y, tab.width);
            Widgets.DrawLineVertical(tab.xMax, tab.y, tab.height);
            Widgets.DrawLineHorizontal(tab.xMax, tab.yMax, rightx - tab.xMax);
            GUI.color = origColor;
        }
        public static void DrawTabDecoratorVerticalLeft(Rect tab, Rect boundingBox, Color color)
        {
            DrawTabDecoratorVerticalLeft(tab, boundingBox.y, boundingBox.yMax, color);
        }
        public static void DrawTabDecoratorVerticalLeft(Rect tab, float upy, float downy, Color color)
        {
            Color origColor = GUI.color;
            GUI.color = color;
            Widgets.DrawLineVertical(tab.xMax, upy, tab.y - upy);
            Widgets.DrawLineHorizontal(tab.x, tab.y, tab.width);
            Widgets.DrawLineVertical(tab.x, tab.y, tab.height);
            Widgets.DrawLineHorizontal(tab.x, tab.yMax, tab.width);
            Widgets.DrawLineVertical(tab.xMax, tab.yMax, downy - tab.yMax);
            GUI.color = origColor;
        }

        public static void DrawColoredHighlight(Rect rect, Color color)
        {
            Color origColor = GUI.color;
            GUI.color = color;
            Widgets.DrawHighlight(rect);
            GUI.color = origColor;
        }

        public static void DrawColoredBox(Rect rect, Color color)
        {
            Color origColor = GUI.color;
            GUI.color = color;
            Widgets.DrawBox(rect);
            GUI.color = origColor;
        }

        public static void DrawColoredLabel(Rect rect, string text, Color color)
        {
            Color origColor = GUI.color;
            GUI.color = color;
            Widgets.Label(rect, text);
            GUI.color = origColor;
        }

        public static void DrawColoredVerticalLine(float x, float y, float len, Color color)
        {
            Color origColor = GUI.color;
            GUI.color = color;
            Widgets.DrawLineVertical(x, y, len);
            GUI.color = origColor;
        }
        
        public static void DrawColoredHorizontalLine(float x, float y, float len, Color color)
        {
            Color origColor = GUI.color;
            GUI.color = color;
            Widgets.DrawLineHorizontal(x, y, len);
            GUI.color = origColor;
        }

        public static int GetModifier => 1 * (Event.current.shift ? 5 : 1) * (Event.current.control ? 10 : 1);

        /// <summary>
        /// Draws a row of tabs with automatic multi-row overflow using a custom button drawer.
        /// Returns the selected tab index (changed if a tab was clicked).
        /// <paramref name="contentRect"/> is set to the usable area below the tab rows,
        /// bordered on sides and bottom.
        /// </summary>
        public static int DrawTabRow(Rect boundingBox, List<string> tabLabels, int selectedTab,
            out Rect contentRect, Func<Rect, string, bool, bool> buttonDrawer,
            float tabHeight = 20f, float minTabWidth = 100f, Color? borderColor = null)
        {
            Color border = borderColor ?? Color.gray;
            int tabCount = tabLabels.Count;
            int rows = Math.Max(1, Mathf.CeilToInt(tabCount * minTabWidth / boundingBox.width));
            int basePerRow = Mathf.FloorToInt((float)tabCount / rows);

            float totalTabHeight = rows * tabHeight;
            float contentTop = boundingBox.y + totalTabHeight;

            // Track which row the selected tab lands in, and its rect
            Rect chosenRect = new Rect();
            int selectedRow = -1;
            int tabIndex = 0;
            int result = selectedTab;

            for (int row = 0; row < rows; row++)
            {
                // First row gets the remainder
                int tabsThisRow = (row == 0) ? tabCount - (rows - 1) * basePerRow : basePerRow;
                float rowTabWidth = boundingBox.width / tabsThisRow;
                float rowY = boundingBox.y + row * tabHeight;

                for (int col = 0; col < tabsThisRow; col++)
                {
                    Rect tabRect = new Rect(boundingBox.x + col * rowTabWidth, rowY, rowTabWidth, tabHeight);
                    string label = tabLabels[tabIndex];
                    bool isSelected = tabIndex == selectedTab;

                    // Tooltip for truncated labels
                    if (Text.CalcSize(label).x > tabRect.width - 8f)
                    {
                        TooltipHandler.TipRegion(tabRect, label);
                    }

                    if (buttonDrawer(tabRect, label, isSelected))
                    {
                        result = tabIndex;
                    }

                    if (isSelected)
                    {
                        chosenRect = tabRect;
                        selectedRow = row;
                    }

                    tabIndex++;
                }
            }

            // Border drawing
            Color origColor = GUI.color;
            GUI.color = border;

            if (selectedRow == rows - 1)
            {
                // Selected tab is in the bottom row — draw notch border
                DrawTabDecoratorHorizontalTop(chosenRect, boundingBox.x, boundingBox.xMax, border);
            }
            else
            {
                // Selected tab is in an upper row — straight line across content top
                Widgets.DrawLineHorizontal(boundingBox.x, contentTop, boundingBox.width);
            }

            // Content box sides and bottom
            Widgets.DrawLineVertical(boundingBox.x, contentTop, boundingBox.height - totalTabHeight);
            Widgets.DrawLineVertical(boundingBox.xMax, contentTop, boundingBox.height - totalTabHeight);
            Widgets.DrawLineHorizontal(boundingBox.x, boundingBox.yMax - 1, boundingBox.width);

            GUI.color = origColor;

            contentRect = new Rect(boundingBox.x, contentTop, boundingBox.width, boundingBox.height - totalTabHeight);
            return result;
        }

        /// <summary>Draws tabs using <see cref="ButtonFlat"/> with highlighted selection. Default overload.</summary>
        public static int DrawTabRow(Rect boundingBox, List<string> tabLabels, int selectedTab,
            out Rect contentRect, float tabHeight = 20f, float minTabWidth = 100f,
            Color? baseColor = null, Color? borderColor = null)
        {
            return DrawTabRow(boundingBox, tabLabels, selectedTab, out contentRect,
                (r, l, sel) => ButtonFlat(r, l, highlighted: sel, baseColor: baseColor),
                tabHeight, minTabWidth, borderColor);
        }

        /// <summary>Draws tabs using <see cref="Widgets.ButtonText"/>.</summary>
        public static int DrawTabRowButtonText(Rect boundingBox, List<string> tabLabels, int selectedTab,
            out Rect contentRect, float tabHeight = 20f, float minTabWidth = 100f,
            Color? borderColor = null)
        {
            return DrawTabRow(boundingBox, tabLabels, selectedTab, out contentRect,
                (r, l, sel) => Widgets.ButtonText(r, l), tabHeight, minTabWidth, borderColor);
        }

        /// <summary>Draws tabs using <see cref="ButtonFlat"/> with optional label color.</summary>
        public static int DrawTabRowButtonFlat(Rect boundingBox, List<string> tabLabels, int selectedTab,
            out Rect contentRect, Color? labelColor = null, Color? baseColor = null,
            float tabHeight = 20f, float minTabWidth = 100f, Color? borderColor = null)
        {
            return DrawTabRow(boundingBox, tabLabels, selectedTab, out contentRect,
                (r, l, sel) => ButtonFlat(r, l, labelColor: labelColor, highlighted: sel, baseColor: baseColor),
                tabHeight, minTabWidth, borderColor);
        }

        /// <summary>Draws tabs using <see cref="ButtonFlatIcon"/> with optional icon and label color.</summary>
        public static int DrawTabRowButtonFlatIcon(Rect boundingBox, List<string> tabLabels, int selectedTab,
            out Rect contentRect, Texture2D icon = null, Color? labelColor = null, Color? baseColor = null,
            float tabHeight = 20f, float minTabWidth = 100f, Color? borderColor = null)
        {
            return DrawTabRow(boundingBox, tabLabels, selectedTab, out contentRect,
                (r, l, sel) => ButtonFlatIcon(r, l, icon, labelColor: labelColor, highlighted: sel, baseColor: baseColor),
                tabHeight, minTabWidth, borderColor);
        }
        // TABLE RENDERING

        /// <summary>Returns the height a table with the given row count will consume.</summary>
        public static float TableHeight(int rowCount)
        {
            return TableRowHeight + 1f + rowCount * TableRowHeight + 1f;
        }

        /// <summary>Draws a 2-column table. Returns the total height consumed.</summary>
        public static float DrawTable(Rect rect,
            string col1Header, List<string> col1,
            string col2Header, List<string> col2)
        {
            return DrawTableCore(rect,
                new string[] { col1Header, col2Header },
                new List<string>[] { col1, col2 });
        }

        /// <summary>Draws a 3-column table. Returns the total height consumed.</summary>
        public static float DrawTable(Rect rect,
            string col1Header, List<string> col1,
            string col2Header, List<string> col2,
            string col3Header, List<string> col3)
        {
            return DrawTableCore(rect,
                new string[] { col1Header, col2Header, col3Header },
                new List<string>[] { col1, col2, col3 });
        }

        /// <summary>Draws a 4-column table. Returns the total height consumed.</summary>
        public static float DrawTable(Rect rect,
            string col1Header, List<string> col1,
            string col2Header, List<string> col2,
            string col3Header, List<string> col3,
            string col4Header, List<string> col4)
        {
            return DrawTableCore(rect,
                new string[] { col1Header, col2Header, col3Header, col4Header },
                new List<string>[] { col1, col2, col3, col4 });
        }

        private static float DrawTableCore(Rect rect, string[] headers, List<string>[] columns)
        {
            int colCount = headers.Length;
            int rowCount = columns[0].Count;
            float colW = rect.width / colCount;
            float curY = rect.y;

            // Header row
            Rect headerRect = new Rect(rect.x, curY, rect.width, TableRowHeight);
            Widgets.DrawBoxSolid(headerRect, TableHeaderBgColor);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = TableHeaderTextColor;
            for (int c = 0; c < colCount; c++)
                Widgets.Label(new Rect(rect.x + c * colW, curY, colW, TableRowHeight), headers[c]);

            // Header bottom line
            GUI.color = TableLineColor;
            Widgets.DrawLineHorizontal(rect.x, curY + TableRowHeight, rect.width);
            curY += TableRowHeight + 1f;

            // Data rows
            for (int r = 0; r < rowCount; r++)
            {
                if (r % 2 == 1)
                    Widgets.DrawBoxSolid(new Rect(rect.x, curY, rect.width, TableRowHeight), TableAltRowColor);

                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.white;
                for (int c = 0; c < colCount; c++)
                    Widgets.Label(new Rect(rect.x + c * colW, curY, colW, TableRowHeight), columns[c][r]);

                curY += TableRowHeight;
            }

            // Bottom line
            GUI.color = TableLineColor;
            Widgets.DrawLineHorizontal(rect.x, curY, rect.width);

            // Vertical dividers between columns
            float tableTop = rect.y;
            float tableHeight = curY - tableTop;
            for (int c = 1; c < colCount; c++)
                Widgets.DrawLineVertical(rect.x + c * colW, tableTop, tableHeight);

            // Reset
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            return curY + 1f - rect.y;
        }
    }
}
