"""
Photobooth Client - Chạy trên máy ảnh (Canon/Fuji)
Listen trên TCP port 5050, cung cấp API truy cập file cho server
"""
import os
import sys
import json
import asyncio
import logging
import socket
from datetime import datetime
from pathlib import Path
from aiohttp import web
from aiohttp.web import json_response

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Cấu hình
HOST = "0.0.0.0"  # Bind tất cả interface
PORT = 5050
SESSIONS_PATH = r"C:\photobooth\sessions"

# Server API - Tự động phát hiện server IP
SERVER_API_PORT = 5051
_SERVER_IP = None  # Sẽ được phát hiện tự động


def get_local_ip() -> str:
    """Lấy IP local của máy"""
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
        ip = s.getsockname()[0]
        s.close()
        return ip
    except Exception:
        return "127.0.0.1"


def discover_server() -> str:
    """Tìm server IP từ network scan đơn giản"""
    global _SERVER_IP
    
    local_ip = get_local_ip()
    logger.info(f"Local IP: {local_ip}")
    
    # Tách subnet từ IP local (ví dụ: 192.168.1.15 -> 192.168.1.)
    parts = local_ip.split('.')
    subnet = f"{parts[0]}.{parts[1]}.{parts[2]}."
    
    # Thử các gateway thường gặp + scan subnet
    candidates = [
        f"{subnet}1",    # .1 thường là router
        f"{subnet}2",    # .2 
        f"{subnet}10",   # .10
        f"{subnet}100",  # .100
        f"{subnet}146",  # IP server hiện tại
    ]
    
    # Thử từng candidate
    for ip in candidates:
        if ip == local_ip:
            continue
        try:
            import httpx
            resp = httpx.get(f"http://{ip}:{SERVER_API_PORT}/api/health", timeout=2.0)
            if resp.status_code == 200:
                _SERVER_IP = ip
                logger.info(f"Discovered server at {ip}")
                return ip
        except Exception:
            pass
    
    # Nếu không tìm được, dùng mặc định
    default_server = f"{subnet}146"
    _SERVER_IP = default_server
    logger.warning(f"Server not found, using default: {default_server}")
    return default_server


async def register_to_server(client_id: str, client_name: str, machine_type: str, ip_address: str):
    """Đăng ký client với server"""
    server_ip = discover_server()
    url = f"http://{server_ip}:{SERVER_API_PORT}/api/clients/register"
    
    payload = {
        "client_id": client_id,
        "name": client_name,
        "machine_type": machine_type,
        "port": PORT
    }
    
    try:
        import httpx
        response = httpx.post(url, json=payload, timeout=10.0)
        if response.status_code == 200:
            data = response.json()
            logger.info(f"Registered to server: {data}")
            return True
        else:
            logger.warning(f"Failed to register: {response.status_code}")
    except Exception as e:
        logger.error(f"Failed to connect to server: {e}")
    
    return False


async def heartbeat_loop(client_id: str, server_ip: str):
    """Gửi heartbeat định kỳ để báo server client vẫn online"""
    while True:
        try:
            import httpx
            url = f"http://{server_ip}:{SERVER_API_PORT}/api/clients/{client_id}/heartbeat"
            response = httpx.post(url, timeout=10.0)
            if response.status_code == 200:
                logger.debug(f"Heartbeat sent to server")
        except Exception as e:
            logger.debug(f"Heartbeat failed: {e}")
        
        await asyncio.sleep(30)  # Gửi heartbeat mỗi 30 giây


def run_heartbeat_loop(client_id: str, server_ip: str):
    """Chạy heartbeat loop trong thread riêng"""
    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)
    loop.run_until_complete(heartbeat_loop(client_id, server_ip))


def get_file_info(path: Path) -> dict:
    """Lấy thông tin file"""
    try:
        stat = path.stat()
        return {
            "name": path.name,
            "path": str(path),
            "size": stat.st_size,
            "modified": datetime.fromtimestamp(stat.st_mtime).isoformat(),
            "is_folder": path.is_dir()
        }
    except Exception as e:
        return {
            "name": path.name,
            "path": str(path),
            "size": 0,
            "modified": None,
            "is_folder": path.is_dir(),
            "error": str(e)
        }


async def get_tree(path: str) -> dict:
    """Lấy cấu trúc thư mục đệ quy"""
    base_path = Path(path) if path else Path(SESSIONS_PATH)
    
    if not base_path.exists():
        return {
            "name": base_path.name,
            "path": str(base_path),
            "is_folder": True,
            "children": [],
            "error": "Path not found"
        }
    
    def scan_folder(folder_path: Path) -> dict:
        info = get_file_info(folder_path)
        info["children"] = []
        
        if folder_path.is_dir():
            try:
                entries = sorted(folder_path.iterdir(), key=lambda x: (not x.is_dir(), x.name.lower()))
                for entry in entries:
                    info["children"].append(scan_folder(entry))
            except PermissionError:
                info["error"] = "Permission denied"
        
        return info
    
    return scan_folder(base_path)


