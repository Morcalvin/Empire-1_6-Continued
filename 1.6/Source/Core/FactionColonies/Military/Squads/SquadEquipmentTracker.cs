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
    /// Per-squad equipment tracker. Owns the lists of weapons / apparel currently
    /// in use by the squad's mercenaries (the post-battle cleanup uses these to
    /// destroy gear that fell off non-player pawns) and the equip / strip / outfit
    /// methods that mutate them. Equip-time list snapshotting is internal: callers
    /// just call EquipPawn and the tracker appends the worn items.
    /// </summary>
    public class SquadEquipmentTracker : IExposable
    {
        protected MercenarySquadFC squad;

        public List<ThingWithComps> UsedWeaponList;
        public List<Apparel> UsedApparelList;

        public SquadEquipmentTracker() { }

        public SquadEquipmentTracker(MercenarySquadFC squad)
        {
            this.squad = squad;
            UsedWeaponList = new List<ThingWithComps>();
            UsedApparelList = new List<Apparel>();
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref UsedWeaponList, "UsedWeaponList", LookMode.Reference);
            Scribe_Collections.Look(ref UsedApparelList, "UsedApparelList", LookMode.Reference);
        }

        /// <summary>Re-equips every mercenary slot from <paramref name="outfit"/>'s units,
        /// generating fresh pawns for empty / mismatched slots and stripping/re-applying gear
        /// on existing pawns. Called only from explicit player actions: <see cref="MercenarySquadFC.InitiateSquad"/>
        /// at hire, <see cref="SquadUpgradeUtil.UpgradeToTemplate"/>, <see cref="MercenarySquadFC.FillEmptySlots"/>,
        /// and the per-pawn editor in <c>Dialog_PawnLoadout</c>.</summary>
        public virtual void OutfitSquad(MilSquadFC outfit)
        {
            if (squad is null) return;
            int count = 0;
            squad.outfit = outfit;
            UsedWeaponList = new List<ThingWithComps>();
            UsedApparelList = new List<Apparel>();
            squad.animals = new List<Mercenary>();
            foreach (MilUnitFC loadout in outfit.Units)
            {
                try
                {
                    if (loadout == null)
                    {
                        count++;
                        continue;
                    }

                    // Ensure we have enough mercenaries in the list
                    while (squad.mercenaries.Count <= count)
                    {
                        Mercenary newMerc = new Mercenary(true);
                        MercenaryPawnFactory.CreateNewPawn(squad, ref newMerc, loadout?.pawnKind, loadout?.xenotype, loadout?.customXenotypeName, loadout);
                        if (newMerc?.pawn != null)
                        {
                            squad.mercenaries.Add(newMerc);
                        }
                        else
                        {
                            LogUtil.Warning($"Could not create mercenary for slot {count}.");
                            break;
                        }
                    }

                    // Skip if we still don't have enough mercenaries
                    if (count >= squad.mercenaries.Count || squad.mercenaries[count]?.pawn == null)
                    {
                        LogUtil.Warning($"Skipping outfit slot {count} - no valid mercenary available.");
                        count++;
                        continue;
                    }

                    if (squad.mercenaries[count].pawn.kindDef != loadout.pawnKind || squad.mercenaries[count].pawn.Dead)
                    {
                        Mercenary pawn = new Mercenary(true);
                        MercenaryPawnFactory.CreateNewPawn(squad, ref pawn, loadout.pawnKind, loadout.xenotype, loadout.customXenotypeName, loadout);
                        // Only replace if new pawn was successfully created
                        if (pawn?.pawn != null)
                        {
                            squad.mercenaries.Replace(squad.mercenaries[count], pawn);
                        }
                        else
                        {
                            LogUtil.Warning($"Failed to create replacement pawn for slot {count}.");
                        }
                    }

                    // Skip operations if pawn is null
                    if (squad.mercenaries[count]?.pawn == null)
                    {
                        count++;
                        continue;
                    }

                    StripPawn(squad.mercenaries[count]);
                    if (loadout != null)
                    {
                        EquipPawn(squad.mercenaries[count], loadout);
                        if (loadout.animal != null)
                        {
                            Mercenary animal = new Mercenary(true);
                            MercenaryPawnFactory.CreateNewAnimal(squad, ref animal, loadout.animal);
                            animal.handler = squad.mercenaries[count];
                            squad.mercenaries[count].animal = animal;
                            squad.animals.Add(animal);
                        }

                        squad.mercenaries[count].loadout = loadout;
                        // Sync currentLoadout with what we just equipped — clear any prior
                        // divergence since this is a fresh outfit pass.
                        squad.mercenaries[count].ownedLoadout = null;
                        squad.mercenaries[count].currentLoadout = loadout.Clone();
                    }
                }
                catch (Exception e)
                {
                    LogUtil.Error($"Something went wrong when outfitting a squad (slot {count}): {e}");
                    if (!squad.mercenaries.NullOrEmpty())
                    {
                        LogUtil.Error($"Number of Mercs: {squad.mercenaries.Count}, Any null pawn: {squad.mercenaries.Any(m => m?.pawn == null)}");
                    }
                }
                count++;
            }
            FindFC.Military?.RebuildMercenaryPawnSet();
        }

        /// <summary>Applies <paramref name="loadout"/>'s apparel + weapons to <paramref name="merc"/>
        /// and snapshots the worn items into <see cref="UsedWeaponList"/> / <see cref="UsedApparelList"/>
        /// so post-battle cleanup can identify and destroy dropped gear.</summary>
        public virtual void EquipPawn(Mercenary merc, MilUnitFC loadout)
        {
            if (merc?.pawn == null || loadout == null) return;

            if (merc.pawn.apparel != null)
            {
                FactionFC factionComp = FindFC.FactionComp;
                foreach (SavedThing apparelDef in loadout.apparel)
                {
                    Thing thing = apparelDef.CreateThing();
                    if (thing is Apparel ap)
                    {
                        Color resolved = factionComp?.ResolveApparelColor(apparelDef) ?? Color.white;
                        thing.SetColor(resolved, reportFailure: false);
                        merc.pawn.apparel.Wear(ap);
                    }
                }
            }

            // Carried inventory. Populate BEFORE CE auto-loads the weapon so CE can draw on
            // player-chosen ammo already in the pack. Counts above the item's stack limit are
            // split across multiple stacks.
            if (merc.pawn.inventory?.innerContainer != null && loadout.inventory != null)
            {
                foreach (SavedThing invDef in loadout.inventory)
                {
                    if (invDef.thing == null) continue;
                    int remaining = Mathf.Max(1, invDef.count);
                    int stackLimit = Mathf.Max(1, invDef.thing.stackLimit);
                    while (remaining > 0)
                    {
                        int take = Mathf.Min(remaining, stackLimit);
                        Thing invThing = new SavedThing(invDef.thing, invDef.stuff, take).CreateThing();
                        if (invThing == null) break;
                        merc.pawn.inventory.innerContainer.TryAdd(invThing, true);
                        remaining -= take;
                    }
                }
            }

            if (merc.pawn.equipment != null)
            {
                foreach (SavedThing weaponDef in loadout.weapons)
                {
                    Thing weaponThing = weaponDef.CreateThing();
                    if (weaponThing is ThingWithComps twc)
                    {
                        merc.pawn.equipment.AddEquipment(twc);
                    }
                }

                if (CombatExtendedUtil.IsCELoaded && merc.pawn.equipment.Primary != null)
                {
                    CombatExtendedUtil.EquipWeaponWithAmmo(merc.pawn, merc.pawn.equipment.Primary);
                }
            }

            // Let CE re-index the pawn's inventory/ammo after we populated it.
            CombatExtendedUtil.UpdateInventory(merc.pawn);

            // Snapshot the equipped items so RemoveDroppedEquipment can find them after a battle.
            if (UsedWeaponList == null) UsedWeaponList = new List<ThingWithComps>();
            if (UsedApparelList == null) UsedApparelList = new List<Apparel>();
            if (merc.pawn.equipment?.AllEquipmentListForReading != null)
                UsedWeaponList.AddRange(merc.pawn.equipment.AllEquipmentListForReading);
            if (merc.pawn.apparel?.WornApparel != null)
                UsedApparelList.AddRange(merc.pawn.apparel.WornApparel);
        }

        public virtual void StripPawn(Mercenary merc)
        {
            if (merc?.pawn == null) return;

            try
            {
                merc.pawn.apparel?.DestroyAll();
                merc.pawn.equipment?.DestroyAllEquipment();
                merc.pawn.inventory?.innerContainer?.ClearAndDestroyContents();
            }
            catch (Exception e)
            {
                LogUtil.Error($"Error stripping pawn equipment (mod conflict likely): {e}");
            }
            CombatExtendedUtil.UpdateInventory(merc.pawn);
        }

        public void StripSquad()
        {
            if (squad?.mercenaries is null) return;
            for (int count = 0; count < squad.mercenaries.Count && count < MilSquadFC.MaxSquadSize; count++)
            {
                if (squad.mercenaries[count]?.pawn != null)
                {
                    StripPawn(squad.mercenaries[count]);
                }
            }
        }

        /// <summary>Syncs a merc's companion animal to <paramref name="target"/>'s animal:
        /// creates, replaces, or destroys the animal-merc as needed and keeps
        /// <see cref="MercenarySquadFC.animals"/> consistent (no orphaned entries). Shared by the per-pawn
        /// Upgrade path and the bulk <see cref="SquadUpgradeUtil.UpgradeToTemplate"/> — <see cref="EquipPawn"/> only
        /// touches apparel + weapons, so the animal has to be reconciled separately. A
        /// fresh-hire merc (<c>animal == null</c>) is handled too: it just creates the
        /// animal when the target has one.</summary>
        public void ReconcileAnimal(Mercenary merc, MilUnitFC target)
        {
            if (merc is null || squad is null) return;
            PawnKindDef wanted = target?.animal;

            /* No animal wanted — drop any existing one. */
            if (wanted is null)
            {
                if (merc.animal != null)
                {
                    if (merc.animal.pawn != null && !merc.animal.pawn.Destroyed) merc.animal.pawn.Destroy();
                    squad.animals?.Remove(merc.animal);
                    merc.animal = null;
                }
                return;
            }

            /* Correct animal already present — leave it. */
            if (merc.animal?.pawn?.kindDef == wanted) return;

            /* Wrong / missing animal — destroy the old one (if any), create the wanted one. */
            if (merc.animal != null)
            {
                if (merc.animal.pawn != null && !merc.animal.pawn.Destroyed) merc.animal.pawn.Destroy();
                squad.animals?.Remove(merc.animal);
                merc.animal = null;
            }

            Mercenary animal = new Mercenary(true);
            MercenaryPawnFactory.CreateNewAnimal(squad, ref animal, wanted);
            animal.handler = merc;
            merc.animal = animal;
            if (squad.animals is null) squad.animals = new List<Mercenary>();
            squad.animals.Add(animal);
        }

        public void RemoveDroppedEquipment()
        {
            if (UsedApparelList != null)
            {
                for (int i = UsedApparelList.Count - 1; i >= 0; i--)
                {
                    Apparel apparel = UsedApparelList[i];
                    if (apparel.ParentHolder is Pawn_ApparelTracker tracker)
                    {
                        Pawn pawn = tracker.pawn;
                        if ((pawn.Faction == FindFC.EmpireFaction ||
                             pawn.Faction == Find.FactionManager.OfPlayer) && !pawn.Dead)
                            continue;
                    }
                    UsedApparelList.RemoveAt(i);
                    if (apparel != null && !apparel.Destroyed)
                        apparel.Destroy();
                }
            }

            if (UsedWeaponList != null)
            {
                for (int i = UsedWeaponList.Count - 1; i >= 0; i--)
                {
                    ThingWithComps weapon = UsedWeaponList[i];
                    if (weapon.ParentHolder is Pawn_EquipmentTracker tracker)
                    {
                        Pawn pawn = tracker.pawn;
                        if ((pawn.Faction == FindFC.EmpireFaction ||
                             pawn.Faction == Find.FactionManager.OfPlayer) && !pawn.Dead)
                            continue;
                    }
                    UsedWeaponList.RemoveAt(i);
                    if (weapon != null && !weapon.Destroyed)
                        weapon.Destroy();
                }
            }
        }

        /// <summary>Migration drain — adopts top-level UsedWeaponList / UsedApparelList from a
        /// pre-refactor save into this tracker. Called by <see cref="MercenarySquadFC.ExposeData"/>
        /// in PostLoadInit. Always-drains: passing nulls leaves the tracker's existing (empty) lists alone.</summary>
        internal void AdoptLegacyLists(List<ThingWithComps> weapons, List<Apparel> apparel)
        {
            if (weapons != null) UsedWeaponList = weapons;
            if (apparel != null) UsedApparelList = apparel;
        }
    }
}
