import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { AuthProvider, useAuth } from './context/AuthContext'
import LoginPage from './pages/LoginPage'
import Sidebar from './components/Sidebar'
import DashboardPage from './pages/DashboardPage'
import ClientsPage from './pages/ClientsPage'
import SessionsPage from './pages/SessionsPage'
import UsersPage from './pages/UsersPage'
import SettingsPage from './pages/SettingsPage'
import ClientDashboardPage from './pages/ClientDashboardPage'

// Route for admin users (with sidebar)
function AdminRoute({ children }) {
  const { user, loading } = useAuth()

  if (loading) {
    return <div className="loading" />
  }

  if (!user) {
    return <Navigate to="/login" replace />
  }

  // Redirect client users to their dashboard
  if (user.role === 'client') {
    return <Navigate to="/client" replace />
  }

  return (
    <div className="app-layout">
      <Sidebar />
      <main className="main-content">
        {children}
      </main>
    </div>
  )
}

// Route for client users (without sidebar)
function ClientRoute({ children }) {
  const { user, loading } = useAuth()

  if (loading) {
    return <div className="loading" />
  }

  if (!user) {
    return <Navigate to="/login" replace />
  }

  // Redirect non-client users to admin dashboard
  if (user.role !== 'client') {
    return <Navigate to="/" replace />
  }

  // Client dashboard has no sidebar
  return children
}

function PublicRoute({ children }) {
  const { user, loading } = useAuth()

  if (loading) {
    return <div className="loading" />
  }

  if (user) {
    // Redirect based on role
    if (user.role === 'client') {
      return <Navigate to="/client" replace />
    }
    return <Navigate to="/" replace />
  }

  return children
}

function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route
            path="/login"
            element={
              <PublicRoute>
                <LoginPage />
              </PublicRoute>
            }
          />
          {/* Admin routes */}
          <Route
            path="/"
            element={
              <AdminRoute>
                <DashboardPage />
              </AdminRoute>
            }
          />
          <Route
            path="/clients"
            element={
              <AdminRoute>
                <ClientsPage />
              </AdminRoute>
            }
          />
          <Route
            path="/sessions"
            element={
              <AdminRoute>
                <SessionsPage />
              </AdminRoute>
            }
          />
          <Route
            path="/users"
            element={
              <AdminRoute>
                <UsersPage />
              </AdminRoute>
            }
          />
          <Route
            path="/settings"
            element={
              <AdminRoute>
                <SettingsPage />
              </AdminRoute>
            }
          />
          {/* Client routes */}
          <Route
            path="/client"
            element={
              <ClientRoute>
                <ClientDashboardPage />
              </ClientRoute>
            }
          />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  )
}

export default App
