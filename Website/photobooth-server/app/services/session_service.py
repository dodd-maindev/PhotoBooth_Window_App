import json
import os
import uuid
import shutil
from datetime import datetime
from typing import List, Optional
from fastapi import HTTPException, status

from app.config import settings
from app.models import Session, SessionStatus, Photo, SessionCreate, SessionUpdate


class SessionService:
    def __init__(self):
        self.sessions_file = os.path.join(settings.DATA_DIR, "sessions.json")
        self.downloads_dir = settings.DOWNLOADS_DIR
        self._init_storage()
    
    def _init_storage(self):
        os.makedirs(settings.DATA_DIR, exist_ok=True)
        os.makedirs(self.downloads_dir, exist_ok=True)
        
        if not os.path.exists(self.sessions_file):
            self._save_sessions([])
    
    def _load_sessions(self) -> List[Session]:
        try:
            with open(self.sessions_file, 'r', encoding='utf-8') as f:
                data = json.load(f)
                return [Session(**s) for s in data]
        except (FileNotFoundError, json.JSONDecodeError):
            return []
    
    def _save_sessions(self, sessions: List[Session]):
        with open(self.sessions_file, 'w', encoding='utf-8') as f:
            json.dump([s.model_dump() for s in sessions], f, indent=2, ensure_ascii=False, default=str)
    
    def create_session(self, data: SessionCreate, client_name: str) -> Session:
        sessions = self._load_sessions()
        
        session = Session(
            id=str(uuid.uuid4())[:8],
            client_id=data.client_id,
            client_name=client_name,
            name=data.name,
            date=datetime.now().strftime("%Y-%m-%d"),
            folder_path=data.folder_path,
            status=SessionStatus.ACTIVE
        )
        
        sessions.append(session)
        self._save_sessions(sessions)
        
        return session
    
    def get_session(self, session_id: str) -> Optional[Session]:
        sessions = self._load_sessions()
        for s in sessions:
            if s.id == session_id:
                return s
        return None
    
    def get_all_sessions(self) -> List[Session]:
        return self._load_sessions()
    
    def get_sessions_by_client(self, client_id: str) -> List[Session]:
        sessions = self._load_sessions()
        return [s for s in sessions if s.client_id == client_id]
    
    def update_session(self, session_id: str, data: SessionUpdate) -> Session:
        sessions = self._load_sessions()
        
        for i, s in enumerate(sessions):
            if s.id == session_id:
                if data.status is not None:
                    sessions[i].status = data.status
                    if data.status == SessionStatus.COMPLETED:
                        sessions[i].completed_at = datetime.now()
                if data.photo_count is not None:
                    sessions[i].photo_count = data.photo_count
                
                self._save_sessions(sessions)
                return sessions[i]
        
        raise HTTPException(status_code=404, detail="Session not found")
    
    def delete_session(self, session_id: str) -> bool:
        sessions = self._load_sessions()
        sessions = [s for s in sessions if s.id != session_id]
        self._save_sessions(sessions)
        return True
    
    def mark_synced(self, session_id: str) -> Session:
        sessions = self._load_sessions()
        
        for i, s in enumerate(sessions):
            if s.id == session_id:
                sessions[i].status = SessionStatus.SYNCED
                sessions[i].synced_at = datetime.now()
                self._save_sessions(sessions)
                return sessions[i]
        
        raise HTTPException(status_code=404, detail="Session not found")
    
    def get_stats(self) -> dict:
        sessions = self._load_sessions()
        total_photos = sum(s.photo_count for s in sessions)
        synced = len([s for s in sessions if s.status == SessionStatus.SYNCED])
        
        total_size = 0
        for s in sessions:
            if os.path.exists(s.folder_path):
                for f in os.listdir(s.folder_path):
                    p = os.path.join(s.folder_path, f)
                    if os.path.isfile(p):
                        total_size += os.path.getsize(p)
        
        return {
            "total_sessions": len(sessions),
            "total_photos": total_photos,
            "synced_sessions": synced,
            "storage_used_gb": round(total_size / (1024**3), 2)
        }


session_service = SessionService()
