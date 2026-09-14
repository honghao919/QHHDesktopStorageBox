[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$SigningCertificatePath,
    [string]$SigningCertificatePassword,
    [string]$SignToolPath,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'src\QHHDesktopStorageBox.App\QHHDesktopStorageBox.App.csproj'
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
$manualPath = Join-Path $repoRoot 'docs\QHH-Desktop-Storage-Box-User-Manual.pdf'

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $Version = $props.Project.PropertyGroup.Version
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
    throw "Version must be a numeric semantic version, but was '$Version'."
}

if (-not (Test-Path -LiteralPath $manualPath)) {
    throw "User manual is missing: $manualPath"
}

function Resolve-SignToolPath {
    param([string]$RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $resolvedRequestedPath = (Resolve-Path -LiteralPath $RequestedPath -ErrorAction Stop).Path
        if (-not (Test-Path -LiteralPath $resolvedRequestedPath -PathType Leaf)) {
            throw "SignTool was not found: $resolvedRequestedPath"
        }

        return $resolvedRequestedPath
    }

    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kitsRoot) {
        return Get-ChildItem -LiteralPath $kitsRoot -Recurse -Filter signtool.exe -File |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }

    return $null
}

function Invoke-AuthenticodeSigning {
    param(
        [string]$FilePath,
        [string]$CertificatePath,
        [string]$CertificatePassword,
        [string]$RequestedSignToolPath,
        [string]$TimestampServer
    )

    $resolvedSignToolPath = Resolve-SignToolPath -RequestedPath $RequestedSignToolPath
    if (-not $resolvedSignToolPath) {
        throw 'SignTool is required when a signing certificate is provided.'
    }

    & $resolvedSignToolPath sign `
        /fd SHA256 `
        /td SHA256 `
        /tr $TimestampServer `
        /f $CertificatePath `
        /p $CertificatePassword `
        $FilePath
    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode signing failed for $FilePath with exit code $LASTEXITCODE."
    }
}

$shouldSign = -not [string]::IsNullOrWhiteSpace($SigningCertificatePath)
if ($shouldSign) {
    $SigningCertificatePath = (Resolve-Path -LiteralPath $SigningCertificatePath -ErrorAction Stop).Path
    if ([string]::IsNullOrWhiteSpace($SigningCertificatePassword)) {
        throw 'SigningCertificatePassword is required when SigningCertificatePath is provided.'
    }
}

$publishDir = Join-Path $repoRoot "publish\v$Version"
$zipPath = Join-Path $repoRoot "publish\QHHDesktopStorageBox-v$Version-$RuntimeIdentifier.zip"
$shaPath = "$zipPath.sha256"
$installerPath = Join-Path $repoRoot "publish\QHHDesktopStorageBox-Setup-v$Version-x64.exe"
$installerShaPath = "$installerPath.sha256"
$publishedManualPath = Join-Path $repoRoot 'publish\QHH-Desktop-Storage-Box-User-Manual.pdf'

New-Item -ItemType Directory -Path (Split-Path $publishDir) -Force | Out-Null
if (Test-Path -LiteralPath $publishDir) {
    Get-ChildItem -LiteralPath $publishDir -Force | Remove-Item -Recurse -Force
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$appExe = Join-Path $publishDir 'QHHDesktopStorageBox.App.exe'
if (-not (Test-Path -LiteralPath $appExe)) {
    throw "Publish output is missing $appExe."
}

if ($shouldSign) {
    Invoke-AuthenticodeSigning `
        -FilePath $appExe `
        -CertificatePath $SigningCertificatePath `
        -CertificatePassword $SigningCertificatePassword `
        -RequestedSignToolPath $SignToolPath `
        -TimestampServer $TimestampUrl
}

Copy-Item -LiteralPath $manualPath -Destination (Join-Path $publishDir 'QHH-Desktop-Storage-Box-User-Manual.pdf') -Force
Copy-Item -LiteralPath $manualPath -Destination $publishedManualPath -Force

# Package every publish output. This remains correct if a future runtime or
# framework requires files beside the executable.
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() + "  " + (Split-Path $zipPath -Leaf) |
    Set-Content -LiteralPath $shaPath -NoNewline

if (-not $SkipInstaller) {
    $isccCommand = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($isccCommand) {
        $isccPath = $isccCommand.Source
    } else {
        $knownIsccPaths = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe')
        )
        $isccPath = $knownIsccPaths |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
    }

    if (-not $isccPath) {
        throw 'Inno Setup 6 (ISCC.exe) is required to produce the installable Setup.exe. Use -SkipInstaller only for portable-package checks.'
    }

    & $isccPath "/DMyAppVersion=$Version" "/DPublishDir=$publishDir" (Join-Path $repoRoot 'installer\QHHDesktopStorageBox.iss')
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $installerPath)) {
        throw "Inno Setup completed but did not produce $installerPath."
    }

    if ($shouldSign) {
        Invoke-AuthenticodeSigning `
            -FilePath $installerPath `
            -CertificatePath $SigningCertificatePath `
            -CertificatePassword $SigningCertificatePassword `
            -RequestedSignToolPath $SignToolPath `
            -TimestampServer $TimestampUrl
    }

    (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant() + "  " + (Split-Path $installerPath -Leaf) |
        Set-Content -LiteralPath $installerShaPath -NoNewline
}

Write-Host "Portable package: $zipPath"
Write-Host "Portable SHA-256: $shaPath"
Write-Host "User manual: $publishedManualPath"
if (-not $SkipInstaller) {
    Write-Host "Installer: $installerPath"
    Write-Host "Installer SHA-256: $installerShaPath"
}
