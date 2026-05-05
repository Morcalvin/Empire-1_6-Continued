using RimWorld;
using Verse;

namespace FactionColonies
{
    public enum BillKindFC
    {
        Tax = 0,
        SquadDeployment = 1,
    }

    public class BillFC : ILoadReferenceable, IExposable
    {
        /* Default lifespan for tax bills (5 in-game days). Surfaced as a const so the
         * SquadDeployment kind can override it via FCSettings.deploymentBillLifespan_days. */
        public const int DefaultLifespanTicks = GenDate.TicksPerDay * 5;

        //internal variables
        public int loadID;
        public int dueTick;
        public BillKindFC kind = BillKindFC.Tax;


        //ref
        public WorldSettlementFC settlement;
        public TaxesFC taxes;



        public void ExposeData()
        {
            Scribe_Values.Look(ref loadID, "loadID", -1);
            Scribe_Values.Look(ref dueTick, "dueTick", -1);
            Scribe_Values.Look(ref kind, "kind", BillKindFC.Tax);


            Scribe_References.Look(ref settlement, "settlement");
            Scribe_Deep.Look(ref taxes, "taxes");

        }

        public string GetUniqueLoadID()
        {
            return "Bill_" + loadID;
        }

        public BillFC()
        {

        }

        public BillFC(WorldSettlementFC settlement)
            : this(settlement, BillKindFC.Tax, DefaultLifespanTicks) { }

        public BillFC(WorldSettlementFC settlement, BillKindFC kind, int lifespanTicks)
        {
            SetUniqueLoadID();
            this.settlement = settlement;
            this.kind = kind;
            dueTick = Find.TickManager.TicksGame + lifespanTicks;
            taxes = new TaxesFC(this);
        }

        public void SetUniqueLoadID()
        {
            FactionFC comp = FactionCache.FactionComp;
            if (comp is object)
            {
                loadID = comp.GetNextBillID();
            }
            else
            {
                loadID = Rand.Int;
                LogUtil.Error($"BillFC.SetUniqueLoadID: FactionComp is null. Assigned fallback loadID {loadID}.");
            }
        }

        public bool Resolve()
        {
            FactionFC factionfc = FactionCache.FactionComp;
            if (AttemptResolve())
            {
                return true;
            }

            if (settlement != null)
            {
                string messageString = "FCNotEnoughSilverForBill".Translate() + " " + settlement.Name + ". " + "FCConfiscatedTithes".Translate() + "." + " " + "FCUnpaidTitheEffect".Translate();
                settlement.GainUnrestWithReason(new Message(messageString, MessageTypeDefOf.NegativeEvent), FCSettings.billUnpaidUnrestPenalty);
                settlement.GainHappiness(-FCSettings.billUnpaidHappinessPenalty);
            }
            else
            {
                LogUtil.Warning("BillFC.Resolve: bill has null settlement (loadID=" + loadID + "). Skipping penalty.");
            }
            factionfc.Bills.Remove(this);
            return false;
        }

        public bool AttemptResolve()
        {
            FactionFC factionfc = FactionCache.FactionComp;
            if (PaymentUtil.GetSilver() >= -1 * taxes.silverAmount || taxes.silverAmount >= 0)
            { //if have enough silver on the current map to pay  & map belongs to player

                FCEventMaker.CreateTaxEvent(this);
                if (taxes.resourcePools.Count > 0)
                {
                    factionfc.AddResourcePools(taxes.resourcePools);
                }

                return true;

            }

            return false;
        }
    }
}
