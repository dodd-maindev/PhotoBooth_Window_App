"""
Photobooth Client - Chạy trên máy ảnh (Canon/Fuji)
Listen trên TCP port 5050, cung cấp API truy cập file cho server
"""
import os
import sys
import json
import asyncio
import logging
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
    logger.info(f"Starting Photobooth Client on http://{HOST}:{PORT}")
    logger.info(f"Sessions path: {SESSIONS_PATH}")
    
    app = init_app()
    web.run_app(app, host=HOST, port=PORT, print=None)


if __name__ == "__main__":
    main()
