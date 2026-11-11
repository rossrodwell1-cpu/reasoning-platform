# PWA foundation and deployment

Stage 1 adds installation metadata and safe-area layout support. Stage 2 adds request failure handling and explicit answer recovery. Stage 3 adds public-response revalidation and published-release transition checks. Learning still requires the ASP.NET Core API and PostgreSQL.

## Architecture

- `wwwroot/manifest.webmanifest` identifies the app as **ReasoningLab**, with stable `id`, `start_url` and `scope` set to `/`, and `display: standalone`. Deployment is at the origin root. No orientation is locked.
- `wwwroot/icons/icon.svg` preserves the existing favicon's mark and colours. The PNGs are rasterisations of that vector at 192px, 512px and 180px. The opaque square 512px maskable variant keeps the mark within the central 80% safe circle, allowing the operating system to choose its crop. Rasterise the SVG at the required pixel dimensions when replacing an icon; the 512px regular and maskable outputs intentionally share the same artwork.
- The HTML links the manifest and 180px Apple touch icon, supplies an Apple application title, and enables `viewport-fit=cover`. CSS applies the device's safe-area insets to the page and fixed elements. Zoom remains unrestricted.
- The launch background and manifest theme colour use Crisp Light's canvas (`#f5f7fc`). The existing startup script still applies the saved/system theme and updates the page's theme-colour metadata. OS splash screens and installation chrome may retain the manifest colour.
- No service worker, application-managed offline/API cache, background sync, IndexedDB storage, installation prompt component or new dependency is introduced. Existing .NET 10 fingerprinting workarounds, API and learner identity are unchanged. Stage 3 changes the public HTTP cache policy described below.

## Installation

Use a production HTTPS origin. Browsers treat localhost as a secure context for development, but that does not substitute for testing the deployed HTTPS site.

- **Android:** open the site in a supporting browser and use its Install app/Add to Home screen command.
- **iPhone/iPad:** in Safari, use Share → Add to Home Screen. Where offered, enable Open as Web App. Launch from the new home-screen icon.
- **Windows / desktop Chrome or Edge:** use the browser's install icon or app-install menu command, then launch from the installed shortcut.
- **macOS Safari:** use its Add to Dock web-app workflow where supported.

Browser versions differ in wording, eligibility, promotion and OS integration. Installation must be checked on target devices; no identical prompt or universal support is promised. No app-store publication is involved.

The same application routes and themes are used after installation. Existing browser storage is used for theme preferences and the anonymous learner cookie; continuity across browsers or installation contexts is not guaranteed. Clearing browser data can lose access to the prototype learner profile.

## Connectivity and updates

Offline cold launch can show the browser's network-error page. There is no offline learning or custom offline launch screen. Answer recovery works only while the practice page remains open and the service becomes reachable again.

There is no service-worker cache or activation lifecycle. Public GET/HEAD responses use `Cache-Control: no-cache`, including HTML fallback routes, the manifest, CSS, JavaScript, fonts, icons and framework assets. Browsers may store these resources but must revalidate before reuse; ETag/Last-Modified validators allow unchanged files to return 304. Learner API responses retain `Cache-Control: no-store`, including errors. The existing Blazor runtime can cache integrity-addressed boot resources in Cache Storage; that is not a service worker or an application-managed API cache. Never clear learner cookies/storage as an update strategy.

The policy deliberately includes public assets that could otherwise be immutable: the existing .NET 10 workaround leaves stable asset URLs, so a single conservative policy avoids maintaining an error-prone list of mutable paths. The trade-off is extra validation requests on a fresh load. No polling, update prompt or automatic page reload is added. Already-running clients keep their current code and selected answers; a new application load obtains the current release. API compatibility with already-open clients must be maintained across deployments.

Future offline learning would require a separate design for versioned public content, durable attempt identifiers, reconciliation, identity protection and assessment restrictions. It is not provided by this installation foundation.

## Verification

Publish the server and run it with the normal database configuration. From the repository root, point the browser checks at that published process:

