using ContentImporter.Application.ContentProviders.ContentSource;
using ContentImporter.Domain.Entities;

namespace ContentImporter.Application.ContentProviders
{
    public interface IPipelineExecutor
    {
        string ProviderCode { get; }

        /// <summary>
        /// To deserialize the source content item into a DTO that can be used for normalization.
        /// </summary>
        Task DeserializeDtoAsync(SourceContentItem sourceContentItem);

        /// <summary>
        /// To validate the DTO after deserialization.
        /// This method should be called after DeserializeDtoAsync to ensure that the DTO is valid before proceeding with normalization.
        /// </summary>
        Task<bool> ValidateDtoAsync();

        /// <summary>
        /// To map the DTO to a ContentItem entity that can be used for further processing or storage.
        /// </summary>
        Task<ContentItem> DtoMapEntityAsync();
    }
}
