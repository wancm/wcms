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
