@echo off
echo ========================================
echo   Photobooth Client Setup
echo ========================================
echo.

REM Create virtual environment
python -m venv venv

REM Activate and install
call venv\Scripts\activate.bat
pip install -r requirements.txt

echo.
echo Setup complete!
echo Run 'run.bat' to start the client.
pause
