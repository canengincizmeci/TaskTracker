# Backend tests

Build and run tests from the repository root:

```powershell
dotnet build TaskTracker.slnx
dotnet test TaskTracker.slnx --no-build
```

Automated tests use isolated, in-memory SQLite databases and require no database
server. PostgreSQL is still required for migration and provider verification;
use the same major version as your deployment.

## Migration verification

Start a separate, disposable PostgreSQL instance and use an empty test database.
The example below assumes that instance is listening on loopback port 55483 with
the test role `tasktracker_test`. Verify the host, port, database and credentials
before running it. The design-time factory does not load application settings.

**Never point test migration commands at a production database.**

```powershell
dotnet ef database update --project TaskTracker.Core --startup-project TaskTracker.Tests --connection 'Host=127.0.0.1;Port=55483;Database=workflow_clean;Username=tasktracker_test'
```

This applies migrations to the disposable database. Stop the disposable instance
when verification is complete.
