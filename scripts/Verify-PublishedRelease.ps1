param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$receipt = Get-Content -LiteralPath (Join-Path $PackageDirectory 'published.json') -Raw | ConvertFrom-Json
$headers = @{ Accept = 'application/vnd.github+json'; 'User-Agent' = 'GeurtsPerformancePrefect-release' }
if ($env:GITHUB_TOKEN) { $headers.Authorization = 'Bearer ' + $env:GITHUB_TOKEN }
$latest = Invoke-RestMethod 'https://api.github.com/repos/Geurtsy/Geurts-Performance-Prefect/releases/latest' -Headers $headers
if ($latest.tag_name -ne ('v' + $receipt.Version) -or $latest.draft -or $latest.prerelease) { throw 'Latest stable release is not the version just published.' }
$asset = @($latest.assets | Where-Object name -EQ 'GeurtsPerformancePrefect-win-x64.zip')
$checksum = @($latest.assets | Where-Object name -EQ 'GeurtsPerformancePrefect-win-x64.zip.sha256')
if ($asset.Count -ne 1 -or $checksum.Count -ne 1 -or $asset[0].digest -ne ('sha256:' + $receipt.Sha256)) { throw 'Latest release assets do not match the validated package.' }
$download = Join-Path $PackageDirectory 'public-download.zip'
Invoke-WebRequest $asset[0].browser_download_url -OutFile $download
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $receipt.Sha256) { throw 'Public download checksum mismatch.' }
$checksumText = (Invoke-WebRequest $checksum[0].browser_download_url).Content
if ($checksumText -is [byte[]]) { $checksumText = [Text.Encoding]::UTF8.GetString($checksumText) }
if ($checksumText.Trim() -ne ($receipt.Sha256 + '  GeurtsPerformancePrefect-win-x64.zip')) { throw 'Public checksum file mismatch.' }
$fixture = [IO.Path]::GetFullPath((Join-Path $repoRoot 'Build\Updater-release-fixture'))
$marker = Join-Path $fixture 'disposable-fixture.json'
$smoke = 'No older packaged fixture was available.'
if (Test-Path -LiteralPath $marker) {
    $plan = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
    $target = [IO.Path]::GetFullPath((Join-Path $fixture 'application'))
    if ($plan.Target -ne $target -or $plan.ExpectedVersion -ne $receipt.Version -or [version]$plan.PreviousVersion -ge [version]$receipt.Version) { throw 'Invalid disposable updater fixture.' }
    $executable = Join-Path $target 'GeurtsPerformancePrefect.exe'
    $oldVersion = ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $target 'GeurtsPerformancePrefect.dll')).ProductVersion -split '\+')[0]
    if ($oldVersion -ne $plan.PreviousVersion) { throw 'The updater fixture executable is not the expected older version.' }
    $report = Join-Path $fixture 'updater-smoke.txt'
    $process = Start-Process -FilePath $executable -ArgumentList '--update-smoke', ('"' + $report + '"') -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'Older fixture updater did not exit.' }
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    while (!(Test-Path -LiteralPath $report) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Seconds 1 }
    if (!(Test-Path -LiteralPath $report)) { throw 'Updater fixture did not produce a result.' }
    $smoke = Get-Content -LiteralPath $report -Raw
    $installed = Get-Content -LiteralPath (Join-Path $target 'update-manifest.json') -Raw | ConvertFrom-Json
    if ($smoke -notmatch '^PASS:' -or $installed.Version -ne $receipt.Version -or $installed.Commit -ne $receipt.Commit) { throw "Disposable updater verification failed: $smoke" }
}
$receipt | Add-Member -NotePropertyName PublicDownloadVerified -NotePropertyValue $true -Force
$receipt | Add-Member -NotePropertyName UpdaterSmoke -NotePropertyValue $smoke.Trim() -Force
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PackageDirectory 'published.json')
Write-Output "Verified public release $($receipt.Version), downloaded SHA256 $($receipt.Sha256)."
Write-Output $smoke