```powershell
$env:BROWSER_TEST_URL = 'http://localhost:5195'
dotnet run --project tests/ReasoningLab.BrowserTests --no-build -- --pwa-only
```

Use `BROWSER_EXECUTABLE` for an installed Chrome, or install the project's Playwright Chromium. The focused suite checks manifest delivery/parsing, icon dimensions, Chromium installation diagnostics, absence of service workers, direct-route launch, theme persistence, responsive layouts and simulated safe-area insets. The default browser suite also includes these checks.

Safe-area overrides and viewport dimensions are simulations. They do not constitute physical-device testing, OS installation, native standalone-window testing, browser zoom testing or a full accessibility audit.

### Stage 1 results — 10 November 2026

- Based on `origin/main` at `c481649`, on `feat/pwa-foundation`.
- Complete solution restore and build passed with 0 warnings and 0 errors. Restore used the existing workspace package cache; no dependency changes were required.
- Release publish passed; browser checks ran against that published application in Production on localhost.
- Existing domain/PostgreSQL suite: **13 passed, 0 failed**. The full learning workflow and 84-case theme suite were not repeated for this installation-only change; focused checks were used instead.
- Manifest returned the correct media type and parsed without Chromium errors. All declared PNG sizes and the 180px Apple touch icon loaded correctly. Chromium reported **no installation eligibility errors** using an isolated non-private profile. No service-worker registration was present.
- Layout checks passed in all three themes at 1440×1000, 820×1180, 1180×820, 390×844, 844×390 and 320×700. Simulated 24px top, 44px side and 34px bottom safe areas passed without horizontal overflow; header bounds and mobile menu access were checked. Portrait and landscape screenshots were inspected.
- Direct practice-route launch and theme persistence after reload passed, with no page or console errors.
- An initial private-context installation check was correctly rejected by Chromium (`in-incognito`); the test now uses a dedicated non-private temporary profile. A transient Windows executable lock during that test-harness rebuild was cleared before the successful final run.

Not verified: physical iOS/Android installation, installed OS shortcuts/windows, Safari/Firefox, production HTTPS deployment, real device cutouts, or a new deployment transition. No offline or improved submission-recovery behaviour is claimed. Subsequent stages remain unimplemented.


## Stage 2 — Connectivity and answer recovery

`LabApi` classifies failures as connection/timeout, explicit HTTP rejection, service unavailability, or unreadable response. Requests have a 20-second bound. HTTP 408 and 5xx responses are treated conservatively: they cannot establish whether an answer committed. The UI uses actual request outcomes, not `navigator.onLine`, to report availability.

`Practice` retains one immutable pending submission in component memory: session ID, assignment ID and choice. A transport failure, timeout, unavailable service or unreadable success response displays **“We couldn't confirm whether your answer was saved”**. Choices remain selected and locked. **Retry same answer** sends that exact payload to the existing endpoint; its transaction and assignment-based duplicate protection remain unchanged. There is no automatic POST retry, offline queue, service worker or durable storage.

An explicit rejection displays the server's validation message and offers **Refresh session**, a GET that reconciles the page with server state before unlocking further answers. A confirmed answer and its feedback remain visible if the subsequent progress-summary GET fails; **Refresh progress** only retries that read. Outdated responses from a previous pathway cannot overwrite the current page, and newer progress refreshes take precedence over older ones.

Diagnostic timers retain their original server deadline. Time spent disconnected does not pause an assessment. The timer never retries a pending answer. Without a pending answer it checks expiry once; a failed check offers manual recovery instead of continuous polling. A completed diagnostic returned by a retry is shown as a server-confirmed result, without claiming that the individual late answer was saved. Diagnostic correctness feedback is still withheld until completion.

Pending selections are not persisted across page reload, navigation, closing the app or another browser. Reopening uses existing server session recovery; it cannot reconstruct an unconfirmed local selection. An in-flight request can still commit after navigation or a timeout. These limitations are deliberately preserved until a separate durable-recovery design is approved.

