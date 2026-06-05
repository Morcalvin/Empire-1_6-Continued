using FactionColonies.util;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Resolves which items a unit may carry, from the union of all <see cref="FCInventoryCategoryDef"/>
    /// whitelists. Submods add to the pool by shipping their own def. The result is cached per
    /// game session (def databases don't change at runtime).
    /// </summary>
    public static class MilitaryInventoryUtil
    {
        private static HashSet<ThingDef> allowedCache;

        /// <summary>The full whitelisted set, ignoring research/availability (that gate is applied
        /// at list-build time via <see cref="CraftUtil.CanCraftItem"/>).</summary>
        public static HashSet<ThingDef> AllowedItems()
        {
            if (allowedCache != null) return allowedCache;

            HashSet<ThingDef> set = new HashSet<ThingDef>();
            HashSet<ThingDef> excluded = new HashSet<ThingDef>();
            foreach (FCInventoryCategoryDef def in DefDatabase<FCInventoryCategoryDef>.AllDefs)
            {
                if (def.categories != null)
                    foreach (ThingCategoryDef cat in def.categories)
                        if (cat != null)
                            foreach (ThingDef t in cat.DescendantThingDefs)
                                set.Add(t);
                if (def.things != null)
                    foreach (ThingDef t in def.things)
                        if (t != null)
                            set.Add(t);

                if (def.excludeCategories != null)
                    foreach (ThingCategoryDef cat in def.excludeCategories)
                        if (cat != null)
                            foreach (ThingDef t in cat.DescendantThingDefs)
                                excluded.Add(t);
                if (def.excludeThings != null)
                    foreach (ThingDef t in def.excludeThings)
                        if (t != null)
                            excluded.Add(t);
            }

            // Blacklists win over whitelists, across all defs.
            set.ExceptWith(excluded);

            allowedCache = set;
            return set;
        }

        /// <summary>Whether <paramref name="thing"/> is allowed in inventory at all (whitelist only).</summary>
        public static bool IsAllowed(ThingDef thing) => thing != null && AllowedItems().Contains(thing);

        /// <summary>Whitelisted items the player can currently carry: in the whitelist, haulable, and
        /// unlocked by research (same gate the apparel/weapon pickers use).</summary>
        public static List<ThingDef> AvailableItems()
        {
            return AllowedItems()
                .Where(t => t.EverHaulable && !t.IsCorpse && CraftUtil.CanCraftItem(t))
                .OrderBy(t => t.label)
                .ToList();
        }
    }
}
