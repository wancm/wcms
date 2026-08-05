using ContentImporter.Application.ContentProviders.ContentSource;

namespace ContentImporter.Application.ContentProviders
{
    internal interface IDtoSerializer<T>
    {
        Task<T> SerializeAsync(SourceContentItem sourceContentItem);
    }
}
