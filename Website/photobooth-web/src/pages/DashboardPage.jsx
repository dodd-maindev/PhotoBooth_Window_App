import { useState, useEffect } from 'react'
import api from '../utils/api'

export default function DashboardPage() {
  const [stats, setStats] = useState({
    total_clients: 0,
    online_clients: 0,
    total_sessions: 0,
    total_photos: 0,
    synced_sessions: 0,
  })
  const [clients, setClients] = useState([])
  const [sessions, setSessions] = useState([])
  const [loading, setLoading] = useState(true)

  const fetchData = async () => {
    try {
      const [clientsRes, sessionsRes] = await Promise.all([
        api.get('/clients'),
        api.get('/sessions'),
      ])
      setClients(clientsRes.data)
      setSessions(sessionsRes.data)

      const online = clientsRes.data.filter((c) => c.status === 'online').length
      const synced = sessionsRes.data.filter((s) => s.status === 'synced').length
      const photos = sessionsRes.data.reduce((acc, s) => acc + (s.photo_count || 0), 0)

      setStats({
        total_clients: clientsRes.data.length,
        online_clients: online,
        total_sessions: sessionsRes.data.length,
        total_photos: photos,
        synced_sessions: synced,
      })
    } catch (error) {
      console.error('Failed to fetch data:', error)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchData()
    const interval = setInterval(fetchData, 30000)
    return () => clearInterval(interval)
  }, [])

  if (loading) {
    return <div className="loading" />
  }

  return (
    <div>
      <div className="page-header">
        <h1 className="page-title">Dashboard</h1>
      </div>

      <div className="stats-grid">
        <div className="stat-card">
          <div className="stat-value">{stats.total_clients}</div>
          <div className="stat-label">Total Clients</div>
        </div>
        <div className="stat-card">
          <div className="stat-value">{stats.online_clients}</div>
          <div className="stat-label">Online</div>
        </div>
        <div className="stat-card">
          <div className="stat-value">{stats.total_sessions}</div>
          <div className="stat-label">Total Sessions</div>
        </div>
        <div className="stat-card">
          <div className="stat-value">{stats.total_photos}</div>
          <div className="stat-label">Photos</div>
        </div>
      </div>

      <div className="page-grid">
        <div className="card">
          <div className="card-header">
            <h3 className="card-title">Clients</h3>
          </div>
          <div className="table-container" style={{ border: 'none' }}>
            <table>
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Type</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {clients.length === 0 ? (
                  <tr>
                    <td colSpan="3">
                      <div className="empty-state">No clients connected</div>
                    </td>
                  </tr>
                ) : (
                  clients.map((client) => (
                    <tr key={client.id}>
                      <td className="font-bold">{client.name}</td>
                      <td className="text-muted">{client.machine_type}</td>
                      <td>
                        <span className={`status-badge status-${client.status}`}>
                          {client.status}
                        </span>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>

        <div className="card">
          <div className="card-header">
            <h3 className="card-title">Sessions</h3>
          </div>
          <div className="table-container" style={{ border: 'none' }}>
            <table>
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Client</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {sessions.length === 0 ? (
                  <tr>
                    <td colSpan="3">
                      <div className="empty-state">No sessions yet</div>
                    </td>
                  </tr>
                ) : (
                  sessions.slice(0, 5).map((session) => (
                    <tr key={session.id}>
                      <td className="font-bold">{session.name}</td>
                      <td className="text-muted">{session.client_name}</td>
                      <td>
                        <span
                          className={`status-badge ${
                            session.status === 'synced' ? 'status-synced' : 'status-offline'
                          }`}
                        >
                          {session.status}
                        </span>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  )
}
