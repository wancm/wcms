namespace ContentImporter.Application.ContentProviders
{
    internal interface IDtoValidator<T>
    {
        Task<bool> ValidateAsync(T obj);
    }
}
