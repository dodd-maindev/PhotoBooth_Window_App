import { useState, useEffect, useRef } from 'react'
import api from '../utils/api'
import ConfirmModal, { SuccessModal, ErrorModal } from '../components/ConfirmModal'

// Demo mode flag - set to false when using real client connections
const DEMO_MODE = false

// Tree View Component
function TreeNode({ node, selectedPath, onSelect, onDownload, depth = 0 }) {
  const [isExpanded, setIsExpanded] = useState(depth < 1)
  const hasChildren = node.children && node.children.length > 0
  const isSelected = selectedPath === node.path

  return (
    <div className="tree-node">
      <div
        className={`tree-item ${isSelected ? 'selected' : ''}`}
        style={{ paddingLeft: `${depth * 20 + 8}px` }}
        onClick={() => onSelect(node)}
      >
        {hasChildren ? (
          <span
            className={`tree-toggle ${isExpanded ? 'expanded' : ''}`}
            onClick={(e) => {
              e.stopPropagation()
              setIsExpanded(!isExpanded)
            }}
          >
            {isExpanded ? '▼' : '▶'}
          </span>
        ) : (
          <span className="tree-toggle-placeholder"></span>
        )}
        <span className={`tree-icon ${node.is_folder ? 'folder' : 'file'}`}>
          {node.is_folder ? '📁' : '📄'}
        </span>
        <span className="tree-name">{node.name}</span>
        {!node.is_folder && node.size && (
          <span className="tree-size">{formatSize(node.size)}</span>
        )}
        {node.is_folder && (
          <button
            className="btn btn-sm btn-primary tree-download-btn"
            onClick={(e) => {
              e.stopPropagation()
              onDownload(node)
            }}
            title="Download this folder"
          >
            ⬇
          </button>
        )}
      </div>
      {hasChildren && isExpanded && (
        <div className="tree-children">
          {node.children.map((child, index) => (
            <TreeNode
              key={`${child.path}-${index}`}
              node={child}
              selectedPath={selectedPath}
              onSelect={onSelect}
              onDownload={onDownload}
              depth={depth + 1}
            />
          ))}
        </div>
      )}
    </div>
  )
}

function formatSize(bytes) {
  if (!bytes || bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i]
}

