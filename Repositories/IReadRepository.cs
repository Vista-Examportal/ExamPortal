namespace ExamPortal.Repositories
{
    public interface IReadRepository<TEntity> where TEntity : class
    {
        IQueryable<TEntity> Query();
        IQueryable<TEntity> TrackableQuery();
    }
}