### Focused checks

```powershell
# Against the published application:
dotnet run --project tests/ReasoningLab.BrowserTests --no-build -- --recovery-only
```

Browser tests inject a dropped request, a response lost after a real server commit, HTTP 503, malformed success JSON and HTTP 409. They inspect actual persisted attempt counts, compare retry URLs/payloads, check selected/disabled radios and verify that progress refreshes do not write answers. Diagnostic recovery checks a lost response and a simulated device-clock jump without changing the real server deadline. Separate PostgreSQL integration tests advance the server test clock past expiry and confirm that retries preserve only previously committed answers.

### Stage 2 verification — 10 November 2026

- Stage 1 was reviewed and committed locally as `844eb7b48542ef36f5f00cb200e755bf3810468c`. Stage 2 was approved for local commit on `feat/pwa-foundation` after the targeted timeout verification below.
- Solution builds and Release publish passed with zero build warnings/errors. No new dependencies, backend changes, shared contracts, migrations or learning/assessment rules were introduced.
- PostgreSQL/domain tests: **15 passed, 0 failed**, including both committed and uncommitted answer retries after server-side diagnostic expiry.
- Published application: existing acceptance workflow passed at desktop, tablet and mobile widths (practice, mastery promotion, reload persistence, LSAT switch, fixed diagnostic and results).
- Published focused recovery suite: **8 scenarios passed** — request dropped before sending; response dropped after a real commit; HTTP 503; malformed success response; explicit HTTP 409; confirmed answer followed by dashboard failure; lost diagnostic response; and device-clock correction with expiry checking re-armed. Repeated uncertain retries retained identical URLs/payloads and produced exactly one persisted attempt.
- Recovery messages were checked for mobile overflow and screenshots were inspected. Diagnostic tests verified no early correctness feedback, no timer-driven answer submission and no deadline extension. The final focused suite passed after the clock-correction fix.
- Existing EF Core multiple-collection query warnings remain. Tests used Chromium and an actual local PostgreSQL server; transport faults and device-clock changes were simulated. Physical network transitions, other browser engines, installed OS applications and a full screen-reader audit were not performed. The final targeted verification below also exercises the real 20-second client timeout using a withheld response.
- The installation/theme matrices were not repeated because those assets were unchanged. Deployment/update verification remains out of scope. Nothing was pushed, merged or submitted as a pull request.


### Final targeted timeout verification — 10 November 2026

Two additional published-browser scenarios passed, for general practice and the LSAT diagnostic. Each forwards the answer to the real server, verifies its successful save, then withholds the response without injecting an abort or HTTP 408. The unchanged application's real 20-second cancellation fires; no virtual clock is used in these timeout tests.

Both scenarios verified the explicit timeout and uncertain-outcome message, retained/locked selected answer, absence of premature correctness feedback or a rejection claim, and no automatic resubmission. An explicit retry used the identical session URL and JSON payload (including assignment ID and choice). Server reads confirmed exactly one recorded answer before and after retry. Practice feedback was restored and mastery evidence remained at one attempt. In the diagnostic, the server clock advanced during the wait, the deadline remained identical, and no early correctness feedback appeared.

The two PostgreSQL integration cases for retries after diagnostic expiry were rerun and passed: a previously committed answer remains counted once, while an answer that never arrived is not accepted after expiry. These tests use the server test clock; the browser timeout scenarios exercise recovery before expiry against the published application.

Run the additional targeted browser checks with:

```powershell
dotnet run --project tests/ReasoningLab.BrowserTests --no-build -- --timeout-only
```

They are also included in `--recovery-only` and the default browser suite. The browser-test project rebuilt successfully. Only tests and documentation were adjusted during this final verification; application behaviour was unchanged. The earlier 15-test domain/database run, eight recovery scenarios and three-viewport learning workflow remain recorded above rather than being unnecessarily repeated.

