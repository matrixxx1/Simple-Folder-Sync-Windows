param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\SimpleFolderSync\SimpleFolderSync.csproj'),
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PackageVersion = "0.1.1.0",
    [string]$PackageName = "m3Coding.SimpleFolderSync",
    [string]$Publisher = "CN=AFF85DD5-3D92-42A5-BA39-3AF6D41B1837",
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'Output'),
    [string]$PfxPath = "",
    [string]$PfxPassword = "",
    [switch]$SkipSign
)
$ErrorActionPreference = 'Stop'

function Require-Tool {
    param([string]$Name)
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $cmd) {
        $programFilesX86 = [Environment]::GetFolderPath("ProgramFilesX86")
        $programFiles = [Environment]::GetFolderPath("ProgramFiles")
        $sdkRoots = @(
            (Join-Path $programFilesX86 "Windows Kits\10\bin"),
            (Join-Path $programFiles "Windows Kits\10\bin")
        )
        $preferredArchs = @('x64', 'x86', 'arm64')
        $candidate = $null

        foreach ($root in $sdkRoots | Where-Object { Test-Path $_ }) {
            $directCandidates = @()
            foreach ($arch in $preferredArchs) {
                $candidateFromArch = Get-ChildItem -Path (Join-Path $root $arch) -Filter "$Name.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($candidateFromArch) {
                    $directCandidates += $candidateFromArch
                    break
                }
            }
            if (-not $directCandidates) {
                $versionCandidates = Get-ChildItem -Path (Join-Path $root '10.0.*') -Directory -ErrorAction SilentlyContinue |
                    Sort-Object Name -Descending
                foreach ($version in $versionCandidates) {
                    foreach ($arch in $preferredArchs) {
                        $candidateFromArch = Get-ChildItem -Path (Join-Path $version.FullName $arch) -Filter "$Name.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
                        if ($candidateFromArch) {
                            $directCandidates += $candidateFromArch
                            break
                        }
                    }
                    if ($directCandidates) { break }
                }
            }
            if ($directCandidates) {
                $candidate = $directCandidates[0].FullName
                break
            }
        }

        if ($candidate) {
            Write-Host "Found $Name at $candidate"
            return $candidate
        }
        throw "Required tool '$Name' not found. Install Windows SDK or run Developer Command Prompt and retry."
    }
    return $cmd.Source
}

function Invoke-Checked {
    param(
        [scriptblock]$Action,
        [string]$ActionName
    )
    Write-Host "==> $ActionName"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed in step: $ActionName"
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
$projectDir = Split-Path $ProjectPath
$projectDir = (Resolve-Path $projectDir).Path
$manifestSource = Join-Path $scriptRoot 'AppxManifest.xml'
$storeAssets = $scriptRoot

if (-not (Test-Path $manifestSource)) {
    throw "Missing AppxManifest.xml at $manifestSource. Run after reserving identity values are in place."
}

$publishDir = Join-Path $OutputRoot 'publish'
$packageImage = Join-Path $OutputRoot ("Image-" + [guid]::NewGuid().ToString())
$msixOutput = Join-Path $OutputRoot "$PackageName`_$PackageVersion`_x64.msix"

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $packageImage -Force | Out-Null

Push-Location $repoRoot

Write-Host "Project: $ProjectPath"
Write-Host "Config: $Configuration $Runtime"

$makeappx = Require-Tool "makeappx"
$signTool = $null
if (-not $SkipSign) {
    $signTool = Require-Tool "signtool"
}

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $packageImage -Force | Out-Null

Invoke-Checked -ActionName "Publishing .NET app" -Action {
    dotnet publish $ProjectPath -c $Configuration -r $Runtime --no-self-contained /p:PublishReadyToRun=false /p:PublishSingleFile=false -o $publishDir
}

Get-ChildItem -Path $publishDir -Filter 'SimpleFolderSync.exe' -File | Copy-Item -Destination $packageImage -Force
Get-ChildItem -Path $publishDir -Filter 'SimpleFolderSync.dll' -File | Copy-Item -Destination $packageImage -Force
Get-ChildItem -Path $publishDir -Filter 'SimpleFolderSync.pdb' -File | Copy-Item -Destination $packageImage -Force
Get-ChildItem -Path $publishDir -Filter 'SimpleFolderSync.deps.json' -File | Copy-Item -Destination $packageImage -Force
Get-ChildItem -Path $publishDir -Filter 'SimpleFolderSync.runtimeconfig.json' -File | Copy-Item -Destination $packageImage -Force
Copy-Item -Path $manifestSource -Destination (Join-Path $packageImage 'AppxManifest.xml') -Force
Copy-Item -Path (Join-Path $storeAssets 'StoreLogo.png') -Destination $packageImage -Force
Copy-Item -Path (Join-Path $storeAssets 'Square44x44Logo.png') -Destination $packageImage -Force
Copy-Item -Path (Join-Path $storeAssets 'Square150x150Logo.png') -Destination $packageImage -Force
Copy-Item -Path (Join-Path $storeAssets 'Square310x310Logo.png') -Destination $packageImage -Force
Copy-Item -Path (Join-Path $storeAssets 'Wide310x150Logo.png') -Destination $packageImage -Force

Copy-Item -Path (Join-Path $projectDir 'Assets\folder-sync.ico') -Destination (Join-Path $packageImage 'folder-sync.ico') -Force

$updatedManifest = Join-Path $packageImage 'AppxManifest.xml'
$manifestXml = [xml](Get-Content $updatedManifest -Raw)
$identityNode = $manifestXml.Package.Identity
if ($identityNode) {
    $identityNode.Name = $PackageName
    $identityNode.Publisher = $Publisher
    $identityNode.Version = $PackageVersion
}
$manifestXml.Save($updatedManifest)

Push-Location $packageImage
Invoke-Checked -ActionName "Packing MSIX" -Action {
    & $makeappx pack /o /h SHA256 /d . /p $msixOutput
}
Pop-Location

if (-not $SkipSign) {
    if ([string]::IsNullOrWhiteSpace($PfxPath) -or -not (Test-Path $PfxPath)) {
        Write-Warning "Signing skipped: no PFX file provided. Pass -PfxPath to sign the package."
    } else {
        Invoke-Checked -ActionName "Signing MSIX" -Action {
            if ([string]::IsNullOrWhiteSpace($PfxPassword)) {
                & $signTool sign /fd SHA256 /f $PfxPath /tr http://timestamp.digicert.com /td SHA256 $msixOutput
            } else {
                & $signTool sign /fd SHA256 /f $PfxPath /p $PfxPassword /tr http://timestamp.digicert.com /td SHA256 $msixOutput
            }
        }
    }
}

if (Test-Path $msixOutput) {
    Write-Host "MSIX created: $msixOutput"
} else {
    throw "MSIX package was not created."
}

$msixUploadOutput = Join-Path $OutputRoot "$PackageName`_$PackageVersion`_x64.msixupload"
if (Test-Path $msixUploadOutput) {
    Remove-Item $msixUploadOutput -Force
}

# Microsoft Store expects the upload to be an archive container holding the app package.
# Keep this as an archive (not a raw extracted MSIX payload).
Compress-Archive -Path $msixOutput -DestinationPath "$msixUploadOutput.zip" -Force
Move-Item -Path "$msixUploadOutput.zip" -Destination $msixUploadOutput -Force
Write-Host "Package upload-ready copy: $msixUploadOutput"

Write-Host "Deployment prep completed."
Pop-Location
