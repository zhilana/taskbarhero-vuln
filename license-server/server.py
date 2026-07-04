import os
import secrets
import hashlib
import sqlite3
import time
from datetime import datetime, timedelta
from typing import Optional, Dict, Tuple
from contextlib import asynccontextmanager
from collections import defaultdict

from fastapi import FastAPI, HTTPException, Depends, Header, Request
from fastapi.security import HTTPBearer, HTTPAuthorizationCredentials
from fastapi.middleware.cors import CORSMiddleware
from fastapi.middleware.trustedhost import TrustedHostMiddleware
from starlette.middleware.base import BaseHTTPMiddleware
from starlette.responses import Response
from pydantic import BaseModel
from jose import JWTError, jwt

SECRET_KEY = os.getenv("LICENSE_SECRET", secrets.token_hex(32))
ALGORITHM = "HS256"
ACCESS_TOKEN_EXPIRE_HOURS = 24

DB_PATH = os.getenv("DB_PATH", "licenses.db")

security = HTTPBearer()

rate_limit_store: Dict[str, list] = defaultdict(list)
RATE_LIMIT_WINDOW = 60
RATE_LIMIT_MAX_REQUESTS = 10


def check_rate_limit(ip: str) -> bool:
    now = time.time()
    rate_limit_store[ip] = [t for t in rate_limit_store[ip] if now - t < RATE_LIMIT_WINDOW]
    if len(rate_limit_store[ip]) >= RATE_LIMIT_MAX_REQUESTS:
        return False
    rate_limit_store[ip].append(now)
    return True


def init_db():
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    c.execute("""
        CREATE TABLE IF NOT EXISTS licenses (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            license_key TEXT UNIQUE NOT NULL,
            hwid TEXT DEFAULT NULL,
            is_active BOOLEAN DEFAULT 1,
            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            expires_at TIMESTAMP NOT NULL,
            last_validated TIMESTAMP DEFAULT NULL,
            max_hwid_changes INTEGER DEFAULT 3,
            hwid_changes_used INTEGER DEFAULT 0
        )
    """)
    c.execute("""
        CREATE TABLE IF NOT EXISTS admins (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT UNIQUE NOT NULL,
            password_hash TEXT NOT NULL
        )
    """)
    c.execute("""
        CREATE TABLE IF NOT EXISTS validation_log (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            license_key TEXT NOT NULL,
            hwid TEXT NOT NULL,
            ip_address TEXT,
            result TEXT NOT NULL,
            timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        )
    """)
    conn.commit()
    conn.close()


@asynccontextmanager
async def lifespan(app: FastAPI):
    init_db()
    yield


