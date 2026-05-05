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
                    bool owedMoney = bill.taxes.silverAmount < 0;
                    WorldSettlementFC settlement = bill.settlement;

                    if (bill.AttemptResolve())
                    {
                        if (owedMoney && settlement != null)
                        {
                            latePaidSettlements.Add(settlement);
                        }
                    }
                    else
                    {
                        if (settlement != null)
                        {
                            string messageString = "FCNotEnoughSilverForBill".Translate() + " "
                                + settlement.Name + ". "
                                + "FCConfiscatedTithes".Translate() + "."
                                + " " + "FCUnpaidTitheEffect".Translate();
                            settlement.GainUnrestWithReason(new Message(messageString, MessageTypeDefOf.NegativeEvent), FCSettings.billUnpaidUnrestPenalty);
                            settlement.GainHappiness(-FCSettings.billUnpaidHappinessPenalty);
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
                foreach (WorldSettlementFC settlement in latePaidSettlements)
                {
                    settlement.GainUnrest(FCSettings.billLatePaidUnrestPenalty);
                    settlement.GainHappiness(-FCSettings.billLatePaidHappinessPenalty);
                }

                string settlementList = string.Join("\n", latePaidSettlements.Select(s => "  - " + s.Name));
                Find.LetterStack.ReceiveLetter(
                    "FCLateBillAutoPaidLabel".Translate(),
                    "FCLateBillAutoPaidDesc".Translate(latePaidSettlements.Count, settlementList),
                    LetterDefOf.NegativeEvent);
            }
        }
    }
}