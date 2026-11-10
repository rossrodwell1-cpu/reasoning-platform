# UI themes and typography

## Scope and structure

This refinement preserves the .NET/Blazor architecture, learning policies, assessment rules, API and database schema.

- `src/ReasoningLab.Client/wwwroot/css/tokens.css` defines the three approved palettes and shared typography tokens. Components consume semantic colours for text, surfaces, controls, selection, success, review and errors. Crisp Light is the fallback; Dark and Warm Study override values rather than duplicating component rules.
- `wwwroot/css/app.css` retains the existing page structure and artwork, with readable supporting copy, larger question text, visible focus, touch targets and responsive navigation. The question prompt uses Source Sans 3 as reading content; page headings use Manrope.
- `wwwroot/js/theme.js` is a small synchronous startup script, loaded before CSS and Blazor. It validates `reasoninglab.theme` in local storage and applies `data-theme` before first paint. With no valid explicit preference, it follows `prefers-color-scheme`, including live system changes. Choosing **Use system setting** clears the override. If storage is blocked, selection still works in memory for the current page.
- `Components/ThemePicker.razor` exposes a labelled native select and calls that same script through Blazor JS interop. No theme service, backend preference or dependency is needed for this scope.
- `Layout/MainLayout.razor` keeps the desktop sidebar, adds the theme selector, and provides a keyboard-operable expandable mobile menu. Content studio is reachable in the main navigation at every width. Navigation closes the mobile menu; existing route focus handling remains in place.
- `Components/QuestionPanel.razor` keeps native radio semantics and assessment behaviour. Selected radios use the approved 24px contrasting circle; submitted answers include text identifying the chosen and correct answers. Windows forced-colour mode restores native radio rendering.
- `Pages/NotFound.razor` now has a top-level heading, consistent with the router's focus target.

## Fonts

Manrope and Source Sans 3 are bundled locally under `wwwroot/fonts`, together with SIL Open Font Licences and source URLs. The two WOFF2 files total 53,404 bytes and support weights 400–700. Both are preloaded, use `font-display: swap`, and have Segoe UI/Arial fallbacks. There are no runtime font-service requests. Final glyph metrics can differ from fallback fonts during a cold load; preloading reduces that interval without hiding reading content.

## Verification commands

Use the existing PostgreSQL test configuration documented in README, then:

```powershell
dotnet restore
dotnet build --no-restore
dotnet test tests/ReasoningLab.Tests --no-build
# Start the server in another terminal, then:
dotnet run --project tests/ReasoningLab.BrowserTests --no-build
# Optional focused repeat after CSS/theme changes:
dotnet run --project tests/ReasoningLab.BrowserTests --no-build -- --themes-only
```

`BROWSER_TEST_URL` selects the running application (default `http://localhost:5180`). `BROWSER_EXECUTABLE` can select an installed Chrome; otherwise use the project's Playwright browser installation instructions.

The browser suite checks all routes across three themes and four viewport widths, explicit/reload preference persistence, live system changes, early startup without Blazor, invalid/blocked storage, mobile navigation, native radio keyboard selection, textual feedback, forced colours, reduced motion and font loading. It also checks representative semantic colour pairs against WCAG contrast thresholds. Screenshots are written to the ignored `artifacts` directory.

Viewport reflow checks are not a substitute for testing actual browser zoom, screen readers, every browser engine or physical touch devices. This is not a claim of a complete WCAG audit.
