import httpx
import os
from datetime import datetime
from typing import Optional, List, Dict, Any
from fastapi import HTTPException

from app.config import settings


class FileItem:
    """Model for a file or folder item"""
    def __init__(self, name: str, path: str, is_folder: bool, size: int = 0, 
                 modified: Optional[str] = None, children: Optional[List['FileItem']] = None):
        self.name = name
        self.path = path
        self.is_folder = is_folder
        self.size = size
        self.modified = modified
        self.children = children or []


class FileService:
    """Service to interact with client machines for file operations"""
    
    # Default sessions path on client machines
    DEFAULT_SESSIONS_PATH = r"C:\photobooth\sessions"
    
    async def get_folder_tree(self, client_base_url: str, path: str = None) -> Dict[str, Any]:
        """
        Get folder tree structure from client machine
        Returns nested folder/file structure
        """
        if not path:
            path = self.DEFAULT_SESSIONS_PATH
        
        try:
            async with httpx.AsyncClient(timeout=30.0) as client:
                response = await client.get(
                    f"{client_base_url}/api/files/tree",
                    params={"path": path}
                )
                
                if response.status_code == 200:
                    return response.json()
                elif response.status_code == 404:
                    raise HTTPException(status_code=404, detail="Path not found on client")
                else:
                    raise HTTPException(status_code=response.status_code, detail="Failed to get folder tree")
                    
        except httpx.ConnectError:
            raise HTTPException(status_code=503, detail="Client is offline or unreachable")
        except httpx.TimeoutException:
            raise HTTPException(status_code=504, detail="Request to client timed out")
        except Exception as e:
            raise HTTPException(status_code=500, detail=f"Error: {str(e)}")
    
    async def get_folder_contents(self, client_base_url: str, path: str) -> List[Dict[str, Any]]:
        """
        Get contents of a specific folder from client machine
        """
        try:
            async with httpx.AsyncClient(timeout=30.0) as client:
                response = await client.get(
                    f"{client_base_url}/api/files/contents",
                    params={"path": path}
                )
                
                if response.status_code == 200:
                    return response.json()
                elif response.status_code == 404:
                    return []
                else:
                    return []
                    
        except Exception:
            return []
    
    async def download_folder(self, client_base_url: str, source_path: str, 
                             dest_path: str, on_progress: callable = None) -> Dict[str, Any]:
        """
        Download a folder from client machine to server
        
        Args:
            client_base_url: Client's base URL (e.g., http://192.168.1.100:5050)
            source_path: Path on client machine to download
            dest_path: Local path on server to save files
            on_progress: Optional callback for progress updates
            
        Returns:
            Result dict with downloaded count and status
        """
        result = {
            "success": True,
            "downloaded": 0,
            "failed": 0,
            "total": 0,
            "source": source_path,
            "dest": dest_path
        }
        
        try:
            # Get folder tree first
            tree = await self.get_folder_tree(client_base_url, source_path)
            
            if not tree.get("children"):
                return result
            
            # Create destination directory
            os.makedirs(dest_path, exist_ok=True)
            
            # Process files recursively
            async def process_items(items, current_dest):
                downloaded = 0
                failed = 0
                total = 0
                
                for item in items:
                    if item.get("is_folder"):
                        # Create folder locally
                        folder_name = item.get("name", "unknown")
                        new_dest = os.path.join(current_dest, folder_name)
                        os.makedirs(new_dest, exist_ok=True)
                        
                        # Process children
                        d, f, t = await process_items(item.get("children", []), new_dest)
                        downloaded += d
                        failed += f
                        total += t
                    else:
                        # Download file
                        total += 1
                        file_path = item.get("path")
                        file_name = item.get("name")
                        
                        try:
                            # Download from client
                            async with httpx.AsyncClient(timeout=60.0) as client:
                                response = await client.get(
                                    f"{client_base_url}/api/files/download",
                                    params={"path": file_path}
                                )
                                
                                if response.status_code == 200:
                                    dest_file = os.path.join(current_dest, file_name)
                                    os.makedirs(os.path.dirname(dest_file), exist_ok=True)
                                    
                                    with open(dest_file, 'wb') as f:
                                        f.write(response.content)
                                    downloaded += 1
                                    
                                    if on_progress:
                                        on_progress(downloaded, total, file_name)
                                else:
                                    failed += 1
                                    
                        except Exception as e:
                            print(f"Failed to download {file_path}: {e}")
                            failed += 1
                
                return downloaded, failed, total
            
            result["downloaded"], result["failed"], result["total"] = await process_items(
                tree.get("children", []), dest_path
            )
            
        except Exception as e:
            result["success"] = False
            result["error"] = str(e)
        
        return result
    
    async def download_file(self, client_base_url: str, source_path: str, 
                           dest_path: str) -> bool:
        """
        Download a single file from client machine
        """
        try:
            os.makedirs(os.path.dirname(dest_path), exist_ok=True)
            
            async with httpx.AsyncClient(timeout=60.0) as client:
                response = await client.get(
                    f"{client_base_url}/api/files/download",
                    params={"path": source_path}
                )
                
                if response.status_code == 200:
                    with open(dest_path, 'wb') as f:
                        f.write(response.content)
                    return True
                    
        except Exception as e:
            print(f"Failed to download file: {e}")
        
        return False
    
    def build_local_tree(self, base_path: str, relative_to: str = None) -> Dict[str, Any]:
        """
        Build folder tree from local path (for testing/debugging)
        """
        if not os.path.exists(base_path):
            return {
                "path": base_path,
                "name": os.path.basename(base_path),
                "is_folder": True,
                "children": []
            }
        
        def scan_folder(path: str) -> Dict[str, Any]:
            name = os.path.basename(path) or path
            is_folder = os.path.isdir(path)
            
            item = {
                "name": name,
                "path": path,
                "is_folder": is_folder
            }
            
            if is_folder:
                children = []
                try:
                    for entry in os.scandir(path):
                        children.append(scan_folder(entry.path))
                    # Sort: folders first, then files, alphabetically
                    children.sort(key=lambda x: (not x["is_folder"], x["name"].lower()))
                except PermissionError:
                    pass
                item["children"] = children
            else:
                try:
                    item["size"] = os.path.getsize(path)
                    item["modified"] = datetime.fromtimestamp(
                        os.path.getmtime(path)
                    ).isoformat()
                except:
                    item["size"] = 0
                    item["modified"] = None
            
            return item
        
        tree = scan_folder(base_path)
        
        # If relative_to is provided, make paths relative
        if relative_to:
            def make_relative(item, base):
                item["path"] = os.path.relpath(item["path"], base)
                for child in item.get("children", []):
                    make_relative(child, base)
            make_relative(tree, relative_to)
        
        return tree


file_service = FileService()
