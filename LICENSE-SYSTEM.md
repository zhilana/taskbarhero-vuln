# J3L1XD License System

Sistem lisensi untuk melindungi J3L1XD BepInEx Plugin dari pembajakan.

## Fitur

- **HWID Binding** - Lisensi terikat ke hardware spesifik
- **Online Validation** - Validasi lisensi via server
- **Anti-Tamper** - Deteksi debugger dan integrity check
- **Rate Limiting** - Proteksi brute force
- **Admin Dashboard** - CLI untuk manage lisensi

## Setup Server

### 1. Install Dependencies

```bash
cd license-server
pip install -r requirements.txt
```

### 2. Buat Admin User

```bash
python admin.py create-admin admin your_password
```

### 3. Jalankan Server

```bash
python server.py
```

Server berjalan di `http://localhost:8000`

### 4. Generate License Key

```bash
python admin.py --admin-user admin --admin-pass your_password generate --days 30 --max-hwid 3
```

## Setup Client (BepInEx Plugin)

### 1. Build Plugin

```bash
BEPINEX_PATH="C:\path\to\game" dotnet build -c Release
```

### 2. Install Plugin

Copy `bin/J3L1XD.dll` ke `BepInEx/plugins/J3L1XD/`

### 3. Input License Key

- Jalankan game
- Akan muncul popup input license key
- Masukkan key yang didapat dari admin
- Restart game

## Admin Commands

```bash
# Generate license
python admin.py generate --days 30 --max-hwid 3

# List all licenses
python admin.py list

# Revoke license
python admin.py revoke XXXX-XXXX-XXXX-XXXX

# Ban HWID (force deactivate)
python admin.py ban XXXX-XXXX-XXXX-XXXX
```

## Deployment Server

### Railway/Render

1. Push repo ke GitHub
2. Create new project di Railway/Render
3. Set environment variable:
   - `LICENSE_SECRET`: random secret key
   - `DB_PATH`: `/data/licenses.db`
4. Deploy

### VPS

```bash
# Install
git clone <repo>
cd license-server
pip install -r requirements.txt

# Run with systemd
sudo tee /etc/systemd/system/license-server.service << EOF
[Unit]
Description=J3L1XD License Server
After=network.target

[Service]
User=www-data
WorkingDirectory=/opt/license-server
ExecStart=/usr/bin/python3 server.py
Restart=always
Environment=LICENSE_SECRET=your_secret_here
Environment=DB_PATH=/opt/license-server/licenses.db

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl enable license-server
sudo systemctl start license-server
```

## Endpoint API

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/validate` | Validasi license key + HWID |
| POST | `/api/auth/login` | Login admin |
| POST | `/api/admin/generate-license` | Generate license key |
| GET | `/api/admin/list-licenses` | List semua licenses |
| POST | `/api/admin/revoke-license` | Revoke license |
| POST | `/api/admin/ban-hwid` | Ban HWID |
| GET | `/api/health` | Health check |

## Keamanan

- License key di-hash dengan SHA256 sebelum disimpan
- HWID di-hash untuk privasi
- Rate limiting: 10 requests per menit per IP
- JWT token untuk admin auth
- HTTPS wajib untuk production

## Troubleshooting

### License validation gagal

1. Cek koneksi internet
2. Pastikan server berjalan
3. Cek log di `BepInEx/LogOutput.log`

### Server tidak bisa diakses

1. Pastikan port 8000 terbuka
2. Cek firewall settings
3. Gunakan HTTPS untuk production
