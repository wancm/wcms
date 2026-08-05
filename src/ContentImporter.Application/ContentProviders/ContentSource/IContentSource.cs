namespace ContentImporter.Application.ContentProviders.ContentSource
{
    public interface IContentSource
    {
        IAsyncEnumerable<SourceContentItem> ReadAsync(CancellationToken cancellationToken = default);
    }
}
