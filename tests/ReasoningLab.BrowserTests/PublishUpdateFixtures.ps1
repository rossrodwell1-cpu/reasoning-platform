param([string]$OutputRoot = 'artifacts/update-releases')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not [System.IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot = Join-Path $repo $OutputRoot }

foreach ($label in @('A', 'B')) {
    $release = Join-Path $OutputRoot $label
    if (Test-Path -LiteralPath $release) { throw "Use an empty fixture directory: $release already exists." }
    $version = '1.0.0-update-' + $label.ToLowerInvariant()
    dotnet publish (Join-Path $repo 'src/ReasoningLab.Server') -c Release --no-restore "-p:Version=$version" -o $release
    if ($LASTEXITCODE -ne 0) { throw "Release $label publish failed." }

    # Verification-only markers: never modify the application source. Different
    # assembly informational versions also produce genuinely different WASM assets.
    $wwwroot = Join-Path $release 'wwwroot'
    $html = Join-Path $wwwroot 'index.html'
    [IO.File]::WriteAllText($html, [IO.File]::ReadAllText($html).Replace('</head>', "<meta name=`"deployment-test-release`" content=`"$label`" /></head>"))
    [IO.File]::AppendAllText((Join-Path $wwwroot 'css/app.css'), "`n:root { --deployment-test-release: '$label'; }`n")
    [IO.File]::AppendAllText((Join-Path $wwwroot 'js/theme.js'), "`nwindow.deploymentTestRelease = '$label';`n")
    $manifestPath = Join-Path $wwwroot 'manifest.webmanifest'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.description = "ReasoningLab deployment verification release $label"
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 10))
    foreach ($file in @('index.html', 'css/app.css', 'js/theme.js', 'manifest.webmanifest')) {
        foreach ($extension in @('.br', '.gz')) {
            $compressed = Join-Path $wwwroot ($file + $extension)
            if (Test-Path -LiteralPath $compressed) { Remove-Item -LiteralPath $compressed }
        }
    }
    [IO.File]::WriteAllText((Join-Path $wwwroot 'deployment-probe.json'), "{`"release`":`"$label`"}")
}
Write-Host "Published verification fixtures at $OutputRoot (not deployment artifacts)."
