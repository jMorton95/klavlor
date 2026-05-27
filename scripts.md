# Handy Scripts

EF Core migrations (run from repo root):

```bash
# Add a new migration
dotnet ef migrations add <MigrationName> --project KlavLor.Infrastructure --startup-project KlavLor.Web

# Remove the last migration (if not yet applied)
dotnet ef migrations remove --project KlavLor.Infrastructure --startup-project KlavLor.Web
```

Migrations apply automatically on app startup; there's no separate `database update` step for local dev.
