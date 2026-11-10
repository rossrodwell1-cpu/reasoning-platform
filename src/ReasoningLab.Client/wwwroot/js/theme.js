// Run before styles and Blazor so the first painted frame uses the right theme.
(() => {
    const key = 'reasoninglab.theme';
    const themes = ['light', 'dark', 'warm'];
    const system = matchMedia('(prefers-color-scheme: dark)');
    let preference = 'system';
    try {
        const saved = localStorage.getItem(key);
        if (themes.includes(saved)) preference = saved;
    } catch { /* Storage may be unavailable; keep an in-memory preference. */ }

    function apply() {
        const theme = preference === 'system' ? (system.matches ? 'dark' : 'light') : preference;
        document.documentElement.dataset.theme = theme;
        document.querySelector('meta[name="theme-color"]')?.setAttribute('content',
            { light: '#f5f7fc', dark: '#0f172a', warm: '#f5f0e6' }[theme]);
    }
    window.reasoningTheme = {
        get: () => preference,
        set(value) {
            preference = themes.includes(value) ? value : 'system';
            try {
                if (preference === 'system') localStorage.removeItem(key);
                else localStorage.setItem(key, preference);
            } catch { /* Applying a theme does not require browser storage. */ }
            apply();
        }
    };
    system.addEventListener('change', apply);
    apply();
})();
