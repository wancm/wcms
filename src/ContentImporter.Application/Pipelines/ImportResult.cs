namespace ContentImporter.Application.Pipelines
{
    public sealed record ImportError(string CorrelationId, string Message);

    public sealed record ImportResult(
        int Imported,
        int Failed,
        IReadOnlyList<ImportError> Errors,
        TimeSpan Duration);
}
