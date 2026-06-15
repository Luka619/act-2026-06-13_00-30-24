[CmdletBinding()]
param(
    [string[]]$ArtifactId,
    [string]$Destination,
    [string]$Proxy,
    [switch]$List
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir '..')
$ManifestPath = Join-Path $RepoRoot 'installers\installers.json'

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    throw "Installer manifest not found: $ManifestPath"
}

if (-not $Destination) {
    $Destination = Join-Path $RepoRoot 'installers\downloads'
}

if (-not $Proxy) {
    if ($env:HTTPS_PROXY) {
        $Proxy = $env:HTTPS_PROXY
    } elseif ($env:HTTP_PROXY) {
        $Proxy = $env:HTTP_PROXY
    }
}

$Manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
$Artifacts = @($Manifest.artifacts)

function Format-Bytes {
    param([long]$Bytes)

    if ($Bytes -ge 1GB) {
        return ('{0:N2} GB' -f ($Bytes / 1GB))
    }

    if ($Bytes -ge 1MB) {
        return ('{0:N2} MB' -f ($Bytes / 1MB))
    }

    return "$Bytes bytes"
}

function Test-ExistingArtifact {
    param(
        [Parameter(Mandatory = $true)]$Artifact,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $Item = Get-Item -LiteralPath $Path
    $ExpectedSize = [long]$Artifact.sizeBytes

    if ($Item.Length -gt $ExpectedSize) {
        throw "Existing file is larger than expected: $Path"
    }

    if ($Item.Length -lt $ExpectedSize) {
        Write-Host "Resuming partial file: $($Artifact.fileName) ($(Format-Bytes $Item.Length) of $(Format-Bytes $ExpectedSize))"
        return $false
    }

    $Hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Hash -ne $Artifact.sha256) {
        throw "SHA256 mismatch for existing file: $Path"
    }

    Write-Host "Already downloaded and verified: $($Artifact.fileName)"
    return $true
}

if ($List) {
    $Artifacts | ForEach-Object {
        [PSCustomObject]@{
            Id = $_.id
            Name = $_.name
            Version = $_.version
            Size = Format-Bytes ([long]$_.sizeBytes)
            FileName = $_.fileName
        }
    } | Format-Table -AutoSize
    return
}

if ($ArtifactId -and $ArtifactId.Count -gt 0) {
    $Requested = @{}
    foreach ($Id in $ArtifactId) {
        $Requested[$Id] = $true
    }

    $Artifacts = @($Artifacts | Where-Object { $Requested.ContainsKey($_.id) })

    foreach ($Id in $ArtifactId) {
        if (-not ($Artifacts | Where-Object { $_.id -eq $Id })) {
            throw "Unknown installer artifact id: $Id"
        }
    }
}

if ($Artifacts.Count -eq 0) {
    throw 'No installer artifacts selected.'
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null

$Curl = Get-Command curl.exe -ErrorAction SilentlyContinue
if (-not $Curl) {
    throw 'curl.exe is required for resumable downloads on Windows.'
}

foreach ($Artifact in $Artifacts) {
    $TargetPath = Join-Path $Destination $Artifact.fileName

    if (Test-ExistingArtifact -Artifact $Artifact -Path $TargetPath) {
        continue
    }

    Write-Host "Downloading $($Artifact.fileName) ($(Format-Bytes ([long]$Artifact.sizeBytes)))"

    $CurlArgs = @(
        '-L',
        '--fail',
        '--retry', '10',
        '--retry-delay', '3',
        '--retry-all-errors',
        '-o', $TargetPath
    )

    if ($Proxy) {
        $CurlArgs = @('--proxy', $Proxy) + $CurlArgs
        Write-Host "Using proxy: $Proxy"
    }

    if (Test-Path -LiteralPath $TargetPath) {
        $CurlArgs += @('-C', '-')
    }

    $CurlArgs += $Artifact.url
    & $Curl.Source @CurlArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Download failed for $($Artifact.id). Exit code: $LASTEXITCODE"
    }

    $Item = Get-Item -LiteralPath $TargetPath
    if ($Item.Length -ne [long]$Artifact.sizeBytes) {
        throw "Size mismatch for $TargetPath. Expected $($Artifact.sizeBytes), got $($Item.Length)."
    }

    $Hash = (Get-FileHash -LiteralPath $TargetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Hash -ne $Artifact.sha256) {
        throw "SHA256 mismatch for $TargetPath. Expected $($Artifact.sha256), got $Hash."
    }

    Write-Host "Verified: $($Artifact.fileName)"
}
