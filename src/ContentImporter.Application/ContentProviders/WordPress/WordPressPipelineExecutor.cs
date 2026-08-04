using ContentImporter.Application.ContentProviders.ContentSource;
using ContentImporter.Application.ContentProviders.Dtos;
using ContentImporter.Domain.Entities;

namespace ContentImporter.Application.ContentProviders.WordPress
{
    internal class WordPressPipelineExecutor : IPipelineExecutor
    {
        public string ProviderCode => "WordPress";

        private WordPressDto? dto;

        // '=' not '=>'. An expression-bodied property would construct a new serializer on every
        // read of the field, silently, once per item.
        private readonly WordPressDtoSerializer serializer = new();

        private readonly WordPressDtoValidator validator = new WordPressDtoValidator();

        private readonly WordPressDtoEntityMapper mapper = new WordPressDtoEntityMapper();

        public async Task DeserializeDtoAsync(SourceContentItem sourceContentItem)
        {
            dto = await serializer.SerializeAsync(sourceContentItem);
        }

        public async Task<ValidationOutcome> ValidateDtoAsync()
        {
            // DeserializeDtoAsync has to run first - there is nothing to validate otherwise.
            return dto is null
                ? new ValidationOutcome(["nothing to validate: DeserializeDtoAsync did not run"])
                : await validator.ValidateAsync(dto);
        }

        public async Task<ContentItem> DtoMapEntityAsync()
        {
            // DeserializeDtoAsync and ValidateDtoAsync have to run first.
            if (dto is null)
            {
                throw new InvalidOperationException("DTO is not initialized.");
            }

            return await mapper.MapAsync(dto);
        }

    }
}
