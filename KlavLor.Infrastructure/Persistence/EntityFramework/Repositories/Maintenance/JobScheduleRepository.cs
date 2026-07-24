using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Maintenance;

internal sealed class JobScheduleRepository(DataContext dataContext) : IJobScheduleRepository
{
    public async Task<bool> TryClaim(string jobName, TimeSpan interval)
    {
        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        // One atomic statement decides and claims. On first sight of a job the INSERT wins (it
        // runs immediately after startup). Thereafter the conflict UPDATE only fires — stamping
        // LastRunAt and clearing the flag — when a manual run is pending or the interval has
        // elapsed; otherwise nothing is written and RETURNING yields no row, so the claim fails.
        // Being a single statement, two overlapping polls can't both claim.
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO "JobSchedules" ("JobName", "LastRunAt", "ManualRequestedAt")
            VALUES (@job, now(), NULL)
            ON CONFLICT ("JobName") DO UPDATE
            SET "LastRunAt" = now(), "ManualRequestedAt" = NULL
            WHERE "JobSchedules"."ManualRequestedAt" IS NOT NULL
               OR "JobSchedules"."LastRunAt" <= now() - @interval
            RETURNING "JobName";
            """;
        cmd.Parameters.Add(new NpgsqlParameter("job", jobName));
        cmd.Parameters.Add(new NpgsqlParameter("interval", NpgsqlDbType.Interval) { Value = interval });

        var claimed = await cmd.ExecuteScalarAsync();
        return claimed is not null;
    }

    public async Task RequestManual(string jobName)
    {
        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO "JobSchedules" ("JobName", "LastRunAt", "ManualRequestedAt")
            VALUES (@job, now(), now())
            ON CONFLICT ("JobName") DO UPDATE SET "ManualRequestedAt" = now();
            """;
        cmd.Parameters.Add(new NpgsqlParameter("job", jobName));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyCollection<string>> GetPendingJobNames()
    {
        return await dataContext.JobSchedules
            .AsNoTracking()
            .Where(s => s.ManualRequestedAt != null)
            .Select(s => s.JobName)
            .ToListAsync();
    }
}
