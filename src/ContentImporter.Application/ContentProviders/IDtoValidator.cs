namespace ContentImporter.Application.ContentProviders
{
    internal interface IDtoValidator<T>
    {
        Task<ValidationOutcome> ValidateAsync(T obj);
    }
}