export default function SessionsPage() {
  const [clients, setClients] = useState([])
  const [selectedClient, setSelectedClient] = useState(null)
  const [folderTree, setFolderTree] = useState(null)
  const [selectedPath, setSelectedPath] = useState(null)
  const [loadingClients, setLoadingClients] = useState(true)
  const [loadingTree, setLoadingTree] = useState(false)
  const [error, setError] = useState(null)
  const [downloading, setDownloading] = useState(false)
  const [downloadProgress, setDownloadProgress] = useState(null)
  
  // Track current request with ref (sync update, won't cause stale closure)
  const currentRequestRef = useRef(0)
  
  // Modal states
  const [showConfirmModal, setShowConfirmModal] = useState(false)
  const [pendingDownload, setPendingDownload] = useState(null)
  const [showSuccessModal, setShowSuccessModal] = useState(false)
  const [successMessage, setSuccessMessage] = useState('')
  const [showErrorModal, setShowErrorModal] = useState(false)
  const [errorMessage, setErrorMessage] = useState('')

  // Fetch clients on mount
  useEffect(() => {
    fetchClients()
  }, [])

  const fetchClients = async () => {
    try {
      setLoadingClients(true)
      
      if (DEMO_MODE) {
        // Demo mode: create mock clients
        const demoClients = [
          {
            id: 'demo-client-1',
            name: 'Client 1 - Demo',
            ip_address: '192.168.1.100',
            port: 5050,
            status: 'online',
            machine_type: 'Photobooth Pro'
          },
          {
            id: 'demo-client-2',
            name: 'Client 2 - Demo',
            ip_address: '192.168.1.101',
            port: 5050,
            status: 'online',
            machine_type: 'Photobooth Mini'
          }
        ]
        setClients(demoClients)
        
        // Auto-select first client in demo mode
        if (!selectedClient) {
          handleSelectClient(demoClients[0])
        }
      } else {
        // Real mode: fetch from server
        const response = await api.get('/clients')
        const onlineClients = response.data.filter(c => c.status === 'online')
        setClients(onlineClients)
        
        // Auto-select first client if available
        if (onlineClients.length > 0 && !selectedClient) {
          handleSelectClient(onlineClients[0])
        }
      }
    } catch (error) {
      console.error('Failed to fetch clients:', error)
      setError('Failed to load clients')
    } finally {
      setLoadingClients(false)
    }
  }

  const handleSelectClient = async (client) => {
    // Increment request ID to invalidate any pending requests (sync with ref)
    currentRequestRef.current += 1
    const requestId = currentRequestRef.current
    
    setSelectedClient(client)
    setFolderTree(null)
    setSelectedPath(null)
    setError(null)
    
    if (client.id === 'local') {
      await fetchFolderTree('local', true, requestId)
    } else {
      await fetchFolderTree(client.id, false, requestId)
    }
  }

  const fetchFolderTree = async (clientId, useLocal = false, requestId) => {
    try {
      setLoadingTree(true)
      setError(null)
      
      // Abort if another request started
      if (requestId !== currentRequestRef.current) {
        return
      }
      
      let response;
      if (DEMO_MODE || clientId.startsWith('demo-')) {
        response = await api.get('/files/demo/tree')
      } else if (useLocal || clientId === 'local') {
        response = await api.get('/files/local/tree')
      } else {
        response = await api.get(`/files/clients/${clientId}/tree`)
      }
      
      // Only update if this is still the current request
      if (requestId !== currentRequestRef.current) return
      
      setFolderTree(response.data)
      if (response.data?.path) {
        setSelectedPath(response.data.path)
      }
    } catch (error) {
      // Only show error if this is still the current request
      if (requestId !== currentRequestRef.current) return
      
      console.error('Failed to fetch folder tree:', error)
      if (error.response?.status === 503) {
        setError('Client is offline. Please wait for it to come online.')
      } else if (error.response?.status === 404) {
        setError('Folder not found. Make sure C:\\photobooth\\sessions exists.')
      } else {
        setError('Failed to load folder structure. Try clicking "Use Local Folder" button.')
      }
      setFolderTree(null)
    } finally {
      // Only update loading if this is still the current request
      if (requestId !== currentRequestRef.current) return
      setLoadingTree(false)
    }
  }

  const handleRefresh = () => {
    if (selectedClient) {
      fetchFolderTree(selectedClient.id)
    }
  }

  const handleSelectItem = (node) => {
    setSelectedPath(node.path)
  }

  const handleDownloadFolder = async (node) => {
    if (!selectedClient || !node.is_folder) return
    
    setPendingDownload(node)
    setShowConfirmModal(true)
  }

  const confirmDownload = async () => {
    const node = pendingDownload
    setShowConfirmModal(false)
    
    try {
      setDownloading(true)
      setDownloadProgress({ current: 0, total: 0, file: 'Starting...' })
      
      const destPath = `data/downloads/${selectedClient.id}/${node.name}`
      
      if (DEMO_MODE || selectedClient.id.startsWith('demo-') || selectedClient.id === 'local') {
        // Demo mode: simulate download
        await new Promise(resolve => setTimeout(resolve, 1500))
        setSuccessMessage(`[Demo] Folder "${node.name}" would be downloaded to: ${destPath}`)
        setShowSuccessModal(true)
      } else {
        // Real mode: call API
        await api.post(`/files/clients/${selectedClient.id}/download-folder`, null, {
          params: {
            source_path: node.path,
            dest_path: destPath
          }
        })
        setSuccessMessage(`Folder "${node.name}" downloaded successfully!`)
        setShowSuccessModal(true)
      }
    } catch (error) {
      console.error('Download failed:', error)
      setErrorMessage(error.response?.data?.detail || 'Failed to download folder. Please try again.')
      setShowErrorModal(true)
    } finally {
      setDownloading(false)
      setDownloadProgress(null)
      setPendingDownload(null)
    }
  }

  const getClientStatusClass = (client) => {
    return client.status === 'online' ? 'status-online' : 'status-offline'
  }

  if (loadingClients) {
    return <div className="loading">Loading clients...</div>
  }

  return (
    <div className="file-explorer">
      {/* Left Sidebar - Client List */}
      <div className="explorer-sidebar">
        <div className="sidebar-header">
          <h3>Clients</h3>
          <div className="sidebar-header-actions">
            {DEMO_MODE && (
              <span className="demo-badge">DEMO</span>
            )}
            <button
              className="btn btn-sm btn-secondary"
              onClick={fetchClients}
              title="Refresh clients"
            >
              ↻
            </button>
          </div>
        </div>
        <div className="client-list">
          {clients.length === 0 ? (
            <div className="empty-state">
              <p>No clients online</p>
              <button
                className="btn btn-sm btn-secondary"
                onClick={() => fetchFolderTree('local', true)}
                style={{ marginTop: '8px' }}
              >
                📁 Use Local Folder
              </button>
            </div>
          ) : (
            <>
              <div
                className={`client-item ${selectedClient?.id === 'local' ? 'selected' : ''}`}
                onClick={() => handleSelectClient({ id: 'local', name: 'Local Folder' })}
              >
                <div className="client-status">
                  <span className="status-dot status-local"></span>
                </div>
                <div className="client-info">
                  <div className="client-name">📁 Local Folder</div>
                  <div className="client-meta">Server's C:\photobooth</div>
                </div>
              </div>
              {clients.map(client => (
                <div
                  key={client.id}
                  className={`client-item ${selectedClient?.id === client.id ? 'selected' : ''}`}
                  onClick={() => handleSelectClient(client)}
                >
                  <div className="client-status">
                    <span className={`status-dot ${getClientStatusClass(client)}`}></span>
                  </div>
                  <div className="client-info">
                    <div className="client-name">{client.name}</div>
                    <div className="client-meta">
                      {client.ip_address}:{client.port}
                    </div>
                  </div>
                </div>
              ))}
            </>
          )}
        </div>
      </div>

      {/* Main Content - Folder Tree */}
      <div className="explorer-main">
        <div className="explorer-toolbar">
          <div className="toolbar-left">
            <h2 className="page-title">
              {selectedClient ? `${selectedClient.name} - Sessions` : 'Select a Client'}
            </h2>
          </div>
          <div className="toolbar-right">
            {selectedClient && (
              <>
                <button
                  className="btn btn-secondary"
                  onClick={handleRefresh}
                  disabled={loadingTree}
                >
                  {loadingTree ? 'Loading...' : '↻ Refresh'}
                </button>
              </>
            )}
          </div>
        </div>

        <div className="explorer-content">
          {error && (
            <div className="error-message">
              <span className="error-icon">⚠</span>
              {error}
            </div>
          )}

          {!selectedClient && (
            <div className="empty-state">
              Select a client from the sidebar to view their sessions
            </div>
          )}

          {selectedClient && !folderTree && !loadingTree && !error && (
            <div className="empty-state">
              No sessions folder found on this client
            </div>
          )}

          {loadingTree && (
            <div className="loading">Loading folder structure...</div>
          )}

          {folderTree && !loadingTree && (
            <div className="folder-tree-container">
              <div className="tree-header">
                <span className="tree-path">
                  {folderTree.path}
                  {folderTree.is_demo && <span className="demo-indicator"> (Demo Data)</span>}
                </span>
              </div>
              <div className="tree-body">
                {folderTree.children && folderTree.children.length > 0 ? (
                  folderTree.children.map((child, index) => (
                    <TreeNode
                      key={`${child.path}-${index}`}
                      node={child}
                      selectedPath={selectedPath}
                      onSelect={handleSelectItem}
                      onDownload={handleDownloadFolder}
                      depth={0}
                    />
                  ))
                ) : (
                  <div className="empty-folder">This folder is empty</div>
                )}
              </div>
            </div>
          )}
        </div>

        {/* Download Progress */}
        {downloading && downloadProgress && (
          <div className="download-progress-bar">
            <div className="progress-text">
              Downloading: {downloadProgress.file}
            </div>
          </div>
        )}
      </div>

      {/* Modals */}
      <ConfirmModal
        isOpen={showConfirmModal}
        onClose={() => setShowConfirmModal(false)}
        onConfirm={confirmDownload}
        title="Download Folder"
        message={pendingDownload ? `Download folder "${pendingDownload.name}" to server?` : ''}
        confirmText="Download"
        cancelText="Cancel"
        isLoading={downloading}
      />
      
      <SuccessModal
        isOpen={showSuccessModal}
        onClose={() => setShowSuccessModal(false)}
        title="Success"
        message={successMessage}
      />
      
      <ErrorModal
        isOpen={showErrorModal}
        onClose={() => setShowErrorModal(false)}
        title="Error"
        message={errorMessage}
      />
    </div>
  )
}
