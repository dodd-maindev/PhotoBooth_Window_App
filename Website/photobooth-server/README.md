# Photobooth Server

## Installation

```bash
pip install -r requirements.txt
```

## Run

```bash
python main.py
```

Server will start at http://localhost:8080

## Default Users

| Username | Password | Role |
|----------|----------|------|
| admin | admin123 | admin |
| client1 | client123 | client |
| client2 | client123 | client |

## API Endpoints

### Auth
- POST /api/auth/login - Login
- POST /api/auth/logout - Logout
- GET /api/auth/me - Get current user

### Clients
- POST /api/clients/register - Register client
- GET /api/clients - Get all clients
- GET /api/clients/online - Get online clients

### Sessions
- POST /api/sessions - Create session
- GET /api/sessions - Get all sessions
- GET /api/sessions/{id} - Get session by ID

### Webhook
- POST /api/webhook/receive - Receive webhook


Username	Password	Role	Display Name
admin	admin123	admin	Administrator
client1	client123	client	Máy Canon 1
client2	client123	client	Máy Fuji 22