param([Parameter(Mandatory)][string]$PackageDirectory, [Parameter(Mandatory)][string]$NotesFile)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$archivePath = Join-Path $PackageDirectory 'GeurtsPerformancePrefect-win-x64.zip'
$manifest = Get-Content -LiteralPath (Join-Path $PackageDirectory 'package\update-manifest.json') -Raw | ConvertFrom-Json
$tag = 'v' + $manifest.Version
if ((git -C $repoRoot status --porcelain).Length -gt 0) { throw 'Commit all source changes before publishing.' }
if ((git -C $repoRoot branch --show-current) -ne 'main') { throw 'Publish from main.' }
$commit = git -C $repoRoot rev-parse HEAD
if ($manifest.Commit -ne $commit -or $manifest.SourceDirty) { throw 'Rebuild this package from the clean committed source before publishing.' }
$remoteCommit = (git -C $repoRoot ls-remote origin refs/heads/main) -split '\s+'
if ($commit -ne $remoteCommit[0]) { throw 'Push main before publishing.' }
$credentialOutput = "protocol=https`nhost=github.com`n`n" | git credential fill
$credentialValues = @{}
foreach ($line in $credentialOutput) { $pair = $line -split '=',2; if ($pair.Count -eq 2) { $credentialValues[$pair[0]] = $pair[1] } }
if (!$credentialValues.password) { throw 'Sign in to GitHub with Git Credential Manager first.' }
$headers = @{ Authorization = 'Bearer ' + $credentialValues.password; Accept = 'application/vnd.github+json'; 'User-Agent' = 'GeurtsPerformancePrefect-release'; 'X-GitHub-Api-Version' = '2026-03-10' }
$base = 'https://api.github.com/repos/Geurtsy/Geurts-Performance-Prefect'
$body = @{ tag_name = $tag; target_commitish = $commit; name = "Geurts Performance Prefect $($manifest.Version)"; body = [IO.File]::ReadAllText([IO.Path]::GetFullPath($NotesFile)); draft = $true; prerelease = $false } | ConvertTo-Json
$release = Invoke-RestMethod "$base/releases" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
foreach ($assetPath in @($archivePath, ($archivePath + '.sha256'))) {
    $assetName = [IO.Path]::GetFileName($assetPath)
    $uploadUrl = ($release.upload_url -split '\{')[0] + '?name=' + [Uri]::EscapeDataString($assetName)
    $asset = Invoke-RestMethod $uploadUrl -Method Post -Headers $headers -ContentType 'application/octet-stream' -InFile $assetPath
    if ($assetName.EndsWith('.zip')) {
        $expected = 'sha256:' + (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($asset.digest -ne $expected) { throw 'GitHub did not confirm the package checksum. The release remains a draft.' }
    }
}
$published = Invoke-RestMethod "$base/releases/$($release.id)" -Method Patch -Headers $headers -ContentType 'application/json' -Body '{"draft":false,"make_latest":"true"}'
Write-Output $published.html_url
