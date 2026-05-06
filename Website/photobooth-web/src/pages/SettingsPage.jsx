export default function SettingsPage() {
  return (
    <div>
      <div className="page-header">
        <h1 className="page-title">Settings</h1>
      </div>

      <div className="card">
        <div className="card-header">
          <h3 className="card-title">Server Configuration</h3>
        </div>
        <div className="form-group">
          <label>Server URL</label>
          <input type="text" defaultValue="http://localhost:8080" readOnly />
        </div>
        <div className="form-group">
          <label>Data Directory</label>
          <input type="text" defaultValue="data/" readOnly />
        </div>
        <div className="form-group">
          <label>Download Directory</label>
          <input type="text" defaultValue="data/downloads/" readOnly />
        </div>
      </div>

      <div className="card mt-4">
        <div className="card-header">
          <h3 className="card-title">About</h3>
        </div>
        <div className="text-muted" style={{ fontSize: '0.875rem', lineHeight: 1.8 }}>
          <p>Photobooth Management System v1.0.0</p>
          <p>Server API running on port 8080</p>
        </div>
      </div>
    </div>
  )
}
