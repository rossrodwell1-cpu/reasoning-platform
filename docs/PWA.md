# PWA installation foundation

Stage 1 adds installation metadata and safe-area layout support. Stage 2 adds request failure handling and explicit answer recovery. Learning still requires the ASP.NET Core API and PostgreSQL. Deployment/update verification remains deferred.

## Architecture

- `wwwroot/manifest.webmanifest` identifies the app as **ReasoningLab**, with stable `id`, `start_url` and `scope` set to `/`, and `display: standalone`. Deployment is at the origin root. No orientation is locked.
- `wwwroot/icons/icon.svg` preserves the existing favicon's mark and colours. The PNGs are rasterisations of that vector at 192px, 512px and 180px. The opaque square 512px maskable variant keeps the mark within the central 80% safe circle, allowing the operating system to choose its crop. Rasterise the SVG at the required pixel dimensions when replacing an icon; the 512px regular and maskable outputs intentionally share the same artwork.
- The HTML links the manifest and 180px Apple touch icon, supplies an Apple application title, and enables `viewport-fit=cover`. CSS applies the device's safe-area insets to the page and fixed elements. Zoom remains unrestricted.
- The launch background and manifest theme colour use Crisp Light's canvas (`#f5f7fc`). The existing startup script still applies the saved/system theme and updates the page's theme-colour metadata. OS splash screens and installation chrome may retain the manifest colour.
- No service worker, offline cache, background sync, IndexedDB storage, installation prompt component or new dependency is introduced. Existing .NET 10 fingerprinting workarounds, hosting, API and learner identity are unchanged.

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

There is no service-worker cache or activation lifecycle. Existing HTTP/Blazor caching behaviour is unchanged; this stage does not claim to verify deployment transitions or cache invalidation. Those checks and any justified hosting changes belong to the approved later update stage. Never clear learner cookies/storage as an update strategy.

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

Remaining limits: browser interception simulates a stalled response after a real save; physical network transitions and other browser engines are untested. Pending selections remain in memory only. No service worker, queue, schema or learning-rule changes were introduced. Stage 3 has not begun; no push, merge or pull request is authorised by this verification.
