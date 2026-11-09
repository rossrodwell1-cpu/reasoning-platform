# Verification — 9 November 2026

- .NET 10 build: passed.
- xUnit domain and PostgreSQL integration suite: **13 passed, 0 failed** against an isolated PostgreSQL 18 database. Includes the complete practice-to-diagnostic workflow, learner isolation, duplicate/concurrent submissions, late submissions, spaced review, delayed retry safety and question versioning.
- Release publish with trimming: passed.
- Published application, Production environment: the C# Playwright workflow passed at **1440×1100, 820×1180 and 390×844** using installed Chrome. Verified correct answers, level advancement, reload persistence, switching to LSAT, completing its fixed diagnostic, persisted results and no document horizontal overflow.
- Desktop, tablet and mobile screenshots were inspected. No full accessibility audit has been performed.

## Known tooling issues and limits

The Windows sandbox could not restore NuGet packages through its normal TLS credential path. Local verification used a temporary loopback adapter fetching NuGet over verified HTTPS; that workaround and all caches remain outside the repository. Normal `dotnet restore` is documented for a standard developer environment. No private package source or credentials are required.

The .NET 10 SDK left fingerprint placeholders unresolved when publishing the hosted WASM application. The existing client project disables asset fingerprinting as a compatibility setting, with the [upstream issue](https://github.com/dotnet/aspnetcore/issues/64543) referenced in the project file. The resulting published application was tested successfully. Revisit this setting when the SDK fix is verified.

Windows event-log permissions and test Data Protection storage initially interrupted tests; the implementation now uses console logging and isolated ephemeral test keys. All final integration tests pass.

GitHub Actions is configured; its remote status is reported on the pull request. Local passing tests do not imply that remote CI has completed.

This is a first vertical slice: full mock examinations, a complete curriculum, validated mastery/scoring, production accounts/roles and commercial operations are not implemented. See IMPLEMENTATION.md for scope and unresolved requirements. No public deployment or merge has been performed.
