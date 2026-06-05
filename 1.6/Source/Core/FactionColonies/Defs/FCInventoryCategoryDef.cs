using System.Collections.Generic;
using RimWorld;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Declares which item categories (and individual items) a unit may carry in its inventory.
    /// The unit designer's inventory picker shows the union of every FCInventoryCategoryDef's
    /// categories (expanded to all descendant ThingDefs) plus its explicit <see cref="things"/>.
    /// Submods extend the allowed pool simply by shipping their own FCInventoryCategoryDef (or
    /// PatchOperation-ing the base one) — no code changes required.
    /// </summary>
    public class FCInventoryCategoryDef : Def
    {
        /// <summary>Whole categories to allow — every descendant ThingDef becomes carryable.</summary>
        public List<ThingCategoryDef> categories = new List<ThingCategoryDef>();

        /// <summary>Individual items to allow that aren't covered by <see cref="categories"/>.</summary>
        public List<ThingDef> things = new List<ThingDef>();
    }
}
