param([Parameter(Mandatory)][string]$PackageDirectory, [switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$archivePath = Join-Path $PackageDirectory 'GeurtsPerformancePrefect-win-x64.zip'
$package = Join-Path $PackageDirectory 'package'
$manifest = Get-Content -LiteralPath (Join-Path $package 'update-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.SourceDirty -ne $false -or $manifest.Commit -notmatch '^[0-9a-f]{40}$') { throw 'Release package must identify clean committed source.' }
if ($manifest.Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Release package version is invalid.' }
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksum = (Get-Content -LiteralPath ($archivePath + '.sha256') -Raw).Trim()
if ($checksum -ne "$hash  GeurtsPerformancePrefect-win-x64.zip") { throw 'Archive checksum mismatch.' }
$files = @($manifest.Files.PSObject.Properties)
if ($files.Count -lt 5 -or $files.Count -gt 256) { throw 'Invalid package file count.' }
foreach ($file in $files) {
    if ($file.Name -match '(^/|\\|(^|/)\.\.(/|$)|:)' -or $file.Value -notmatch '^[0-9a-f]{64}$') { throw 'Invalid package file path or hash.' }
    if ((Get-FileHash -LiteralPath (Join-Path $package $file.Name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.Value) { throw "Package file hash mismatch: $($file.Name)" }
}
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entries = @($archive.Entries)
    if ($entries.Count -ne $files.Count + 1 -or @($entries.FullName | Sort-Object -Unique).Count -ne $entries.Count) { throw 'Archive file count mismatch or duplicate paths.' }
    foreach ($entry in $entries) {
        if ($entry.FullName -ne 'update-manifest.json' -and $entry.FullName -notin $files.Name) { throw 'Archive contains an unmanaged file.' }
        $stream = $entry.Open()
        try { $entryHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
        finally { $stream.Dispose() }
        $expected = if ($entry.FullName -eq 'update-manifest.json') { (Get-FileHash -LiteralPath (Join-Path $package $entry.FullName) -Algorithm SHA256).Hash.ToLowerInvariant() } else { $manifest.Files.($entry.FullName) }
        if ($entryHash -ne $expected) { throw "Archive contents differ from validated package: $($entry.FullName)" }
    }
} finally { $archive.Dispose() }
$application = Join-Path $package 'GeurtsPerformancePrefect.exe'
$version = ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $package 'GeurtsPerformancePrefect.dll')).ProductVersion -split '\+')[0]
if ($version -ne $manifest.Version) { throw 'Application version does not match the manifest.' }
if ($ValidateOnly) { Write-Output "Validated $($files.Count) packaged files and ZIP checksum."; return }
$reportDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'Verification\Release'
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$receiptPath = Join-Path $PackageDirectory 'validation.json'
if (Test-Path -LiteralPath $receiptPath) { Remove-Item -LiteralPath $receiptPath }
foreach ($mode in @('--self-test', '--exit-test', '--power-options-test', '--hardware-detection-test', '--fan-control-test', '--settings-hud-test', '--settings-window-test')) {
    $report = Join-Path $reportDirectory ($mode.TrimStart('-') + '.txt')
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
    $process = Start-Process -FilePath $application -ArgumentList $mode, ('"' + $report + '"') -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(600000)) { $process.Kill(); throw "Packaged $mode timed out." }
    if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report | Write-Output }
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report)) { throw "Packaged $mode failed. See $report" }
    $result = Get-Content -LiteralPath $report -Raw
    if ($result -match '(?im)^(FAILED\b|[A-Za-z0-9_.]*Exception:)' -or $result -notmatch '(?m)^PASS: ' -or
        ($mode -eq '--self-test' -and $result -notmatch '(?m)^\d+ checks passed\.')) { throw "Packaged $mode did not finish successfully. See $report" }
}
[ordered]@{ Version = $manifest.Version; Commit = $manifest.Commit; Sha256 = $hash; Passed = $true } |
    ConvertTo-Json | Set-Content -LiteralPath $receiptPath
Write-Output 'Packaged application tests passed.'
