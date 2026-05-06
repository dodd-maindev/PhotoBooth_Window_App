import { useState, useEffect } from 'react'
import api from '../utils/api'

export default function ClientsPage() {
  const [clients, setClients] = useState([])
  const [loading, setLoading] = useState(true)

  const fetchClients = async () => {
    try {
      const response = await api.get('/clients')
      setClients(response.data)
    } catch (error) {
      console.error('Failed to fetch clients:', error)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchClients()
    const interval = setInterval(fetchClients, 10000)
    return () => clearInterval(interval)
  }, [])

  if (loading) {
    return <div className="loading" />
  }

  return (
    <div>
      <div className="page-header flex justify-between items-center">
        <h1 className="page-title">Clients</h1>
        <button onClick={fetchClients} className="btn btn-secondary">
          Refresh
        </button>
      </div>

      <div className="card">
        <div className="table-container">
          <table>
            <thead>
              <tr>
                <th>Name</th>
                <th>Machine</th>
                <th>IP Address</th>
                <th>Port</th>
                <th>Status</th>
                <th>Last Seen</th>
              </tr>
            </thead>
            <tbody>
              {clients.length === 0 ? (
                <tr>
                  <td colSpan="6">
                    <div className="empty-state">No clients connected</div>
                  </td>
                </tr>
              ) : (
                clients.map((client) => (
                  <tr key={client.id}>
                    <td className="font-bold">{client.name}</td>
                    <td className="text-muted">{client.machine_type}</td>
                    <td className="mono">{client.ip_address}</td>
                    <td className="mono">{client.port}</td>
                    <td>
                      <span className={`status-badge status-${client.status}`}>
                        {client.status}
                      </span>
                    </td>
                    <td className="text-muted">
                      {client.last_seen
                        ? new Date(client.last_seen).toLocaleString()
                        : '—'}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
