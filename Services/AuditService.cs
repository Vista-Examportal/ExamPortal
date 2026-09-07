using ExamPortal.Data;
using ExamPortal.Models;

namespace ExamPortal.Services
{
    public class AuditService
    {
        private readonly AppDbContext _db;
        public AuditService(AppDbContext db) => _db = db;

        public void Record(string actor, string action, string entityName, int? entityId, string details = "")
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Actor = actor,
                Action = action,
                EntityName = entityName,
                EntityId = entityId,
                Details = details
            });
        }
    }
}
