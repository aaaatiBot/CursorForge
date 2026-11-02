# CursorForge

> **Next-generation Windows cursor manager and cloud-connected creator platform.**  
> Built with **.NET 10 LTS**, **WPF-UI**, and **Supabase**.

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)]()
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%20Windows%2011%20(x64)-blue.svg)]()
[![Runtime](https://img.shields.io/badge/.NET-10.0%20LTS-purple.svg)]()
[![UI](https://img.shields.io/badge/UI-WPF--UI%20(Fluent)-0078d4.svg)]()
[![Backend](https://img.shields.io/badge/backend-Supabase%20(PostgreSQL%20%2B%20Deno)-3ecf8e.svg)]()
[![License](https://img.shields.io/badge/license-MIT-green.svg)]()

---

## Overview

Windows system cursors have been trapped in legacy dialogs and low-res `.cur` files since Windows 95. **CursorForge** reinvents cursor customization for modern high-DPI displays (4K, multi-monitor, high-refresh rate), bringing together:

1. **System-Native Engine**: Directly manages Windows cursor schemes via Win32 API (`SystemParametersInfo`) with zero-risk atomic registry snapshots, dynamic default resolution, and crash recovery.
2. **Multi-DPI `.cur` Encoder**: Resamples transparent PNGs into genuine multi-resolution Windows binary `.cur` files with embedded 32px, 48px, 64px, 96px, and 128px DIB icons, 32-bit premultiplied alpha, and DWORD-aligned 1-bit AND masks.
3. **Offline Theme Library**: Bundles 3 out-of-the-box themes (*Neon*, *Slate*, *Cyber*), manages custom libraries in `%APPDATA%\CursorForge`, and supports shareable `.cursorforge` ZIP archives.
4. **Cloud Community**: Discover, preview, like, and download community-created cursor sets powered by Supabase PostgreSQL, Row Level Security, and Deno Edge Functions.
5. **Creator Hub**: In-app hotspot coordinates picker, automated server-side PNG sanitization, and seamless theme publishing.

---

## Architecture Diagram

```
+-------------------------------------------------------------------------------+
|                            CursorForge Desktop App                            |
|                        (.NET 10 LTS · WPF · WPF-UI)                           |
+-------------------------------------------------------------------------------+
| Views & ViewModels (MVVM)                                                     |
|   ├── LibraryView    (Offline themes, apply, backup, restore, ZIP import/export) |
|   ├── BrowseView     (Cloud catalog, keyset pagination, full-text search, live likes) |
|   ├── UploadView     (Creator studio, multi-role hotspot picker, session upload) |
|   ├── MyCursorsView  (Manage published themes, review status, deletion requests)|
|   └── AccountView    (Pseudonymous sign-up, Crockford Base32 recovery modal)  |
+-------------------------------------------------------------------------------+
| Services Layer                                                                |
|   ├── SystemCursorService     ──> Win32 SPI_SETCURSORS, Registry Snapshots     |
|   ├── CursorConverterService  ──> Multi-DPI .cur Binary Encoder (32-128px)    |
|   ├── ThemeManagerService     ──> Local filesystem storage & .cursorforge ZIP |
|   ├── DpapiTokenStorage       ──> CurrentUser DPAPI-encrypted Auth tokens     |
|   └── SupabaseClientService   ──> PostgREST, GoTrue, Realtime, Storage API    |
+-------------------------------------------------------------------------------+
                                     │  HTTPS / WSS
                                     ▼
+-------------------------------------------------------------------------------+
|                             Supabase Cloud Backend                            |
+-------------------------------------------------------------------------------+
| PostgreSQL 15+ Engine                                                         |
|   ├── Profiles, Cursors, Cursor_Downloads, Cursor_Likes, Upload_Sessions     |
|   ├── Row-Level Security (RLS) & Column-Level Grants (Restricted anon/auth)   |
|   ├── Atomic RPCs (like_cursor, unlike_cursor, rate_limit_write, complete_...) |
|   └── Deletion Outbox (Idempotent worker-leased asynchronous cleanup)         |
+-------------------------------------------------------------------------------+
| Deno Edge Functions                                                           |
|   ├── validate-upload         ──> Server-side PNG header, dimension & size check|
|   ├── process-deletion-outbox ──> Batched Storage object deletion with retries|
|   ├── reset-password          ──> Single-use Crockford Base32 recovery flow   |
|   ├── sweep-expired-sessions  ──> Automated abandoned session cleanup        |
|   └── delete-account          ──> Full GDPR-compliant user data & storage purge|
+-------------------------------------------------------------------------------+
```

---

## Core Capabilities

### 1. High-Fidelity Registry Snapshots & Recovery
- Before modifying system cursors, CursorForge takes a byte-perfect snapshot of `HKCU\Control Panel\Cursors` and saves it to `%APPDATA%\CursorForge\original_cursors.json`.
- Accurately captures registry types (`REG_SZ` vs `REG_EXPAND_SZ`) and explicitly flags missing keys.
- **Dynamic Default Resolution**: Unlike naive tools, CursorForge avoids hardcoding static paths like `C:\Windows\Cursors\aero_arrow.cur`; it dynamically queries default system schemes or uses empty string semantics for native Windows fallback.
- **Crash Recovery**: An atomic `pending_apply.json` marker ensures that even if power is cut midway through an apply operation, the client rolls back to safety on next boot.

### 2. Multi-Resolution `.cur` Binary Encoding
Windows cursors require specific binary alignment to prevent rendering glitches on high-DPI displays:
- Generates 5 discrete resolution tiers per cursor: **32×32, 48×48, 64×64, 96×96, and 128×128 pixels**.
- Converts standard PNG RGBA into premultiplied 32-bit BGRA DIB buffers.
- Synthesizes 1-bit monochrome AND masks with exact DWORD (32-bit) scanline padding.
- Automatically adjusts hotspot coordinates proportionally across all resolution tiers.
- Validated at build time via native Win32 `LoadCursorFromFileW` and user32 `GetIconInfo`.

### 3. Complete Role Coverage (15 Roles)
CursorForge natively supports all 15 Windows cursor roles:
| Role Name | Registry Key Name | Default Function |
| :--- | :--- | :--- |
| **Arrow** | `Arrow` | Standard normal select pointer |
| **Help** | `Help` | Context help pointer |
| **AppStarting** | `AppStarting` | Working in background pointer |
| **Wait** | `Wait` | Busy hourglass / spinner pointer |
| **Crosshair** | `Crosshair` | Precision select pointer |
| **IBeam** | `IBeam` | Text selection pointer |
| **NWSE** | `SizeNWSE` | Diagonal resize 1 (`\` direction) |
| **NESW** | `SizeNESW` | Diagonal resize 2 (`/` direction) |
| **WE** | `SizeWE` | Horizontal resize (`↔` direction) |
| **NS** | `SizeNS` | Vertical resize (`↕` direction) |
| **SizeAll** | `SizeAll` | Move cursor (4-way compass) |
| **No** | `No` | Unavailable / forbidden pointer |
| **Hand** | `Hand` | Link select pointer |
| **UpArrow** | `UpArrow` | Alternate select pointer |
| **Pin** | `Pin` | Location select pointer |

---

## Getting Started

### Prerequisites
- **Operating System**: Windows 10 (Build 1809+) or Windows 11 (x64)
- **SDK**: [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- **IDE**: Visual Studio 2026 / Visual Studio Code with C# Dev Kit

### Building from Source

```powershell
# Clone the repository
git clone https://github.com/aaaatiBot/cursorforge.git
cd cursorforge

# Restore and build the solution
dotnet build CursorForge.slnx
```

### Running Tests

The test suite includes 25 unit and integration tests covering multi-DPI binary conversion, registry snapshot serialization, crash recovery markers, and navigation.

> **Note**: To prevent Windows local loopback socket IPC isolation issues between `vstest.console` and `testhost.exe`, run tests with standard execution:

```powershell
dotnet test CursorForge.Tests\CursorForge.Tests.csproj --no-restore
```

### Running the Application

```powershell
dotnet run --project CursorForge.Client\CursorForge.Client.csproj
```

---

## Project Structure

```
cursorforge/
├── .github/
│   └── workflows/
│       ├── ci.yml                 # PR & push validation (.NET 10, tests, warnings-as-errors)
│       └── release.yml            # Velopack packaging & GitHub release automation
├── CursorForge.Client/            # Main desktop client
│   ├── Assets/StarterThemes/      # Bundled themes (Neon, Slate, Cyber)
│   ├── Converters/                # WPF Value Converters
│   ├── Models/                    # Theme manifests, registry snapshots, cursor roles
│   ├── Services/                  # Win32 cursor engine, .cur converter, local storage
│   ├── ViewModels/                # CommunityToolkit.Mvvm ViewModels
│   ├── Views/                     # Modern WPF-UI XAML views
│   ├── Program.cs                 # Velopack bootstrap entry point
│   └── App.xaml                   # Global Fluent resources & DI container
├── CursorForge.Tests/             # Unit & Integration test suite
│   ├── Unit/                      # Converter tests, registry snapshot tests
│   └── Integration/               # ThemeManager, LocalStorage, SystemCursor tests
├── docs/                          # Detailed architecture & engineering docs
│   ├── ARCHITECTURE.md            # In-depth system architecture & Win32 internals
│   ├── SETUP.md                   # Complete local setup & configuration guide
│   ├── WINDOWS_CURSOR_ENGINE.md   # Low-level Win32 API & registry reference
│   ├── SUPABASE_BACKEND.md        # Database schema, RLS matrix & Edge Functions
│   └── CONTRIBUTING.md            # Coding standards & PR guidelines
├── supabase/                      # Cloud backend configuration
│   ├── functions/                 # Deno Edge Functions (validate, reset, delete, outbox)
│   ├── migrations/                # PostgreSQL schema, triggers, and RLS policies
│   └── tests/                     # Adversarial RLS & security test suites
├── CursorForge.slnx               # .NET 10 solution file
└── README.md                      # This file
```

---

## Security Model

- **Local Secrets**: User authentication tokens are encrypted using Windows DPAPI (`DataProtectionScope.CurrentUser`). Never stored in plain text.
- **Privacy by Design**: Accounts use pseudonymous internal usernames with `<username>@cursorforge.invalid` addresses. No spam, no marketing emails.
- **Single-Use Recovery**: Account recovery utilizes 20-character Crockford Base32 keys hashed with salted SHA-256 in PostgreSQL. Keys are atomic and permanently invalidated upon first use.
- **Zero Raw PII in Logs**: `StructuredLogger` automatically strips passwords, authorization tokens, email patterns, and recovery keys before writing to `%APPDATA%\CursorForge\logs\app.log`.
- **Server-Side Validation**: Storage uploads are gated behind server-side validation (`validate-upload` Edge Function) verifying PNG magic bytes (`0x89504E47`), max dimensions ($\le 128\times 128$), and byte limits ($\le 100$ KB) before publishing.

---

## Roadmap & Implementation Status

| Phase | Milestone | Status |
| :---: | :--- | :---: |
| **Phase 1** | Foundation (.NET 10 LTS, WPF-UI navigation shell, Velopack bootstrap, StructuredLogger) | ✅ Complete |
| **Phase 2** | Cursor Engine (Multi-DPI `.cur` encoder, Win32 registry manager, atomic recovery) | ✅ Complete |
| **Phase 3** | Offline Library (Theme manager, starter themes, `.cursorforge` ZIP import/export) | ✅ Complete |
| **Phase 4** | Supabase Backend (PostgreSQL schema, RLS policies, RPCs, Deno Edge Functions) | ✅ Complete |
| **Phase 5** | Authentication (DPAPI secure storage, Account UI, single-use recovery code modal) | 🔄 Next |
| **Phase 6** | Online Browse (Public cursor gallery, keyset pagination, live likes & downloads) | 📅 Scheduled |
| **Phase 7** | Creator Studio (In-app hotspot picker, upload session workflow, theme publishing) | 📅 Scheduled |
| **Phase 8** | Production Hardening (CI/CD GitHub Actions, Velopack packaging, release testing) | 📅 Scheduled |

---

## License

This project is licensed under the [MIT License](LICENSE).
