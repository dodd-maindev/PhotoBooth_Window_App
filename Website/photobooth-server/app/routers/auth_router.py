from fastapi import APIRouter, Depends, HTTPException, status, Header
from typing import Optional

from app.models import LoginRequest, Token, UserResponse, UserCreate
from app.services.auth_service import auth_service

router = APIRouter(prefix="/api/auth", tags=["Authentication"])


def get_current_user(authorization: Optional[str] = Header(None)):
    if not authorization:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Authorization header required"
        )
    
    if not authorization.startswith("Bearer "):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid authorization format"
        )
    
    token = authorization.replace("Bearer ", "")
    user = auth_service.get_current_user(token)
    return user


@router.post("/login", response_model=Token)
async def login(request: LoginRequest):
    """Login with username and password"""
    user = auth_service.authenticate(request.username, request.password)
    
    if not user:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid username or password"
        )
    
    token = auth_service.create_token(user)
    
    return Token(
        access_token=token,
        user=UserResponse(
            username=user.username,
            display_name=user.display_name,
            role=user.role
        )
    )


@router.post("/logout")
async def logout(authorization: Optional[str] = Header(None)):
    """Logout current user"""
    if not authorization:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Authorization header required"
        )
    
    token = authorization.replace("Bearer ", "")
    auth_service.logout(token)
    
    return {"message": "Logged out successfully"}


@router.get("/me", response_model=UserResponse)
async def get_me(current_user = Depends(get_current_user)):
    """Get current user info"""
    return UserResponse(
        username=current_user.username,
        display_name=current_user.display_name,
        role=current_user.role
    )


@router.post("/users", response_model=UserResponse)
async def create_user(user_data: UserCreate, current_user = Depends(get_current_user)):
    """Create new user (admin only)"""
    if current_user.role.value != "admin":
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Admin access required"
        )
    
    user = auth_service.create_user(user_data)
    
    return UserResponse(
        username=user.username,
        display_name=user.display_name,
        role=user.role
    )


@router.get("/users", response_model=list[UserResponse])
async def list_users(current_user = Depends(get_current_user)):
    """List all users (admin only)"""
    if current_user.role.value != "admin":
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Admin access required"
        )
    
    return auth_service.list_users()
