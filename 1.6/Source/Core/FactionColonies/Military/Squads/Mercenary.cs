using RimWorld;
using System.Collections.Generic;
using Verse;

namespace FactionColonies
{
    public class Mercenary : IExposable, ILoadReferenceable
    {
        //init variables
        /* Unit template pointer for display ("Marksman" template tag). Mutable from the
         * outside via DesignUnitsWindow — do NOT trust this for cost/gear queries.
         * Goes null only on unit template deletion. */
        public MilUnitFC loadout;
        /* "This pawn has been personalized" marker. Cloned from currentLoadout on
         * direct edit (Dialog_PawnLoadout) or unit template deletion. Cleared on per-pawn
         * template swap, bulk Upgrade All claim, or "Reset to unit template" action. */
        public MilUnitFC ownedLoadout;
        /* Truth of equipped gear right now. Always populated when the pawn is alive.
         * Re-cloned on every gear-touching event (hire / fill / upgrade / per-pawn
         * edit / reset). All cost / gear queries should read this, not loadout. */
        public MilUnitFC currentLoadout;
        public MercenarySquadFC squad;
        public WorldSettlementFC settlement;
        public Mercenary handler;
        public Mercenary animal;
        public Pawn pawn;
        public int loadID;

        /// <summary>True when this merc has a real (non-blank) loadout assigned and is
        /// therefore eligible to be sent on a deployment. Derived — there is no field to
        /// scribe and no setter to forget. Returns false during early init when
        /// <c>blankUnit</c> isn't available yet (matches the legacy default).</summary>
        public bool deployable
        {
            get
            {
                MilUnitFC blank = FindFC.FactionComp?.military?.blankUnit;
                return blank != null && loadout != null && loadout != blank;
            }
        }

        /// <summary>Equipped truth — the gear actually on the pawn right now.
        /// Falls back to <see cref="BlueprintLoadout"/> if <see cref="currentLoadout"/>
        /// has not yet been populated (intermediate save shape, freshly created merc,
        /// empty slot).</summary>
        public MilUnitFC EffectiveLoadout => currentLoadout ?? BlueprintLoadout;

        /// <summary>Blueprint of last record — what gear the merc *should* have.
        /// <see cref="ownedLoadout"/> (personalization snapshot) supersedes
        /// <see cref="loadout"/> (pool reference). Use this for "what would Fill /
        /// Upgrade equip", not for "what's worn right now".</summary>
        public MilUnitFC BlueprintLoadout => ownedLoadout ?? loadout;
        /* True when the slot has no pawn — alive in the list as a placeholder for Fill. */
        public bool IsEmptySlot => pawn is null;
        // True when the pawn has another deep owner at save time (Map.mapPawns or
        // WorldPawns). Falls back to Scribe_References to avoid duplicate-id load
        // errors. Scribed under the legacy "isOnMap" key for back-compat with
        // older saves; defaults to false so old saves keep using Scribe_Deep.
        private bool isExternallyOwned = false;

        /// <summary>
        /// Extensible data dictionary for submods. Keyed by submod namespace to avoid collisions.
        /// Use <see cref="GetCustomData{T}"/>, <see cref="SetCustomData"/>, <see cref="RemoveCustomData"/>.
        /// </summary>
        private Dictionary<string, IExposable> customData;

        public Mercenary()
        {

        }

        public Mercenary(bool blank)
        {
            loadID = FindFC.FactionComp.GetNextMercenaryID();
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                isExternallyOwned = pawn is object &&
                    (pawn.Map is object
                     || pawn.SpawnedOrAnyParentSpawned
                     || (Find.WorldPawns is object && Find.WorldPawns.Contains(pawn)));
            }

            Scribe_Values.Look(ref isExternallyOwned, "isOnMap", false);
            Scribe_References.Look(ref loadout, "loadout");
            Scribe_Deep.Look(ref ownedLoadout, "ownedLoadout");
            Scribe_Deep.Look(ref currentLoadout, "currentLoadout");
            Scribe_References.Look(ref squad, "squad");
            Scribe_References.Look(ref settlement, "settlement");
            Scribe_References.Look(ref handler, "handler");
            Scribe_References.Look(ref animal, "animal");

            if (isExternallyOwned)
            {
                Scribe_References.Look(ref pawn, "pawn");
            }
            else
            {
                Scribe_Deep.Look(ref pawn, "pawn");
            }

            Scribe_Values.Look(ref loadID, "loadID");

            // Custom data — only expose if non-empty (backwards compatible with older saves)
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                bool hasData = customData != null && customData.Count > 0;
                Scribe_Values.Look(ref hasData, "hasCustomData", false);
                if (hasData)
                    Scribe_Collections.Look(ref customData, "customData", LookMode.Value, LookMode.Deep);
            }
            else
            {
                bool hasData = false;
                Scribe_Values.Look(ref hasData, "hasCustomData", false);
                if (hasData)
                    Scribe_Collections.Look(ref customData, "customData", LookMode.Value, LookMode.Deep);
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit && pawn != null && pawn.kindDef == null)
            {
                pawn.kindDef = PawnKindDefOf.Colonist;
                LogUtil.Warning($"Mercenary pawn {pawn.LabelShort} had null kindDef on load, reset to Colonist.");
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                /* currentLoadout migration. Three save shapes:
                 *   1. Pre-refactor saves: only loadout (ref) populated.
                 *   2. Intermediate-refactor saves: loadout + ownedLoadout, no currentLoadout.
                 *   3. Post-refactor saves: currentLoadout already populated.
                 * Adopt ownedLoadout when present (it was the truth in shape 2), else
                 * clone the unit template for shape 1. Mercs without a pawn or loadout
                 * stay null (empty slot or fresh-created). */
                if (currentLoadout == null)
                {
                    if (ownedLoadout != null)
                        currentLoadout = ownedLoadout;
                    else if (loadout != null)
                        currentLoadout = loadout.Clone();
                }
            }
        }

        public T GetCustomData<T>(string key) where T : class, IExposable
        {
            if (customData != null && customData.TryGetValue(key, out IExposable val))
                return val as T;
            return null;
        }

        public void SetCustomData(string key, IExposable data)
        {
            if (customData == null) customData = new Dictionary<string, IExposable>();
            customData[key] = data;
        }

        public void RemoveCustomData(string key)
        {
            customData?.Remove(key);
        }

        public string GetUniqueLoadID()
        {
            return "Mercenary_" + loadID;
        }
    }
}