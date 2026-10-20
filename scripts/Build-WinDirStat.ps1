param([Parameter(Mandatory)][string]$OutputDirectory, [string]$AtlRoot = '')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'Choose a fresh native build output directory.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$sourceArchive = Join-Path $repoRoot 'Source\Geurts Performance Prefect\Assets\WinDirStat\WinDirStat-source.zip'
Expand-Archive -LiteralPath $sourceArchive -DestinationPath $outputRoot
$nativeSource = Join-Path $outputRoot 'source'
$project = Join-Path $nativeSource 'windirstat.sln'
if (!(Test-Path -LiteralPath $project)) { throw 'The bundled native source project is missing.' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsRoot = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsRoot) { throw 'Visual Studio C++ build tools are required (v145, Windows SDK and ATL).' }
$msbuild = Join-Path $vsRoot 'MSBuild\Current\Bin\MSBuild.exe'
$arguments = @($project, '/p:Configuration=Release', '/p:Platform=x64', '/m:4', '/v:minimal',
    ('/p:OutDir=' + (Join-Path $outputRoot 'bin') + '\'), ('/p:IntDir=' + (Join-Path $outputRoot 'obj') + '\'))
if ($AtlRoot) {
    $AtlRoot = [IO.Path]::GetFullPath($AtlRoot)
    if (!(Test-Path -LiteralPath (Join-Path $AtlRoot 'include\atlbase.h'))) { throw 'AtlRoot must contain include\atlbase.h and lib\x64.' }
    $arguments += '/p:PrefectAtlRoot=' + $AtlRoot
}
& $msbuild @arguments
if ($LASTEXITCODE -ne 0) { throw 'Native WinDirStat build failed.' }
$binary = Join-Path $outputRoot 'bin\WinDirStat_x64.exe'
Write-Output "Built: $binary"
Write-Output ('SHA256: ' + (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant())
# This command builds a separate native dependency; it never installs or updates an app.
