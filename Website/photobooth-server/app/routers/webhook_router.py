from fastapi import APIRouter, Depends, BackgroundTasks
from typing import List

from app.models import WebhookPayload, Session, Client
from app.services.session_service import session_service
from app.services.client_service import client_service

router = APIRouter(prefix="/api/webhook", tags=["Webhook"])


@router.post("/receive")
async def receive_webhook(payload: WebhookPayload, background_tasks: BackgroundTasks):
    """Receive webhook from clients"""
    event = payload.event
    
    if event == "session_created":
        session = session_service.get_session(payload.data.get("session_id"))
        return {"status": "received", "session": session}
    
    elif event == "session_completed":
        session_id = payload.data.get("session_id")
        if session_id:
            session_service.update_session(session_id, {"status": "completed"})
        return {"status": "received"}
    
    elif event == "client_heartbeat":
        client_service.heartbeat(payload.client_id)
        return {"status": "received"}
    
    return {"status": "received", "event": event}


@router.get("/health")
async def webhook_health():
    """Webhook health check"""
    return {"status": "healthy"}
