param([string]$Version = '0.1.1')

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$files = @(
    (Join-Path $repo 'mod\bin\Release\CS1McpBridge.dll'),
    (Join-Path $repo 'LICENSE'),
    (Join-Path $repo 'WINDOWS-INSTALL.md')
)
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Release input missing: $file" }
}
$dist = Join-Path $repo 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$archive = Join-Path $dist "cs1-mcp-windows-v$Version.zip"
if (Test-Path -LiteralPath $archive) { throw "Archive already exists: $archive" }
Compress-Archive -LiteralPath $files -DestinationPath $archive -CompressionLevel Optimal
Copy-Item -LiteralPath (Join-Path $repo 'RELEASE-NOTES.md') -Destination (Join-Path $dist 'release-notes.md') -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $archive)" | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS') -Encoding ascii
Write-Host "Created $archive with only: CS1McpBridge.dll, LICENSE, WINDOWS-INSTALL.md"