Remaining Stage 2 limits: browser interception simulates a stalled response after a real save; physical network transitions and other browser engines are untested. Pending selections remain in memory only. No service worker, queue, schema or learning-rule changes were introduced. Stage 3 results follow; no push, merge or pull request is authorised.

## Stage 3 — Application updates and deployment verification

### Finding and focused hosting change

Inspection of the existing published application found ETag/Last-Modified validators but no explicit Cache-Control on HTML, fallback HTML, the manifest, CSS or theme JavaScript. Framework JavaScript already returned `no-cache`; the dashboard returned `no-store`, including an unauthenticated 401. The missing public policy allowed heuristic freshness at stable URLs. The server now sets `no-cache` for non-API GET/HEAD responses in an `OnStarting` callback so static-file middleware cannot replace the policy. API handling is unchanged.

All four .NET 10 workaround settings remain false: `OverrideHtmlAssetPlaceholders`, `WasmFingerprintAssets`, `StaticWebAssetFingerprintingEnabled` and `StaticWebAssetsFingerprintContent`. No database, contracts, learner rules, identity handling, theme storage or dependencies changed.

### Repeatable published-release checks

Run from the repository root with packages restored, installed Chrome or Playwright Chromium, and an existing migrated/seeded disposable PostgreSQL test database:

```powershell
$env:ConnectionStrings__ReasoningLab = '<test database connection string>'
# Optional when using an installed browser:
$env:BROWSER_EXECUTABLE = 'C:/Program Files/Google/Chrome/Application/chrome.exe'
dotnet build ReasoningLab.slnx --no-restore
./tests/ReasoningLab.BrowserTests/PublishUpdateFixtures.ps1
dotnet run --project tests/ReasoningLab.BrowserTests --no-build -- --updates-only
```

The fixture script publishes A and B into separate directories under ignored `artifacts/update-releases`. It refuses to overwrite either directory; for another publication use `-OutputRoot artifacts/update-releases-next` and set `UPDATE_RELEASE_ROOT` to that same directory before the browser run. Each fixture has a different assembly informational version, producing different compiled WASM bytes, plus test-only HTML/CSS/JS/manifest markers. Modified fixture files' compressed siblings are removed to avoid testing stale fixture compression. These marked outputs are verification fixtures, not production deployment artifacts.

The harness starts the published server DLLs in Production, switches them on the same localhost origin (default port 5197, configurable with `UPDATE_TEST_URL`), and stops only its own child processes. Both releases use the same PostgreSQL database and Data Protection key directory. Normal transition checks use a persistent browser profile with warm HTTP caches, without routing, cache clearing or cache-busting query strings. A separate context intercepts one boot request for the deliberate cutover race. Logs, a release-B screenshot and the observed integrity error are written under ignored `artifacts/`. These tests create disposable learners in the supplied database.

### Results — 10 November 2026

| Check | Result |
| --- | --- |
| Solution build | Passed, 0 warnings and 0 errors. |
| Two separate Release publications | Passed; A/B compiled client WASM hashes differ. |
| Domain/PostgreSQL suite | 15 passed, 0 failed. |
| Public HTTP policy in both published releases | Passed: HTML and direct-route fallback, manifest, application CSS/JS, representative font/icon and framework JS/WASM returned `no-cache`; matching ETags returned 304 with the policy; manifest HEAD also retained it. |
| Changed release validators | Passed: B returned 200 for A's HTML, manifest, CSS, JS and client WASM ETags. |
| Learner API cache policy | Passed: session/dashboard GETs and answer/session POSTs remained `no-store`. |
| Already-open A practice and timed diagnostic against B | Passed: no navigation/reload, original selections retained, answers submitted successfully, persisted progress unchanged by cutover, original persisted deadline unchanged. |
| Fresh B load with A's warm browser cache | Passed: rendered B HTML/CSS/JS markers and browser-fetched asset hashes matched the B publication, including compiled WASM; same learner cookie, two practice attempts and Warm Study preference retained. |
| Full browser-process restart using the same profile | **Failed learner continuity**: the learner cookie disappeared before navigation, as did an independent persistent probe cookie. B loaded and the theme survived, but the app created a new learner with no attempts. The cookie had a future expiry before shutdown. This is broader than the deployment transition; its cause has not been established. |
| Deliberate boot request spanning A → B | Reproduced integrity rejection of B's client WASM against A's boot metadata; a normal explicit reload then booted B successfully. This verifies recovery, not seamless startup across cutover. |
| Service worker/application API cache | No service-worker registrations and no API URLs in Cache Storage in tested clients. Application source adds neither registration nor API caching. |

