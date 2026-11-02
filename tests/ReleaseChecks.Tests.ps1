$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$scratchBase = [IO.Path]::GetFullPath((Join-Path $workspace 'tmp'))
$scratch = Join-Path $scratchBase ('release-check-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $scratch 'source'
$publish = Join-Path $scratch 'publish'
$checker = Join-Path $workspace 'scripts/check-release.ps1'
New-Item -ItemType Directory -Path $source, $publish -Force | Out-Null
try {
    git -C $source init --quiet
    [IO.File]::WriteAllText((Join-Path $source 'README.md'), 'Synthetic release test.')
    git -C $source add README.md
    [IO.File]::WriteAllText((Join-Path $publish 'CodexTaskMonitor.exe'), 'Synthetic executable fixture.')
    & $checker -SourceRoot $source -PublishDirectory $publish
    Write-Host 'PASS clean release fixture'

    foreach ($relative in @('settings.json', 'Pets/example/spritesheet.png', 'CodexTaskMonitor.pdb', 'private.bundle', 'unknown.txt')) {
        $path = Join-Path $publish $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, 'Synthetic forbidden file.')
        $blocked = $false
        try { & $checker -SourceRoot $source -PublishDirectory $publish } catch { $blocked = $true }
        Remove-Item -LiteralPath $path -Force
        if (-not $blocked) { throw "Release checker accepted forbidden file: $relative" }
        Write-Host "PASS rejects $relative"
    }

    $path = Join-Path $source 'settings.json'
    [IO.File]::WriteAllText($path, '{}')
    git -C $source add -f settings.json
    $blocked = $false
    try { & $checker -SourceRoot $source -PublishDirectory $publish } catch { $blocked = $true }
    if (-not $blocked) { throw 'Release checker accepted tracked runtime data.' }
    Write-Host 'PASS rejects tracked runtime data'
    git -C $source rm --quiet -f settings.json

    [IO.File]::WriteAllText((Join-Path $source 'README.md'), $env:USERPROFILE)
    $blocked = $false
    try { & $checker -SourceRoot $source -PublishDirectory $publish } catch { $blocked = $true }
    if (-not $blocked) { throw 'Release checker accepted a private user profile path.' }
    Write-Host 'PASS rejects a private profile path'
} finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    if (-not $resolved.StartsWith($scratchBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside the test scratch directory.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
