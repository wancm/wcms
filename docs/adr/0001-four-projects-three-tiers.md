# Four projects, three tiers, dependencies inverted

The brief asks for a three-tier application and for SOLID, which pull against each other:
classic three-tier points Business at Data Access, and that is exactly what the Dependency
Inversion Principle forbids. We resolved it by splitting the business tier into
`ContentImporter.Domain` (no project references) and `ContentImporter.Application` (references
Domain only), and having `ContentImporter.Infrastructure` reference both. The tiers are still
three — Console is presentation, Domain plus Application is business, Infrastructure is data
access — but every dependency arrow now points inward, and `Application.csproj` provably
contains no reference to Infrastructure.

## Consequences

- Swapping the store or the message transport touches one class in Infrastructure and one line
  in `Program.cs`. Domain and Application do not recompile.
- Four assemblies is more ceremony than a demo strictly needs. The defence is that the
  constraint is machine-checked rather than a naming convention, and a reflection-based
  architecture test in `ContentImporter.Tests` fails the build if a careless `ProjectReference`
  ever inverts an arrow.
- Ports live in `Application/Interfaces`, not in Domain, because they describe what the
  application needs done, not what content *is*.
- **Source-provider adapters live in Application, not Infrastructure.** Reading a customer's
  export is what this application is *for*, so `WordPressJsonContentSource`, the wire DTOs and
  their serializers sit in `Application/ContentProviders` and are free to name
  `System.Text.Json`. An adapter forbidden from naming its own wire format is not an adapter —
  it is an interface with the interesting part moved somewhere else. Transport and storage stay
  behind ports, because those are resources the process *connects to*: swapping the store or the
  broker must not recompile Application. `ArchitectureTests` enforces exactly that split, and
  holds Domain to the stricter rule — it may not name a wire format at all.
