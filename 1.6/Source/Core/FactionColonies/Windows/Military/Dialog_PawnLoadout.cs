using FactionColonies.util;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Per-pawn loadout editor. Mutates <see cref="Mercenary.ownedLoadout"/> only —
    /// the merc's *target/assigned* loadout. Does NOT touch
    /// <see cref="Mercenary.currentLoadout"/> (equipped snapshot) or the pawn's
    /// actual gear. The per-pawn Upgrade button on the inspection window does the
    /// assigned → equipped transition (paying the silver cost diff).
    ///
    /// Reuses <see cref="FCWindow_ItemStuffPicker"/> for weapon and apparel pickers (same UX
    /// as the unit designer). Pawn identity (kindDef / xenotype) is preserved.
    /// </summary>
    public class Dialog_PawnLoadout : Window
    {
        public override Vector2 InitialSize => new Vector2(640f, 600f);

        private readonly MercenarySquadFC squad;
        private readonly Mercenary merc;
        private Vector2 apparelScroll;

        public Dialog_PawnLoadout(MercenarySquadFC squad, Mercenary merc)
        {
            this.squad = squad;
            this.merc = merc;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            draggable = true;
        }

        /* Returns the merc's ownedLoadout (the target/assigned loadout), creating
         * it as a clone of the squad template on first edit. */
        private MilUnitFC EnsureOwnedLoadout()
        {
            if (merc is null) return null;
            if (merc.ownedLoadout != null) return merc.ownedLoadout;

            MilUnitFC source = merc.loadout ?? merc.currentLoadout;
            MilUnitFC clone;
            if (source != null)
            {
                clone = source.Clone();
            }
            else
            {
                clone = MilTemplateFactory.CreateUnit(false);
                clone.name = (string)"FCSquadInspectionPersonalLoadoutDefaultName".Translate();
            }
            merc.ownedLoadout = clone;
            return merc.ownedLoadout;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (merc is null) { Close(); return; }

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Header
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            string title = merc.pawn != null
                ? (string)"FCDialogPawnLoadoutTitle".Translate(merc.pawn.LabelShortCap)
                : (string)"FCDialogPawnLoadoutTitleEmpty".Translate();
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), title);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            string sub = merc.ownedLoadout != null
                ? (string)"FCDialogPawnLoadoutDivergedSubtitle".Translate()
                : (string)"FCDialogPawnLoadoutInheritedSubtitle".Translate(merc.loadout?.name ?? (string)"FCNone".Translate());
            Widgets.Label(new Rect(inRect.x, inRect.y + 28f, inRect.width, 18f), sub);

            // Layout: portrait + slots on the left, apparel list on the right
            float topY = inRect.y + 54f;
            float bottomBtnH = 36f;

            float leftW = 220f;
            Rect leftPanel = new Rect(inRect.x, topY, leftW, inRect.height - (topY - inRect.y) - bottomBtnH - 8f);
            Rect rightPanel = new Rect(inRect.x + leftW + 10f, topY, inRect.width - leftW - 10f, leftPanel.height);

            DrawLeftPanel(leftPanel);
            DrawApparelPanel(rightPanel);

            // Bottom: Pick from pool + Reset to pool + Done
            Rect bottomRect = new Rect(inRect.x, inRect.yMax - bottomBtnH, inRect.width, bottomBtnH);
            float bx = bottomRect.x;
            float gap = 8f;
            float wideW = 220f;

            if (Widgets.ButtonText(new Rect(bx, bottomRect.y, wideW, bottomRect.height),
                "FCDialogPawnLoadoutPickFromPool".Translate()))
            {
                OpenPickFromPoolMenu();
            }
            bx += wideW + gap;

            // Reset: clear ownedLoadout so BlueprintLoadout falls back to the squad
            // template (loadout). Pawn equipment is untouched — Upgrade does the sync.
            bool canReset = merc.ownedLoadout != null && merc.loadout != null;
            Color colorBefore = GUI.color;
            if (!canReset) GUI.color = Color.gray;
            Rect resetRect = new Rect(bx, bottomRect.y, wideW, bottomRect.height);
            if (Widgets.ButtonText(resetRect, "FCDialogPawnLoadoutResetToPool".Translate(), true, true, canReset))
            {
                merc.ownedLoadout = null;
            }
            GUI.color = colorBefore;

            float doneW = 120f;
            Rect doneRect = new Rect(bottomRect.xMax - doneW, bottomRect.y, doneW, bottomRect.height);
            if (Widgets.ButtonText(doneRect, "OK".Translate())) Close();

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Left panel: pawn portrait + weapon/animal slots ---

        private void DrawLeftPanel(Rect rect)
        {
            const float portraitH = 130f;
            const float slotSize = 50f;
            const float gap = 14f;

            Rect portraitRect = new Rect(rect.x + (rect.width - 100f) / 2f, rect.y, 100f, portraitH);
            if (merc.pawn != null)
                UIUtil.DrawPawnPortrait(portraitRect, merc.pawn);
            else
                Widgets.DrawMenuSection(portraitRect);

            float slotsY = portraitRect.yMax + gap + 14f;
            float slotsTotalW = slotSize * 2 + 16f;
            float slotsX = rect.x + (rect.width - slotsTotalW) / 2f;
            Rect animalSlot = new Rect(slotsX, slotsY, slotSize, slotSize);
            Rect weaponSlot = new Rect(slotsX + slotSize + 16f, slotsY, slotSize, slotSize);

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            const float labelW = 80f;
            Widgets.Label(new Rect(animalSlot.center.x - labelW / 2f, animalSlot.y - 14f, labelW, 14f), "fcLabelAnimal".Translate());
            Widgets.Label(new Rect(weaponSlot.center.x - labelW / 2f, weaponSlot.y - 14f, labelW, 14f), "fcLabelWeapon".Translate());
            Widgets.DrawMenuSection(animalSlot);
            Widgets.DrawMenuSection(weaponSlot);
            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;

            // Display the target (assigned) loadout — what the player is editing.
            MilUnitFC current = merc.BlueprintLoadout;
            // Use non-interactive draws so ButtonInvisible below handles all clicks.
            if (current?.HasWeapon == true)
                Widgets.DrawTextureFitted(weaponSlot, current.weapons[0].thing.uiIcon, 1f);
            if (current?.animal != null)
                Widgets.DrawTextureFitted(animalSlot, current.animal.race.uiIcon, 1f);

            if (Widgets.ButtonInvisible(weaponSlot))
            {
                OpenWeaponPicker();
            }
            if (Widgets.ButtonInvisible(animalSlot))
            {
                OpenAnimalPicker();
            }
        }

        // --- Right panel: apparel list + add button ---

        private void DrawApparelPanel(Rect rect)
        {
            const float headerH = 24f;
            const float rowH = 28f;
            const float iconSize = 24f;

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 110f, headerH), "fcEquippedApparel".Translate());

            Rect addBtnRect = new Rect(rect.xMax - 100f, rect.y, 100f, headerH);
            if (Widgets.ButtonText(addBtnRect, "fcAddApparel".Translate()))
            {
                OpenApparelPicker();
            }

            // Display the target (assigned) loadout — what the player is editing.
            MilUnitFC current = merc.BlueprintLoadout;
            List<SavedThing> apparel = current?.apparel?.Where(a => a.thing != null).ToList() ?? new List<SavedThing>();

            Rect listRect = new Rect(rect.x, rect.y + headerH + 4f, rect.width, rect.height - headerH - 4f);
            float viewH = apparel.Count * rowH;
            Rect viewRect = new Rect(0, 0, listRect.width - 16f, viewH);
            Widgets.BeginScrollView(listRect, ref apparelScroll, viewRect);
            for (int i = 0; i < apparel.Count; i++)
            {
                SavedThing item = apparel[i];
                Rect row = new Rect(0, i * rowH, viewRect.width, rowH);
                if (i % 2 == 0) Widgets.DrawHighlight(row);

                Rect iconRect = new Rect(row.x + 4f, row.y + 2f, iconSize, iconSize);
                Widgets.ThingIcon(iconRect, item.thing, item.stuff);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(iconRect.xMax + 6f, row.y, row.width - iconSize - 50f, rowH),
                    item.thing.LabelCap);

                Rect removeRect = new Rect(row.xMax - 28f, row.y + 4f, 24f, rowH - 8f);
                if (Widgets.ButtonText(removeRect, "X"))
                {
                    SavedThing captured = item;
                    MilUnitFC target = EnsureOwnedLoadout();
                    if (target != null)
                    {
                        target.RemoveApparel(captured.thing);
                    }
                }
            }
            Widgets.EndScrollView();

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        // --- Pickers ---

        private void OpenWeaponPicker()
        {
            MilUnitFC target = EnsureOwnedLoadout();
            if (target is null) return;
            List<ThingDef> weaponDefs = DefDatabase<ThingDef>.AllDefs
                .Where(t => t.IsWeapon && t.BaseMarketValue != 0
                    && !CraftUtil.WeaponBlockedForMercs(t)
                    && t.generateAllowChance > 0f
                    && CraftUtil.CanCraftItem(t)
                    && HARUtil.CanRaceUseWeapon(target.pawnKind?.race, t))
                .OrderBy(t => t.label)
                .ToList();

            SavedThing? currentWeapon = target.HasWeapon ? target.weapons[0] : (SavedThing?)null;
            Find.WindowStack.Add(new FCWindow_ItemStuffPicker(
                weaponDefs,
                onConfirm: (item, stuff) =>
                {
                    target.SetWeapon(item, stuff);
                },
                onUnequip: () =>
                {
                    target.ClearWeapon();
                },
                titleKey: "fcPickWeapon",
                initialItem: currentWeapon?.thing,
                initialStuff: currentWeapon?.stuff
            ));
        }

        private void OpenApparelPicker()
        {
            MilUnitFC target = EnsureOwnedLoadout();
            if (target is null) return;
            BodyDef body = target.pawnKind?.race?.race?.body ?? BodyDefOf.Human;
            List<ThingDef> apparelDefs = DefDatabase<ThingDef>.AllDefs
                .Where(t => t.IsApparel
                    && t.apparel.PawnCanWear(Gender.None, DevelopmentalStage.Adult)
                    && CraftUtil.CanCraftItem(t)
                    && HARUtil.CanRaceWearApparel(target.pawnKind?.race, t)
                    && !target.apparel.Any(a => a.thing == t))
                .OrderBy(t => t.label)
                .ToList();
            Find.WindowStack.Add(new FCWindow_ItemStuffPicker(
                apparelDefs,
                onConfirm: (item, stuff) =>
                {
                    target.SetApparel(item, stuff);
                },
                titleKey: "fcPickApparel"
            ));
        }

        private void OpenAnimalPicker()
        {
            MilUnitFC target = EnsureOwnedLoadout();
            if (target is null) return;
            // The animal picker mutates the target unit directly. Animal data is
            // informational here — the actual animal companion attachment to a pawn is
            // rebuilt only on full outfit changes (Upgrade All / Fill).
            Find.WindowStack.Add(new FCWindow_AnimalPicker(target));
        }

        // --- Pick from pool ---

        private void OpenPickFromPoolMenu()
        {
            MilitaryCustomizationUtil util = FactionCache.FactionComp?.militaryCustomizationUtil;
            if (util?.units is null) return;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (MilUnitFC unit in util.units)
            {
                MilUnitFC captured = unit;
                options.Add(new FloatMenuOption(captured.name, delegate
                {
                    // Personalize this merc to use the picked pool unit's gear as their
                    // assigned loadout. Squad ref (loadout) and equipped state
                    // (currentLoadout) are untouched — Upgrade does the equipment sync.
                    merc.ownedLoadout = captured.Clone();
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("FCNoUnitAvailable".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
