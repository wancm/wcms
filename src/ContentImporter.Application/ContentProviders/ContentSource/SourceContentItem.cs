namespace ContentImporter.Application.ContentProviders.ContentSource
{
    public record SourceContentItem(
        string ProviderCode)
    {
        private static readonly IReadOnlyDictionary<string, string> NoFields =
            new Dictionary<string, string>();

        /// <summary>
        /// A unique identifier for the content item, used for correlation and tracking purposes.
        /// </summary>
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString();

        public string Content { get; init; } = string.Empty;

        /// <summary>Arbitrary extra fields carried over from the source system. [if applicable]</summary>
        public IReadOnlyDictionary<string, string> Fields { get; init; } = NoFields;
    }
}
