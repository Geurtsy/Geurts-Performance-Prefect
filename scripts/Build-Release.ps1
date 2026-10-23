param([string]$OutputDirectory = '', [string]$Version = '')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$sourceCommit = git -C $repoRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Release builds require a committed Git checkout.' }
$sourceStatus = git -C $repoRoot status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'Could not verify the release source status.' }
if ($sourceStatus) { throw 'Commit all source changes before building a release. Use dotnet publish for a development build.' }
& (Join-Path $PSScriptRoot 'Get-HardwareTools.ps1')
if (!$?) { throw 'Hardware detection helper preparation failed.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'Build\Release' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$packageDirectory = Join-Path $OutputDirectory 'package'
if (Test-Path $packageDirectory) { throw "Choose an empty output location: $packageDirectory already exists." }
$project = Join-Path $repoRoot 'Source\Geurts Performance Prefect\GeurtsPerformancePrefect.csproj'
$publishArguments = @('publish', $project, '-c', 'Release', '--self-contained', 'false', '-o', $packageDirectory, '--configfile', (Join-Path $repoRoot 'Source\NuGet.Config'), '-p:RequireHardwareTools=true')
if ($Version) { $publishArguments += "-p:Version=$Version" }
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'Source\Start-here.txt'), (Join-Path $repoRoot 'Source\Third-party-notices.txt') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $packageDirectory 'GeurtsPerformancePrefect.exe') -Destination (Join-Path $packageDirectory 'HardwareOverlay.exe')
$releaseVersion = ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $packageDirectory 'GeurtsPerformancePrefect.dll')).ProductVersion -split '\+')[0]
$files = [ordered]@{}
Get-ChildItem -LiteralPath $packageDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relativePath = [IO.Path]::GetRelativePath($packageDirectory, $_.FullName).Replace('\', '/')
    $files[$relativePath] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$finalCommit = git -C $repoRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $finalCommit -ne $sourceCommit) { throw 'Source commit changed while building. Rebuild from a clean checkout.' }
$finalStatus = git -C $repoRoot status --porcelain
if ($LASTEXITCODE -ne 0 -or $finalStatus) { throw 'Source changed while building. Rebuild from a clean checkout.' }
$manifest = @{ Version = $releaseVersion; Files = $files; Commit = $sourceCommit; SourceDirty = $false } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $packageDirectory 'update-manifest.json'), $manifest, [Text.UTF8Encoding]::new($false))
$archivePath = Join-Path $OutputDirectory 'GeurtsPerformancePrefect-win-x64.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath $packageDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relativePath = [IO.Path]::GetRelativePath($packageDirectory, $_.FullName).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $relativePath, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($archivePath + '.sha256', "$hash  GeurtsPerformancePrefect-win-x64.zip`n")
Write-Output "Version: $releaseVersion"
Write-Output "Package: $archivePath"
Write-Output "SHA256: $hash"
