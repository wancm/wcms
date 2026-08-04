namespace ContentImporter.Application.ContentProviders
{
    /// <summary>
    /// The result of first-level validation: valid, or the reasons an item was refused.
    /// </summary>
    /// <remarks>
    /// A list rather than a single reason, because the rules run without short-circuiting. An item
    /// missing three things should say all three at once, so one look at the import report is
    /// enough to fix it - not three runs, each revealing the next problem.
    /// </remarks>
    public sealed record ValidationOutcome(IReadOnlyList<string> Failures)
    {
        public static readonly ValidationOutcome Valid = new([]);

        public bool IsValid => Failures.Count == 0;

        /// <summary>Reads as "title is mandatory; status 'archived' is not one WordPress exports".</summary>
        public override string ToString() => string.Join("; ", Failures);
    }
}
