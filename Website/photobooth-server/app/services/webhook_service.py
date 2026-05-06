import httpx
import os
import shutil
import asyncio
from datetime import datetime
from typing import Optional

from app.config import settings
from app.models import WebhookPayload, SessionStatus


class WebhookService:
    def __init__(self):
        self.downloads_dir = settings.DOWNLOADS_DIR
    
    async def download_file(self, url: str, dest_path: str) -> bool:
        try:
            os.makedirs(os.path.dirname(dest_path), exist_ok=True)
            async with httpx.AsyncClient(timeout=30.0) as client:
                response = await client.get(url)
                if response.status_code == 200:
                    with open(dest_path, 'wb') as f:
                        f.write(response.content)
                    return True
        except Exception as e:
            print(f"Download error: {e}")
        return False
    
    async def sync_session(self, client_base_url: str, session_id: str, dest_dir: str) -> dict:
        result = {
            "session_id": session_id,
            "downloaded": 0,
            "failed": 0,
            "total": 0
        }
        
        try:
            async with httpx.AsyncClient(timeout=30.0) as client:
                resp = await client.get(f"{client_base_url}/api/sessions/{session_id}/photos")
                if resp.status_code == 200:
                    photos = resp.json()
                    result["total"] = len(photos)
                    
                    for photo in photos:
                        filename = photo.get("filename", f"{photo['id']}.jpg")
                        dest = os.path.join(dest_dir, filename)
                        url = f"{client_base_url}/api/files/{photo['id']}"
                        
                        if await self.download_file(url, dest):
                            result["downloaded"] += 1
                        else:
                            result["failed"] += 1
        
        except Exception as e:
            print(f"Sync error: {e}")
        
        return result


webhook_service = WebhookService()
