# PWA installation foundation

Stage 1 adds installation metadata and safe-area layout support to the existing application. Learning still requires the ASP.NET Core API and PostgreSQL. Connectivity recovery and deployment/update verification are deferred to later stages.

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

Offline cold launch can show the browser's network-error page. This stage provides no offline learning or custom offline launch screen, and it does not yet improve uncertain answer-submission recovery.

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
