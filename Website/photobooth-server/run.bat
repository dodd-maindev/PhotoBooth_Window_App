@echo off
cd /d "%~dp0"
echo ====================================
echo Photobooth Server
echo ====================================
echo.
echo Installing dependencies...
pip install -r requirements.txt
echo.
echo Starting server...
echo.
python main.py
pause
