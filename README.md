# Photobooth desktop + FastAPI image service

## Architecture overview

The system is split into two processes:

1. The WPF desktop app watches a camera drop folder such as `C:\photos`.
2. When a new file appears, the app waits until the file is no longer locked, copies it to a processing folder, and shows it immediately in the left preview panel.
3. The desktop app sends the copied file to a separate Python FastAPI service using `multipart/form-data`.
4. The Python service applies the selected filter and returns the processed image as `image/png`.
5. The desktop app stores the processed image locally, updates the second preview panel, and keeps the last 4 processed images for printing.

The queue is single-consumer so bursts of new camera files are serialized instead of crashing the UI. API calls include retry logic and the app writes a simple timestamped log file.

## C# code

The WPF app is in [Photobooth.Desktop](Photobooth.Desktop). Key files:

- [MainWindow.xaml](Photobooth.Desktop/MainWindow.xaml)
- [ViewModels/MainViewModel.cs](Photobooth.Desktop/ViewModels/MainViewModel.cs)
- [Services/CameraFolderWatcher.cs](Photobooth.Desktop/Services/CameraFolderWatcher.cs)
- [Services/ImageProcessingClient.cs](Photobooth.Desktop/Services/ImageProcessingClient.cs)
- [Services/PrintService.cs](Photobooth.Desktop/Services/PrintService.cs)

UI layout:

- Left top: original camera image.
- Left bottom: processed image.
- Right panel: filter buttons.
- Bottom bar: status, error message, and print actions.
- Fullscreen overlay: loading spinner while processing.

## Python code

The image service is in [python-service/app/main.py](python-service/app/main.py).

Supported filters:

- `grayscale`
- `blur`
- `vintage`
- `beauty`
- `remove_background` optional, uses `mediapipe` if installed

Endpoint:

- `POST /process-image`

Request:

- `image_file`: uploaded image file
- `filter_type`: one of the supported filter values

Response:

- `image/png` bytes containing the processed result

Example request:

```bash
curl -X POST "http://127.0.0.1:8000/process-image" ^
  -F "image_file=@C:\photos\sample.jpg" ^
  -F "filter_type=beauty" --output processed.png
```

## Run instructions

### 1. Run the Python service

```bash
cd python-service
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
uvicorn app.main:app --host 127.0.0.1 --port 8000
```

If you want background removal, also install `mediapipe`.

### 2. Run the WPF app

```bash
dotnet restore Photobooth.sln
dotnet build Photobooth.sln
dotnet run --project Photobooth.Desktop
```

### 3. Configure camera folder

Edit [Photobooth.Desktop/appsettings.json](Photobooth.Desktop/appsettings.json) if the camera software writes somewhere other than `C:\photos`.

### 4. Printing

The footer provides `Print 1` and `Print 4` actions. `Print 1` prints the latest processed image. `Print 4` prints the last four processed images as a 2x2 layout.

## Request / response flow

1. Camera software writes a file into the watch folder.
2. `FileSystemWatcher` detects it.
3. The desktop app waits until the file is unlocked, then copies it into the processing folder.
4. The app uploads the file to the Python API as `multipart/form-data`.
5. The Python service returns the processed PNG.
6. The desktop app saves the processed result and updates the UI.