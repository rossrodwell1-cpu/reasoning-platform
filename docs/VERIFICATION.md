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

## Theme refinement verification — 10 November 2026

- Complete solution restore and Debug build passed (0 build warnings, 0 errors). The existing dependencies on merged `main` required additional packages; restore used the same temporary verified-HTTPS loopback adapter and workspace cache described above. No project dependencies or NuGet configuration were changed.
- Existing domain/PostgreSQL tests: **13 passed, 0 failed**.
- Existing browser acceptance workflow passed on the updated application at desktop (1440px), tablet (820px) and mobile (390px), including practice promotion, refresh persistence, LSAT switching, fixed diagnostic and results.
- Theme browser checks passed on the published application: **3 themes × 7 routes × 4 widths** (1440, 820, 390, 320px), without document horizontal overflow. Routes include overview, general/LSAT/UCAT practice, skill library, content studio and not-found.
- Theme checks also passed for reload persistence, system preference changes, explicit overrides, early startup before Blazor, corrupt/blocked storage fallback, mobile menu keyboard operation, radio selection, feedback labels, local fonts, reduced motion and forced-colour radio rendering.
- Representative palette pairs exceed 4.5:1 for normal text and 3:1 for control outlines. The lowest tested text pairing is Warm Study muted text on its subtle surface (5.37:1); selected indicators use a high-contrast centre dot and an explicit checked state. This is a token-level check, not a full rendered-page WCAG audit.
- Release publish passed. Desktop overview and mobile practice/feedback screenshots were visually inspected. Evidence is generated in ignored `artifacts/` files; it is not application source.

Known runtime/tooling limitations: EF Core logs its existing multiple-collection query warning during the dashboard workflow. Local development Data Protection keys are persisted without encryption; production key management remains outside this UI task. There were no compiler warnings. Chromium was tested; Safari/Firefox, screen readers and physical touch devices were not tested. Viewport reflow approximates browser zoom but is not a native browser-zoom test.

See [UI-THEMES.md](UI-THEMES.md) for implementation details and repeatable test commands. No backend, learning policy, assessment rule or database-schema changes were made.

Final published-build check: theme switching, correct-answer feedback in all three themes, locally loaded fonts, restoring the saved preference into a new browser context, and 200% text enlargement at 390px all passed, with no page or console errors. The enlarged-text check found and verified a fix for mobile header wrapping. The final complete solution build again passed with 0 warnings and 0 errors.
