from fastapi import APIRouter, Depends, HTTPException, status
from typing import List

from app.models import Client, ClientRegister, ClientStatusUpdate, ClientStatus
from app.services.client_service import client_service
from app.routers.auth_router import get_current_user

router = APIRouter(prefix="/api/clients", tags=["Clients"])


@router.post("/register", response_model=Client)
async def register_client(client_data: ClientRegister, ip_address: str = "127.0.0.1"):
    """Register a new client (photobooth machine)"""
    return client_service.register_client(client_data, ip_address)


@router.get("", response_model=List[Client])
async def get_all_clients(current_user = Depends(get_current_user)):
    """Get all clients"""
    return client_service.get_all_clients()


@router.get("/online", response_model=List[Client])
async def get_online_clients(current_user = Depends(get_current_user)):
    """Get all online clients"""
    return client_service.get_online_clients()


@router.get("/{client_id}", response_model=Client)
async def get_client(client_id: str, current_user = Depends(get_current_user)):
    """Get client by ID"""
    client = client_service.get_client(client_id)
    if not client:
        raise HTTPException(status_code=404, detail="Client not found")
    return client


@router.put("/{client_id}/status", response_model=Client)
async def update_client_status(client_id: str, data: ClientStatusUpdate, current_user = Depends(get_current_user)):
    """Update client status"""
    return client_service.update_status(client_id, data)


@router.post("/{client_id}/heartbeat", response_model=Client)
async def client_heartbeat(client_id: str):
    """Client heartbeat to indicate it's still online"""
    return client_service.heartbeat(client_id)


@router.post("/{client_id}/offline")
async def client_offline(client_id: str):
    """Mark client as offline"""
    client_service.offline_client(client_id)
    return {"message": "Client marked as offline"}
