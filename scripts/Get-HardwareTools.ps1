$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$toolDirectory = Join-Path $repoRoot 'Build\HardwareTools'
$archive = Join-Path $toolDirectory 'fastfetch-windows-amd64.zip'
$expected = '89e5a20f46ac04b154c7d0ea13dbac97e43f799a98d7ae4c598863ba357a8c7f'
New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    $download = Join-Path $toolDirectory ('download-' + [Guid]::NewGuid().ToString('N') + '.zip')
    try {
        Invoke-WebRequest 'https://github.com/fastfetch-cli/fastfetch/releases/download/2.70.0/fastfetch-windows-amd64.zip' -OutFile $download
        if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'The pinned Fastfetch archive did not match its SHA256.' }
        Move-Item -LiteralPath $download -Destination $archive -Force
    } finally { if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download } }
}
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try { if (@($zip.Entries | Where-Object Name -EQ 'fastfetch.exe').Count -ne 1) { throw 'Pinned Fastfetch archive has no unique Windows executable.' } }
finally { $zip.Dispose() }
Write-Output 'Pinned Fastfetch 2.70.0 Windows archive verified for embedding.'
