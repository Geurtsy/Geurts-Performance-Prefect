param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath (Join-Path $PackageDirectory 'package\update-manifest.json') -Raw | ConvertFrom-Json
$headers = @{ Accept = 'application/vnd.github+json'; 'User-Agent' = 'GeurtsPerformancePrefect-release' }
if ($env:GITHUB_TOKEN) { $headers.Authorization = 'Bearer ' + $env:GITHUB_TOKEN }
$releases = Invoke-RestMethod 'https://api.github.com/repos/Geurtsy/Geurts-Performance-Prefect/releases?per_page=100' -Headers $headers
$previous = $releases | Where-Object { !$_.draft -and !$_.prerelease -and $_.tag_name -match '^v\d+\.\d+\.\d+$' -and [version]$_.tag_name.Substring(1) -lt [version]$manifest.Version } |
    Sort-Object { [version]$_.tag_name.Substring(1) } -Descending | Select-Object -First 1
if (!$previous) { Write-Output 'No older stable packaged fixture is available.'; return }
$asset = @($previous.assets | Where-Object name -EQ 'GeurtsPerformancePrefect-win-x64.zip')
if ($asset.Count -ne 1 -or $asset[0].digest -notmatch '^sha256:[0-9a-f]{64}$') { throw 'Previous release has no verified Windows package.' }
$fixture = [IO.Path]::GetFullPath((Join-Path $repoRoot 'Build\Updater-release-fixture'))
if (Test-Path -LiteralPath $fixture) { throw 'Updater fixture must be a fresh directory.' }
New-Item -ItemType Directory -Path $fixture | Out-Null
$archivePath = Join-Path $fixture 'previous.zip'
Invoke-WebRequest $asset[0].browser_download_url -OutFile $archivePath
if (('sha256:' + (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()) -ne $asset[0].digest) { throw 'Previous fixture package checksum mismatch.' }
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -match '(^/|\\|(^|/)\.\.(/|$)|:)') { throw 'Unsafe previous package path.' }
    }
} finally { $archive.Dispose() }
$target = Join-Path $fixture 'application'
Expand-Archive -LiteralPath $archivePath -DestinationPath $target
[ordered]@{ Target = $target; PreviousVersion = $previous.tag_name.Substring(1); ExpectedVersion = $manifest.Version } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixture 'disposable-fixture.json')
Write-Output "Prepared older packaged fixture: $($previous.tag_name)"
