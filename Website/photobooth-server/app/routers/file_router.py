from fastapi import APIRouter, Depends, HTTPException, Query
from fastapi.responses import StreamingResponse
from typing import List, Optional
import os
import zipfile
import io

from app.models import Client
from app.services.file_service import file_service
from app.services.client_service import client_service
from app.routers.auth_router import get_current_user

router = APIRouter(prefix="/api/files", tags=["Files"])


def get_client_url(client_id: str) -> str:
    """Get client's base URL from client_id"""
    client = client_service.get_client(client_id)
    if not client:
        raise HTTPException(status_code=404, detail="Client not found")
    # Return base_url even if offline - will be handled by file_service
    return client.base_url


@router.get("/clients/{client_id}/tree")
async def get_client_folder_tree(
    client_id: str, 
    path: Optional[str] = None,
    current_user = Depends(get_current_user)
):
    """
    Get folder tree structure from a specific client machine
    Falls back to local folder if client is offline (for testing)
    """
    client = client_service.get_client(client_id)
    if not client:
        raise HTTPException(status_code=404, detail="Client not found")
    
    # Try to get from client first
    try:
        from app.services.file_service import file_service
        tree = await file_service.get_folder_tree(client.base_url, path or file_service.DEFAULT_SESSIONS_PATH)
        tree["source"] = "client"
        tree["client_name"] = client.name
        return tree
    except HTTPException:
        raise  # Re-raise 404/503
    except Exception as e:
        # Client unreachable - fallback to local for testing
        local_path = path or file_service.DEFAULT_SESSIONS_PATH
        tree = file_service.build_local_tree(local_path)
        tree["source"] = "local"
        tree["client_name"] = client.name
        tree["fallback"] = True
        return tree


@router.get("/clients/{client_id}/contents")
async def get_client_folder_contents(
    client_id: str,
    path: str = Query(..., description="Folder path on client machine"),
    current_user = Depends(get_current_user)
):
    """
    Get contents of a specific folder from client machine
    """
    client_base_url = get_client_url(client_id)
    return await file_service.get_folder_contents(client_base_url, path)


@router.post("/clients/{client_id}/download-folder")
async def download_folder_from_client(
    client_id: str,
    source_path: str = Query(..., description="Source folder path on client"),
    dest_path: str = Query(..., description="Destination path on server"),
    current_user = Depends(get_current_user)
):
    """
    Download a folder from client machine to server
    """
    client_base_url = get_client_url(client_id)
    result = await file_service.download_folder(client_base_url, source_path, dest_path)
    return result


@router.get("/clients/{client_id}/download-file")
async def download_file_from_client(
    client_id: str,
    path: str = Query(..., description="File path on client machine"),
    current_user = Depends(get_current_user)
):
    """
    Download a single file from client machine
    Returns the file as streaming response
    """
    client_base_url = get_client_url(client_id)
    
    # Get client info
    client = client_service.get_client(client_id)
    if not client:
        raise HTTPException(status_code=404, detail="Client not found")
    
    try:
        import httpx
        async with httpx.AsyncClient(timeout=60.0) as http_client:
            response = await http_client.get(
                f"{client_base_url}/api/files/download",
                params={"path": path}
            )
            
            if response.status_code == 200:
                filename = os.path.basename(path)
                return StreamingResponse(
                    io.BytesIO(response.content),
                    media_type="application/octet-stream",
                    headers={
                        "Content-Disposition": f'attachment; filename="{filename}"'
                    }
                )
            else:
                raise HTTPException(status_code=response.status_code, detail="Failed to download file from client")
                
    except httpx.ConnectError:
        raise HTTPException(status_code=503, detail="Client is offline")
    except httpx.TimeoutException:
        raise HTTPException(status_code=504, detail="Request timed out")


@router.get("/local/tree")
async def get_local_folder_tree(
    path: str = Query(default=r"C:\photobooth\sessions"),
    current_user = Depends(get_current_user)
):
    """
    Get folder tree from local path (for testing without client)
    """
    tree = file_service.build_local_tree(path)
    return tree


@router.get("/server-folders")
async def get_server_folders(
    current_user = Depends(get_current_user)
):
    """
    Get available folders on server for browsing
    """
    import os
    
    # Common paths to check
    paths_to_check = [
        r"C:\photobooth\sessions",
        r"D:\photobooth\sessions",
        r"E:\photobooth\sessions",
        settings.SESSIONS_DIR,
        settings.DATA_DIR,
        "data",
        ".",
    ]
    
    available_folders = []
    
    for path in paths_to_check:
        abs_path = os.path.abspath(path)
        if os.path.exists(abs_path) and os.path.isdir(abs_path):
            # Count items
            try:
                items = os.listdir(abs_path)
                folder_count = sum(1 for i in items if os.path.isdir(os.path.join(abs_path, i)))
                file_count = sum(1 for i in items if os.path.isfile(os.path.join(abs_path, i)))
                
                available_folders.append({
                    "path": abs_path,
                    "name": os.path.basename(abs_path) or abs_path,
                    "folder_count": folder_count,
                    "file_count": file_count,
                    "is_default": "photobooth" in abs_path.lower()
                })
            except PermissionError:
                pass
    
    return {
        "folders": available_folders,
        "default_path": r"C:\photobooth\sessions" if os.path.exists(r"C:\photobooth\sessions") else None
    }


