using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace FactionColonies
{
    public static class PaymentUtil
    {
        public const string Reason_SquadDeployment = "squad_deployment";
        public const string Reason_FireSupport = "fire_support";
        public const string Reason_BuildingConstruction = "building_construction";
        public const string Reason_BuildingDemolition = "building_demolition";
        public const string Reason_SettlementCreation = "settlement_creation";
        public const string Reason_SettlementUpgrade = "settlement_upgrade";
        public const string Reason_EventOption = "event_option";
        public const string Reason_TaxPayment = "tax_payment";
        public const string Reason_SilverPayment = "silver_payment";
        public const string Reason_PolicyRepick = "policy_repick";
        public const string Reason_SquadHire = "squad_hire";
        public const string Reason_SquadUpgrade = "squad_upgrade";
        public const string Reason_SquadDismissalRefund = "squad_dismissal_refund";
        public const string Reason_SquadUpgradeRefund = "squad_upgrade_refund";
        public const string Reason_SquadFillSlot = "squad_fill_slot";

        public static (List<BillFC>, List<BillFC>) returnBillTypes(List<BillFC> bills)
        {
            List<BillFC> positiveBills = new List<BillFC>();
            List<BillFC> negativeBills = new List<BillFC>();

            foreach (BillFC bill in bills)
            {
                if (bill.taxes.silverAmount >= 0)
                {
                    positiveBills.Add(bill);
                }
                else
                {
                    negativeBills.Add(bill);
                }
            }

            return (negativeBills, positiveBills);
        }

        public static void AutoresolveBills(List<BillFC> bills)
        {
            int resolvedBills = 0;

            (List<BillFC> negativeBills, List<BillFC> positiveBills) = returnBillTypes(bills);

            // Offset negative bills against positive bills, then resolve any remainder.
            int i = 0;
            int maxOuterIterations = bills.Count * bills.Count + 1;
            int outerIterations = 0;
            while (i < negativeBills.Count)
            {
                if (++outerIterations > maxOuterIterations)
                {
                    LogUtil.Error($"AutoresolveBills: exceeded {maxOuterIterations} outer iterations. Bailing out to prevent freeze.");
                    break;
                }
                BillFC negativeBill = negativeBills[i];
                bool matched = false;
                int j = 0;
                int maxInnerIterations = bills.Count * 2 + 1;
                int innerIterations = 0;
                while (j < positiveBills.Count)
                {
                    if (++innerIterations > maxInnerIterations)
                    {
                        LogUtil.Error("AutoresolveBills: exceeded max inner iterations. Bailing out to prevent freeze.");
                        break;
                    }
                    BillFC positiveBill = positiveBills[j];
                    float result = positiveBill.taxes.silverAmount + negativeBill.taxes.silverAmount;
                    if (result == 0)
                    {
                        // Bills cancel each other out — resolve both, restart outer
                        positiveBill.taxes.silverAmount = 0;
                        negativeBill.taxes.silverAmount = 0;
                        positiveBill.Resolve();
                        negativeBill.Resolve();
                        resolvedBills += 2;
                        (negativeBills, positiveBills) = returnBillTypes(bills);
                        i = 0;
                        matched = true;
                        break;
                    }
                    else if (result > 0)
                    {
                        // Positive bill covers the negative — resolve negative, restart outer
                        positiveBill.taxes.silverAmount = result;
                        negativeBill.taxes.silverAmount = 0;
                        negativeBill.Resolve();
                        resolvedBills++;
                        (negativeBills, positiveBills) = returnBillTypes(bills);
                        i = 0;
                        matched = true;
                        break;
                    }
                    else // result < 0
                    {
                        // Negative exceeds positive — resolve positive, continue inner
                        positiveBill.taxes.silverAmount = 0;
                        negativeBill.taxes.silverAmount = result;
                        positiveBill.Resolve();
                        resolvedBills++;
                        (negativeBills, positiveBills) = returnBillTypes(bills);
                        j = 0;
                        continue;
                    }
                }

                if (!matched)
                {
                    if (negativeBill.AttemptResolve())
                    {
                        (negativeBills, positiveBills) = returnBillTypes(bills);
                        resolvedBills++;
                    }
                    else
                    {
                        i++;  // Only skip bills that genuinely can't be resolved
                    }
                }
            }

            // Resolve remaining positive bills
            foreach (BillFC positiveBill in new List<BillFC>(positiveBills))
            {
                positiveBill.Resolve();
                resolvedBills++;
            }

            Messages.Message("FCNumberTaxesHasBeenSolved".Translate(resolvedBills), MessageTypeDefOf.NeutralEvent);
        }

        public static void PlaceThing(Thing thing)
        {
            /* Active tax delivery spot (a Building_TaxSpot the player toggled on) wins
               outright -- its position and map override the default tax map. */
            if (CheckForActiveTaxDeliverySpot(out IntVec3 activeSpot, out Map activeMap))
            {
                GenPlace.TryPlaceThing(thing, activeSpot, activeMap, ThingPlaceMode.Near);
                return;
            }

            /* Otherwise drop onto the canonical tax map (capital -> current -> any home).
               Distinct out-locals above so the fallback taxMap is never clobbered. */
            Map taxMap = GetActiveTaxDeliveryMap();
            if (taxMap is null)
            {
                LogUtil.Warning("PaymentUtil.PlaceThing: no tax map available; thing not placed: "
                    + (thing?.LabelCap ?? "<null>"));
                if (thing is object && !thing.Destroyed) thing.Destroy();
                return;
            }

            if (CheckForTaxSpot(taxMap, out IntVec3 taxSpot))
            {
                GenPlace.TryPlaceThing(thing, taxSpot, taxMap, ThingPlaceMode.Near);
            }
            else
            {
                IntVec3 dropSpot = DropCellFinder.TradeDropSpot(taxMap);
                GenPlace.TryPlaceThing(thing, dropSpot, taxMap, ThingPlaceMode.Near);
            }
        }

        public static void DeliverThings(FCEvent evt, Letter let = null, Message msg = null)
        {
            DeliveryEvent.Action(evt, let, msg);
        }


        public static void DeliverThings(List<Thing> things, PlanetTile source, Letter let = null, Message msg = null)
        {
            DeliveryEvent.CreateDeliveryEvent(things, source, let, msg);
        }

        public static bool PaySilver(int amount, string reason = null, WorldSettlementFC settlement = null)
        {
            SilverPaymentContext context = new SilverPaymentContext(amount, reason, settlement);
            SilverPaymentRegistry.InvokeModifiers(context);
            amount = context.Amount;
            if (amount <= 0) return true;

            List<Thing> silverStacks = new List<Thing>();
            foreach (Map map in Find.Maps)
            {
                if (map.IsPlayerHome)
                {
                    silverStacks.AddRange(
                        map.listerThings.ThingsOfDef(ThingDefOf.Silver)
                           .Where(s => s.IsInAnyStorage()));
                }
            }

            foreach (Thing stack in silverStacks)
            {
                if (amount <= 0) break;

                if (stack.stackCount <= amount)
                {
                    amount -= stack.stackCount;
                    stack.Destroy(DestroyMode.Vanish);
                }
                else
                {
                    stack.SplitOff(amount).Destroy(DestroyMode.Vanish);
                    amount = 0;
                }
            }

            return true;
        }
        /// <summary>Refunds <paramref name="amount"/> silver back to the player by spawning
        /// a silver Thing onto the active tax map. Symmetric counterpart to
        /// <see cref="PaySilver"/>: invokes <see cref="SilverPaymentRegistry"/> with a
        /// negative-amount context so observers can record the refund alongside other
        /// payment events. <paramref name="amount"/> must be non-negative.</summary>
        public static void RefundSilver(int amount, string reason = null, WorldSettlementFC settlement = null)
        {
            if (amount <= 0) return;
            // Surface the refund through the same observer channel as PaySilver. The negative
            // amount lets modifiers distinguish refunds from outflows.
            SilverPaymentContext context = new SilverPaymentContext(-amount, reason, settlement);
            SilverPaymentRegistry.InvokeModifiers(context);
            int finalAmount = -context.Amount;
            if (finalAmount <= 0) return;

            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = finalAmount;
            PlaceThing(silver);
        }

        public static int GetSilver()
        {
            int silver = 0;

            foreach (Map map in Find.Maps)
            {
                if (map.IsPlayerHome)
                {
                    foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Silver).Where(s => s.IsInAnyStorage()))
                    {
                        silver += thing.stackCount;
                    }
                }
            }
            return silver;
        }

        public static bool CheckForTaxSpot(Map map, out IntVec3 dropSpot)
        {
            if (map is null)
            {
                LogUtil.Warning("CheckForTaxSpot received null map, bailing out");
                dropSpot = new IntVec3();
                return false;
            }
            foreach (Building building in map.listerBuildings.allBuildingsColonist.Where(b => b?.def?.defName == "TaxSpot"))
            {
                if (building is Building_TaxSpot taxSpot && taxSpot.IsActiveTaxDeliverySpot)
                {
                    dropSpot = taxSpot.Position;
                    return true;
                }
            }

            dropSpot = new IntVec3();
            return false;
        }

        public static ThingSetMakerParams ReturnThingSetMakerParams(int baseValue, int rangeMod)
        {
            ThingSetMakerParams parms = new ThingSetMakerParams();
            parms.techLevel = Find.FactionManager.OfPlayer.def.techLevel;
            parms.totalMarketValueRange = new FloatRange(baseValue - rangeMod, baseValue + rangeMod);
            return parms;
        }

        public static List<Thing> GenerateRaidLoot(int lootLevel, TechLevel techLevel)
        {
            FactionFC faction = FactionCache.FactionComp;

            float lootMultiplier = (float)faction.GetStatValue(FCStatDefOf.lootMultiplier);

            List<Thing> things = new List<Thing>();
            ThingSetMaker thingSetMaker = new ThingSetMaker_MarketValue();
            ThingSetMakerParams param = new ThingSetMakerParams();
            param.totalMarketValueRange = new FloatRange((500 + (lootLevel * 200)) * lootMultiplier,
                (1000 + (lootLevel * 500)) * lootMultiplier);
            param.filter = new ThingFilter();
            param.techLevel = techLevel;
            param.countRange = new IntRange(3, 20);

            //set allow
            param.filter.SetAllow(ThingCategoryDefOf.Weapons, true);
            param.filter.SetAllow(ThingCategoryDefOf.Apparel, true);
            param.filter.SetAllow(ThingCategoryDefOf.BuildingsArt, true);
            param.filter.SetAllow(ThingCategoryDefOf.Drugs, true);
            param.filter.SetAllow(ThingCategoryDefOf.Items, true);
            param.filter.SetAllow(ThingCategoryDefOf.Medicine, true);
            param.filter.SetAllow(ThingCategoryDefOf.Techprints, true);
            param.filter.SetAllow(ThingCategoryDefOf.Buildings, true);

            //set disallow
            param.filter.SetAllow(DefDatabase<ThingDef>.GetNamedSilentFail("Teachmat"), false);

            things = thingSetMaker.Generate(param);
            return things;
        }

        public static Pawn GeneratePrisoner(Faction faction)
        {
            Pawn pawn;

            PawnKindDef raceChoice;
            raceChoice = faction.RandomPawnKind();

            pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind: raceChoice,
                faction: FactionCache.PlayerColonyFaction, context: PawnGenerationContext.NonPlayer, tile: -1,
                forceGenerateNewPawn: false, allowDead: false, allowDowned: false,
                canGeneratePawnRelations: false, mustBeCapableOfViolence: true, colonistRelationChanceFactor: 0,
                forceAddFreeWarmLayerIfNeeded: false, allowGay: false, allowFood: false, allowAddictions: false,
                inhabitant: false, certainlyBeenInCryptosleep: false, forceRedressWorldPawnIfFormerColonist: false,
                worldPawnFactionDoesntMatter: false, biocodeWeaponChance: 0, extraPawnForExtraRelationChance: null,
                relationWithExtraPawnChanceFactor: 0));
            pawn.equipment.DestroyAllEquipment();
            pawn.apparel.DestroyAll();
            pawn.SetFaction(faction);
            pawn.guest.guestStatusInt = GuestStatus.Prisoner;

            return pawn;
        }

        public static List<Thing> GenerateRewardThings(double valueBase, ResourceEventRewardDef rewardDef)
        {
            if (rewardDef == null)
            {
                LogUtil.Error("GenerateRewardThings called with null rewardDef");
                return new List<Thing>();
            }

            ThingSetMakerParams param = rewardDef.BuildParams(valueBase, out ThingSetMaker thingSetMaker);
            List<Thing> things = null;
            for (int attempts = 0; attempts < 100; attempts++)
            {
                things = thingSetMaker.Generate(param);
                if (PaymentUtil.ReturnValueOfTithe(things) >= param.totalMarketValueRange.Value.min)
                {
                    return things;
                }
            }

            LogUtil.Warning($"GenerateRewardThings failed to meet minimum value after 100 attempts for {rewardDef.defName}. Returning last result.");
            return things;
        }

        public static double ReturnValueOfTithe(List<Thing> things)
        {
            double totalValue = 0;
            foreach (Thing thing in things)
            {
                totalValue += thing.stackCount * thing.MarketValue;
            }

            return totalValue;
        }

        private static Map GetActiveTaxDeliveryMap()
        {
            // First try to find a map with an active tax delivery spot
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;

                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    if (building is Building_TaxSpot taxSpot && taxSpot.IsActiveTaxDeliverySpot)
                    {
                        return map;
                    }
                }
            }

            // Fallback to existing tax map logic
            return FactionCache.FactionComp.TaxMap;
        }

        public static bool CheckForActiveTaxDeliverySpot(out IntVec3 dropSpot, out Map taxMap)
        {
            // Search all player home maps for an active tax delivery spot
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;

                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    if (building is Building_TaxSpot taxSpot && taxSpot.IsActiveTaxDeliverySpot)
                    {
                        dropSpot = building.Position;
                        taxMap = map;
                        return true;
                    }
                }
            }

            dropSpot = IntVec3.Invalid;
            taxMap = null;
            return false;
        }
    }
}