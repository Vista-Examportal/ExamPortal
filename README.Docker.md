# Running ExamPortal in Docker

Docker is **optional** — for local development in Visual Studio, just press
F5; it uses LocalDB automatically and needs no containers at all. Use this
setup if you want a containerized run (e.g. to test against a real SQL
Server instead of LocalDB, or to deploy).

## Quick start

```bash
cp .env.example .env
# edit .env — set MSSQL_SA_PASSWORD at minimum

docker compose up --build
```

- App: http://localhost:8080
- SQL Server: localhost:1433 (sa / the password you set)

Database schema is created automatically on startup (see
`DatabaseInitializer.EnsureSchema` in `Program.cs`) — there's no separate
migration step to run.

## What's running

- **app** — the ASP.NET Core site, built from the `Dockerfile` in this
  folder. It does *not* run `npm run build` — the compiled CSS
  (`wwwroot/css/site.min.css`, `tailwind-built.css`) is already committed,
  so the image build only needs the .NET SDK. If you edit
  `tailwind-input.css` or `site.css`, run `npm run build:tailwind` /
  `npm run build:css` locally first and commit the output before rebuilding
  the image.
- **db** — `mcr.microsoft.com/mssql/server:2022-latest`. Data persists in
  the `mssql_data` Docker volume across restarts.

Candidate uploads (resumes, documents, offer letters) are written to
`App_Data/uploads` inside the container and persisted via the `app_data`
volume, so they survive rebuilds too.

## Optional sign-in / email providers

Google sign-in, LinkedIn sign-in, and outbound email are all inactive by
default (same as running locally) until you set their credentials —
either in `.env` (see `.env.example`) or as real environment variables in
your deployment target. The app runs and every non-social-login feature
works fine without them.

## Stopping / resetting

```bash
docker compose down          # stop containers, keep data
docker compose down -v       # stop containers AND delete the DB/uploads volumes
```
