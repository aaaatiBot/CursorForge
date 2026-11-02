# CursorForge — Development Setup Guide

This guide walks you through setting up a complete development environment for building, running, and testing the **CursorForge** client and backend.

---

## 1. Prerequisites

### Windows Environment
- **Operating System**: Windows 10 (Version 1809+, 64-bit) or Windows 11 (x64).
- **.NET SDK**: [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`net10.0-windows`).
  Verify installation:
  ```powershell
  dotnet --version
  # Should output 10.0.xxx
  ```
- **Git**: Git for Windows with long paths enabled:
  ```powershell
  git config --global core.longpaths true
  ```
- **IDE**:
  - **Visual Studio 2026** (or latest preview) with the *.NET Desktop Development* workload.
  - OR **VS Code** with *C# Dev Kit* and *C#* extensions.

### Supabase & Cloud Tools (Optional for local cloud emulation)
- **Node.js**: v18+ (LTS)
- **Deno**: v1.38+ (for local Edge Function testing)
- **Supabase CLI**:
  ```powershell
  scoop bucket add supabase https://github.com/supabase/scoop-bucket.git
  scoop install supabase
  ```

---

## 2. Solution Setup & Build

### 2.1 Clone the Repository
```powershell
git clone https://github.com/aaaatiBot/cursorforge.git
cd cursorforge
```

### 2.2 Configure Local App Settings
Copy the template configuration file into your local app directory:
```powershell
# Create local data directory if it doesn't exist
$configDir = "$env:APPDATA\CursorForge"
if (!(Test-Path $configDir)) { New-Item -ItemType Directory -Path $configDir -Force }

# Copy example config
Copy-Item "CursorForge.Client\config.example.json" "$configDir\config.json"
```

Edit `%APPDATA%\CursorForge\config.json` with your Supabase credentials:
```json
{
  "SupabaseUrl": "https://your-project-id.supabase.co",
  "SupabaseAnonKey": "your-anon-key-here",
  "Environment": "Development",
  "EnableRealtime": true
}
```

### 2.3 Build the Solution
```powershell
# Restore dependencies and build under Warnings-As-Errors enforcement
dotnet build CursorForge.slnx -warnaserror
```

---

## 3. Running Unit and Integration Tests

The test project `CursorForge.Tests` covers multi-resolution binary encoding, Win32 registry snapshots, crash recovery markers, and MVVM navigation.

To execute tests:
```powershell
dotnet test CursorForge.Tests\CursorForge.Tests.csproj --no-restore
```

> ⚠️ **Important Testing Note on Windows Sandboxes & Sockets**:  
> In restricted execution environments or sandboxes, `vstest.console` and `testhost.exe` communicate over local loopback sockets. If socket creation is constrained by environment policy, run tests directly with unrestricted socket access.

---

## 4. Running the Desktop Application

Launch the WPF client directly:
```powershell
dotnet run --project CursorForge.Client\CursorForge.Client.csproj
```

Upon first launch:
1. CursorForge automatically initializes `%APPDATA%\CursorForge`.
2. Starter themes (`Neon`, `Slate`, `Cyber`) are extracted from compiled assets into `%APPDATA%\CursorForge\themes`.
3. An atomic registry snapshot is written to `%APPDATA%\CursorForge\original_cursors.json`.
4. High-performance JSON-lines logs are recorded at `%APPDATA%\CursorForge\logs\app.log`.

---

## 5. Supabase Backend Setup (Local Development)

If you are developing or modifying backend migrations and Edge Functions:

### 5.1 Initialize and Start Supabase
```powershell
supabase init
supabase start
```

### 5.2 Apply Database Migrations
```powershell
supabase db reset
```
This executes `supabase/migrations/20261102000000_init.sql`, provisioning all tables, RLS policies, PostgreSQL triggers, and stored procedures.

### 5.3 Run Edge Functions Locally
```powershell
supabase functions serve --no-verify-jwt
```

### 5.4 Run SQL Test Suites
```powershell
# Run adversarial RLS tests
supabase test db --debug
```
Or execute `supabase/tests/rls_adversarial.sql` directly inside the Supabase SQL editor.
