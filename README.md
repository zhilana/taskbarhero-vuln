# J3L1XD - TaskBar Hero BepInEx Plugin

Cheat plugin for TaskBar Hero (Unity IL2CPP) using BepInEx + Harmony.

## Features

- **God Mode** — Immortal hero
- **One Hit Kill** — Instantly kill monsters
- **Attack Speed** — 10x faster attacks
- **Move Speed** — 100x faster movement
- **Max Gold** — Always have max coins (survives save/load)
- **Max Exp** — 10,000x experience multiplier
- **Chest Bypass** — Enter stages without chest space check
- **License System** — HWID-bound online license validation

## Quick Install (Drop-In)

1. Open the game install folder that contains `TaskBarHero.exe`
2. Extract everything from `J3L1XD-BepInEx-DropIn.zip` into that folder
3. If Windows asks to merge folders or replace files, choose Yes
4. Start the game
5. Enter your license key when prompted, then restart

> All cheats start OFF by default. Press F5 in-game to toggle the menu.

## Build from Source

### Prerequisites

- .NET 6.0 SDK
- BepInEx installed in TaskBar Hero

### Build

```bash
BEPINEX_PATH="$HOME/.local/share/Steam/steamapps/common/TaskbarHero" dotnet build -c Release
```

### Install

Copy `bin/J3L1XD.dll` to `BepInEx/plugins/J3L1XD/`

## License System

See [LICENSE-SYSTEM.md](LICENSE-SYSTEM.md) for full setup guide.

### Quick Start

```bash
# Server setup
cd license-server
pip install -r requirements.txt
python admin.py create-admin admin your_password
python server.py

# Generate a key
python admin.py --admin-user admin --admin-pass your_password generate --days 30

# Client: place key in BepInEx/config/J3L1XD.license
```

## Server Deployment

### Railway / Render

1. Push repo to GitHub
2. Create new project on Railway or Render
3. Set environment variables:
   - `LICENSE_SECRET` — random secret key
   - `DB_PATH` — `/data/licenses.db`
4. Deploy

### VPS (systemd)

```bash
sudo tee /etc/systemd/system/license-server.service << 'EOF'
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

sudo systemctl enable --now license-server
```

## Project Structure

```
J3L1XD/
  Plugin.cs            — Main plugin (Harmony patches + license gate)
  LicenseSystem.cs     — HWID, anti-tamper, license validator
  license-server/
    server.py          — FastAPI license validation server
    admin.py           — CLI for key management
    requirements.txt   — Python dependencies
  LICENSE-SYSTEM.md    — Full setup documentation
```

## License

Private — not for redistribution.
