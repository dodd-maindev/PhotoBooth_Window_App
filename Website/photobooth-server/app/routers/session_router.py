from fastapi import APIRouter, Depends, HTTPException, status
from typing import List

from app.models import Session, SessionCreate, SessionUpdate, SessionStatus
from app.services.session_service import session_service
from app.routers.auth_router import get_current_user

router = APIRouter(prefix="/api/sessions", tags=["Sessions"])


@router.post("", response_model=Session)
async def create_session(data: SessionCreate, current_user = Depends(get_current_user)):
    """Create a new session"""
    client = None
    return session_service.create_session(data, client_name="Unknown")


@router.get("", response_model=List[Session])
async def get_all_sessions(current_user = Depends(get_current_user)):
    """Get all sessions"""
    return session_service.get_all_sessions()


@router.get("/{session_id}", response_model=Session)
async def get_session(session_id: str, current_user = Depends(get_current_user)):
    """Get session by ID"""
    session = session_service.get_session(session_id)
    if not session:
        raise HTTPException(status_code=404, detail="Session not found")
    return session


@router.put("/{session_id}", response_model=Session)
async def update_session(session_id: str, data: SessionUpdate, current_user = Depends(get_current_user)):
    """Update session"""
    return session_service.update_session(session_id, data)


@router.delete("/{session_id}")
async def delete_session(session_id: str, current_user = Depends(get_current_user)):
    """Delete session"""
    session_service.delete_session(session_id)
    return {"message": "Session deleted"}


@router.post("/{session_id}/sync", response_model=Session)
async def sync_session(session_id: str, current_user = Depends(get_current_user)):
    """Mark session as synced"""
    return session_service.mark_synced(session_id)


@router.get("/stats/summary")
async def get_stats(current_user = Depends(get_current_user)):
    """Get session statistics"""
    return session_service.get_stats()
