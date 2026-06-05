using System;
using System.Collections.Generic;
using System.Linq;

namespace FactionColonies
{
    /// <summary>
    /// Shared loadout-upgrade math used by BOTH the per-pawn Upgrade button
    /// (<see cref="Dialog_SquadInspection"/>) and the bulk Upgrade All path
    /// (<see cref="SquadUpgradeUtil.UpgradeToTemplate"/>).
    /// Keeping the cost and equivalence logic in one place is load-bearing: the two callers
    /// must agree on what "needs upgrading" and "costs what" or the UI desyncs (the bug this
    /// class was extracted to fix).
    /// </summary>
    public static class LoadoutUpgradeUtil
    {
        /// <summary>Equipment-only market value for a loadout (apparel + weapons). Race base
        /// cost is excluded so a pawnKind mismatch between assigned and equipped snapshots
        /// doesn't leak a phantom race-cost diff (the pawn doesn't change race on an upgrade).</summary>
        public static double SumEquipmentCost(MilUnitFC unit)
        {
            if (unit is null) return 0;
            double total = 0;
            if (unit.apparel != null)
                foreach (SavedThing a in unit.apparel) total += a.MarketValue;
            if (unit.weapons != null)
                foreach (SavedThing w in unit.weapons) total += w.MarketValue;
            if (unit.inventory != null)
                foreach (SavedThing inv in unit.inventory) total += inv.MarketValue;
            if (unit.implants != null)
                foreach (SavedImplant im in unit.implants) total += MilUnitFC.ImplantCost(im.recipe);
            return total;
        }

        /// <summary>Unscaled, unrounded positive equipment-cost difference between a target
        /// loadout and the currently-equipped one. Zero when the target costs the same or
        /// less (the upgrade still applies — it's just free). Callers that need the player-
        /// facing silver amount use <see cref="UpgradeCostDiff"/>; the bulk planner sums the
        /// raw diff and scales once at the end to keep its total round-of-sum.</summary>
        public static double RawEquipmentDiff(MilUnitFC target, MilUnitFC current)
        {
            if (target is null) return 0;
            return Math.Max(0, SumEquipmentCost(target) - SumEquipmentCost(current));
        }

        /// <summary>Silver cost to bring <paramref name="current"/> in line with
        /// <paramref name="target"/> — <see cref="RawEquipmentDiff"/> scaled by
        /// <see cref="FCSettings.squadUpgradeCostMultiplier"/> and rounded.</summary>
        public static int UpgradeCostDiff(MilUnitFC target, MilUnitFC current)
        {
            return (int)Math.Round(RawEquipmentDiff(target, current) * FCSettings.squadUpgradeCostMultiplier);
        }

        /// <summary>True when <paramref name="target"/> differs from <paramref name="current"/>
        /// in any applied way (animal, apparel set/stuff/quality/color, weapon). A null target
        /// means "nothing assigned" -> false; a null current with a real target -> true.</summary>
        public static bool LoadoutsDiffer(MilUnitFC target, MilUnitFC current)
        {
            if (target is null) return false;
            if (current is null) return true;
            if (target.animal != current.animal) return true;
            if (!ApparelEquivalent(target.apparel, current.apparel)) return true;
            if (!WeaponsEquivalent(target.weapons, current.weapons)) return true;
            if (!InventoryEquivalent(target.inventory, current.inventory)) return true;
            if (!ImplantsEquivalent(target.implants, current.implants)) return true;
            return false;
        }

        /// <summary>True when the implant set differs between <paramref name="target"/> and
        /// <paramref name="current"/>. Re-equipping (apparel/weapons/inventory) doesn't touch
        /// surgically-applied implants, so the upgrade paths additionally run an in-place implant
        /// reconcile (<see cref="MilUnitFC.ReconcileImplantsOnPawn"/>) when this is true — no pawn
        /// regeneration, identity preserved.</summary>
        public static bool ImplantsChanged(MilUnitFC target, MilUnitFC current)
        {
            if (target is null) return false;
            if (current is null) return true;
            return !ImplantsEquivalent(target.implants, current.implants);
        }

