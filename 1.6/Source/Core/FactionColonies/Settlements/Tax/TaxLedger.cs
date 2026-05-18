using System;
using System.Collections.Generic;
using FactionColonies.util;
using RimWorld;
using Verse;

namespace FactionColonies
{
    /* Owns the faction's tax/billing state and the tax-cycle orchestration.
     * Lives on FactionFC.taxLedger.
     *
     * State persisted via nested <taxLedger> Scribe element. Legacy flat fields
     * (Bills, OldBills, taxTimeDue, autoResolveBills, allowLatePayments, nextTaxID,
     * nextBillID) on old saves are migrated by FactionFC.ExposeData's migration
     * shim, which calls SeedFromLegacy() once during ResolvingCrossRefs. */
    public class TaxLedger : IExposable
    {
        /* Owned state. UI checkbox widgets pass these by ref (autoResolve,
         * allowLatePayments) — that's why they're public fields, not properties. */
        internal List<BillFC> bills = new List<BillFC>();
        internal List<BillFC> oldBills = new List<BillFC>();
        public bool autoResolve;
        public bool allowLatePayments = true;
        public int nextTaxDueTick = Find.TickManager.TicksGame;
        internal int nextTaxId = 1;
        internal int nextBillId = 1;

        /* Read-only facade for external callers. */
        public IReadOnlyList<BillFC> Bills => bills;
        public IReadOnlyList<BillFC> OldBills => oldBills;

        /* Mutation */
        public void AddBill(BillFC bill)
        {
            if (bill is null) return;
            bills.Add(bill);
        }

        public bool RemoveBill(BillFC bill)
        {
            if (bill is null) return false;
            return bills.Remove(bill);
        }

