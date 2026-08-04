using ContentImporter.Domain.Results;

namespace ContentImporter.Tests.Domain;

public sealed class ResultTests
{
    private static readonly DomainError AnError = new("test.failed", "Something was wrong.");

    [Fact]
    public void A_success_carries_its_value()
    {
        var result = Result<string>.Success("content");

        Assert.True(result.IsSuccess);
        Assert.Equal("content", result.Value);
    }

    [Fact]
    public void A_failure_carries_its_error()
    {
        var result = Result<string>.Failure(AnError);

        Assert.False(result.IsSuccess);
        Assert.Equal(AnError, result.Error);
    }

    /// <summary>
    /// Reading the value of a failure is a bug in the caller, not a bad record, so this is the one
    /// place the domain does throw.
    /// </summary>
    [Fact]
    public void Reading_the_value_of_a_failure_throws()
    {
        var result = Result<string>.Failure(AnError);

        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("test.failed", exception.Message, StringComparison.Ordinal);
    }
}
