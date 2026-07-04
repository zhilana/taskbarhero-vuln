#!/usr/bin/env python3
import argparse
import httpx
import json
import sys

DEFAULT_SERVER = "http://localhost:8000"


def get_token(server: str, username: str, password: str) -> str:
    resp = httpx.post(
        f"{server}/api/auth/login",
        json={"username": username, "password": password}
    )
    if resp.status_code != 200:
        print(f"Login failed: {resp.json().get('detail', 'Unknown error')}")
        sys.exit(1)
    return resp.json()["access_token"]


def generate_license(args):
    token = get_token(args.server, args.admin_user, args.admin_pass)
    resp = httpx.post(
        f"{server}/api/admin/generate-license",
        headers={"Authorization": f"Bearer {token}"},
        json={
            "license_key": args.key,
            "duration_days": args.days,
            "max_hwid_changes": args.max_hwid
        }
    )
    if resp.status_code != 200:
        print(f"Error: {resp.json().get('detail', 'Unknown error')}")
        sys.exit(1)

    data = resp.json()
    print(f"\nLicense generated:")
    print(f"  Key: {data['license_key']}")
    print(f"  Expires: {data['expires_at']}")
    print(f"  Max HWID changes: {data['max_hwid_changes']}")


def list_licenses(args):
    token = get_token(args.server, args.admin_user, args.admin_pass)
    resp = httpx.get(
        f"{server}/api/admin/list-licenses",
        headers={"Authorization": f"Bearer {token}"}
    )
    if resp.status_code != 200:
        print(f"Error: {resp.json().get('detail', 'Unknown error')}")
        sys.exit(1)

    licenses = resp.json()["licenses"]
    if not licenses:
        print("No licenses found.")
        return

    print(f"\n{'Key':<25} {'Active':<8} {'HWID':<20} {'Expires':<25} {'Changes'}")
    print("-" * 100)
    for lic in licenses:
        hwid = lic["hwid"][:16] + "..." if lic["hwid"] else "None"
        active = "Yes" if lic["is_active"] else "No"
        print(f"{lic['license_key']:<25} {active:<8} {hwid:<20} {lic['expires_at']:<25} {lic['hwid_changes_used']}/{lic['max_hwid_changes']}")


def revoke_license(args):
    token = get_token(args.server, args.admin_user, args.admin_pass)
    resp = httpx.post(
        f"{server}/api/admin/revoke-license",
        headers={"Authorization": f"Bearer {token}"},
        params={"license_key": args.key}
    )
    if resp.status_code != 200:
        print(f"Error: {resp.json().get('detail', 'Unknown error')}")
        sys.exit(1)
    print(f"License {args.key} revoked.")


def ban_hwid(args):
    token = get_token(args.server, args.admin_user, args.admin_pass)
    resp = httpx.post(
        f"{server}/api/admin/ban-hwid",
        headers={"Authorization": f"Bearer {token}"},
        params={"license_key": args.key}
    )
    if resp.status_code != 200:
        print(f"Error: {resp.json().get('detail', 'Unknown error')}")
        sys.exit(1)
    print(f"HWID banned for license {args.key}.")


def create_admin(args):
    resp = httpx.post(
        f"{server}/api/auth/login",
        json={"username": args.admin_user, "password": args.admin_pass}
    )
    if resp.status_code == 200:
        token = resp.json()["access_token"]
        resp2 = httpx.post(
            f"{server}/api/admin/create-admin",
            headers={"Authorization": f"Bearer {token}"},
            json={"username": args.new_user, "password": args.new_pass}
        )
        if resp2.status_code != 200:
            print(f"Error: {resp2.json().get('detail', 'Unknown error')}")
            sys.exit(1)
        print(f"Admin '{args.new_user}' created.")
    else:
        print("Creating initial admin (first run)...")
        resp3 = httpx.post(
            f"{server}/api/admin/create-admin",
            json={"username": args.new_user, "password": args.new_pass}
        )
        if resp3.status_code != 200:
            print(f"Error: {resp3.json().get('detail', 'Unknown error')}")
            sys.exit(1)
        print(f"Admin '{args.new_user}' created.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="J3L1XD License Admin Tool")
    parser.add_argument("--server", default=DEFAULT_SERVER, help="License server URL")
    parser.add_argument("--admin-user", default="admin", help="Admin username")
    parser.add_argument("--admin-pass", default="admin", help="Admin password")

    subparsers = parser.add_subparsers(dest="command", help="Command to run")

    gen_parser = subparsers.add_parser("generate", help="Generate a license key")
    gen_parser.add_argument("--key", help="Specific license key (auto-generated if not provided)")
    gen_parser.add_argument("--days", type=int, default=30, help="License duration in days")
    gen_parser.add_argument("--max-hwid", type=int, default=3, help="Max HWID changes allowed")

    list_parser = subparsers.add_parser("list", help="List all licenses")

    revoke_parser = subparsers.add_parser("revoke", help="Revoke a license")
    revoke_parser.add_argument("key", help="License key to revoke")

    ban_parser = subparsers.add_parser("ban", help="Ban HWID for a license")
    ban_parser.add_argument("key", help="License key to ban")

    admin_parser = subparsers.add_parser("create-admin", help="Create admin user")
    admin_parser.add_argument("new_user", help="New admin username")
    admin_parser.add_argument("new_pass", help="New admin password")

    args = parser.parse_args()

    if args.command == "generate":
        generate_license(args)
    elif args.command == "list":
        list_licenses(args)
    elif args.command == "revoke":
        revoke_license(args)
    elif args.command == "ban":
        ban_hwid(args)
    elif args.command == "create-admin":
        create_admin(args)
    else:
        parser.print_help()