        /* Order-independent equality over inventory rows (thing + stuff + quality + count). */
        public static bool InventoryEquivalent(List<SavedThing> a, List<SavedThing> b)
        {
            int an = a == null ? 0 : a.Count(x => x.thing != null);
            int bn = b == null ? 0 : b.Count(x => x.thing != null);
            if (an != bn) return false;
            if (an == 0) return true;
            List<SavedThing> sa = a.Where(x => x.thing != null).OrderBy(x => x.thing.defName).ThenBy(x => x.count).ToList();
            List<SavedThing> sb = b.Where(x => x.thing != null).OrderBy(x => x.thing.defName).ThenBy(x => x.count).ToList();
            for (int i = 0; i < an; i++)
            {
                if (!SavedThingEquivalent(sa[i], sb[i])) return false;
                if (sa[i].count != sb[i].count) return false;
            }
            return true;
        }

        /* Order-independent equality over implants (recipe + body part + occurrence index). */
        public static bool ImplantsEquivalent(List<SavedImplant> a, List<SavedImplant> b)
        {
            int an = a == null ? 0 : a.Count(x => x.recipe != null);
            int bn = b == null ? 0 : b.Count(x => x.recipe != null);
            if (an != bn) return false;
            if (an == 0) return true;
            List<SavedImplant> sa = a.Where(x => x.recipe != null)
                .OrderBy(x => x.recipe.defName).ThenBy(x => x.bodyPart?.defName ?? "").ThenBy(x => x.bodyPartIndex).ToList();
            List<SavedImplant> sb = b.Where(x => x.recipe != null)
                .OrderBy(x => x.recipe.defName).ThenBy(x => x.bodyPart?.defName ?? "").ThenBy(x => x.bodyPartIndex).ToList();
            for (int i = 0; i < an; i++)
            {
                if (sa[i].recipe != sb[i].recipe) return false;
                if (sa[i].bodyPart != sb[i].bodyPart) return false;
                if (sa[i].bodyPartIndex != sb[i].bodyPartIndex) return false;
            }
            return true;
        }

        public static bool ApparelEquivalent(List<SavedThing> a, List<SavedThing> b)
        {
            int an = a == null ? 0 : a.Count(x => x.thing != null);
            int bn = b == null ? 0 : b.Count(x => x.thing != null);
            if (an != bn) return false;
            if (an == 0) return true;
            List<SavedThing> sa = a.Where(x => x.thing != null).OrderBy(x => x.thing.defName).ToList();
            List<SavedThing> sb = b.Where(x => x.thing != null).OrderBy(x => x.thing.defName).ToList();
            for (int i = 0; i < an; i++)
            {
                if (!SavedThingEquivalent(sa[i], sb[i])) return false;
            }
            return true;
        }

        /* Compares only the first non-null weapon in each list — matches the rest of the
         * military system, which treats a unit as single-weapon. Kept deliberately as-is so
         * the per-pawn and bulk paths share identical semantics. */
        public static bool WeaponsEquivalent(List<SavedThing> a, List<SavedThing> b)
        {
            SavedThing? wa = a == null ? (SavedThing?)null : a.Where(x => x.thing != null).Select(x => (SavedThing?)x).FirstOrDefault();
            SavedThing? wb = b == null ? (SavedThing?)null : b.Where(x => x.thing != null).Select(x => (SavedThing?)x).FirstOrDefault();
            if (wa.HasValue != wb.HasValue) return false;
            if (!wa.HasValue) return true;
            return SavedThingEquivalent(wa.Value, wb.Value);
        }

        public static bool SavedThingEquivalent(SavedThing a, SavedThing b)
        {
            if (a.thing != b.thing) return false;
            if (a.stuff != b.stuff) return false;
            if (a.quality != b.quality) return false;
            if (a.hasColor != b.hasColor) return false;
            if (a.hasColor && a.color != b.color) return false;
            return true;
        }
    }
}