async def get_contents(request: web.Request) -> json_response:
    """API: Lấy nội dung thư mục"""
    path = request.query.get("path", SESSIONS_PATH)
    folder_path = Path(path)
    
    if not folder_path.exists():
        return json_response([], status=404)
    
    if not folder_path.is_dir():
        return json_response([], status=400)
    
    items = []
    try:
        for entry in sorted(folder_path.iterdir(), key=lambda x: (not x.is_dir(), x.name.lower())):
            items.append(get_file_info(entry))
    except PermissionError:
        pass
    
    return json_response(items)


async def get_tree_handler(request: web.Request) -> json_response:
    """API: Lấy cấu trúc cây thư mục"""
    path = request.query.get("path", SESSIONS_PATH)
    tree = await get_tree(path)
    return json_response(tree)


async def download_file(request: web.Request) -> web.Response:
    """API: Download file"""
    file_path = request.query.get("path")
    
    if not file_path:
        return json_response({"error": "Missing path"}, status=400)
    
    path = Path(file_path)
    if not path.exists() or not path.is_file():
        return json_response({"error": "File not found"}, status=404)
    
    filename = path.name
    content_type = "application/octet-stream"
    if filename.lower().endswith(('.jpg', '.jpeg')):
        content_type = "image/jpeg"
    elif filename.lower().endswith('.png'):
        content_type = "image/png"
    elif filename.lower().endswith('.gif'):
        content_type = "image/gif"
    
    try:
        with open(path, 'rb') as f:
            content = f.read()
        return web.Response(
            body=content,
            content_type=content_type,
            headers={'Content-Disposition': f'attachment; filename="{filename}"'}
        )
    except Exception as e:
        return json_response({"error": str(e)}, status=500)


async def health_check(request: web.Request) -> json_response:
    """API: Health check"""
    return json_response({
        "status": "online",
        "sessions_path": SESSIONS_PATH,
        "sessions_exists": Path(SESSIONS_PATH).exists()
    })


def setup_routes(app: web.Application):
    """Thiết lập routes"""
    app.router.add_get('/api/health', health_check)
    app.router.add_get('/api/files/tree', get_tree_handler)
    app.router.add_get('/api/files/contents', get_contents)
    app.router.add_get('/api/files/download', download_file)


async def init_app() -> web.Application:
    """Khởi tạo ứng dụng"""
    app = web.Application()
    setup_routes(app)
    
    # Tạo thư mục sessions nếu chưa có
    sessions_dir = Path(SESSIONS_PATH)
    if not sessions_dir.exists():
        sessions_dir.mkdir(parents=True, exist_ok=True)
        logger.info(f"Created sessions directory: {sessions_dir}")
    
    return app




def main():
    """Main entry point"""
    local_ip = get_local_ip()
    logger.info(f"Starting Photobooth Client on http://{HOST}:{PORT}")
    logger.info(f"Sessions path: {SESSIONS_PATH}")
    logger.info(f"Local IP: {local_ip}")
    
    # Tạo client ID từ hostname + IP
    import socket
    hostname = socket.gethostname()
    client_id = f"client_{hostname}_{local_ip.replace('.', '')}"
    
    # Tên client hiển thị
    client_name = f"Máy {hostname}"
    
    # Thử đăng ký với server
    logger.info("Attempting to register with server...")
    server_ip = discover_server()
    
    # Đăng ký đồng bộ (không dùng await)
    try:
        import httpx
        url = f"http://{server_ip}:{SERVER_API_PORT}/api/clients/register"
        payload = {
            "client_id": client_id,
            "name": client_name,
            "machine_type": "Photobooth",
            "port": PORT,
            "ip_address": local_ip  # Gửi IP thật lên server
        }
        response = httpx.post(url, json=payload, timeout=10.0)
        if response.status_code == 200:
            logger.info(f"Registered to server successfully")
        else:
            logger.warning(f"Failed to register: {response.status_code}")
    except Exception as e:
        logger.error(f"Failed to connect to server: {e}")
    
    # Tạo app thủ công (không dùng async)
    app = web.Application()
    
    # Routes
    app.router.add_get('/api/health', health_check)
    app.router.add_get('/api/files/tree', get_tree_handler)
    app.router.add_get('/api/files/download', download_file)
    app.router.add_get('/api/files/contents', get_contents)
    
    app['client_id'] = client_id
    app['server_ip'] = server_ip
    
    # Chạy heartbeat trong thread riêng
    import threading
    heartbeat_thread = threading.Thread(target=run_heartbeat_loop, args=(client_id, server_ip), daemon=True)
    heartbeat_thread.start()
    
    logger.info("Starting background heartbeat...")
    
    web.run_app(app, host=HOST, port=PORT, print=None)


if __name__ == "__main__":
    main()
