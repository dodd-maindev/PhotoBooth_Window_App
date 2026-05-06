import { createContext, useContext, useState, useEffect } from 'react'
import api from '../utils/api'

const AuthContext = createContext(null)

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const token = localStorage.getItem('token')
    if (token) {
      fetchUser(token)
    } else {
      setLoading(false)
    }
  }, [])

  const fetchUser = async (token) => {
    try {
      const response = await api.get('/auth/me')
      setUser(response.data)
    } catch (error) {
      localStorage.removeItem('token')
    }
    setLoading(false)
  }

  const login = async (username, password) => {
    const response = await api.post('/auth/login', { username, password })
    const { access_token, user } = response.data
    localStorage.setItem('token', access_token)
    setUser(user)
    return user
  }

  const logout = async () => {
    const token = localStorage.getItem('token')
    if (token) {
      try {
        await api.post('/auth/logout', {})
      } catch (e) {}
    }
    localStorage.removeItem('token')
    setUser(null)
  }

  return (
    <AuthContext.Provider value={{ user, loading, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within AuthProvider')
  }
  return context
}
