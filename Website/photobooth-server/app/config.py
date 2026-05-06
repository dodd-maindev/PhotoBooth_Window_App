from pydantic_settings import BaseSettings
from typing import Optional


class Settings(BaseSettings):
    APP_NAME: str = "Photobooth Server"
    APP_VERSION: str = "1.0.0"
    
    # Server settings
    HOST: str = "0.0.0.0"
    PORT: int = 5051
    
    # Paths
    DATA_DIR: str = "data"
    DOWNLOADS_DIR: str = "data/downloads"
    SESSIONS_DIR: str = "data/sessions"
    
    # Auth settings
    SECRET_KEY: str = "photobooth-secret-key-change-in-production"
    ACCESS_TOKEN_EXPIRE_MINUTES: int = 60 * 24  # 24 hours
    SESSION_FILE: str = "data/sessions_store.json"
    USERS_FILE: str = "data/users.json"
    
    # Client settings
    CLIENT_API_PORT: int = 5050
    CLIENTS_CONFIG_FILE: str = "data/clients.json"
    
    class Config:
        env_file = ".env"
        case_sensitive = True


settings = Settings()
