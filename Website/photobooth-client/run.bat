@echo off
echo ========================================
echo   Photobooth Client
echo ========================================
echo.

REM Check if virtual environment exists
if exist venv\Scripts\activate.bat (
    echo Activating virtual environment...
    call venv\Scripts\activate.bat
) else (
    echo No virtual environment found, using system Python...
)

REM Install dependencies if needed
pip install -r requirements.txt

echo.
echo Starting Photobooth Client on port 5050...
echo Press Ctrl+C to stop
echo.

python photobooth_client.py

pause
