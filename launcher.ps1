param(
    [string]$PublishDir = "$PSScriptRoot/src/SharpRecon/bin/publish"
)

$ErrorActionPreference = "Stop"

$baseTempDir = Join-Path ([System.IO.Path]::GetTempPath()) "SharpRecon"
if (-not (Test-Path $baseTempDir)) {
    New-Item -ItemType Directory -Path $baseTempDir -Force | Out-Null
}

$cutoff = (Get-Date).AddHours(-1)
Get-ChildItem -Path $baseTempDir -Directory -ErrorAction SilentlyContinue | Where-Object {
    $_.CreationTime -lt $cutoff
} | ForEach-Object {
    Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

$shadowDir = Join-Path $baseTempDir ([System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $shadowDir -Force | Out-Null

try {
    Copy-Item -Path (Join-Path $PublishDir "*") -Destination $shadowDir -Recurse -Force

    if ($IsWindows -or ($null -eq $IsWindows)) {
        $exe = Join-Path $shadowDir "SharpRecon.exe"
    } else {
        $exe = Join-Path $shadowDir "SharpRecon"
        chmod +x $exe 2>$null
    }

    & $exe
    exit $LASTEXITCODE
} finally {
    Remove-Item $shadowDir -Recurse -Force -ErrorAction SilentlyContinue
}
