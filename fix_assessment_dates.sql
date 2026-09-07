-- One-time fix: the 3 demo assessments were seeded once (see DbSeeder.SeedExams in
-- Data/AppDbContext.cs) with date windows relative to whenever the database was first
-- created — that seeding only ever runs once, so those windows don't refresh on later
-- app restarts. By 22 Aug 2026 they'd all expired (windows ended in early July).
--
-- This just extends each one 30 days from whenever you actually run this script,
-- using GETUTCDATE() rather than a hardcoded date so it isn't stale the moment you
-- read it. Safe to re-run.
--
-- Run this against the EXAMDB database — in Visual Studio: View > SQL Server Object
-- Explorer > (localdb)\MSSQLLocalDB > Databases > EXAMDB > right-click > New Query,
-- or via sqlcmd / SSMS / Azure Data Studio pointed at your connection string.

UPDATE Exams
SET StartDateTime = DATEADD(DAY, -1, GETUTCDATE()),
    EndDateTime   = DATEADD(DAY, 30, GETUTCDATE())
WHERE Title = 'Software Engineer Eligibility Assessment';

UPDATE Exams
SET StartDateTime = GETUTCDATE(),
    EndDateTime   = DATEADD(DAY, 30, GETUTCDATE())
WHERE Title = 'Frontend Developer Eligibility Assessment';

UPDATE Exams
SET StartDateTime = DATEADD(DAY, -2, GETUTCDATE()),
    EndDateTime   = DATEADD(DAY, 30, GETUTCDATE())
WHERE Title = 'Database Engineer Eligibility Assessment';

-- Verify:
SELECT Title, Status, IsActive, StartDateTime, EndDateTime FROM Exams;
