import json
import os
from datetime import datetime
from typing import List, Optional
from fastapi import HTTPException

from app.config import settings
from app.models import Client, ClientStatus, ClientRegister, ClientStatusUpdate


class ClientService:
    def __init__(self):
        self.clients_file = settings.CLIENTS_CONFIG_FILE
        self._init_storage()
    
    def _init_storage(self):
        os.makedirs(settings.DATA_DIR, exist_ok=True)
        
        if not os.path.exists(self.clients_file):
            self._save_clients([])
    
    def _load_clients(self) -> List[Client]:
        try:
            with open(self.clients_file, 'r', encoding='utf-8') as f:
                data = json.load(f)
                return [Client(**c) for c in data]
        except (FileNotFoundError, json.JSONDecodeError):
            return []
    
    def _save_clients(self, clients: List[Client]):
        with open(self.clients_file, 'w', encoding='utf-8') as f:
            json.dump([c.model_dump() for c in clients], f, indent=2, ensure_ascii=False, default=str)
    
    def register_client(self, data: ClientRegister, ip_address: str) -> Client:
        clients = self._load_clients()
        
        # Ưu tiên IP từ payload (client gửi), không dùng IP kết nối TCP
        final_ip = data.ip_address if data.ip_address else ip_address
        
        existing = [c for c in clients if c.id == data.client_id]
        if existing:
            for i, c in enumerate(clients):
                if c.id == data.client_id:
                    clients[i].status = ClientStatus.ONLINE
                    clients[i].last_seen = datetime.now()
                    clients[i].ip_address = final_ip
                    clients[i].base_url = f"http://{final_ip}:{data.port}"
                    self._save_clients(clients)
                    return clients[i]
        
        client = Client(
            id=data.client_id,
            name=data.name,
            machine_type=data.machine_type,
            ip_address=final_ip,
            port=data.port,
            status=ClientStatus.ONLINE,
            last_seen=datetime.now(),
            base_url=f"http://{final_ip}:{data.port}"
        )
        
        clients.append(client)
        self._save_clients(clients)
        
        return client
    
    def update_status(self, client_id: str, data: ClientStatusUpdate) -> Client:
        clients = self._load_clients()
        
        for i, c in enumerate(clients):
            if c.id == client_id:
                clients[i].status = data.status
                clients[i].last_seen = datetime.now()
                self._save_clients(clients)
                return clients[i]
        
        raise HTTPException(status_code=404, detail="Client not found")
    
    def get_client(self, client_id: str) -> Optional[Client]:
        clients = self._load_clients()
        for c in clients:
            if c.id == client_id:
                return c
        return None
    
    def get_all_clients(self) -> List[Client]:
        return self._load_clients()
    
    def get_online_clients(self) -> List[Client]:
        clients = self._load_clients()
        return [c for c in clients if c.status == ClientStatus.ONLINE]
    
    def heartbeat(self, client_id: str) -> Client:
        clients = self._load_clients()
        
        for i, c in enumerate(clients):
            if c.id == client_id:
                clients[i].last_seen = datetime.now()
                clients[i].status = ClientStatus.ONLINE
                self._save_clients(clients)
                return clients[i]
        
        raise HTTPException(status_code=404, detail="Client not found")
    
    def offline_client(self, client_id: str) -> bool:
        clients = self._load_clients()
        
        for i, c in enumerate(clients):
            if c.id == client_id:
                clients[i].status = ClientStatus.OFFLINE
                self._save_clients(clients)
                return True
        
        return False
    
    def update_client_ip(self, client_id: str, ip_address: str) -> Optional[Client]:
        """Update client IP address"""
        clients = self._load_clients()
        
        for i, c in enumerate(clients):
            if c.id == client_id:
                clients[i].ip_address = ip_address
                clients[i].base_url = f"http://{ip_address}:{c.port}"
                self._save_clients(clients)
                return clients[i]
        
        return None


client_service = ClientService()
