<#
.SYNOPSIS
  Fabrique les AppBundles APS Design Automation (Revit 2026 et 2027).

.DESCRIPTION
  1. Compile Egis.Revit.SystemFilter.DesignAutomation en Release (net8 -> R2026, net10 -> R2027).
  2. Assemble un bundle par version :
       EgisSystemFilter.bundle/PackageContents.xml
       EgisSystemFilter.bundle/Contents/EgisSystemFilter.addin + DLL (+ DesignAutomationBridge.dll)
  3. Produit artifacts/EgisSystemFilter_R<année>.zip et vérifie son contenu (échec sinon).
  Aucun appel APS, aucune publication.

.EXAMPLE
  pwsh ./build/package-da.ps1
  pwsh ./build/package-da.ps1 -Revit 2027 -NoPdb
  pwsh ./build/package-da.ps1 -DotNet "$env:USERPROFILE\.dotnet10\dotnet.exe"
#>
[CmdletBinding()]
param(
    [string[]] $Revit = @('2026', '2027'),
    [string] $Configuration = 'Release',
    [string] $DotNet = 'dotnet',
    [string] $OutputDir,
    [switch] $NoBuild,
    [switch] $NoPdb
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts' }
$project = Join-Path $root 'src/Egis.Revit.SystemFilter.DesignAutomation/Egis.Revit.SystemFilter.DesignAutomation.csproj'
$template = Join-Path $root 'bundle/PackageContents.template.xml'
$addin = Join-Path $root 'bundle/EgisSystemFilter.addin'
$bundleName = 'EgisSystemFilter.bundle'
$tfms = @{ '2026' = 'net8.0-windows'; '2027' = 'net10.0-windows' }

# Fichiers obligatoires dans Contents/ (le .addin pointe vers la DLL principale).
$required = @(
    'EgisSystemFilter.addin',
    'Egis.Revit.SystemFilter.DesignAutomation.dll',
    'Egis.Revit.SystemFilter.Core.dll',
    'Egis.Revit.SystemFilter.DesignAutomation.Io.dll',
    'DesignAutomationBridge.dll'
)

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$produced = @()

foreach ($year in $Revit) {
    if (-not $tfms.ContainsKey($year)) { throw "Version Revit non gérée : $year (attendu : 2026 ou 2027)." }
    $tfm = $tfms[$year]
    Write-Host "== Revit $year ($tfm) =="

    if (-not $NoBuild) {
        & $DotNet build $project --configuration $Configuration --framework $tfm -p:EnableWindowsTargeting=true
        if ($LASTEXITCODE -ne 0) { throw "Échec de la compilation pour Revit $year." }
    }

    $bin = Join-Path $root "src/Egis.Revit.SystemFilter.DesignAutomation/bin/$Configuration/$tfm"
    if (-not (Test-Path $bin)) { throw "Sortie de compilation introuvable : $bin" }

    $stage = Join-Path $OutputDir "R$year"
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    $bundle = Join-Path $stage $bundleName
    $contents = Join-Path $bundle 'Contents'
    New-Item -ItemType Directory -Force -Path $contents | Out-Null

    # PackageContents.xml à partir du modèle (@REVIT@ -> année).
    $xml = (Get-Content -Raw -Encoding UTF8 $template).Replace('@REVIT@', $year)
    [System.IO.File]::WriteAllText((Join-Path $bundle 'PackageContents.xml'), $xml, (New-Object System.Text.UTF8Encoding($false)))
    Copy-Item $addin $contents

    # DLL du plugin (l'API Revit n'est pas en sortie : références seules).
    $patterns = @('*.dll')
    if (-not $NoPdb) { $patterns += '*.pdb' }
    foreach ($p in $patterns) {
        Get-ChildItem -Path $bin -Filter $p -File | Copy-Item -Destination $contents
    }

    # Vérification du dossier.
    $missing = $required | Where-Object { -not (Test-Path (Join-Path $contents $_)) }
    if ($missing) { throw "Fichiers manquants dans Contents/ (R$year) : $($missing -join ', ')" }

    # ZIP : la racine de l'archive est EgisSystemFilter.bundle/, séparateurs '/'.
    $zip = Join-Path $OutputDir "EgisSystemFilter_R$year.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    $archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        Get-ChildItem -Path $bundle -Recurse -File | ForEach-Object {
            $rel = $bundleName + '/' + $_.FullName.Substring($bundle.Length + 1).Replace('\', '/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $rel, 'Optimal')
        }
    } finally { $archive.Dispose() }

    # Vérification du ZIP.
    $read = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try { $entries = @($read.Entries | ForEach-Object { $_.FullName }) } finally { $read.Dispose() }
    $expected = @("$bundleName/PackageContents.xml") + ($required | ForEach-Object { "$bundleName/Contents/$_" })
    $absent = $expected | Where-Object { $entries -notcontains $_ }
    if ($absent) { throw "ZIP incomplet (R$year) : $($absent -join ', ')" }
    $pc = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $sr = New-Object System.IO.StreamReader($pc.GetEntry("$bundleName/PackageContents.xml").Open())
        $pcText = $sr.ReadToEnd(); $sr.Dispose()
    } finally { $pc.Dispose() }
    if ($pcText -notmatch "SeriesMin=`"R$year`"" -or $pcText -match '@REVIT@') { throw "PackageContents.xml invalide pour R$year." }

    Write-Host "OK : $zip"
    $entries | Sort-Object | ForEach-Object { Write-Host "   $_" }
    $produced += $zip
}

Write-Host "`nBundles produits :"
$produced | ForEach-Object { Write-Host "  $_" }
