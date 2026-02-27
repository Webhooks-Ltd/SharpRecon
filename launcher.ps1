param(
    [string]$PublishDir = "$PSScriptRoot/src/SharpRecon/bin/publish"
)

$ErrorActionPreference = "Stop"
$repo = "Webhooks-Ltd/SharpRecon"

function Get-RuntimeId {
    $arch = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLower()
    if ($IsWindows -or ($null -eq $IsWindows)) { return "win-$arch" }
    if ($IsMacOS) { return "osx-$arch" }
    return "linux-$arch"
}

function Get-LatestReleaseTag {
    try {
        $headers = @{ Accept = "application/vnd.github+json" }
        $resp = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" `
            -Headers $headers -UseBasicParsing -TimeoutSec 5
        return $resp.tag_name
    } catch {
        return $null
    }
}

function Install-Release {
    param([string]$TargetDir, [string]$Tag)

    $rid = Get-RuntimeId
    $ext = if ($rid.StartsWith("win")) { "zip" } else { "tar.gz" }
    $fileName = "sharp-recon-$rid.$ext"

    $urlBase = if ($Tag) { "releases/download/$Tag" } else { "releases/latest/download" }
    $url = "https://github.com/$repo/$urlBase/$fileName"

    Write-Host "Downloading SharpRecon ($rid) from GitHub releases..." -ForegroundColor Cyan
    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) $fileName

    try {
        Invoke-WebRequest -Uri $url -OutFile $tempFile -UseBasicParsing
    } catch {
        throw "Failed to download $url — have you created a release? Error: $_"
    }

    if (Test-Path $TargetDir) {
        Remove-Item (Join-Path $TargetDir "*") -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null

    if ($ext -eq "zip") {
        Expand-Archive -Path $tempFile -DestinationPath $TargetDir -Force
    } else {
        tar -xzf $tempFile -C $TargetDir
    }

    Remove-Item $tempFile -Force -ErrorAction SilentlyContinue

    if ($Tag) {
        Set-Content -Path (Join-Path $TargetDir ".release-tag") -Value $Tag -NoNewline
    }

    Write-Host "Installed SharpRecon $Tag to $TargetDir" -ForegroundColor Green
}

$tagFile = Join-Path $PublishDir ".release-tag"
$localTag = if (Test-Path $tagFile) { Get-Content $tagFile -Raw } else { $null }
$hasFiles = (Test-Path $PublishDir) -and
    @(Get-ChildItem -Path $PublishDir -Filter "SharpRecon*" -ErrorAction SilentlyContinue).Count -gt 0

if (-not $hasFiles) {
    $latestTag = Get-LatestReleaseTag
    Install-Release -TargetDir $PublishDir -Tag $latestTag
} elseif ($localTag) {
    $latestTag = Get-LatestReleaseTag
    if ($latestTag -and $latestTag -ne $localTag) {
        Write-Host "Update available: $localTag -> $latestTag" -ForegroundColor Yellow
        Install-Release -TargetDir $PublishDir -Tag $latestTag
    }
}

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
