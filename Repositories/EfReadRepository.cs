using ExamPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamPortal.Repositories
{
    public class EfReadRepository<TEntity> : IReadRepository<TEntity> where TEntity : class
    {
        private readonly AppDbContext _db;

        public EfReadRepository(AppDbContext db)
        {
            _db = db;
        }

        public IQueryable<TEntity> Query() => _db.Set<TEntity>().AsNoTracking();

        public IQueryable<TEntity> TrackableQuery() => _db.Set<TEntity>();
    }
}
