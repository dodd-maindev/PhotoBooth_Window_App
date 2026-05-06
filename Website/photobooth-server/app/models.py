from pydantic import BaseModel, Field
from typing import Optional, List, Dict
from datetime import datetime
from enum import Enum


class UserRole(str, Enum):
    ADMIN = "admin"
    CLIENT = "client"


class PhotoFormat(str, Enum):
    JPEG = "jpeg"
    PNG = "png"
    RAW = "raw"


# ============= AUTH MODELS =============

class User(BaseModel):
    username: str
    password_hash: str
    role: UserRole
    display_name: str
    created_at: datetime = Field(default_factory=datetime.now)
    is_active: bool = True


class UserCreate(BaseModel):
    username: str = Field(..., min_length=3, max_length=50)
    password: str = Field(..., min_length=4)
    role: UserRole = UserRole.CLIENT
    display_name: str


class UserResponse(BaseModel):
    username: str
    display_name: str
    role: UserRole


class Token(BaseModel):
    access_token: str
    token_type: str = "bearer"
    user: UserResponse


class LoginRequest(BaseModel):
    username: str
    password: str


# ============= CLIENT MODELS =============

class ClientStatus(str, Enum):
    ONLINE = "online"
    OFFLINE = "offline"
    ERROR = "error"


class Client(BaseModel):
    id: str
    name: str
    machine_type: str
    ip_address: str
    port: int = 5050
    status: ClientStatus = ClientStatus.OFFLINE
    last_seen: Optional[datetime] = None
    base_url: str = ""


class ClientRegister(BaseModel):
    client_id: str
    name: str
    machine_type: str
    port: int = 5050


class ClientStatusUpdate(BaseModel):
    status: ClientStatus
    session_count: int = 0
    disk_free_gb: float = 0


# ============= SESSION MODELS =============

class SessionStatus(str, Enum):
    ACTIVE = "active"
    COMPLETED = "completed"
    SYNCED = "synced"
    ERROR = "error"


class Photo(BaseModel):
    id: str
    filename: str
    path: str
    size: int
    format: PhotoFormat = PhotoFormat.JPEG
    created_at: datetime = Field(default_factory=datetime.now)
    is_downloaded: bool = False
    local_path: Optional[str] = None


class Session(BaseModel):
    id: str
    client_id: str
    client_name: str
    name: str
    date: str
    folder_path: str
    photo_count: int = 0
    photos: List[Photo] = []
    status: SessionStatus = SessionStatus.ACTIVE
    created_at: datetime = Field(default_factory=datetime.now)
    completed_at: Optional[datetime] = None
    synced_at: Optional[datetime] = None


class SessionCreate(BaseModel):
    client_id: str
    name: str
    folder_path: str


class SessionUpdate(BaseModel):
    status: Optional[SessionStatus] = None
    photo_count: Optional[int] = None


# ============= WEBHOOK MODELS =============

class WebhookPayload(BaseModel):
    event: str
    client_id: str
    timestamp: datetime = Field(default_factory=datetime.now)
    data: Dict = {}


# ============= DASHBOARD MODELS =============

class DashboardStats(BaseModel):
    total_clients: int
    online_clients: int
    total_sessions: int
    total_photos: int
    synced_sessions: int
    storage_used_gb: float


class SystemHealth(BaseModel):
    server_status: str = "healthy"
    clients_online: List[Client] = []
    recent_sessions: List[Session] = []
    disk_usage_percent: float = 0
