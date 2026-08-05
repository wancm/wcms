using ContentImporter.Domain.Entities;

namespace ContentImporter.Application.ContentProviders
{
    internal interface IDtoEntityMapper<T>
    {
        Task<ContentItem> MapAsync(T dto);
    }
}
