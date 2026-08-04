using System.Reflection;

namespace ContentImporter.Tests;

/// <summary>
/// Guards the solution's layering, but only where the build cannot guard it already.
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
/// stops someone opening a socket or a database connection inside Application, or parsing JSON
/// inside Domain. That is what these tests check — and the two layers are held to different
/// standards on purpose, see below.
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
    /// Transport and storage are Infrastructure's job. A layer that opens a socket or a database
    /// connection has taken on a concern that belongs behind a port.
    /// </summary>
    /// <remarks>
    /// Serialization used to be on this list, and deliberately is not any more. Provider adapters
    /// live in Application by design — reading a customer's export is what the application layer
    /// is *for* — and an adapter that may not name its own wire format is not an adapter.
    /// Transport and storage are different: those are resources the process connects to, and
    /// swapping them must not recompile Application.
    /// </remarks>
    private static readonly string[] TransportAndStorage =
    [
        "System.Net.Http",
        "System.Data.Common",
    ];

    /// <summary>
    /// Domain is held to the stricter rule. It sits at the centre and describes what content
    /// <em>is</em>, so it has no business knowing any wire format at all.
    /// </summary>
    private static readonly string[] DomainConcerns =
    [
        .. TransportAndStorage,
        "System.Text.Json",
        "System.Xml",
        "System.Xml.ReaderWriter",
        "System.Private.Xml",
    ];

    [Fact]
    public void Application_is_free_of_transport_and_storage()
    {
        AssertFreeOf(Application, TransportAndStorage);
    }

    [Fact]
    public void Domain_is_free_of_infrastructure_concerns()
    {
        AssertFreeOf(Domain, DomainConcerns);
    }

    private static void AssertFreeOf(string layer, string[] concerns)
    {
        string[] violations = [.. ReferencedAssemblyNamesOf(layer).Intersect(concerns)];

        Assert.True(
            violations.Length == 0,
            $"{layer} references {string.Join(", ", violations)}, which belongs in Infrastructure " +
            "behind a port that Application declares.");
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