app = FastAPI(
    title="J3L1XD License Server",
    description="License validation API for J3L1XD BepInEx Plugin",
    version="1.0.0",
    lifespan=lifespan
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.add_middleware(
    TrustedHostMiddleware,
    allowed_hosts=["*"]
)


class SecurityHeadersMiddleware(BaseHTTPMiddleware):
    async def dispatch(self, request: Request, call_next):
        response: Response = await call_next(request)
        response.headers["X-Content-Type-Options"] = "nosniff"
        response.headers["X-Frame-Options"] = "DENY"
        response.headers["X-XSS-Protection"] = "1; mode=block"
        response.headers["Referrer-Policy"] = "strict-origin-when-cross-origin"
        response.headers["Cache-Control"] = "no-store, no-cache, must-revalidate"
        response.headers["Pragma"] = "no-cache"
        return response


app.add_middleware(SecurityHeadersMiddleware)


class LicenseCreate(BaseModel):
    license_key: Optional[str] = None
    duration_days: int = 30
    max_hwid_changes: int = 3


class LicenseValidate(BaseModel):
    license_key: str
    hwid: str


class AdminLogin(BaseModel):
    username: str
    password: str


class LicenseResponse(BaseModel):
    valid: bool
    message: str
    expires_at: Optional[str] = None
    hwid_changes_remaining: Optional[int] = None


class TokenResponse(BaseModel):
    access_token: str
    token_type: str = "bearer"


def generate_license_key():
    parts = []
    for _ in range(4):
        part = secrets.token_hex(2).upper()
        parts.append(part)
    return "-".join(parts)


def hash_hwid(hwid: str) -> str:
    return hashlib.sha256(hwid.encode()).hexdigest()


def create_access_token(data: dict):
    to_encode = data.copy()
    expire = datetime.utcnow() + timedelta(hours=ACCESS_TOKEN_EXPIRE_HOURS)
    to_encode.update({"exp": expire})
    return jwt.encode(to_encode, SECRET_KEY, algorithm=ALGORITHM)


def verify_token(credentials: HTTPAuthorizationCredentials = Depends(security)):
    try:
        payload = jwt.decode(credentials.credentials, SECRET_KEY, algorithms=[ALGORITHM])
        username: str = payload.get("sub")
        if username is None:
            raise HTTPException(status_code=401, detail="Invalid token")
        return username
    except JWTError:
        raise HTTPException(status_code=401, detail="Invalid token")


@app.post("/api/auth/login", response_model=TokenResponse)
def admin_login(login: AdminLogin):
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    c.execute("SELECT password_hash FROM admins WHERE username = ?", (login.username,))
    result = c.fetchone()
    conn.close()

    if not result:
        raise HTTPException(status_code=401, detail="Invalid credentials")

    from passlib.hash import bcrypt
    if not bcrypt.verify(login.password, result[0]):
        raise HTTPException(status_code=401, detail="Invalid credentials")

    token = create_access_token(data={"sub": login.username})
    return TokenResponse(access_token=token)


@app.post("/api/admin/create-admin")
def create_admin(login: AdminLogin, username: str = Depends(verify_token)):
    from passlib.hash import bcrypt
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    try:
        password_hash = bcrypt.hash(login.password)
        c.execute(
            "INSERT INTO admins (username, password_hash) VALUES (?, ?)",
            (login.username, password_hash)
        )
        conn.commit()
    except sqlite3.IntegrityError:
        conn.close()
        raise HTTPException(status_code=400, detail="Admin already exists")
    conn.close()
    return {"message": "Admin created successfully"}


@app.post("/api/admin/generate-license")
def generate_license(
    license_create: LicenseCreate,
    username: str = Depends(verify_token)
):
    license_key = license_create.license_key or generate_license_key()
    expires_at = datetime.utcnow() + timedelta(days=license_create.duration_days)

    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    try:
        c.execute(
            """INSERT INTO licenses (license_key, is_active, expires_at, max_hwid_changes)
               VALUES (?, 1, ?, ?)""",
            (license_key, expires_at.isoformat(), license_create.max_hwid_changes)
        )
        conn.commit()
    except sqlite3.IntegrityError:
        conn.close()
        raise HTTPException(status_code=400, detail="License key already exists")
    conn.close()

    return {
        "license_key": license_key,
        "expires_at": expires_at.isoformat(),
        "max_hwid_changes": license_create.max_hwid_changes
    }


@app.get("/api/admin/list-licenses")
def list_licenses(username: str = Depends(verify_token)):
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    c.execute("""
        SELECT license_key, hwid, is_active, created_at, expires_at,
               last_validated, hwid_changes_used, max_hwid_changes
        FROM licenses ORDER BY created_at DESC
    """)
    licenses = []
    for row in c.fetchall():
        licenses.append({
            "license_key": row[0],
            "hwid": row[1],
            "is_active": bool(row[2]),
            "created_at": row[3],
            "expires_at": row[4],
            "last_validated": row[5],
            "hwid_changes_used": row[6],
            "max_hwid_changes": row[7]
        })
    conn.close()
    return {"licenses": licenses}


@app.post("/api/admin/revoke-license")
def revoke_license(license_key: str, username: str = Depends(verify_token)):
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    c.execute("UPDATE licenses SET is_active = 0 WHERE license_key = ?", (license_key,))
    if c.rowcount == 0:
        conn.close()
        raise HTTPException(status_code=404, detail="License not found")
    conn.commit()
    conn.close()
    return {"message": "License revoked"}


@app.post("/api/admin/ban-hwid")
def ban_hwid(license_key: str, username: str = Depends(verify_token)):
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    c.execute(
        "UPDATE licenses SET hwid = NULL, hwid_changes_used = max_hwid_changes WHERE license_key = ?",
        (license_key,)
    )
    if c.rowcount == 0:
        conn.close()
        raise HTTPException(status_code=404, detail="License not found")
    conn.commit()
    conn.close()
    return {"message": "HWID banned, license deactivated"}


@app.post("/api/validate", response_model=LicenseResponse)
def validate_license(validation: LicenseValidate, request: Request):
    client_ip = request.client.host
    if not check_rate_limit(client_ip):
        raise HTTPException(status_code=429, detail="Too many requests")

    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()

    c.execute("""
        SELECT is_active, hwid, expires_at, max_hwid_changes, hwid_changes_used
        FROM licenses WHERE license_key = ?
    """, (validation.license_key,))
    result = c.fetchone()

    if not result:
        conn.close()
        return LicenseResponse(valid=False, message="License not found")

    is_active, hwid, expires_at, max_changes, changes_used = result

    if not is_active:
        conn.close()
        return LicenseResponse(valid=False, message="License is revoked")

    if datetime.fromisoformat(expires_at) < datetime.utcnow():
        conn.close()
        return LicenseResponse(valid=False, message="License expired")

    hashed_hwid = hash_hwid(validation.hwid)

    if hwid is None:
        c.execute(
            "UPDATE licenses SET hwid = ?, last_validated = CURRENT_TIMESTAMP WHERE license_key = ?",
            (hashed_hwid, validation.license_key)
        )
        conn.commit()
        conn.close()
        return LicenseResponse(
            valid=True,
            message="License activated",
            expires_at=expires_at,
            hwid_changes_remaining=max_changes
        )

    if hwid != hashed_hwid:
        if changes_used >= max_changes:
            conn.close()
            return LicenseResponse(valid=False, message="HWID limit exceeded")

        c.execute(
            """UPDATE licenses SET hwid = ?, hwid_changes_used = hwid_changes_used + 1,
               last_validated = CURRENT_TIMESTAMP WHERE license_key = ?""",
            (hashed_hwid, validation.license_key)
        )
        conn.commit()
        conn.close()
        return LicenseResponse(
            valid=True,
            message="HWID updated",
            expires_at=expires_at,
            hwid_changes_remaining=max_changes - changes_used - 1
        )

    c.execute(
        "UPDATE licenses SET last_validated = CURRENT_TIMESTAMP WHERE license_key = ?",
        (validation.license_key,)
    )
    conn.commit()
    conn.close()

    return LicenseResponse(
        valid=True,
        message="License valid",
        expires_at=expires_at,
        hwid_changes_remaining=max_changes - changes_used
    )


@app.get("/api/health")
def health_check():
    return {"status": "ok", "version": "1.0.0"}


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
