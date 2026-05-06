import { useState, useEffect } from 'react'
import api from '../utils/api'
import { useAuth } from '../context/AuthContext'

export default function UsersPage() {
  const [users, setUsers] = useState([])
  const [loading, setLoading] = useState(true)
  const [showForm, setShowForm] = useState(false)
  const [formData, setFormData] = useState({
    username: '',
    password: '',
    display_name: '',
    role: 'client',
  })
  const { user: currentUser } = useAuth()

  const fetchUsers = async () => {
    try {
      const response = await api.get('/auth/users')
      setUsers(response.data)
    } catch (error) {
      console.error('Failed to fetch users:', error)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchUsers()
  }, [])

  const handleSubmit = async (e) => {
    e.preventDefault()
    try {
      await api.post('/auth/users', formData)
      setShowForm(false)
      setFormData({ username: '', password: '', display_name: '', role: 'client' })
      fetchUsers()
    } catch (error) {
      alert(error.response?.data?.detail || 'Failed to create user')
    }
  }

  const isAdmin = currentUser?.role === 'admin'

  if (loading) {
    return <div className="loading" />
  }

  return (
    <div>
      <div className="page-header flex justify-between items-center">
        <h1 className="page-title">Users</h1>
        {isAdmin && (
          <button
            onClick={() => setShowForm(!showForm)}
            className={showForm ? 'btn btn-ghost' : 'btn btn-primary'}
          >
            {showForm ? 'Cancel' : 'Add User'}
          </button>
        )}
      </div>

      {showForm && isAdmin && (
        <div className="card mb-4">
          <div className="card-header">
            <h3 className="card-title">Create New User</h3>
          </div>
          <form onSubmit={handleSubmit}>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--spacing-md)' }}>
              <div className="form-group">
                <label>Username</label>
                <input
                  type="text"
                  value={formData.username}
                  onChange={(e) => setFormData({ ...formData, username: e.target.value })}
                  required
                  placeholder="username"
                />
              </div>
              <div className="form-group">
                <label>Password</label>
                <input
                  type="password"
                  value={formData.password}
                  onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                  required
                  placeholder="password"
                />
              </div>
              <div className="form-group">
                <label>Display Name</label>
                <input
                  type="text"
                  value={formData.display_name}
                  onChange={(e) => setFormData({ ...formData, display_name: e.target.value })}
                  required
                  placeholder="Full Name"
                />
              </div>
              <div className="form-group">
                <label>Role</label>
                <select
                  value={formData.role}
                  onChange={(e) => setFormData({ ...formData, role: e.target.value })}
                >
                  <option value="client">Client</option>
                  <option value="admin">Admin</option>
                </select>
              </div>
            </div>
            <button type="submit" className="btn btn-primary" style={{ marginTop: 'var(--spacing-md)' }}>
              Create User
            </button>
          </form>
        </div>
      )}

      <div className="card">
        <div className="table-container">
          <table>
            <thead>
              <tr>
                <th>Username</th>
                <th>Display Name</th>
                <th>Role</th>
              </tr>
            </thead>
            <tbody>
              {users.map((user) => (
                <tr key={user.username}>
                  <td className="mono">{user.username}</td>
                  <td>{user.display_name}</td>
                  <td>
                    <span
                      className={`status-badge ${
                        user.role === 'admin' ? 'status-synced' : 'status-offline'
                      }`}
                    >
                      {user.role}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
