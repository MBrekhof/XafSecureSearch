# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DevExpress XAF (eXpressApp Framework) Blazor Server application with EF Core, targeting .NET 8. Uses DevExpress v25.2.5. The database is SQL Server (LocalDB by default). The project name suggests it will implement secure search functionality within XAF.

## Solution Structure

```
XafSecureSearch.slnx                          # XML-based solution file (slnx format)
XafSecureSearch/
  XafSecureSearch.Module/                     # Platform-agnostic module (business objects, logic, updaters)
  XafSecureSearch.Blazor.Server/              # Blazor Server host (startup, API controllers, UI)
```

- **Module project** contains business objects, DbContext, security config, and database updaters — anything shared across platforms.
- **Blazor.Server project** is the runnable host with Startup.cs configuration, JWT auth, Web API/OData endpoints, Swagger, and Blazor UI.

## Build and Run

```bash
# Build
dotnet build XafSecureSearch/XafSecureSearch.Blazor.Server/XafSecureSearch.Blazor.Server.csproj

# Run (requires valid SQL Server connection string in appsettings.json)
dotnet run --project XafSecureSearch/XafSecureSearch.Blazor.Server/XafSecureSearch.Blazor.Server.csproj

# Update database schema via CLI
dotnet run --project XafSecureSearch/XafSecureSearch.Blazor.Server/XafSecureSearch.Blazor.Server.csproj -- --updateDatabase --silent
```

Build configurations: `Debug`, `Release`, `EasyTest`. The `EASYTEST` conditional compilation symbol switches connection strings. The `RELEASE` build skips seed data creation in `Updater.cs`.

## Architecture Details

### Database / EF Core
- DbContext: `XafSecureSearchEFCoreDbContext` in Module project
- Uses `SecuredEFCore` object space provider (XAF security integrated with EF Core)
- Connection string key: `ConnectionString` in appsettings.json (defaults to LocalDB)
- `CheckCompatibilityType.DatabaseSchema` — XAF auto-migrates in Debug when debugger is attached; throws in production
- EF Core conventions: deferred deletion, optimistic locking, `SetNull`/`Cascade` delete behavior, `ChangingAndChangedNotificationsWithOriginalValues` change tracking

### Security
- XAF integrated security with `PermissionPolicyRole` and custom `ApplicationUser` (extends `PermissionPolicyUser`)
- `PermissionsReloadMode.NoCache` — permissions re-read from DB on every new DbContext access
- Cookie auth for Blazor UI, JWT Bearer auth for Web API
- JWT config in `appsettings.json` under `Authentication:Jwt`
- Auth endpoint: `POST /api/Authentication/Authenticate` (returns JWT token)

### Web API
- OData v4.01 at `/api/odata` (max 100 query results)
- Reports API at `/api/Report/DownloadByKey({key})` and `/api/Report/DownloadByName({displayName})`
- Swagger UI available in Development mode at `/swagger`
- Business objects must be explicitly exposed via `webApiBuilder.ConfigureOptions` in Startup.cs

### XAF Modules Registered
Cloning, ConditionalAppearance, Dashboards, FileAttachments, Notifications, Office, ReportsV2 (XML store mode), Scheduler, Validation, ViewVariants.

### Seed Data
`Updater.cs` creates "Administrators" and "Default" roles plus "Admin" and "User" accounts (empty passwords) in non-Release builds.

### Roslyn Dependencies
The Module project references `Microsoft.CodeAnalysis.CSharp.Workspaces` and `Microsoft.CodeAnalysis.Workspaces.MSBuild` — likely intended for code analysis or generation features.
