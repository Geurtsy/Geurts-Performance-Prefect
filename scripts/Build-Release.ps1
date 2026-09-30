param([string]$OutputDirectory = '', [string]$Version = '')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'Build\Release' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$packageDirectory = Join-Path $OutputDirectory 'package'
if (Test-Path $packageDirectory) { throw "Choose an empty output location: $packageDirectory already exists." }
$project = Join-Path $repoRoot 'Source\Geurts Performance Prefect\GeurtsPerformancePrefect.csproj'
$publishArguments = @('publish', $project, '-c', 'Release', '--self-contained', 'false', '-o', $packageDirectory, '--configfile', (Join-Path $repoRoot 'Source\NuGet.Config'))
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
$sourceCommit = git -C $repoRoot rev-parse HEAD 2>$null
if ($LASTEXITCODE -ne 0) { $sourceCommit = '' }
$sourceDirty = [bool](git -C $repoRoot status --porcelain)
$manifest = @{ Version = $releaseVersion; Files = $files; Commit = $sourceCommit; SourceDirty = $sourceDirty } | ConvertTo-Json -Depth 4
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
