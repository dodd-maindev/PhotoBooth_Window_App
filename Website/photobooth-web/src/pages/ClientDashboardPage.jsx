import { useState, useEffect } from 'react'
import api from '../utils/api'
import { useAuth } from '../context/AuthContext'

export default function ClientDashboardPage() {
  const { user, logout } = useAuth()
  const [serverStatus, setServerStatus] = useState('connecting')
  const [lastSync, setLastSync] = useState(null)
  const [clientId, setClientId] = useState(null)
  const [errorMessage, setErrorMessage] = useState(null)
  
  const serverUrl = import.meta.env.VITE_API_URL || 'http://localhost:5051'

  // Register client with server and send heartbeat
  useEffect(() => {
    if (user && user.role === 'client') {
      registerClient()
      
      // Send heartbeat every 30 seconds
      const heartbeatInterval = setInterval(sendHeartbeat, 30000)
      // Check connection every 10 seconds
      const checkInterval = setInterval(checkConnection, 10000)
      
      return () => {
        clearInterval(heartbeatInterval)
        clearInterval(checkInterval)
      }
    }
  }, [user])

  const registerClient = async () => {
    try {
      setServerStatus('connecting')
      setErrorMessage(null)
      
      // Get machine info - match ClientRegister model
      const machineInfo = {
        client_id: user?.username, // Use username as client_id
        name: user?.display_name || user?.username || 'Photobooth',
        machine_type: 'Photobooth',
        port: 5050
      }
      
      // Register with server
      const response = await api.post('/clients/register', machineInfo)
      
      if (response.data && response.data.id) {
        setClientId(response.data.id)
        setServerStatus('connected')
        setLastSync(new Date())
        console.log('Client registered with ID:', response.data.id)
      }
    } catch (error) {
      console.error('Failed to register client:', error)
      setServerStatus('error')
      setErrorMessage(error.response?.data?.detail || 'Không thể kết nối đến server')
    }
  }

  const sendHeartbeat = async () => {
    if (!clientId) return
    
    try {
      // Send heartbeat to server
      await api.post(`/clients/${clientId}/heartbeat`, {
        status: 'online',
        last_activity: new Date().toISOString()
      })
      setLastSync(new Date())
    } catch (error) {
      console.error('Heartbeat failed:', error)
    }
  }

  const checkConnection = async () => {
    try {
      // Verify connection by calling auth/me
      const response = await api.get('/auth/me')
      if (response.data) {
        setServerStatus('connected')
      }
    } catch (error) {
      setServerStatus('disconnected')
    }
  }

  const handleLogout = () => {
    logout()
    window.location.href = '/login'
  }

  const getStatusDisplay = () => {
    switch (serverStatus) {
      case 'connecting':
        return { text: 'Đang kết nối...', color: '#f59e0b', bg: 'rgba(245, 158, 11, 0.1)' }
      case 'connected':
        return { text: 'Kết nối thành công', color: '#22c55e', bg: 'rgba(34, 197, 94, 0.1)' }
      case 'disconnected':
        return { text: 'Mất kết nối', color: '#ef4444', bg: 'rgba(239, 68, 68, 0.1)' }
      case 'error':
        return { text: 'Lỗi kết nối', color: '#ef4444', bg: 'rgba(239, 68, 68, 0.1)' }
      default:
        return { text: 'Không xác định', color: '#6b7280', bg: 'rgba(107, 114, 128, 0.1)' }
    }
  }

  const statusDisplay = getStatusDisplay()

  return (
    <div className="client-dashboard">
      <div className="client-dashboard-container">
        <div className="client-header">
          <div className="client-logo">
            <span className="logo-icon">📷</span>
            <div className="logo-text">
              <h1>JOLI FILM</h1>
              <span>Photobooth Client</span>
            </div>
          </div>
          <div className="client-user">
            <span className="user-name">{user?.display_name || user?.username}</span>
            <button onClick={handleLogout} className="btn-logout-sm">Đăng xuất</button>
          </div>
        </div>

        <div className="client-status-card">
          <div className="status-indicator" style={{ backgroundColor: statusDisplay.color }}>
            <div className="status-pulse"></div>
          </div>
          <div className="status-content">
            <h2>Trạng thái kết nối Server</h2>
            <p className="status-text" style={{ color: statusDisplay.color, backgroundColor: statusDisplay.bg }}>
              {statusDisplay.text}
            </p>
            {errorMessage && (
              <p className="error-detail">{errorMessage}</p>
            )}
            {clientId && (
              <p className="client-id-display">Client ID: {clientId}</p>
            )}
          </div>
        </div>

        <div className="client-info-card">
          <h3>Thông tin máy</h3>
          <div className="info-grid">
            <div className="info-item">
              <span className="info-label">Tên máy</span>
              <span className="info-value">{user?.display_name || 'Photobooth'}</span>
            </div>
            <div className="info-item">
              <span className="info-label">Loại tài khoản</span>
              <span className="info-value">Client</span>
            </div>
            <div className="info-item">
              <span className="info-label">Server</span>
              <span className="info-value mono">{serverUrl}</span>
            </div>
            <div className="info-item">
              <span className="info-label">Lần cuối sync</span>
              <span className="info-value">
                {lastSync ? lastSync.toLocaleTimeString() : 'Chưa có'}
              </span>
            </div>
          </div>
        </div>

        <div className="client-instruction-card">
          <div className="instruction-icon">ℹ️</div>
          <div className="instruction-content">
            <h3>Hướng dẫn</h3>
            <p>Máy này đang chờ Admin gọi để đồng bộ dữ liệu.</p>
            <ul>
              <li>Khi Admin cần lấy dữ liệu, hệ thống sẽ tự động kết nối đến máy này</li>
              <li>Đảm bảo máy này đang bật và kết nối mạng</li>
              <li>Không tắt máy trong quá trình đồng bộ</li>
            </ul>
          </div>
        </div>

        <div className="client-footer">
          <p>JOLI FILM Photobooth System v1.0</p>
        </div>
      </div>
    </div>
  )
}
