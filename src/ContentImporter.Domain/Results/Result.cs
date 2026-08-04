namespace ContentImporter.Domain.Results;

/// <summary>
/// Why the domain refused to build something.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Code"/> is the stable, machine-readable half — safe to group by in a log query or
/// assert on in a test. <see cref="Message"/> is for the person reading the import report and may
/// be reworded freely.
/// </para>
/// <para>
/// Deliberately carries no reference to the offending value. Errors are declared as static
/// readonly fields on the type that raises them, so a malformed record costs no allocation at
/// all. The context that makes an error actionable — which record, which Export — is attached
/// further out by the pipeline, which already holds the record's correlation id.
/// </para>
/// </remarks>
public readonly record struct DomainError(string Code, string Message)
{
    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>
/// The outcome of an operation that is expected to fail for some inputs: either a value, or a
/// <see cref="DomainError"/> explaining the refusal.
/// </summary>
/// <remarks>
/// <para>
/// Domain objects are built through static <c>Create</c> methods returning this type rather than
/// through constructors that throw. A constructor cannot return a failure, so enforcing invariants
/// there forces an exception — and malformed records are ordinary here, not exceptional. An Export
/// of a million records with five percent bad content would mean fifty thousand throws, each one
/// capturing a stack trace that gets discarded. This is a struct for the same reason: the common
/// failure path allocates nothing.
/// </para>
/// <para>
/// The type still cannot exist in an invalid state — the constructors stay private, so
/// <c>Create</c> is the only way in.
/// </para>
/// </remarks>
/// <typeparam name="T">The value produced on success.</typeparam>
public readonly struct Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        Error = default;
        IsSuccess = true;
    }

    private Result(DomainError error)
    {
        _value = default;
        Error = error;
        IsSuccess = false;
    }

    /// <summary>Whether the operation produced a value.</summary>
    public bool IsSuccess { get; }

    /// <summary>The refusal. Meaningful only when <see cref="IsSuccess"/> is false.</summary>
    public DomainError Error { get; }

    /// <summary>
    /// The value produced. Throws when the result is a failure — that is a bug in the caller for
    /// not checking <see cref="IsSuccess"/>, not a data problem, so an exception is the right
    /// response.
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Cannot read the value of a failed result. The failure was: {Error}");

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(DomainError error) => new(error);
}
