$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $repoRoot ('Verification\release-script-fixtures-' + [Guid]::NewGuid().ToString('N'))
$bundle = Join-Path $folder 'bundle'
$package = Join-Path $bundle 'package'
New-Item -ItemType Directory -Path $package -Force | Out-Null
$log = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw "FAILED: $Name" }
    $log.Add("PASS: $Name"); Write-Output "PASS: $Name"
}
function Rejects([scriptblock]$Action, [string]$Expected) {
    try { & $Action | Out-Null }
    catch { if ($_.Exception.Message -notlike ('*' + $Expected + '*')) { throw }; return }
    throw "Expected rejection: $Expected"
}
foreach ($path in (Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1').FullName) {
    $tokens = $null; $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count) { throw "PowerShell parse errors in $path : $errors" }
}
Check $true 'All release PowerShell scripts parse successfully'

# Compile a harmless versioned DLL and package placeholders. No executable in
# this fixture is launched, and all network/Git commands below are simulated.
$fixtureProject = Join-Path $folder 'VersionFixture.csproj'
[IO.File]::WriteAllText($fixtureProject, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Version>9.8.7</Version><AssemblyName>GeurtsPerformancePrefect</AssemblyName></PropertyGroup></Project>')
[IO.File]::WriteAllText((Join-Path $folder 'VersionFixture.cs'), 'public class InertReleaseVersionFixture {}')
& dotnet build $fixtureProject -c Release -o $package --configfile (Join-Path $repoRoot 'Source\NuGet.Config') --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Could not build the inert release guard fixture.' }
foreach ($name in @('GeurtsPerformancePrefect.exe', 'HardwareOverlay.exe', 'GeurtsPerformancePrefect.deps.json', 'GeurtsPerformancePrefect.runtimeconfig.json')) {
    [IO.File]::WriteAllText((Join-Path $package $name), 'inert release guard fixture')
}
$fixtureCommit = 'a' * 40
$manifest = [ordered]@{ Version = '9.8.7'; Commit = $fixtureCommit; SourceDirty = $false; Files = [ordered]@{} }
foreach ($file in Get-ChildItem -LiteralPath $package -File) { $manifest.Files[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifestPath = Join-Path $package 'update-manifest.json'
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
$archivePath = Join-Path $bundle 'GeurtsPerformancePrefect-win-x64.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($package, $archivePath)
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$originalArchive = [IO.File]::ReadAllBytes($archivePath)
$checksumPath = $archivePath + '.sha256'
[IO.File]::WriteAllText($checksumPath, "$hash  GeurtsPerformancePrefect-win-x64.zip`n")
$notesPath = Join-Path $bundle 'notes.md'; [IO.File]::WriteAllText($notesPath, 'Release guard fixture')
$receiptPath = Join-Path $bundle 'validation.json'
$publishedPath = Join-Path $bundle 'published.json'
$global:PrefectReleaseGuardState = @{}; $global:PrefectReleaseGuardState.Dirty = $true; $global:PrefectReleaseGuardState.GitFailed = $false; $global:PrefectReleaseGuardState.RemoteMoved = $false
function git {
    $global:LASTEXITCODE = 0
    if ($global:PrefectReleaseGuardState.GitFailed) { $global:LASTEXITCODE = 1; return }
    if ($args -contains '--porcelain') { if ($global:PrefectReleaseGuardState.Dirty) { ' M fixture.txt' }; return }
    if ($args -contains '--show-current') { 'main'; return }
    if ($args -contains 'rev-parse') { $fixtureCommit; return }
    if ($args -contains 'ls-remote') { $(if ($global:PrefectReleaseGuardState.RemoteMoved) { 'b' * 40 } else { $fixtureCommit }) + "`trefs/heads/main"; return }
    throw 'Unexpected Git command in release guard fixture'
}
function dotnet { throw 'A rejected release build must not invoke dotnet.' }
$global:PrefectReleaseGuardState.Calls = [Collections.Generic.List[string]]::new()
$global:PrefectReleaseGuardState.BadDigest = $false; $global:PrefectReleaseGuardState.WrongLatest = $false; $global:PrefectReleaseGuardState.AlreadyPublished = $false; $global:PrefectReleaseGuardState.Draft = $false; $global:PrefectReleaseGuardState.Published = $false
$global:PrefectReleaseGuardState.NewerVersion = $false
function Start-Sleep { param($Seconds) }
function Invoke-RestMethod {
    param([string]$Uri, [string]$Method = 'Get', $Headers, $ContentType, $Body, $InFile)
    $global:PrefectReleaseGuardState.Calls.Add("$Method $Uri")
    if ($Uri -like '*releases?per_page=100' -and $Method -eq 'Get') {
        if ($global:PrefectReleaseGuardState.NewerVersion) { return [pscustomobject]@{ draft = $false; prerelease = $false; tag_name = 'v9.9.0' } }
        if ($global:PrefectReleaseGuardState.AlreadyPublished -or $global:PrefectReleaseGuardState.Draft) {
            return [pscustomobject]@{ id = 123; draft = !$global:PrefectReleaseGuardState.AlreadyPublished; tag_name = 'v9.8.7'; target_commitish = $fixtureCommit;
                upload_url = 'https://uploads.github.com/repos/Geurtsy/Geurts-Performance-Prefect/releases/123/assets{?name,label}'; assets = @() }
        }
        return @()
    }
    if ($Method -eq 'Post' -and $Uri -like '*/releases') {
        return [pscustomobject]@{ id = 123; upload_url = 'https://uploads.github.com/repos/Geurtsy/Geurts-Performance-Prefect/releases/123/assets{?name,label}' }
    }
    if ($Method -eq 'Post' -and $InFile) {
        $digest = (Get-FileHash -LiteralPath $InFile -Algorithm SHA256).Hash.ToLowerInvariant()
        return [pscustomobject]@{ digest = 'sha256:' + $(if ($global:PrefectReleaseGuardState.BadDigest) { '0' * 64 } else { $digest }) }
    }
    if ($Method -eq 'Patch' -and $Uri -like '*/releases/123') { $global:PrefectReleaseGuardState.Published = $true; return [pscustomobject]@{ html_url = 'https://github.com/Geurtsy/Geurts-Performance-Prefect/releases/tag/v9.8.7' } }
    if ($Method -eq 'Get' -and $Uri -like '*/releases/latest') {
        return [pscustomobject]@{ tag_name = $(if ($global:PrefectReleaseGuardState.WrongLatest) { 'v9.8.6' } else { 'v9.8.7' }); draft = $false; prerelease = $false;
            assets = @([pscustomobject]@{ name = 'GeurtsPerformancePrefect-win-x64.zip'; digest = 'sha256:' + $hash }, [pscustomobject]@{ name = 'GeurtsPerformancePrefect-win-x64.zip.sha256' }) }
    }
    throw 'Unexpected network operation in release guard fixture'
}
$previousToken = $env:GITHUB_TOKEN
$env:GITHUB_TOKEN = 'inert-release-guard-fixture'
try {
    Rejects { & "$PSScriptRoot/Build-Release.ps1" -OutputDirectory (Join-Path $folder 'rejected-build') } 'Commit all source changes before building'
    Check (!(Test-Path -LiteralPath (Join-Path $folder 'rejected-build'))) 'Dirty source is rejected before creating a release output or invoking dotnet'
    $global:PrefectReleaseGuardState.GitFailed = $true
    Rejects { & "$PSScriptRoot/Build-Release.ps1" -OutputDirectory (Join-Path $folder 'failed-git-build') } 'committed Git checkout'
    Check $true 'Git command failures cannot be mistaken for a clean source checkout'
    $global:PrefectReleaseGuardState.GitFailed = $false; $global:PrefectReleaseGuardState.Dirty = $false
    & "$PSScriptRoot/Test-Release.ps1" -PackageDirectory $bundle -ValidateOnly | Out-Null
    Check $true 'A complete clean fixture passes file, archive and version validation'
    $manifest.SourceDirty = $true; $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
    Rejects { & "$PSScriptRoot/Test-Release.ps1" -PackageDirectory $bundle -ValidateOnly } 'clean committed source'
    Check $true 'A dirty package manifest is rejected'
    $manifest.SourceDirty = $false; $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
    [IO.File]::WriteAllText($checksumPath, ('0' * 64) + '  GeurtsPerformancePrefect-win-x64.zip')
    Rejects { & "$PSScriptRoot/Test-Release.ps1" -PackageDirectory $bundle -ValidateOnly } 'Archive checksum mismatch'
    Check $true 'An incorrect ZIP checksum prevents release validation'
    [IO.File]::WriteAllText($checksumPath, "$hash  GeurtsPerformancePrefect-win-x64.zip`n")
    $changedArchive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $changedArchive.GetEntry('GeurtsPerformancePrefect.exe').Delete()
        $stream = $changedArchive.CreateEntry('GeurtsPerformancePrefect.exe').Open()
        try { $bytes = [Text.Encoding]::UTF8.GetBytes('tampered archived executable'); $stream.Write($bytes, 0, $bytes.Length) }
        finally { $stream.Dispose() }
    } finally { $changedArchive.Dispose() }
    $changedHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($checksumPath, "$changedHash  GeurtsPerformancePrefect-win-x64.zip`n")
    Rejects { & "$PSScriptRoot/Test-Release.ps1" -PackageDirectory $bundle -ValidateOnly } 'Archive contents differ'
    Check $true 'Archive contents are checked against the package even when the outer ZIP checksum is correct'
    [IO.File]::WriteAllBytes($archivePath, $originalArchive)
    [IO.File]::WriteAllText($checksumPath, "$hash  GeurtsPerformancePrefect-win-x64.zip`n")
    $inertExe = Join-Path $package 'GeurtsPerformancePrefect.exe'; [IO.File]::AppendAllText($inertExe, 'tampered')
    Rejects { & "$PSScriptRoot/Test-Release.ps1" -PackageDirectory $bundle -ValidateOnly } 'Package file hash mismatch'
    Check $true 'A modified packaged executable prevents release validation'
    [IO.File]::WriteAllText($inertExe, 'inert release guard fixture')
    Rejects { & "$PSScriptRoot/Publish-Release.ps1" -PackageDirectory $bundle -NotesFile $notesPath } 'Run scripts/Test-Release.ps1'
    Check ($global:PrefectReleaseGuardState.Calls.Count -eq 0) 'Publishing without successful application tests stops before contacting GitHub'
    @{ Commit = $fixtureCommit; Sha256 = '0' * 64; Passed = $true } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath
    Rejects { & "$PSScriptRoot/Publish-Release.ps1" -PackageDirectory $bundle -NotesFile $notesPath } 'matching successful application-test receipt'
    Check ($global:PrefectReleaseGuardState.Calls.Count -eq 0) 'A test receipt for another package cannot authorize publication'
    @{ Commit = $fixtureCommit; Sha256 = $hash; Passed = $true } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath
    $global:PrefectReleaseGuardState.RemoteMoved = $true
    Rejects { & "$PSScriptRoot/Publish-Release.ps1" -PackageDirectory $bundle -NotesFile $notesPath } 'obsolete checkout'
    Check ($global:PrefectReleaseGuardState.Calls.Count -eq 0) 'A newer main commit prevents an obsolete release upload'
    $global:PrefectReleaseGuardState.RemoteMoved = $false; $global:PrefectReleaseGuardState.NewerVersion = $true
    Rejects { & "$PSScriptRoot/Publish-Release.ps1" -PackageDirectory $bundle -NotesFile $notesPath } 'Never publish an older version'
    Check (@($global:PrefectReleaseGuardState.Calls | Where-Object { $_ -match '^(Post|Patch|Delete) ' }).Count -eq 0) 'A release cannot downgrade the latest stable version'
    $global:PrefectReleaseGuardState.NewerVersion = $false; $global:PrefectReleaseGuardState.BadDigest = $true
    Rejects { & "$PSScriptRoot/Publish-Release.ps1" -PackageDirectory $bundle -NotesFile $notesPath } 'asset checksum'
    Check (!$global:PrefectReleaseGuardState.Published) 'An upload digest mismatch keeps the release unpublished'
    $global:PrefectReleaseGuardState.BadDigest = $false; $global:PrefectReleaseGuardState.WrongLatest = $true
    Rejects { & "$PSScriptRoot/Publish-Release.ps1" -PackageDirectory $bundle -NotesFile $notesPath } 'GitHub latest'
    Check (!(Test-Path -LiteralPath $publishedPath)) 'Publication is not reported complete when latest still exposes the older version'
    $global:PrefectReleaseGuardState.WrongLatest = $false; $global:PrefectReleaseGuardState.Draft = $true
    & "$PSScriptRoot/Publish-Release.ps1" -PackageDirectory $bundle -NotesFile $notesPath | Out-Null
    $receipt = Get-Content -LiteralPath $publishedPath -Raw | ConvertFrom-Json
    Check ($receipt.LatestVerified -and $receipt.Sha256 -eq $hash) 'A matching interrupted draft can resume and produce verified delivery evidence'
    $global:PrefectReleaseGuardState.AlreadyPublished = $true; $global:PrefectReleaseGuardState.Calls.Clear()
    Rejects { & "$PSScriptRoot/Publish-Release.ps1" -PackageDirectory $bundle -NotesFile $notesPath } 'Never replace a published package'
    Check (@($global:PrefectReleaseGuardState.Calls | Where-Object { $_ -match '^(Post|Patch|Delete) ' }).Count -eq 0) 'Retrying an existing published version never replaces its assets'
} finally {
    $env:GITHUB_TOKEN = $previousToken
    $log | Set-Content -LiteralPath (Join-Path $folder 'release-guard-checks.txt')
    Remove-Variable -Name PrefectReleaseGuardState -Scope Global
}
Write-Output "$($log.Count) release guard checks passed. All Git/network operations were simulated."