The final `--updates-only` run exits nonzero for the browser-restart cookie failure; it is not an all-green suite. It completes the separate deployment-race check before reporting that failure. Do not treat fresh-page continuity as proof of browser/OS restart continuity. Earlier harness iterations corrected a timestamp comparison to use PostgreSQL-persisted precision and selected the correct second practice answer. One earlier browser-startup timeout was not reproduced in subsequent completed transition runs.

These were **automated desktop Chrome tests of real published ASP.NET Core processes and real local PostgreSQL**, with simulated release markers and local process cutover. The race used an intentionally delayed browser request. No physical phones/tablets, installed PWA windows, Safari/Firefox, real reverse proxy/CDN, production HTTPS deployment or OS upgrade were tested. The Stage 1 installation matrix and Stage 2 fault-injection matrix were not repeated for this cache-policy change. Existing EF Core multiple-collection query warnings remain.

### Deployment guidance and unresolved risks

1. Publish to a new release directory, verify it, then switch to the completed release. Do not copy files over a running publication. Keep the database and Data Protection keys outside release directories and preserve them during deployment/rollback; losing either breaks learner continuity. Keep APIs compatible with already-open clients. A/B here differ in assets/assembly metadata, not API contracts, so compatibility with future breaking changes is not demonstrated.
2. Switching complete releases avoids partial-directory updates but is not atomic across a client's multiple boot requests. The follow-up below implements a startup-only “Reload application” action for a rejected boot. Eliminating the race altogether would need release-specific asset URLs with retained old assets or reliable release affinity, adding deployment complexity and potentially revisiting the fingerprinting workaround. That larger change is deferred.
3. Verify cookie persistence on an ordinary target-browser profile and installed devices before claiming restart continuity. The automated environment lost both the learner and independent probe cookies; changing authentication or persisting identity elsewhere would be an unrelated workaround, so neither was added. Server-side progress was retained across A/B, but a missing identity cookie prevents the reopened client from accessing it.
4. Recheck all headers through the actual HTTPS proxy/CDN. Intermediaries must preserve public revalidation and must not cache API responses. New response headers cannot retroactively invalidate already-fresh responses cached before this policy existed; first rollout may require a manual reload/revalidation. The A/B fixtures both include the new policy.
5. This policy trades some repeat-load performance for correctness at stable URLs. It does not guarantee offline launch, zero-downtime rollout, instant OS manifest/icon refresh, or safe mixing of incompatible API versions. No service worker, background synchronisation, offline attempt queue or storage-clearing update mechanism is introduced.

Stage 3 implementation is approved for a local commit on `feat/pwa-foundation`. Browser-restart persistence remains an outstanding verification requirement. No push, merge or pull request is authorised.

### Stage 3 follow-up — 11 November 2026

**Cookie investigation.** An isolated probe, independent of ReasoningLab and its API, reproduced loss of a persistent cookie while local storage survived. Playwright's browser log confirmed the same absolute user-data directory, graceful closure with exit code 0, and distinct browser process IDs on reopening. The deployment harness now independently checks the original PID has exited and obtains the new PID through the Chrome DevTools Protocol. It checks both cookies before navigating the reopened client; it does not export/import storage state or reinsert cookies.

