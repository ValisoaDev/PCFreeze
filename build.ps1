$ErrorActionPreference = "Stop"

$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $cscPath)) {
    throw "Compilateur C# natif introuvable : $cscPath"
}

$distDir = Join-Path $PSScriptRoot "dist"
if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

$outExe = Join-Path $distDir "PCFreeze.exe"
$sources = @(
    (Join-Path $PSScriptRoot "src\Models\PCFreezeConfig.cs"),
    (Join-Path $PSScriptRoot "src\Security\CryptoManager.cs"),
    (Join-Path $PSScriptRoot "src\Engine\BaselineSnapshotManager.cs"),
    (Join-Path $PSScriptRoot "src\Engine\FreezeEngines.cs"),
    (Join-Path $PSScriptRoot "src\Engine\ThawSpaceAndDeployManager.cs"),
    (Join-Path $PSScriptRoot "src\UI\PolarIcons.cs"),
    (Join-Path $PSScriptRoot "src\UI\LoginDialog.cs"),
    (Join-Path $PSScriptRoot "src\UI\MainWindow.cs"),
    (Join-Path $PSScriptRoot "src\UI\TrayAgentContext.cs"),
    (Join-Path $PSScriptRoot "src\Program.cs")
)

Write-Host "Compilation de PCFreeze.exe (64-bit natif) en cours..." -ForegroundColor Cyan
& $cscPath /target:winexe /out:$outExe /optimize+ /platform:x64 `
    /r:System.dll `
    /r:System.Core.dll `
    /r:System.Data.dll `
    /r:System.Drawing.dll `
    /r:System.Windows.Forms.dll `
    /r:System.Xml.dll `
    $sources

if ($LASTEXITCODE -ne 0) {
    throw "Échec de la compilation (code $LASTEXITCODE)."
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# Génération du script de test sécurisé (Mode Simulation pour le PC de dev)
$simBat = @"
@echo off
chcp 65001 >nul
echo ============================================================================
echo   PCFREEZE - MODE SIMULATION SECURISE (ORDINATEUR DE DEVELOPPEMENT)
echo   Aucune modification ne sera apportee a vos disques ni au demarrage.
echo ============================================================================
start "" "%~dp0PCFreeze.exe" --simulate
"@
[System.IO.File]::WriteAllText((Join-Path $distDir "1-Tester-Mode-Simulation-Sans-Risque.bat"), $simBat, $utf8NoBom)

# Génération de l'installateur automatisé pour la machine cible
$installBat = @"
@echo off
chcp 65001 >nul
echo ============================================================================
echo   INSTALLATION DE PCFREEZE SUR L'ORDINATEUR CIBLE
echo   ATTENTION : Executez ce script en tant qu'Administrateur UNIQUEMENT
echo   sur l'ordinateur cible que vous souhaitez geler/proteger !
echo ============================================================================
echo.
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERREUR] Ce script doit etre execute en tant qu'Administrateur ^(Clic droit -^> Executer en tant qu'administrateur^).
    pause
    exit /b 1
)
set /p CONFIRM="Confirmez-vous etre sur l'ORDINATEUR CIBLE a proteger ? (OUI/NON) : "
if /I not "%CONFIRM%"=="OUI" (
    echo Installation annulee.
    pause
    exit /b 0
)
"%~dp0PCFreeze.exe" --install-target
echo.
echo Ouverture de la console d'administration PCFreeze...
start "" "C:\Program Files\PCFreeze\PCFreeze.exe"
pause
"@
[System.IO.File]::WriteAllText((Join-Path $distDir "2-Installer-Sur-Ordinateur-Cible.bat"), $installBat, $utf8NoBom)

$oldUninstallBat = Join-Path $distDir "3-Desinstaller-De-Ordinateur-Cible.bat"
if (Test-Path $oldUninstallBat) { Remove-Item $oldUninstallBat -Force }

Write-Host "Compilation réussie -> $outExe" -ForegroundColor Green
