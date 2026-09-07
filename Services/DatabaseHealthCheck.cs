using ExamPortal.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ExamPortal.Services
{
    public class DatabaseHealthCheck : IHealthCheck
    {
        private readonly AppDbContext _db;

        public DatabaseHealthCheck(AppDbContext db)
        {
            _db = db;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database connection is healthy.")
                : HealthCheckResult.Unhealthy("Database connection failed.");
        }
    }
}