Chrome reported `os_crypt_win.cc: Failed to encrypt: The system cannot find the file specified. (0x2)` and `Encryption is not available`. An independent .NET `ProtectedData.Protect` call with `DataProtectionScope.CurrentUser` also failed, reporting that the current user profile may not be loaded. The actual process identity is `CodexSandboxOnline`, although the inherited USERPROFILE points at the normal user's directory. The demonstrated cause is unavailable Windows user encryption in this execution environment, not a session-only cookie, insufficient MaxAge, an ephemeral browser context, an early reopen, or changing server keys. The underlying Windows profile configuration has not been repaired. Production `Endpoints.cs` and identity code are unchanged; all published processes still use the same `artifacts/update-keys` directory.

To diagnose this on another Windows runner without exposing any credentials:

```powershell
[System.Security.Principal.WindowsIdentity]::GetCurrent().Name
# Uses only disposable test bytes. A working user context should succeed.
$probe = [System.Security.Cryptography.ProtectedData]::Protect(
    [byte[]](1,2,3), $null,
    [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
```

Restart continuity must still be rerun under a normal Windows user with working DPAPI. A passing fresh-page test is not a substitute. Do not work around this by disabling browser encryption, changing the production cookie, or restoring cookie values from test memory.

**Startup-only recovery.** `index.html` disables Blazor autostart and loads `js/startup.js`, which awaits `Blazor.start()`. The published .NET 10 integrity-failure test demonstrated that a failed boot download can emit unhandled promise rejections without settling that outer promise. Catching `Blazor.start()` alone failed this test. The final implementation also installs a temporary `unhandledrejection` listener during startup; it removes itself on successful startup or the first failure. It does not suppress browser error logging.

If startup fails while the boot markup is still present, recovery reveals a labelled alert, focuses its error heading, and offers a native “Reload application” button. Reload happens only after activation and keeps the current URL. Successful startup replaces the boot markup. There are no persistent global error handlers, polling, automatic retries, reload timers or active-session hooks. The existing theme and button/focus styling are reused, with a padded responsive boot layout. This keeps the runtime's normal boot-resource loader and cache behaviour intact rather than replacing its fetch mechanism.

The recovery message deliberately does not claim that every startup failure is an update: interrupted downloads can also reject startup. It cannot handle failures before the recovery script is downloaded, JavaScript being disabled, an offline cold launch, or a request that never settles. It does not make the multi-request deployment boundary atomic. The .NET 10 asset workarounds, API cache policy, Data Protection configuration and learning rules remain unchanged.

**Final focused results.** Solution and browser-test builds passed with zero warnings/errors; the startup script passed the JavaScript syntax check. Both final Release fixtures published successfully under `artifacts/update-recovery-final`. The final published-browser results were:

- A/B cache headers, conditional requests and asset hashes, now including `js/startup.js`.
- Already-open practice and diagnostic clients: no forced navigation, preserved selections, successful submissions and unchanged server deadline.
- Fresh B page with warm caches: updated assets, same learner identity, persisted progress and theme.
- Full process restart: the original PID was confirmed exited and a different PID opened the same profile; B and the theme loaded correctly. **Cookie continuity failed**: both cookies were absent before navigation and the learner was recreated. No cookie restoration or weaker encryption was used. This remains the sole final suite failure and produces a nonzero exit.
- Simulated boot/cutover race: integrity rejected the mismatched WASM; the labelled recovery alert appeared and its heading received focus. At 390×844 there was no horizontal overflow; the screenshot was visually inspected. No navigation occurred before user action. Tab reached the reload button, Enter activated it, and the application booted B successfully with matching assets and the recovery markup removed.
- No service-worker registration or API responses in Cache Storage.

The earlier outer-promise-only recovery failure is fixed and was rerun against newly published releases. Browser integrity errors remain expected diagnostic output in the deliberately broken-boot scenario. The previous 15 passing domain/PostgreSQL tests were not repeated because this follow-up changes startup assets and the browser harness only.

To rerun these existing final fixtures after configuring the test database and browser as above:

```powershell
$env:UPDATE_RELEASE_ROOT = 'artifacts/update-recovery-final'
dotnet run --project tests/ReasoningLab.BrowserTests --no-build -- --updates-only
```

