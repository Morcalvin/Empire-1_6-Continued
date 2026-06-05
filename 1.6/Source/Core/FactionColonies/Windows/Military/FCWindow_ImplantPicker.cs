using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Modal picker that lists every surgical implant currently installable on the unit's
    /// preview pawn, reusing the base game's own surgery validation (Recipe_InstallImplant +
    /// RecipeWorker.GetPartsToApplyOn/AvailableOnNow). Because the preview pawn already carries
    /// the unit's previously-chosen implants, taken/conflicting slots are filtered out for free.
    /// Stays open so multiple implants can be added; the option list rebuilds whenever the unit's
    /// editVersion changes (each add regenerates the preview pawn).
    /// </summary>
    public class FCWindow_ImplantPicker : Window
    {
        private struct Option
        {
            public RecipeDef recipe;
            public BodyPartDef bodyPartDef;
            public int bodyPartIndex;
            public string label;
        }

        private readonly Func<MilUnitFC> getDisplayUnit;
        private readonly Func<MilUnitFC> getEditTarget;

        private List<Option> options = new List<Option>();
        private int builtForVersion = int.MinValue;
        private MilUnitFC builtForUnit;
        private string searchTerm = "";
        private Vector2 scrollPos;

        private const float RowHeight = 30f;
        private const float SearchBarHeight = 28f;
        private const float margin = 5f;

        public override Vector2 InitialSize => new Vector2(540f, 620f);

        public FCWindow_ImplantPicker(Func<MilUnitFC> getDisplayUnit, Func<MilUnitFC> getEditTarget)
        {
            this.getDisplayUnit = getDisplayUnit;
            this.getEditTarget = getEditTarget;
            forcePause = false;
            draggable = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "fcPickImplant".Translate());

            RebuildIfStale();

            Rect searchRect = new Rect(0, 40f, inRect.width, SearchBarHeight);
            searchTerm = Widgets.TextField(searchRect, searchTerm);

            Rect listOut = new Rect(0, searchRect.yMax + margin, inRect.width, inRect.height - searchRect.yMax - margin - 40f);
            Widgets.DrawMenuSection(listOut);

            List<Option> filtered = string.IsNullOrEmpty(searchTerm)
                ? options
                : options.Where(o => o.label.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            float viewHeight = filtered.Count * RowHeight;
            Rect scrollView = ScrollUtil.BeginScrollView(listOut, ref scrollPos, viewHeight);

            for (int i = 0; i < filtered.Count; i++)
            {
                Option opt = filtered[i];
                Rect row = new Rect(scrollView.x, scrollView.y + i * RowHeight, scrollView.width, RowHeight);
                if (i % 2 == 0) Widgets.DrawHighlight(row);

                Rect iconRect = new Rect(row.x + margin, row.y, RowHeight, RowHeight);
                if (opt.recipe.UIIconThing != null)
                    Widgets.ThingIcon(iconRect, opt.recipe.UIIconThing);

                Rect infoRect = new Rect(iconRect.xMax, row.y + 2f, RowHeight - 4f, RowHeight - 4f);
                if (opt.recipe.UIIconThing != null)
                    Widgets.InfoCardButton(infoRect, opt.recipe.UIIconThing);

                Rect costRect = new Rect(row.xMax - margin - 65f, row.y, 60f, RowHeight);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(costRect, "$" + MilUnitFC.ImplantCost(opt.recipe).ToString("F0"));

                Rect labelRect = new Rect(infoRect.xMax + margin, row.y, costRect.x - infoRect.xMax - 2 * margin, RowHeight);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, opt.label);

                if (Widgets.ButtonInvisible(row))
                {
                    MilUnitFC target = getEditTarget?.Invoke();
                    if (target != null)
                    {
                        target.AddImplant(opt.recipe, opt.bodyPartDef, opt.bodyPartIndex);
                        builtForVersion = int.MinValue; // force rebuild against the updated preview
                    }
                }
            }

            ScrollUtil.EndScrollView();

            Rect closeRect = new Rect(inRect.width - 120f, inRect.height - 35f, 120f, 30f);
            if (Widgets.ButtonText(closeRect, "FCDialogPawnLoadoutClose".Translate()))
                Close();

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        private void RebuildIfStale()
        {
            MilUnitFC unit = getDisplayUnit?.Invoke();
            int version = unit?.editVersion ?? int.MinValue;
            if (unit == builtForUnit && version == builtForVersion) return;
            builtForUnit = unit;
            builtForVersion = version;
            options = BuildOptions(unit?.PreviewPawn);
        }

        private static List<Option> BuildOptions(Pawn pawn)
        {
            List<Option> result = new List<Option>();
            if (pawn == null || pawn.health == null) return result;

            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefs)
            {
                if (!(recipe.Worker is Recipe_InstallImplant)) continue;
                if (recipe.addsHediff == null) continue;
                if (!recipe.AvailableNow) continue;

                List<BodyPartRecord> parts = new List<BodyPartRecord>(recipe.Worker.GetPartsToApplyOn(pawn, recipe));

                if (!recipe.targetsBodyPart)
                {
                    BodyPartRecord wholeBody = parts.Count > 0 ? parts[0] : null;
                    if (!recipe.Worker.AvailableOnNow(pawn, wholeBody)) continue;
                    Option o = new Option();
                    o.recipe = recipe;
                    o.bodyPartDef = wholeBody != null ? wholeBody.def : null;
                    o.bodyPartIndex = 0;
                    o.label = recipe.Worker.GetLabelWhenUsedOn(pawn, wholeBody).CapitalizeFirst();
                    result.Add(o);
                    continue;
                }

                for (int idx = 0; idx < parts.Count; idx++)
                {
                    BodyPartRecord part = parts[idx];
                    if (!recipe.Worker.AvailableOnNow(pawn, part)) continue;
                    Option o = new Option();
                    o.recipe = recipe;
                    o.bodyPartDef = part != null ? part.def : null;
                    // Store the STABLE occurrence index in body.AllParts (not the filtered-list
                    // index), so the implant resolves to the same part on any pawn of this body
                    // regardless of its current health state. See MilUnitFC.TryResolveImplant.
                    o.bodyPartIndex = MilUnitFC.BodyPartOccurrenceIndex(pawn, part);
                    string lbl = recipe.Worker.GetLabelWhenUsedOn(pawn, part).CapitalizeFirst();
                    if (part != null && !recipe.hideBodyPartNames)
                        lbl = lbl + " (" + part.Label + ")";
                    o.label = lbl;
                    result.Add(o);
                }
            }

            result.Sort((a, b) => string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase));
            return result;
        }
    }
}
