import json
import os
import shutil
import subprocess

def find_doppler():
    return (
        shutil.which("doppler") or
        shutil.which("doppler.exe") or
        r"C:\Users\USER\AppData\Roaming\Python\Python313\Scripts\doppler.exe"
    )

def flatten_dict(d, parent_key=''):
    items = []
    for k, v in d.items():
        new_key = f"{parent_key}__{k}" if parent_key else k
        if isinstance(v, dict):
            items.extend(flatten_dict(v, new_key).items())
        elif v is not None and v != "":
            items.append((new_key.upper(), str(v)))
    return dict(items)

def sync_appsettings(file_path):
    if not os.path.exists(file_path):
        return
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        flat = flatten_dict(data)
        args = []
        for key, val in flat.items():
            clean_key = ''.join(c if (c.isalnum() or c == '_') else '_' for c in key)
            args.append(f"{clean_key}={val}")
        
        if args:
            doppler_bin = find_doppler()
            cmd = [doppler_bin, "secrets", "set"] + args
            res = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='ignore')
            if res.returncode == 0:
                print(f"[Doppler Auto-Sync] Automatically uploaded secrets from {file_path} -> Doppler Cloud")
            else:
                print(f"[Doppler Auto-Sync Warning] Sync skipped for {file_path}: {res.stderr.strip()}")
    except Exception as e:
        print(f"[Doppler Auto-Sync Error] {e}")

if __name__ == "__main__":
    appsettings_files = [
        "src/TicketShield.Core/TicketShield.API/appsettings.json",
        "src/MockOrganizer/MockOrganizer.API/appsettings.json"
    ]
    for path in appsettings_files:
        sync_appsettings(path)