`artifacts/update-recovery-final-run.log`, `artifacts/update-startup-recovery.png` and the isolated `artifacts/cookie-probe.log` contain local evidence; artifacts are intentionally not versioned. For a new checkout, publish fresh fixtures using the script and set `UPDATE_RELEASE_ROOT` accordingly. Re-run under a Windows account with working DPAPI before marking cookie restart acceptance complete. Physical-device/installed-PWA testing, screen-reader verification, other browser engines and production HTTPS/proxy/CDN verification remain outstanding. Only a local Stage 3 commit is approved; no push, merge or pull request is authorised.

### Outstanding restart verification: normal Windows PowerShell

Open PowerShell yourself from the Windows Start menu, under your normal Windows account, outside the Codex sandbox. The following commands use this task's existing compiled browser tests and final A/B publications; they do not touch the separate VS Code checkout. Prerequisites: .NET 10, Chrome at the shown path, the existing migrated/seeded local test PostgreSQL instance running on port 55432, and port 5197 free. The database below is the local development test database, not a production connection. The harness creates new test learners and starts/stops its own published server processes.

```powershell
Set-Location 'C:\Users\ross_\Documents\Codex\2026-11-09\you-are-the-lead-software-engineer\outputs\reasoning-platform'
$ErrorActionPreference = 'Stop'
[System.Security.Principal.WindowsIdentity]::GetCurrent().Name
Add-Type -AssemblyName System.Security
$dpapiProbe = [System.Security.Cryptography.ProtectedData]::Protect(
    [byte[]](1,2,3), $null,
    [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
Write-Host 'Windows user encryption probe succeeded.'

$env:BROWSER_EXECUTABLE = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
$env:ConnectionStrings__ReasoningLab = 'Host=127.0.0.1;Port=55432;Database=reasoninglab;Username=reasoning'
$env:UPDATE_RELEASE_ROOT = (Resolve-Path 'artifacts/update-recovery-final').Path
$env:UPDATE_TEST_URL = 'http://localhost:5197'
dotnet run --project tests/ReasoningLab.BrowserTests --no-build --no-restore -- --updates-only
if ($LASTEXITCODE -ne 0) { throw "Published update/restart verification failed (exit $LASTEXITCODE)." }
```

The Windows identity should be your normal account, not `CodexSandboxOnline`. Stop if the DPAPI probe fails; no browser security or production identity change is an acceptable workaround. The cookie check stays enabled inside `--updates-only` (there is no separate cookie-only switch). Do not delete or replace `artifacts/update-keys` between releases or browser launches. The same key directory and database are used throughout the test.

Acceptance still requires all of the following in the normal-account run: the old browser PID exits; a new PID opens the same profile; **both** `learner cookie retained=True` and `independent persistent probe retained=True` are reported **before navigation**; the reopened app reports `identity retained: True; attempts: 2`; the Warm Study theme survives; and the full command exits 0. Record that run's browser/Windows versions and output before closing the outstanding requirement. A successful DPAPI probe alone does not prove cookie persistence. Any failed assertion must remain visible, even if the other deployment checks pass.

If reproducing from a checkout without generated artifacts, first restore/build and publish fresh fixtures from that checkout's root (using your configured migrated/seeded test database), then run the same `--updates-only` command:

```powershell
dotnet restore ReasoningLab.slnx
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
dotnet build ReasoningLab.slnx --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$env:UPDATE_RELEASE_ROOT = Join-Path (Get-Location).Path ('artifacts/update-normal-' + [guid]::NewGuid().ToString('N'))
& ./tests/ReasoningLab.BrowserTests/PublishUpdateFixtures.ps1 -OutputRoot $env:UPDATE_RELEASE_ROOT
```

Temporary probes (including `artifacts/cookie-probe.cjs`), publications, browser profiles, keys, screenshots and logs remain excluded by `artifacts/` in `.gitignore`. Only the reusable application, test harness, fixture publisher and documentation belong in the Stage 3 commit.
