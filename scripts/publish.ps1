param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory,
    [string]$DotNetPath,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = if ($DotNetPath) { $DotNetPath } elseif (Test-Path -LiteralPath (Join-Path $root 'tools/release-dotnet/dotnet.exe')) {
    Join-Path $root 'tools/release-dotnet/dotnet.exe'
} elseif (Test-Path -LiteralPath (Join-Path $root 'tools/dotnet/dotnet.exe')) {
    Join-Path $root 'tools/dotnet/dotnet.exe'
} else { 'dotnet' }
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'artifacts/publish/win-x64' }
if ((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count -gt 0) {
    throw 'Choose an empty output directory. Existing application files and personal data are never removed automatically.'
}
$arguments = @('publish', (Join-Path $root 'src/CodexTaskMonitor.App/CodexTaskMonitor.App.csproj'),
    '--configuration', $Configuration, '--runtime', 'win-x64', '--self-contained', 'true', '--output', $output,
    '--nologo', '-p:DebugType=None', '-p:DebugSymbols=false')
if ($NoRestore) { $arguments += '--no-restore' }
& $dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

foreach ($name in @('LICENSE', 'README.md', 'README.en.md', 'THIRD_PARTY_NOTICES.md', 'SECURITY.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $output
}
$docs = Join-Path $output 'docs'
New-Item -ItemType Directory -Path $docs -Force | Out-Null
foreach ($name in @('USER_GUIDE.zh-CN.md', 'USER_GUIDE.en.md', 'CUSTOM_PETS.md')) {
    Copy-Item -LiteralPath (Join-Path $root "docs/$name") -Destination $docs
}
$images = Join-Path $docs 'images'
New-Item -ItemType Directory -Path $images -Force | Out-Null
foreach ($name in @('task-center.png', 'floating-tray.png')) {
    Copy-Item -LiteralPath (Join-Path $root "docs/images/$name") -Destination $images
}
& (Join-Path $PSScriptRoot 'export-notices.ps1') -PublishDirectory $output
& (Join-Path $PSScriptRoot 'check-release.ps1') -SourceRoot $root -PublishDirectory $output

Write-Host "Published: $(Join-Path $output 'CodexTaskMonitor.exe')"
