param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [string]$AssetsFile = (Join-Path $PSScriptRoot '../src/CodexTaskMonitor.App/obj/project.assets.json')
)
$ErrorActionPreference = 'Stop'
$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json -AsHashtable
$destination = Join-Path $PublishDirectory 'licenses'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$inventory = @()
foreach ($entry in $assets.libraries.GetEnumerator() | Sort-Object Key) {
    if ($entry.Value.type -ne 'package') { continue }
    $directory = $null
    foreach ($packageRoot in $assets.packageFolders.Keys) {
        $candidate = Join-Path $packageRoot $entry.Value.path
        if (Test-Path -LiteralPath $candidate -PathType Container) { $directory = $candidate; break }
    }
    if (-not $directory) { throw "Package files missing for $($entry.Key)." }
    $spec = Get-ChildItem -LiteralPath $directory -Filter '*.nuspec' -File | Select-Object -First 1
    if (-not $spec) { throw "Package metadata missing for $($entry.Key)." }
    [xml]$xml = Get-Content -LiteralPath $spec.FullName -Raw
    $metadata = $xml.SelectSingleNode('//*[local-name()="metadata"]')
    $id = $metadata.SelectSingleNode('*[local-name()="id"]').InnerText
    $version = $metadata.SelectSingleNode('*[local-name()="version"]').InnerText
    $license = $metadata.SelectSingleNode('*[local-name()="license"]')
    $licenseUrl = $metadata.SelectSingleNode('*[local-name()="licenseUrl"]')
    $projectUrl = $metadata.SelectSingleNode('*[local-name()="projectUrl"]')
    $noticeFiles = @(Get-ChildItem -LiteralPath $directory -File -Recurse | Where-Object {
        $_.Name -match '^(LICENSE|COPYING|NOTICE|THIRD[-_]?PARTY[-_]?NOTICES)(\.[A-Za-z0-9_-]+)?$'
    })
    if ($license -and $license.type -eq 'file') {
        $licensePath = [IO.Path]::GetFullPath((Join-Path $directory $license.InnerText))
        if (-not $licensePath.StartsWith([IO.Path]::GetFullPath($directory) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Invalid license path for $id."
        }
        if (Test-Path -LiteralPath $licensePath -PathType Leaf) { $noticeFiles += Get-Item -LiteralPath $licensePath }
        else { throw "Declared license file missing for $id." }
    }
    $index = 0
    foreach ($file in $noticeFiles | Sort-Object FullName -Unique) {
        $index++
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $destination "$id-$version-$index.txt")
    }
    $inventory += [ordered]@{
        id = $id; version = $version
        license = if ($license) { $license.InnerText } else { $null }
        licenseType = if ($license) { $license.type } else { $null }
        licenseUrl = if ($licenseUrl) { $licenseUrl.InnerText } else { $null }
        projectUrl = if ($projectUrl) { $projectUrl.InnerText } else { $null }
        copyright = $metadata.SelectSingleNode('*[local-name()="copyright"]').InnerText
        copiedNotices = $index
    }
}
# Runtime packs are not necessarily listed in libraries, but their notices must ship too.
foreach ($framework in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.aspnetcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
    foreach ($packageRoot in $assets.packageFolders.Keys) {
        $packRoot = Join-Path $packageRoot $framework
        if (-not (Test-Path -LiteralPath $packRoot)) { continue }
        $pack = Get-ChildItem -LiteralPath $packRoot -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        foreach ($file in Get-ChildItem -LiteralPath $pack.FullName -File | Where-Object { $_.Name -match 'LICENSE|NOTICE' }) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $destination "$framework-$($pack.Name)-$($file.BaseName).txt")
        }
    }
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../licenses') -File) {
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
$inventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'dependencies.json') -Encoding utf8
Write-Host "Exported notices for $($inventory.Count) resolved packages."