        public int RemoveBillsWhere(Predicate<BillFC> match)
        {
            if (match is null) return 0;
            int removed = 0;
            for (int i = bills.Count - 1; i >= 0; i--)
            {
                if (!match(bills[i])) continue;
                bills.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        public void ArchiveResolved(BillFC bill)
        {
            if (bill is null) return;
            bills.Remove(bill);
            oldBills.Add(bill);
        }

        public void ClearAllBills() => bills.Clear();
        public void ClearOldBills() => oldBills.Clear();

        /* ID generators */
        public int NextTaxId() => ++nextTaxId;
        public int NextBillId() => ++nextBillId;

        public void Reschedule(int ticksFromNow) =>
            nextTaxDueTick = Find.TickManager.TicksGame + ticksFromNow;

        /* Tax cycle. Called from FactionFC.WorldComponentTick. */
        public void TaxTick(FactionFC faction, Faction colonyFaction)
        {
            if (faction is null || colonyFaction is null) return;
            if (Find.TickManager.TicksGame < nextTaxDueTick) return;

            AddTax(faction);
            nextTaxDueTick = Find.TickManager.TicksGame + FCSettings.timeBetweenTaxes;

            if (autoResolve)
                PaymentUtil.AutoresolveBills(bills);

            /* Rebuild caravan trader kinds to reflect current worker assignments. */
            faction.RebuildCaravanTraderKinds();
        }

        public void AddTax(FactionFC faction)
        {
            if (faction is null) return;

            LogUtil.Message($"AddTax at tick {Find.TickManager.TicksGame}: settlements={faction.settlements.Count}, timeBetweenTaxes={FCSettings.timeBetweenTaxes}");
            TaxTickRegistry.InvokePreTaxResolution(faction);

            foreach (ResourcePool pool in faction.resourcePools)
            {
                if (pool.resource.PoolResourceResetsAtTaxTime())
                    pool.pool = 0;
            }

            if (faction.settlements.Count != 0)
            {
                foreach (WorldSettlementFC settlement in faction.settlements)
                {
                    faction.AddExperienceToFactionLevel(2f);

                    List<Thing> list = settlement.CreateTax(out int silverAmount);
                    List<ResourcePool> billResourcePools = settlement.CreateResourcePools();

                    BillFC bill = new BillFC(settlement);
                    bill.label = "FCBillKindTax".Translate();
                    bill.taxes.resourcePools = billResourcePools;
                    bill.taxes.itemTithes.AddRange(list);
                    bill.taxes.silverAmount = silverAmount;
                    bill.AddUnpaidPenalty(BillPenaltyStat.Unrest, 10);
                    bill.AddUnpaidPenalty(BillPenaltyStat.Happiness, 10);
                    bill.AddLatePaidPenalty(BillPenaltyStat.Unrest, 4);
                    bill.AddLatePaidPenalty(BillPenaltyStat.Happiness, 4);

                    bills.Add(bill);

                    TextUtil.GetTownTitle(settlement);
                    TaxTickPrisoner(settlement);
                    faction.ForEachBehavior(b => b.OnTaxCollected(faction, settlement));
                }

                Find.LetterStack.ReceiveLetter("FCTaxesBilledShort".Translate(), "FCTaxesBilledDesc".Translate(),
                    LetterDefOf.PositiveEvent);
                faction.DirtyFactionProfitCache();
            }
            else
            {
                Messages.Message("FCNoSettlementsToTax".Translate(), MessageTypeDefOf.NeutralEvent);
            }

            /* Deduct edict upkeep */
            int edictUpkeep = faction.GetEdictUpkeep();
            if (edictUpkeep > 0)
            {
                if (PaymentUtil.GetSilver() >= edictUpkeep)
                {
                    PaymentUtil.PaySilver(edictUpkeep, "EdictUpkeep");
                }
                else
                {
                    faction.RevokeAllEdicts();
                    Messages.Message("FCEdictUpkeepUnpaid".Translate(), MessageTypeDefOf.NegativeEvent);
                }
            }

            TaxTickRegistry.InvokePostTaxResolution(faction);
        }

        public void TaxTickPrisoner(WorldSettlementFC settlement)
        {
            if (settlement is null) return;
            int i = 0;
            while (i < settlement.prisonerList.Count)
            {
                FCPrisoner prisoner = settlement.prisonerList[i];
                bool dead = false;

                switch (prisoner.workload)
                {
                    case FCWorkLoad.Heavy:
                        if (prisoner.AdjustHealth(-20)) dead = true;
                        break;
                    case FCWorkLoad.Medium:
                        if (prisoner.AdjustHealth(-10)) dead = true;
                        break;
                    case FCWorkLoad.Light:
                        if (prisoner.AdjustHealth(4)) dead = true;
                        break;
                }

                /* Only increment if the prisoner hasn't died.
                 * If they *did* die, then AdjustHealth() will have removed them from
                 * the list already. So if we increment, then we'll actually skip the
                 * next prisoner. */
                if (!dead) i++;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref nextTaxDueTick, "nextTaxDueTick", Find.TickManager.TicksGame);
            Scribe_Values.Look(ref autoResolve, "autoResolve");
            Scribe_Values.Look(ref allowLatePayments, "allowLatePayments", true);
            Scribe_Values.Look(ref nextTaxId, "nextTaxId", 1);
            Scribe_Values.Look(ref nextBillId, "nextBillId", 1);
            Scribe_Collections.Look(ref bills, "bills", LookMode.Deep);
            Scribe_Collections.Look(ref oldBills, "oldBills", LookMode.Deep);
            if (bills is null) bills = new List<BillFC>();
            if (oldBills is null) oldBills = new List<BillFC>();
        }

        /* True iff the ledger is at first-construction defaults. Used by the
         * FactionFC migration shim to decide whether to seed from legacy flat fields. */
        public bool IsEmpty =>
            bills.Count == 0 && oldBills.Count == 0
            && nextTaxId == 1 && nextBillId == 1;

        /* One-shot seed from FactionFC's legacy flat scribe fields. Called from
         * FactionFC.ExposeData during ResolvingCrossRefs when an old-format save
         * is loaded. Any field whose legacy value is at its sentinel (-1 for ints,
         * default for bools) is left at the ledger's default. */
        public void SeedFromLegacy(List<BillFC> legacyBills, List<BillFC> legacyOldBills,
            bool legacyAutoResolve, bool legacyAllowLate,
            int legacyTaxTimeDue, int legacyNextTaxId, int legacyNextBillId)
        {
            if (legacyBills != null) bills = legacyBills;
            if (legacyOldBills != null) oldBills = legacyOldBills;
            autoResolve = legacyAutoResolve;
            allowLatePayments = legacyAllowLate;
            if (legacyTaxTimeDue != -1) nextTaxDueTick = legacyTaxTimeDue;
            if (legacyNextTaxId != -1) nextTaxId = legacyNextTaxId;
            if (legacyNextBillId != -1) nextBillId = legacyNextBillId;
        }
    }
}
