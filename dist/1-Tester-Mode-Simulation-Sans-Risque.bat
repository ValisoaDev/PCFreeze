@echo off
chcp 65001 >nul
echo ============================================================================
echo   PCFREEZE - MODE SIMULATION SECURISE (ORDINATEUR DE DEVELOPPEMENT)
echo   Aucune modification ne sera apportee a vos disques ni au demarrage.
echo ============================================================================
start "" "%~dp0PCFreeze.exe" --simulate