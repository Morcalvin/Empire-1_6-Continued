namespace FactionColonies
{
    /// <summary>
    /// One-shot migration: drains pre-refactor military state (on the comp, on FCEvent military
    /// fields, on squad's <c>isDeployed</c>/<c>timeDeployed</c>) into the new
    /// <see cref="MilitaryOperationManager"/> model.
    /// <para>Called from <c>FactionFC.PostLoadInit</c> when the manager is empty but legacy state
    /// is present in the loaded save.</para>
    /// <para>Phase 1: empty stub. Phase 3 fills the migration logic.</para>
    /// </summary>
    public static class MilitaryMigrationUtil
    {
        /// <summary>Returns true if any pre-refactor military state was loaded that has not yet
        /// been drained into the manager. Phase 3 implements; Phase 1 returns false so the
        /// migration path is dormant.</summary>
        public static bool AnyLegacyStatePresent(FactionFC faction) => false;

        /// <summary>Walks loaded legacy state and reconstructs <see cref="MilitaryOperation"/>s
        /// in the manager. Phase 3 implements.</summary>
        public static void Migrate(FactionFC faction)
        {
            // Phase 3 fills in.
        }
    }
}