@router.get("/demo/tree")
async def get_demo_tree(
    current_user = Depends(get_current_user)
):
    """
    Get demo folder tree for testing the File Explorer UI
    Returns a mock structure simulating a client's sessions folder
    """
    # Demo path - try to find actual path or return mock data
    paths_to_try = [
        r"C:\photobooth\sessions",
        r"D:\photobooth\sessions",
        r"E:\photobooth\sessions",
        os.path.join(settings.DATA_DIR, "demo_sessions"),
    ]
    
    for demo_path in paths_to_try:
        if os.path.exists(demo_path):
            tree = file_service.build_local_tree(demo_path)
            tree["is_demo"] = False
            tree["demo_source"] = demo_path
            return tree
    
    # Return mock structure for demo
    from datetime import datetime, timedelta
    
    def create_mock_item(name, is_folder, children=None, days_ago=0):
        return {
            "name": name,
            "path": rf"C:\photobooth\sessions\{name}",
            "is_folder": is_folder,
            "size": 0 if is_folder else 1024 * 1024 * 2,  # 2MB default for files
            "modified": (datetime.now() - timedelta(days=days_ago)).isoformat(),
            "children": children or []
        }
    
    today = datetime.now()
    
    demo_tree = {
        "name": "sessions",
        "path": r"C:\photobooth\sessions",
        "is_folder": True,
        "is_demo": True,
        "demo_source": "mock",
        "children": [
            {
                "name": today.strftime("%Y-%m-%d"),
                "path": rf"C:\photobooth\sessions\{today.strftime('%Y-%m-%d')}",
                "is_folder": True,
                "children": [
                    {
                        "name": "ád_2014",
                        "path": rf"C:\photobooth\sessions\{today.strftime('%Y-%m-%d')}\ád_2014",
                        "is_folder": True,
                        "children": [
                            {
                                "name": "original",
                                "path": rf"C:\photobooth\sessions\{today.strftime('%Y-%m-%d')}\ád_2014\original",
                                "is_folder": True,
                                "children": [
                                    create_mock_item("IMG_001.jpg", False, days_ago=0),
                                    create_mock_item("IMG_002.jpg", False, days_ago=0),
                                    create_mock_item("IMG_003.jpg", False, days_ago=0),
                                ]
                            },
                            {
                                "name": "edited",
                                "path": rf"C:\photobooth\sessions\{today.strftime('%Y-%m-%d')}\ád_2014\edited",
                                "is_folder": True,
                                "children": [
                                    create_mock_item("EDIT_001.jpg", False, days_ago=0),
                                    create_mock_item("EDIT_002.jpg", False, days_ago=0),
                                ]
                            }
                        ]
                    },
                    {
                        "name": "birthday_party",
                        "path": rf"C:\photobooth\sessions\{today.strftime('%Y-%m-%d')}\birthday_party",
                        "is_folder": True,
                        "children": [
                            {
                                "name": "original",
                                "path": rf"C:\photobooth\sessions\{today.strftime('%Y-%m-%d')}\birthday_party\original",
                                "is_folder": True,
                                "children": [
                                    create_mock_item("PARTY_001.jpg", False, days_ago=0),
                                    create_mock_item("PARTY_002.jpg", False, days_ago=0),
                                    create_mock_item("PARTY_003.jpg", False, days_ago=0),
                                    create_mock_item("PARTY_004.jpg", False, days_ago=0),
                                ]
                            }
                        ]
                    }
                ]
            },
            {
                "name": (today - timedelta(days=1)).strftime("%Y-%m-%d"),
                "path": rf"C:\photobooth\sessions\{(today - timedelta(days=1)).strftime('%Y-%m-%d')}",
                "is_folder": True,
                "children": [
                    {
                        "name": "wedding_shoot",
                        "path": rf"C:\photobooth\sessions\{(today - timedelta(days=1)).strftime('%Y-%m-%d')}\wedding_shoot",
                        "is_folder": True,
                        "children": [
                            {
                                "name": "original",
                                "path": rf"C:\photobooth\sessions\{(today - timedelta(days=1)).strftime('%Y-%m-%d')}\wedding_shoot\original",
                                "is_folder": True,
                                "children": [
                                    create_mock_item("WEDDING_001.jpg", False, days_ago=1),
                                    create_mock_item("WEDDING_002.jpg", False, days_ago=1),
                                    create_mock_item("WEDDING_003.jpg", False, days_ago=1),
                                ]
                            },
                            {
                                "name": "edited",
                                "path": rf"C:\photobooth\sessions\{(today - timedelta(days=1)).strftime('%Y-%m-%d')}\wedding_shoot\edited",
                                "is_folder": True,
                                "children": [
                                    create_mock_item("FINAL_001.jpg", False, days_ago=1),
                                    create_mock_item("FINAL_002.jpg", False, days_ago=1),
                                ]
                            }
                        ]
                    }
                ]
            }
        ]
    }
    
    return demo_tree
