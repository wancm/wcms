using System.Reflection;

namespace ContentImporter.Tests;

/// <summary>
/// Guards the layering described in docs/adr/0001-four-projects-three-tiers.md, but only where
/// the build cannot guard it already.
/// </summary>
/// <remarks>
/// <para>
/// The obvious tests — "Application must not reference Infrastructure" and friends — turn out to
/// be unfalsifiable here. Every forbidden edge is the reverse of an edge the design requires, so
/// adding one makes the project graph circular and MSBuild fails the restore with MSB4006 before
/// any test runs. Asserting them in xunit would look diligent while being incapable of failing.
/// </para>
/// <para>
/// What MSBuild will happily allow is a layer reaching for the wrong <em>framework</em>. Nothing
/// stops someone parsing JSON inside Domain or opening a file inside Application, and doing so
/// would quietly move a format concern out of Infrastructure where it belongs. That is what these
/// tests check.
/// </para>
/// <para>
/// The check reads compiled assemblies, and the C# compiler omits references that no type
/// actually uses. So this catches use, not declaration — which is the point, because an unused
/// reference does no harm.
/// </para>
/// </remarks>
public sealed class ArchitectureTests
{
    private const string Domain = "ContentImporter.Domain";
    private const string Application = "ContentImporter.Application";

    /// <summary>
    /// Serialization, transport and storage are Infrastructure's job. A layer that reaches for
    /// one of these has taken on a concern that belongs behind a port.
    /// </summary>
    private static readonly string[] InfrastructureConcerns =
    [
        "System.Text.Json",
        "System.Xml",
        "System.Xml.ReaderWriter",
        "System.Private.Xml",
        "System.Net.Http",
        "System.Data.Common",
    ];

    [Theory]
    [InlineData(Domain)]
    [InlineData(Application)]
    public void Layer_is_free_of_infrastructure_concerns(string layer)
    {
        string[] referenced = ReferencedAssemblyNamesOf(layer);

        string[] violations = [.. referenced.Intersect(InfrastructureConcerns)];

        Assert.True(
            violations.Length == 0,
            $"{layer} references {string.Join(", ", violations)}. Serialization, transport and " +
            "storage belong in Infrastructure, behind a port that Application declares.");
    }

    /// <summary>
    /// Domain sits at the centre and knows nothing about the layers built on top of it. MSBuild
    /// also catches this today, but it stops doing so the moment a fifth project appears that
    /// Domain does not already sit beneath.
    /// </summary>
    [Fact]
    public void Domain_depends_on_no_other_layer()
    {
        string[] siblings = [.. ReferencedAssemblyNamesOf(Domain)
            .Where(name => name.StartsWith("ContentImporter.", StringComparison.Ordinal))];

        Assert.Empty(siblings);
    }

    private static string[] ReferencedAssemblyNamesOf(string assemblyName) =>
        [.. Assembly.Load(assemblyName)
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)];
}
