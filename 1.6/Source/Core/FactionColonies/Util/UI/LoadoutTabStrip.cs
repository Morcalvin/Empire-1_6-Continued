using UnityEngine;
using Verse;

namespace FactionColonies
{
    /* The three sub-tabs of a unit's loadout editor (shared by DesignUnitsWindow and
     * Dialog_PawnLoadout). */
    public enum LoadoutTab
    {
        Apparel,
        Inventory,
        Implants
    }

    /* Small horizontal tab-button row drawn above the loadout panel. Matches the
     * button-row idiom used elsewhere in Empire's windows rather than base-game TabDrawer. */
    public static class LoadoutTabStrip
    {
        public const float TabHeight = 28f;

        private static readonly LoadoutTab[] tabs =
            { LoadoutTab.Apparel, LoadoutTab.Inventory, LoadoutTab.Implants };

        private static readonly string[] tabKeys =
            { "fcTabApparel", "fcTabInventory", "fcTabImplants" };

        public static void Draw(Rect row, ref LoadoutTab selected)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            const float gap = 2f;
            float btnW = (row.width - gap * (tabs.Length - 1)) / tabs.Length;

            for (int i = 0; i < tabs.Length; i++)
            {
                Rect tabRect = new Rect(row.x + i * (btnW + gap), row.y, btnW, row.height);
                bool clicked = Widgets.ButtonText(tabRect, tabKeys[i].Translate(), drawBackground: true, doMouseoverSound: true);
                if (selected == tabs[i])
                    Widgets.DrawHighlightSelected(tabRect);
                if (clicked)
                    selected = tabs[i];
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }
    }
}
