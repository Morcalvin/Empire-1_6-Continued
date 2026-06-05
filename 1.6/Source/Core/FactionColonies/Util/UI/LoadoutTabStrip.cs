using System.Collections.Generic;
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

    /* Draws the loadout tab row using the shared ButtonFlat tab drawer (UIUtil.DrawTabRow) and
     * returns the bordered content area below the tabs. */
    public static class LoadoutTabStrip
    {
        public const float TabHeight = 28f;

        public static LoadoutTab Draw(Rect boundingBox, LoadoutTab selected, out Rect contentRect)
        {
            List<string> labels = new List<string>
            {
                "fcTabApparel".Translate(),
                "fcTabInventory".Translate(),
                "fcTabImplants".Translate()
            };
            int idx = UIUtil.DrawTabRow(boundingBox, labels, (int)selected, out contentRect,
                tabHeight: TabHeight, minTabWidth: 70f);
            return (LoadoutTab)idx;
        }
    }
}
