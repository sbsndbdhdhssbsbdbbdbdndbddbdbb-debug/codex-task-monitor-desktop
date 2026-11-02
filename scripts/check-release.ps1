param(
    [Parameter(Mandatory)][string]$SourceRoot,
    [Parameter(Mandatory)][string]$PublishDirectory
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $SourceRoot).Path
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $publish 'CodexTaskMonitor.exe') -PathType Leaf)) {
    throw 'Release executable is missing.'
}
$tracked = @(git -C $source -c core.quotepath=false ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked source files.' }
$forbidden = '(^|/)(Pets|\.codex|\.git|tmp|artifacts|tools|bin|obj)(/|$)|(^|/)(integration|state|settings)\.json$|(^|/)\.env($|\.)|\.(db|sqlite(-shm|-wal)?|log|pdb|bundle|pem|key|pfx|p12|exe|zip)$'
foreach ($relative in $tracked) {
    if ($relative -match $forbidden) { throw "Private/runtime file is tracked: $relative" }
}
$files = @(Get-ChildItem -LiteralPath $publish -File -Recurse -Force)
$rootAllowed = @('CodexTaskMonitor.exe', 'LICENSE', 'README.md', 'README.en.md', 'THIRD_PARTY_NOTICES.md', 'SECURITY.md')
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($publish, $file.FullName).Replace('\', '/')
    if ($relative -match '(^|/)(Pets|\.codex|\.git|tmp|tools|bin|obj)(/|$)|(^|/)(integration|state|settings)\.json$|\.(pdb|bundle|db|sqlite(-shm|-wal)?|log|pem|key|pfx|p12)$') {
        throw "Private/runtime file is in release: $relative"
    }
    if ($relative -notin $rootAllowed -and $relative -notmatch '^licenses/[A-Za-z0-9_.-]+\.(txt|md|json)$' -and
        $relative -notmatch '^docs/(USER_GUIDE\.(zh-CN|en)\.md|CUSTOM_PETS\.md|images/(task-center|floating-tray)\.png)$') {
        throw "Unexpected release file: $relative"
    }
}

function Assert-NoPrivateMachineMarker([string]$Path, [string]$Label) {
    $markers = @($source, $env:USERPROFILE, $env:USERNAME) | Where-Object { $_ -and $_.Length -ge 5 }
    $patterns = @($markers | ForEach-Object {
        [Text.Encoding]::Latin1.GetString([Text.Encoding]::UTF8.GetBytes($_))
        [Text.Encoding]::Latin1.GetString([Text.Encoding]::Unicode.GetBytes($_))
        [Text.Encoding]::Latin1.GetString([Text.Encoding]::UTF8.GetBytes($_.Replace('\', '/')))
    })
    if ($patterns.Count -eq 0) { return }
    $overlap = ($patterns | ForEach-Object Length | Measure-Object -Maximum).Maximum
    $stream = [IO.File]::OpenRead($Path)
    try {
        $buffer = [byte[]]::new(131072)
        $tail = ''
        while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $text = $tail + [Text.Encoding]::Latin1.GetString($buffer, 0, $read)
            foreach ($pattern in $patterns) {
                if ($text.Contains($pattern, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Private machine marker found in: $Label"
                }
            }
            $tail = $text.Substring([Math]::Max(0, $text.Length - $overlap))
        }
    } finally { $stream.Dispose() }
}
foreach ($relative in $tracked) {
    $path = Join-Path $source $relative
    if (Test-Path -LiteralPath $path -PathType Leaf) { Assert-NoPrivateMachineMarker $path $relative }
}
foreach ($file in $files) { Assert-NoPrivateMachineMarker $file.FullName ([IO.Path]::GetRelativePath($publish, $file.FullName)) }
Write-Host "PASS release privacy/allowlist check ($($tracked.Count) source files, $($files.Count) release files)."
