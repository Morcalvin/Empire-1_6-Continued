using System.Collections.Generic;

namespace FactionColonies
{
    public class SilverPaymentContext
    {
        /// <summary>The amount of silver to charge. Modifiers can change this.</summary>
        public int Amount;
        /// <summary>Why silver is being charged. Match against PaymentUtil.Reason_* constants.</summary>
        public string Reason;
        /// <summary>The settlement this payment is associated with, if any.</summary>
        public WorldSettlementFC Settlement;

        public SilverPaymentContext(int amount, string reason, WorldSettlementFC settlement = null)
        {
            Amount = amount;
            Reason = reason;
            Settlement = settlement;
        }
    }

    public static class SilverPaymentRegistry
    {
        private static readonly RegistryList<ISilverPaymentModifier> _list = new RegistryList<ISilverPaymentModifier>();

        internal static void Register(ISilverPaymentModifier modifier) => _list.Register(modifier);
        internal static void Unregister(ISilverPaymentModifier modifier) => _list.Unregister(modifier);
        internal static void ClearAll() => _list.ClearAll();
        public static IReadOnlyList<ISilverPaymentModifier> Modifiers => _list.Items;

        public static SilverPaymentContext InvokeModifiers(SilverPaymentContext context)
        {
            RegistryDispatch.Each(_list.Items, m => m.ModifyPayment(context), nameof(ISilverPaymentModifier.ModifyPayment));
            return context;
        }
    }
}
