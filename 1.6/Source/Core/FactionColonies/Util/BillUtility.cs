using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace FactionColonies
{
    public class BillUtility
    {
        public static void ProcessBills()
        {
            FactionFC factionfc = FactionCache.FactionComp;
            List<WorldSettlementFC> latePaidSettlements = new List<WorldSettlementFC>();

            for (int i = factionfc.Bills.Count - 1; i >= 0; i--)
            {
                if (factionfc.Bills[i].dueTick < Find.TickManager.TicksGame)
                {
                    BillFC bill = factionfc.Bills[i];
                    // taxes is null on a default-constructed BillFC; a bill with no taxes owes nothing.
                    bool owedMoney = bill.taxes is object && bill.taxes.silverAmount < 0;
                    WorldSettlementFC settlement = bill.settlement;

                    /* When the player has disabled late payments, owed-silver bills skip
                     * the resolve attempt entirely and go straight to the unpaid penalty
                     * path. Positive-silver bills (tax tributes the player would gain
                     * from) always auto-resolve at expiry -- there's nothing to defer. */
                    bool skipAttempt = !factionfc.allowLatePayments && owedMoney;

                    if (!skipAttempt && bill.AttemptResolve())
                    {
                        if (owedMoney && settlement is object)
                        {
                            ApplyPenalties(settlement, bill.latePaidPenalties);
                            latePaidSettlements.Add(settlement);
                        }
                    }
                    else
                    {
                        if (settlement is object)
                        {
                            ApplyPenaltiesWithMessage(settlement, bill);
                        }
                        else
                        {
                            LogUtil.Warning("ProcessBills: overdue bill has null settlement (loadID=" + bill.loadID + "). Removing orphaned bill.");
                        }
                        factionfc.Bills.Remove(bill);
                    }
                }
            }

            if (latePaidSettlements.Count > 0)
            {
                string settlementList = string.Join("\n", latePaidSettlements.Select(s => "  - " + s.Name));
                Find.LetterStack.ReceiveLetter(
                    "FCLateBillAutoPaidLabel".Translate(),
                    "FCLateBillAutoPaidDesc".Translate(latePaidSettlements.Count, settlementList),
                    LetterDefOf.NegativeEvent);
            }
        }

        public static void ApplyPenaltiesWithMessage(WorldSettlementFC settlement, BillFC bill)
        {
            if (settlement is null || bill is null) return;
            string messageString = "FCNotEnoughSilverForBill".Translate(settlement.Name);
            if ((bill.taxes?.itemTithes?.Count ?? 0) > 0)
            {
                messageString += $" {"FCConfiscatedTithes".Translate()}";
            }
            messageString += $" {ApplyPenalties(settlement, bill.unpaidPenalties)}.";
            Messages.Message(new Message(messageString, MessageTypeDefOf.NegativeEvent));
        }

        public static string ApplyPenalties(WorldSettlementFC settlement, List<BillStatPenalty> penalties)
        {
            string penaltyDesc = "";
            if (settlement is null || penalties is null) return penaltyDesc;
            foreach (BillStatPenalty p in penalties)
            {
                if (penaltyDesc.Length > 0) penaltyDesc += ", ";
                penaltyDesc += ApplyPenalty(settlement, p);
            }
            return penaltyDesc;
        }

        private static string ApplyPenalty(WorldSettlementFC settlement, BillStatPenalty p)
        {
            double gain = 0;
            string penalty = "";
            switch (p.stat)
            {
                case BillPenaltyStat.Unrest:
                    gain = settlement.GainUnrest(p.amount);
                    penalty = $"{TextUtil.ColorizeAdditiveBonus(gain, true)} {"FCUnrest".Translate()}";
                    break;
                case BillPenaltyStat.Happiness:
                    gain = settlement.GainHappiness(-p.amount);
                    penalty = $"{TextUtil.ColorizeAdditiveBonus(gain)} {"FCHappiness".Translate()}";
                    break;
                case BillPenaltyStat.Loyalty:
                    gain = settlement.GainLoyalty(-p.amount);
                    penalty = $"{TextUtil.ColorizeAdditiveBonus(gain)} {"FCLoyality".Translate()}";
                    break;
                case BillPenaltyStat.Prosperity:
                    gain = settlement.GainProsperity(-p.amount);
                    penalty = $"{TextUtil.ColorizeAdditiveBonus(gain)} {"FCProsperity".Translate()}";
                    break;
            }
            return penalty;
        }
    }
}
