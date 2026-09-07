using System;
using System.Linq;
using ExamPortal.Data;
using ExamPortal.Models;
using ExamPortal.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExamPortal.Tests.Services
{
    public class CandidateIdServiceTests
    {
        // A fresh, uniquely-named in-memory database per test — using Guid as the
        // database name is the standard way to keep EF Core InMemory tests isolated
        // from each other, since the "database" otherwise persists for the process
        // lifetime and would leak state between tests that ran in parallel.
        private static AppDbContext BuildContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private static User MakeUser(string candidateId) => new User
        {
            CandidateId = candidateId,
            Username = candidateId,
            Email = $"{candidateId}@example.com",
            PasswordHash = "not-a-real-hash",
        };

        [Fact]
        public void GenerateNextCandidateId_EmptyDatabase_ReturnsFirstIdForCurrentYear()
        {
            using var db = BuildContext();
            var service = new CandidateIdService(db);

            var id = service.GenerateNextCandidateId();

            Assert.Equal($"VWT{DateTime.UtcNow.Year}00001", id);
        }

        [Fact]
        public void GenerateNextCandidateId_ExistingSequentialIds_ContinuesFromHighest()
        {
            using var db = BuildContext();
            var year = DateTime.UtcNow.Year;
            db.Users.Add(MakeUser($"VWT{year}00001"));
            db.Users.Add(MakeUser($"VWT{year}00002"));
            db.SaveChanges();

            var service = new CandidateIdService(db);
            var id = service.GenerateNextCandidateId();

            Assert.Equal($"VWT{year}00003", id);
        }

        [Fact]
        public void GenerateNextCandidateId_GapInExistingIds_DoesNotBackfillGap()
        {
            // The algorithm tracks the highest existing suffix and continues past it —
            // it deliberately does not go back and fill a gap left by, say, a deleted
            // candidate record. This test pins that actual (documented) behavior so a
            // future change to "fill gaps instead" is a deliberate choice, not an
            // accidental regression.
            using var db = BuildContext();
            var year = DateTime.UtcNow.Year;
            db.Users.Add(MakeUser($"VWT{year}00001"));
            db.Users.Add(MakeUser($"VWT{year}00003"));
            db.SaveChanges();

            var service = new CandidateIdService(db);
            var id = service.GenerateNextCandidateId();

            Assert.Equal($"VWT{year}00004", id);
        }

        [Fact]
        public void GenerateNextCandidateId_IgnoresIdsFromOtherYears()
        {
            using var db = BuildContext();
            var currentYear = DateTime.UtcNow.Year;
            var otherYear = currentYear - 1;
            // A candidate id from a previous year's prefix should never influence this
            // year's numbering — each year restarts from 00001.
            db.Users.Add(MakeUser($"VWT{otherYear}00042"));
            db.SaveChanges();

            var service = new CandidateIdService(db);
            var id = service.GenerateNextCandidateId();

            Assert.Equal($"VWT{currentYear}00001", id);
        }

        [Fact]
        public void GenerateNextCandidateId_ReturnedIdHasCorrectPrefixAndLength()
        {
            using var db = BuildContext();
            var service = new CandidateIdService(db);

            var id = service.GenerateNextCandidateId();

            Assert.StartsWith($"VWT{DateTime.UtcNow.Year}", id);
            // "VWT" + 4-digit year + 5-digit zero-padded sequence, e.g. VWT202600001.
            Assert.Equal(12, id.Length);
        }
    }
}
