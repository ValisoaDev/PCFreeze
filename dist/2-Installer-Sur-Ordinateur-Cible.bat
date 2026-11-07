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