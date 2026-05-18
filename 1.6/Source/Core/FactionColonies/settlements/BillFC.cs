using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace FactionColonies
{
    public enum BillPenaltyStat
    {
        Happiness = 0,
        Unrest = 1,
        Loyalty = 2,
        Prosperity = 3,
    }

    public class BillStatPenalty : IExposable
    {
        public BillPenaltyStat stat;
        public double amount;

        public BillStatPenalty() { }

        public BillStatPenalty(BillPenaltyStat stat, double amount)
        {
            this.stat = stat;
            this.amount = amount;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref stat, "stat");
            Scribe_Values.Look(ref amount, "amount", 0.0);
        }
    }

    public class BillFC : ILoadReferenceable, IExposable
    {
        /* Default lifespan for tax bills (5 in-game days). Surfaced as a const so
         * squad deployment bill creation can override it via FCSettings.deploymentBillLifespan_days. */
        public const int DefaultLifespanTicks = GenDate.TicksPerDay * 5;

        //internal variables
        public int loadID;
        public int dueTick;
        public string label;
        public List<BillStatPenalty> unpaidPenalties = new List<BillStatPenalty>();
        public List<BillStatPenalty> latePaidPenalties = new List<BillStatPenalty>();

        //ref
        public WorldSettlementFC settlement;
        public TaxesFC taxes;


        public void ExposeData()
        {
            Scribe_Values.Look(ref loadID, "loadID", -1);
            Scribe_Values.Look(ref dueTick, "dueTick", -1);
            Scribe_Values.Look(ref label, "label");
            Scribe_Collections.Look(ref unpaidPenalties, "unpaidPenalties", LookMode.Deep);
            Scribe_Collections.Look(ref latePaidPenalties, "latePaidPenalties", LookMode.Deep);

            Scribe_References.Look(ref settlement, "settlement");
            Scribe_Deep.Look(ref taxes, "taxes");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (unpaidPenalties is null) unpaidPenalties = new List<BillStatPenalty>();
                if (latePaidPenalties is null) latePaidPenalties = new List<BillStatPenalty>();
            }
        }

        public string GetUniqueLoadID()
        {
            return "Bill_" + loadID;
        }

        public BillFC()
        {

        }

        public BillFC(WorldSettlementFC settlement)
            : this(settlement, DefaultLifespanTicks) { }

        public BillFC(WorldSettlementFC settlement, int lifespanTicks)
        {
            SetUniqueLoadID();
            this.settlement = settlement;
            dueTick = Find.TickManager.TicksGame + lifespanTicks;
            taxes = new TaxesFC(this);
        }

        public void SetUniqueLoadID()
        {
            FactionFC comp = FactionCache.FactionComp;
            if (comp is object)
            {
                loadID = comp.taxLedger.NextBillId();
            }
            else
            {
                loadID = Rand.Int;
                LogUtil.Error($"BillFC.SetUniqueLoadID: FactionComp is null. Assigned fallback loadID {loadID}.");
            }
        }

        /*-*-*- Penalty helpers -*-*-*/

        /* Direct: store the raw amount as a positive magnitude. Polarity is applied
         * at penalty time by BillUtility.ApplyPenalties. */
        public void AddUnpaidPenalty(BillPenaltyStat stat, double amount)
        {
            unpaidPenalties.Add(new BillStatPenalty(stat, amount));
        }

        public void AddLatePaidPenalty(BillPenaltyStat stat, double amount)
        {
            latePaidPenalties.Add(new BillStatPenalty(stat, amount));
        }

        /* Scaled: linear in (silver / settlement income), computed at construction time
         * and frozen on the bill. Caller MUST set taxes.silverAmount before calling. */
        public void AddUnpaidPenaltyScaled(BillPenaltyStat stat, double k)
        {
            unpaidPenalties.Add(new BillStatPenalty(stat, ScalePenalty(k)));
        }

        public void AddLatePaidPenaltyScaled(BillPenaltyStat stat, double k)
        {
            latePaidPenalties.Add(new BillStatPenalty(stat, ScalePenalty(k)));
        }

        private double ScalePenalty(double k)
        {
            double income = settlement?.totalIncome ?? 0;
            return k * Math.Abs(taxes.silverAmount) / Math.Max(1.0, income);
        }

        /*-*-*- Resolution -*-*-*/

        public bool Resolve()
        {
            FactionFC factionfc = FactionCache.FactionComp;
            if (AttemptResolve())
            {
                return true;
            }

            if (settlement != null)
            {
                BillUtility.ApplyPenaltiesWithMessage(settlement, this);
            }
            else
            {
                LogUtil.Warning("BillFC.Resolve: bill has null settlement (loadID=" + loadID + "). Skipping penalty.");
            }
            factionfc.taxLedger.RemoveBill(this);
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
