# Migrations — READ THIS BEFORE RUNNING THE APP

## ⚠️ DO NOT run `Add-Migration` or `Update-Database`

This project uses **`EnsureCreated`** at startup instead of EF migration files.
Running `Add-Migration` + `Update-Database` against an existing database causes:

> `SqlException: There is already an object named 'AuditLogs' in the database.`

because EF sees no `__EFMigrationsHistory` entries, assumes the DB is blank,
and tries to `CREATE TABLE` on tables that already exist.

---

## How to run from scratch (clean start)

### Option A — Drop the old database first (recommended)

Open SSMS or run in sqlcmd / Query window:

```sql
USE master;
ALTER DATABASE EXAMDB SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE EXAMDB;
```

Then just run the app:

```powershell
dotnet run
```

`Program.cs` calls `EnsureCreated()` which creates the full schema and seeds
admin + student + 3 sample exams automatically. No migration commands needed.

### Option B — Keep your existing data

If your DB already has the correct tables, just run the app:

```powershell
dotnet run
```

`EnsureCreated()` detects that all tables are present and does nothing.
The two startup `ALTER TABLE` blocks add `CandidateId` and `RecruitmentStage`
to `Users` only if those columns are missing — safe to run every time.

---

## Login credentials (after fresh seed)

Demo accounts (`admin`, `VWT202600001`, etc.) are seeded using the password configured via
.NET User Secrets or the `DemoAccounts__Password` environment variable — see the main
`README.md` → "Login Credentials (Development)" for setup. No password is hardcoded here
or anywhere else in the repository.

---

## Adding new columns in the future

1. Add the property in `Models/Models.cs`.
2. Add a guarded `ALTER TABLE` block in `Program.cs` (copy the pattern already
   there for `CandidateId` / `RecruitmentStage`).
3. Run the app — the column is added automatically on next startup.

No migration files needed.
