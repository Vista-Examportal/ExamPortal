using ExamPortal.Data;

namespace ExamPortal.Services
{
    public class CandidateIdService
    {
        private static readonly object CandidateIdLock = new();
        private readonly AppDbContext _db;

        public CandidateIdService(AppDbContext db)
        {
            _db = db;
        }

        public string GenerateNextCandidateId()
        {
            lock (CandidateIdLock)
            {
                var year = DateTime.UtcNow.Year;
                var prefix = $"VWT{year}";
                var existingIds = _db.Users
                    .Where(u => u.CandidateId.StartsWith(prefix))
                    .Select(u => u.CandidateId)
                    .ToList();

                var next = 1;
                foreach (var candidateId in existingIds)
                {
                    var suffix = candidateId.Length > prefix.Length ? candidateId[prefix.Length..] : "";
                    if (int.TryParse(suffix, out var number) && number >= next)
                        next = number + 1;
                }

                string generated;
                do
                {
                    generated = $"{prefix}{next:D5}";
                    next++;
                }
                while (_db.Users.Any(u => u.CandidateId == generated));

                return generated;
            }
        }
    }
}
