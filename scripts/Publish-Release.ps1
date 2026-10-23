param([Parameter(Mandatory)][string]$PackageDirectory, [Parameter(Mandatory)][string]$NotesFile)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$archivePath = Join-Path $PackageDirectory 'GeurtsPerformancePrefect-win-x64.zip'
$manifest = Get-Content -LiteralPath (Join-Path $PackageDirectory 'package\update-manifest.json') -Raw | ConvertFrom-Json
$tag = 'v' + $manifest.Version
$sourceStatus = git -C $repoRoot status --porcelain
if ($LASTEXITCODE -ne 0 -or $sourceStatus) { throw 'Commit all source changes before publishing.' }
if ((git -C $repoRoot branch --show-current) -ne 'main' -or $LASTEXITCODE -ne 0) { throw 'Publish from main.' }
$commit = git -C $repoRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $manifest.Commit -ne $commit -or $manifest.SourceDirty -ne $false) { throw 'Rebuild this package from the clean committed source before publishing.' }
$remote = git -C $repoRoot ls-remote origin refs/heads/main
if ($LASTEXITCODE -ne 0 -or !$remote) { throw 'Could not verify GitHub main. Nothing published.' }
$remoteCommit = $remote -split '\s+'
if ($commit -ne $remoteCommit[0]) { throw 'Push main before publishing; do not publish an obsolete checkout.' }
& (Join-Path $PSScriptRoot 'Test-Release.ps1') -PackageDirectory $PackageDirectory -ValidateOnly
if (!$?) { throw 'Package validation failed.' }
$receiptPath = Join-Path $PackageDirectory 'validation.json'
if (!(Test-Path -LiteralPath $receiptPath)) { throw 'Run scripts/Test-Release.ps1 before publishing.' }
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($receipt.Commit -ne $commit -or $receipt.Sha256 -ne $hash -or $receipt.Passed -ne $true) { throw 'Package has no matching successful application-test receipt.' }
# Hosted builds use a short-lived token; local publishing retains credential-manager support.
$token = $env:GITHUB_TOKEN
if (!$token) { $token = $env:GH_TOKEN }
if (!$token) {
    $credentialOutput = "protocol=https`nhost=github.com`n`n" | git credential fill
    if ($LASTEXITCODE -ne 0) { throw 'GitHub authentication failed.' }
    foreach ($line in $credentialOutput) {
        $pair = $line -split '=',2
        if ($pair.Count -eq 2 -and $pair[0] -eq 'password') { $token = $pair[1] }
    }
}
if (!$token) { throw 'Supply GITHUB_TOKEN or sign in with Git Credential Manager.' }
$headers = @{ Authorization = 'Bearer ' + $token; Accept = 'application/vnd.github+json'; 'User-Agent' = 'GeurtsPerformancePrefect-release'; 'X-GitHub-Api-Version' = '2026-03-10' }
$base = 'https://api.github.com/repos/Geurtsy/Geurts-Performance-Prefect'
$releases = @(Invoke-RestMethod "$base/releases?per_page=100" -Headers $headers | ForEach-Object { $_ })
if (@($releases | Where-Object { !$_.draft -and !$_.prerelease -and $_.tag_name -match '^v\d+\.\d+\.\d+$' -and [version]$_.tag_name.Substring(1) -gt [version]$manifest.Version }).Count) {
    throw 'Never publish an older version as the latest stable release.'
}
$existing = @($releases | Where-Object tag_name -EQ $tag)
if ($existing.Count -gt 1) { throw 'Duplicate release tags require review.' }
if ($existing.Count -eq 1) {
    $release = $existing[0]
    if (!$release.draft) { throw 'This version is already published. Never replace a published package; increment the version.' }
    if ($release.target_commitish -ne $commit) { throw 'An existing draft belongs to another commit. Review it before retrying.' }
    foreach ($asset in $release.assets) {
        if ($asset.name -in @('GeurtsPerformancePrefect-win-x64.zip', 'GeurtsPerformancePrefect-win-x64.zip.sha256')) {
            Invoke-RestMethod "$base/releases/assets/$($asset.id)" -Method Delete -Headers $headers | Out-Null
        }
    }
} else {
    $body = @{ tag_name = $tag; target_commitish = $commit; name = "Geurts Performance Prefect $($manifest.Version)"; body = [IO.File]::ReadAllText([IO.Path]::GetFullPath($NotesFile)); draft = $true; prerelease = $false } | ConvertTo-Json
    $release = Invoke-RestMethod "$base/releases" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
}
foreach ($assetPath in @($archivePath, ($archivePath + '.sha256'))) {
    $assetName = [IO.Path]::GetFileName($assetPath)
    $uploadUrl = ($release.upload_url -split '\{')[0] + '?name=' + [Uri]::EscapeDataString($assetName)
    $asset = Invoke-RestMethod $uploadUrl -Method Post -Headers $headers -ContentType 'application/octet-stream' -InFile $assetPath
    $expected = 'sha256:' + (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($asset.digest -ne $expected) { throw 'GitHub did not confirm an asset checksum. The release remains a draft.' }
}
$published = Invoke-RestMethod "$base/releases/$($release.id)" -Method Patch -Headers $headers -ContentType 'application/json' -Body '{"draft":false,"make_latest":"true"}'
$verified = $false
for ($attempt = 0; $attempt -lt 6; $attempt++) {
    $latest = Invoke-RestMethod "$base/releases/latest" -Headers $headers
    $windowsAsset = @($latest.assets | Where-Object name -EQ 'GeurtsPerformancePrefect-win-x64.zip')
    $checksumAsset = @($latest.assets | Where-Object name -EQ 'GeurtsPerformancePrefect-win-x64.zip.sha256')
    if ($latest.tag_name -eq $tag -and !$latest.draft -and !$latest.prerelease -and $windowsAsset.Count -eq 1 -and
        $windowsAsset[0].digest -eq ('sha256:' + $hash) -and $checksumAsset.Count -eq 1) { $verified = $true; break }
    Start-Sleep -Seconds 2
}
if (!$verified) { throw 'Publication is not complete: GitHub latest does not expose the expected package and checksum.' }
[ordered]@{ Version = $manifest.Version; Commit = $commit; Sha256 = $hash; Url = $published.html_url; LatestVerified = $true } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PackageDirectory 'published.json')
Write-Output $published.html_url
