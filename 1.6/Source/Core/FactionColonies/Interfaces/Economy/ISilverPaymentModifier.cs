namespace FactionColonies
{
    /// <summary>
    /// Defines an interface to let classes intercept and modify silver payments before they are processed.
    /// </summary>
    public interface ISilverPaymentModifier
    {
        /// <summary>
        /// Called before silver is consumed. Modify context.Amount to change how much is charged.
        /// Use the context's Reason and Settlement fields to determine what the payment is for.
        /// </summary>
        void ModifyPayment(SilverPaymentContext context);
    }
}
