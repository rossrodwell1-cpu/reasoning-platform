# ReasoningLab

A .NET 10 reasoning platform with a Blazor WebAssembly frontend, ASP.NET Core API and PostgreSQL/EF Core persistence. This first increment implements a real, persistent practice workflow; it is not a complete commercial release.

## Run locally

Prerequisites: .NET 10 SDK and PostgreSQL 17 or 18. Create a dedicated database and role using your usual PostgreSQL tools. Do not point development migrations at an existing production database.

```powershell
dotnet restore
$env:ConnectionStrings__ReasoningLab = 'Host=localhost;Database=reasoninglab;Username=reasoning;Password=YOUR_LOCAL_PASSWORD'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/ReasoningLab.Server --no-launch-profile --urls http://localhost:5180
```

Open **http://localhost:5180**. The server serves the WASM client and API together. Development startup applies EF migrations and seeds the original starter content once. Use the same database and Data Protection keys across restarts to retain learner profiles. HTTPS is required for a public deployment; the cookie is secure when served over HTTPS. No public deployment is configured.

For content editing, set `AdminToken` to a long random secret in your environment or .NET user secrets, restart, then visit `/admin`. Never commit that token. The editor appends question versions and maps them to a skill; existing attempts remain attached to the original version. This is deliberately small authoring infrastructure, not a production role system. Only deduction is selectable by the first-slice practice policy; other skill content can be stored but needs curriculum selection in the next increment.

## Try the vertical slice

1. Open Everyday reasoning and answer three introductory questions correctly.
2. Observe the level increase and the more demanding question. Reload: the assigned question and profile persist.
3. Switch to LSAT or UCAT preparation: the shared deduction level carries across.
4. In LSAT, start the three-minute short diagnostic. The three-item form is fixed regardless of mastery. Answers are final; no hints or immediate feedback appear during it.
5. Finish or let the server deadline expire. The dashboard shows raw results separately from practice mastery and recommendations.

The 19 original starter questions are introductory content awaiting subject-matter review. The short diagnostic is **not a full mock exam**. No official scores, endorsements or predictions are claimed. Full mocks remain disabled until blueprint rules and sufficient content are verified.

## Projects

| Project | Responsibility |
| --- | --- |
| `src/ReasoningLab.Core` | API contracts and pure learning policy; no infrastructure dependency |
| `src/ReasoningLab.Server` | HTTP endpoints, application service, EF model/migrations, seed content |
| `src/ReasoningLab.Client` | Blazor WebAssembly pages, reusable question/skill components, responsive CSS |
| `tests/ReasoningLab.Tests` | xUnit domain and HTTP integration tests against real PostgreSQL |
| `tests/ReasoningLab.BrowserTests` | Playwright C# acceptance workflow at desktop/tablet/mobile widths |

Start reading at `Core/Contracts.cs`, then `Server/Application/LearningService.cs`, then `Client/Pages/Practice.razor`. No service bus, cache server, containers, external UI library or separate frontend toolchain is required.

## Verify

Integration tests create and drop a uniquely named test database. The test role needs `CREATEDB`; the application role does not. Set an explicit test connection for your installation:

```powershell
$env:TEST_POSTGRES = 'Host=localhost;Database=postgres;Username=reasoning_test;Password=YOUR_TEST_PASSWORD'
dotnet test tests/ReasoningLab.Tests
dotnet build tests/ReasoningLab.BrowserTests
pwsh tests/ReasoningLab.BrowserTests/bin/Debug/net10.0/playwright.ps1 install chromium
# Keep the application running in a second terminal.
$env:BROWSER_TEST_URL = 'http://localhost:5180'
dotnet run --project tests/ReasoningLab.BrowserTests
dotnet publish src/ReasoningLab.Server -c Release -o artifacts/publish
```

Alternatively set `BROWSER_EXECUTABLE` to an existing Chrome/Edge executable. Browser tests create isolated browser profiles, exercise the UI, check page errors and horizontal overflow, and write screenshots under `artifacts/`. CI runs unit/integration tests, browser workflows and a release publish on a PostgreSQL service.

Schema changes:

```powershell
dotnet tool restore
dotnet ef migrations add DescriptiveName --project src/ReasoningLab.Server
```

For a non-development environment, apply migrations and seed explicitly with `dotnet run --project src/ReasoningLab.Server -- --migrate` before serving traffic. Production startup never automatically migrates. Configure persistent Data Protection storage and appropriate database backup/recovery before deployment.

## Boundaries and next work

Anonymous profiles are browser-bound, with no recovery or cross-device account yet. Mastery is an explainable, uncalibrated heuristic (three distinct successes at the current level); practice accuracy is not an exam score. Review repetitions update evidence but cannot promote a level. The starter selector focuses on deduction and loads its small bank in memory; indexed/paginated selection and prerequisite enforcement for additional skills belong to the curriculum increment.

Full curriculum, full LSAT/UCAT mocks, accommodations, complete content/relationship administration, accounts/roles, payments, achievements, leaderboards and production operations remain incomplete. These are tracked alongside unresolved business decisions in [the implementation plan](docs/IMPLEMENTATION.md). See [official exam research and verification limits](docs/EXAM-SOURCES.md), [API notes](docs/API.md), and [verification report](docs/VERIFICATION.md).
