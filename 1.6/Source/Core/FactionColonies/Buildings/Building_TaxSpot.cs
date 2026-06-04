using RimWorld;
using System.Collections.Generic;
using Verse;

namespace FactionColonies
{
    public class Building_TaxSpot : Building
    {
        private bool isActiveTaxDeliverySpot = false;

        public bool IsActiveTaxDeliverySpot
        {
            get => isActiveTaxDeliverySpot;
            set
            {
                if (value && isActiveTaxDeliverySpot != value)
                {
                    // Disable other tax spots when enabling this one
                    DisableOtherTaxSpots();
                    // Warn if no allowed pack animal can reach this new delivery biome.
                    AnimalBiomeUtil.WarnIfDeliveryBiomeUncovered(Map?.Biome, "FCAnimalBiomeLetterDescTaxSpot");
                }
                isActiveTaxDeliverySpot = value;
            }
        }

        private void DisableOtherTaxSpots()
        {
            // Find all other tax spots and disable their delivery function
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;

                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    if (building is Building_TaxSpot otherTaxSpot && otherTaxSpot != this)
                    {
                        otherTaxSpot.isActiveTaxDeliverySpot = false;
                    }
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref isActiveTaxDeliverySpot, "isActiveTaxDeliverySpot", false);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (Faction.OfPlayer.IsPlayer)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "Set Tax Delivery Spot",
                    defaultDesc = isActiveTaxDeliverySpot
                        ? "This tax spot is currently the active tax delivery location. Click to disable."
                        : "Click to make this tax spot the active tax delivery location for your empire.",
                    icon = TexLoad.iconTrade, // Using existing trade icon
                    isActive = () => isActiveTaxDeliverySpot,
                    toggleAction = () =>
                    {
                        IsActiveTaxDeliverySpot = !IsActiveTaxDeliverySpot;
                        if (IsActiveTaxDeliverySpot)
                        {
                            Messages.Message(
                                "Tax delivery spot set! All taxes and goods will now be delivered to this location.",
                                MessageTypeDefOf.PositiveEvent
                            );
                        }
                        else
                        {
                            Messages.Message(
                                "Tax delivery spot disabled. Taxes will use the fallback tax map if set.",
                                MessageTypeDefOf.NeutralEvent
                            );
                        }
                    }
                };
            }
        }

        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();
            string statusString = isActiveTaxDeliverySpot
                ? "Active tax delivery spot"
                : "Tax spot (delivery disabled)";

            return string.IsNullOrEmpty(baseString)
                ? statusString
                : baseString + "\n" + statusString;
        }
    }
}

