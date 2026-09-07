# ArcaFS

Arca File System — a self-hosted file browser for .NET 10 / Blazor Server, inspired by [File Browser Quantum](https://github.com/gtsteffaniak/filebrowser).

Permissions are **per user, per path, on each source**. That means you can give each person a writable home folder and keep a shared folder read-only for everyone except you — something a source-wide read-only switch cannot express.

## Features

- Multi-source file browsing (tiles, list, compact, gallery)
- Upload, download, copy, move, rename, delete, compress, extract
- Indexed search, thumbnails, text editor, media preview
- Public share links (password, expiry, download limits)
- Path-based ACL with templates for common family/team layouts
- Theme picker (26 Bootswatch themes)
- Hangfire job dashboard and Sejil log dashboard
- ASP.NET Identity (default), Keycloak, or no-auth desktop mode
- SQL Server, PostgreSQL, MySQL, or SQLite

## Run locally

```powershell
dotnet restore
dotnet run --project ArcaFS
```

Open http://localhost:5000 (or the URL printed by `dotnet run`). With the default SQLite + Identity settings, the first-run wizard creates the administrator and the first source.

## Configuration

Set these in `ArcaFS/appsettings.json` or as environment variables (`Database__Provider`, …):

| Key | Values |
|-----|--------|
| `Database:Provider` | `Sqlite` (default), `SqlServer`, `Npgsql`, `MySql` |
| `ConnectionStrings:DefaultConnection` | Provider-specific connection string |
| `Authentication:Provider` | `Identity` (default), `Keycloak`, `None` |
| `Seed:Admin:Email` / `Seed:Admin:Password` | Optional seed administrator (Identity) |

## Docker

```powershell
docker compose up -d
```

The default stack is PostgreSQL + ASP.NET Identity at http://localhost:8080. Copy `.env.example` to `.env` to change passwords. Mount extra folders and add them as sources under **Administration → Sources**.

To use Keycloak, set `Authentication__Provider=Keycloak`, fill in `Authentication__Keycloak__*`, and uncomment the `keycloak` service in `docker-compose.yml`.

## Desktop (Electron)

The desktop build forces **SQLite + no authentication** and opens the signed-in user's profile folder as the first source. Data lives under `%LocalApplicationData%\ArcaFS`.

```powershell
dotnet run --project ArcaFS.Desktop
```

Packaging an installer uses [ElectronNET](https://github.com/ElectronNET/Electron.NET) (`electronize`).

## Permissions

Open **Administration → Access rules**. Templates:

- **Per-user home directories** — each signed-in user owns `/${username}`
- **Shared, read-only except for me** — everyone can browse and download; one nominated user can write
- **Drop box** — upload-only submissions folder
- **Group workspace** — one group collaborates in a folder
- **Administrators only** — deny by default

Rules can also be opened from a folder via **Access rules** in the context menu.

## EF migrations

Generate provider-specific migrations after model changes (set nothing in appsettings — each data project has a design-time factory):

```powershell
dotnet ef migrations add InitialCreate -p ArcaFS.Data.Sqlite -s ArcaFS -c ArcaFS.Data.Sqlite.ApplicationDbContext --output-dir Migrations
dotnet ef migrations add InitialCreate -p ArcaFS.Data.Sql    -s ArcaFS -c ArcaFS.Data.Sql.ApplicationDbContext    --output-dir Migrations
dotnet ef migrations add InitialCreate -p ArcaFS.Data.Npgsql -s ArcaFS -c ArcaFS.Data.Npgsql.ApplicationDbContext --output-dir Migrations
dotnet ef migrations add InitialCreate -p ArcaFS.Data.MySql  -s ArcaFS -c ArcaFS.Data.MySql.ApplicationDbContext  --output-dir Migrations
```
