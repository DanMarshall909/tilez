@echo off
REM Tilez Viewer Launcher

echo.
echo ========================================
echo  Tilez Viewer
echo ========================================
echo.
echo Prerequisites:
echo - LMStudio or Ollama running with Flux model
echo - Godot 4.3+ installed
echo.
echo Starting Godot...
echo.

REM Change to project directory
cd /d "%~dp0project"

REM Launch Godot (assuming godot is in PATH)
REM If godot is not in PATH, provide full path to godot executable
godot

pause
