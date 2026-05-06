import json
import os
import secrets
from datetime import datetime, timedelta
from typing import Optional, Dict, List
import bcrypt
from fastapi import HTTPException, status

from app.config import settings
from app.models import User, UserCreate, UserResponse, UserRole


class AuthService:
    def __init__(self):
        self.users_file = settings.USERS_FILE
        self.sessions_file = settings.SESSION_FILE
        self._init_users_file()
        self._ensure_sessions_file()

    def _init_users_file(self):
        os.makedirs(os.path.dirname(self.users_file), exist_ok=True)
        if not os.path.exists(self.users_file):
            default_users = {
                "users": [
                    {
                        "username": "admin",
                        "password_hash": self.hash_password("admin123"),
                        "role": "admin",
                        "display_name": "Administrator",
                        "created_at": datetime.now().isoformat(),
                        "is_active": True
                    },
                    {
                        "username": "client1",
                        "password_hash": self.hash_password("client123"),
                        "role": "client",
                        "display_name": "Máy Canon 1",
                        "created_at": datetime.now().isoformat(),
                        "is_active": True
                    },
                    {
                        "username": "client2",
                        "password_hash": self.hash_password("client123"),
                        "role": "client",
                        "display_name": "Máy Fuji 1",
                        "created_at": datetime.now().isoformat(),
                        "is_active": True
                    }
                ]
            }
            with open(self.users_file, 'w', encoding='utf-8') as f:
                json.dump(default_users, f, indent=2, ensure_ascii=False)

    def _ensure_sessions_file(self):
        os.makedirs(os.path.dirname(self.sessions_file), exist_ok=True)
        if os.path.exists(self.sessions_file):
            try:
                with open(self.sessions_file, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                    if not isinstance(data, dict):
                        raise ValueError("Invalid format")
            except (json.JSONDecodeError, ValueError):
                with open(self.sessions_file, 'w', encoding='utf-8') as f:
                    json.dump({}, f)
        else:
            with open(self.sessions_file, 'w', encoding='utf-8') as f:
                json.dump({}, f)

    def _load_users(self) -> List[Dict]:
        try:
            with open(self.users_file, 'r', encoding='utf-8') as f:
                data = json.load(f)
                return data.get("users", [])
        except (FileNotFoundError, json.JSONDecodeError):
            return []

    def _save_users(self, users: List[Dict]):
        with open(self.users_file, 'w', encoding='utf-8') as f:
            json.dump({"users": users}, f, indent=2, ensure_ascii=False)

    def _load_sessions(self) -> Dict[str, Dict]:
        try:
            if os.path.exists(self.sessions_file):
                with open(self.sessions_file, 'r', encoding='utf-8') as f:
                    return json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            pass
        return {}

    def _save_sessions(self, sessions: Dict[str, Dict]):
        with open(self.sessions_file, 'w', encoding='utf-8') as f:
            json.dump(sessions, f, indent=2)

    @staticmethod
    def hash_password(password: str) -> str:
        password_bytes = password.encode('utf-8')
        salt = bcrypt.gensalt()
        hashed = bcrypt.hashpw(password_bytes, salt)
        return hashed.decode('utf-8')

    @staticmethod
    def verify_password(plain_password: str, hashed_password: str) -> bool:
        password_bytes = plain_password.encode('utf-8')
        hashed_bytes = hashed_password.encode('utf-8')
        return bcrypt.checkpw(password_bytes, hashed_bytes)

    def authenticate(self, username: str, password: str) -> Optional[User]:
        users = self._load_users()
        for user_data in users:
            if user_data["username"] == username and user_data["is_active"]:
                if self.verify_password(password, user_data["password_hash"]):
                    return User(**user_data)
        return None

    def create_token(self, user: User) -> str:
        token = secrets.token_urlsafe(32)
        sessions = self._load_sessions()
        sessions[token] = {
            "username": user.username,
            "role": user.role.value,
            "created_at": datetime.now().isoformat(),
            "expires_at": (datetime.now() + timedelta(minutes=settings.ACCESS_TOKEN_EXPIRE_MINUTES)).isoformat()
        }
        self._save_sessions(sessions)
        return token

    def validate_token(self, token: str) -> Optional[Dict]:
        sessions = self._load_sessions()
        if token not in sessions:
            return None

        session = sessions[token]
        expires_at = datetime.fromisoformat(session["expires_at"])

        if datetime.now() > expires_at:
            del sessions[token]
            self._save_sessions(sessions)
            return None

        return session

    def logout(self, token: str) -> bool:
        sessions = self._load_sessions()
        if token in sessions:
            del sessions[token]
            self._save_sessions(sessions)
            return True
        return False

    def get_current_user(self, token: str) -> User:
        session = self.validate_token(token)
        if not session:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="Invalid or expired token"
            )

        users = self._load_users()
        for user_data in users:
            if user_data["username"] == session["username"]:
                return User(**user_data)

        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="User not found"
        )

    def create_user(self, user_data: UserCreate) -> User:
        users = self._load_users()

        for user in users:
            if user["username"] == user_data.username:
                raise HTTPException(
                    status_code=status.HTTP_400_BAD_REQUEST,
                    detail="Username already exists"
                )

        new_user = User(
            username=user_data.username,
            password_hash=self.hash_password(user_data.password),
            role=user_data.role,
            display_name=user_data.display_name
        )

        users.append(new_user.model_dump())
        self._save_users(users)

        return new_user

    def list_users(self) -> List[UserResponse]:
        users = self._load_users()
        return [UserResponse(
            username=u["username"],
            display_name=u["display_name"],
            role=UserRole(u["role"])
        ) for u in users if u["is_active"]]


auth_service = AuthService()
