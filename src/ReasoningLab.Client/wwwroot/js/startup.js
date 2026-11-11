(async () => {
    const showRecovery = () => {
        window.removeEventListener('unhandledrejection', showRecovery);
        // Blazor replaces this markup on startup. Never handle runtime errors
        // or reload an active learning session here. Leave browser logging intact.
        const error = document.getElementById('startup-error');
        if (!error || !error.hidden) return;
        document.getElementById('startup-status').hidden = true;
        error.hidden = false;
        document.getElementById('startup-reload').addEventListener('click', () => location.reload());
        document.getElementById('startup-error-title').focus();
    };
    // .NET 10 can reject a boot download without settling Blazor.start().
    // This listener exists only during startup, never for the running app.
    window.addEventListener('unhandledrejection', showRecovery);
    try {
        await Blazor.start();
    } catch {
        showRecovery();
    } finally {
        window.removeEventListener('unhandledrejection', showRecovery);
    }
})();
