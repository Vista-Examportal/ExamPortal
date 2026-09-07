using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ExamPortal.Data
{
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=EXAMDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true",
                sql => sql.EnableRetryOnFailure());
            return new AppDbContext(optionsBuilder.Options);
        }
    }
}
