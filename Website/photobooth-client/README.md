# Photobooth Client

## Cài đặt và chạy

### Bước 1: Cài đặt dependencies

```bash
setup.bat
```

### Bước 2: Chạy client

```bash
run.bat
```

Hoặc chạy trực tiếp:

```bash
venv\Scripts\activate
python photobooth_client.py
```

## Cấu hình

- **Port**: 5050 (TCP)
- **Sessions Path**: `C:\photobooth\sessions`

Đổi port hoặc path trong file `photobooth_client.py`:

```python
HOST = "0.0.0.0"
PORT = 5050
SESSIONS_PATH = r"C:\photobooth\sessions"
```

## APIs

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/health` | GET | Kiểm tra trạng thái client |
| `/api/files/tree` | GET | Lấy cấu trúc cây thư mục |
| `/api/files/contents` | GET | Lấy nội dung thư mục |
| `/api/files/download` | GET | Download file |

## Chạy như Windows Service (tùy chọn)

Để chạy tự động khi khởi động, dùng NSSM:

```bash
nssm install PhotoboothClient "C:\path\to\venv\Scripts\python.exe" "C:\path\to\photobooth_client.py"
```
