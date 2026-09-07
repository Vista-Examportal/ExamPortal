using ExamPortal.Data;
using ExamPortal.Models;

namespace ExamPortal.Services
{
    public class EligibilityService
    {
        private readonly AppDbContext _db;
        public EligibilityService(AppDbContext db) => _db = db;

        public EligibilityResult Evaluate(User candidate, Exam assessment)
        {
            LoadCandidateRegistration(candidate);

            var registrationValid = candidate.Role == PortalRoles.Candidate && candidate.IsEmailVerified && candidate.IsMobileVerified;
            var profileComplete = IsProfileComplete(candidate);
            var resumePath = _db.CandidateDocuments
                .Where(d => d.UserId == candidate.Id && d.DocumentType == "Resume")
                .Select(d => d.FilePath)
                .FirstOrDefault() ?? "";
            var resumeValid = resumePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
            var undergraduate = candidate.EducationRecords.FirstOrDefault(e => e.Level == EducationLevels.Undergraduate);
            var postgraduate = candidate.EducationRecords.FirstOrDefault(e => e.Level == EducationLevels.Postgraduate);
            var educationValid = string.IsNullOrWhiteSpace(assessment.RequiredEducation)
                || (undergraduate?.DegreeOrCourse ?? "").Contains(assessment.RequiredEducation, StringComparison.OrdinalIgnoreCase)
                || (undergraduate?.StreamBranch ?? "").Contains(assessment.RequiredEducation, StringComparison.OrdinalIgnoreCase)
                || (postgraduate?.DegreeOrCourse ?? "").Contains(assessment.RequiredEducation, StringComparison.OrdinalIgnoreCase)
                || (postgraduate?.StreamBranch ?? "").Contains(assessment.RequiredEducation, StringComparison.OrdinalIgnoreCase);
            // All candidates are freshers — experience check always passes.
            // ExperienceLevel on the Exam is retained for display/reporting only.
            var experienceValid = true;
            var skillsMatched = HasAnyMatch(candidate.ProfessionalProfile?.Skills ?? "", assessment.RequiredSkills);
            var locationEligible = HasAnyMatch($"{candidate.Address?.City},{candidate.JobPreference?.PreferredJobLocation}", assessment.EligibleLocations);

            if (string.IsNullOrWhiteSpace(assessment.RequiredSkills)) skillsMatched = true;
            if (string.IsNullOrWhiteSpace(assessment.EligibleLocations)) locationEligible = true;

            var score = new[] { registrationValid, profileComplete, resumeValid, educationValid, experienceValid, skillsMatched, locationEligible }.Count(v => v) * 100 / 7;
            var status = score >= Math.Max(assessment.CutoffScore, assessment.PassingMarks) ? "Eligible" : "Not Eligible";

            var result = new EligibilityResult
            {
                UserId = candidate.Id,
                ExamId = assessment.Id,
                RegistrationValid = registrationValid,
                EducationValid = educationValid,
                ExperienceValid = experienceValid,
                SkillsMatched = skillsMatched,
                LocationEligible = locationEligible,
                EligibilityScore = score,
                Status = status,
                Notes = $"ProfileComplete={profileComplete}; ResumeValid={resumeValid}"
            };

            _db.EligibilityResults.Add(result);
            candidate.CandidateProfile ??= new CandidateProfile { UserId = candidate.Id };
            candidate.CandidateProfile.IsProfileComplete = profileComplete;
            candidate.CandidateProfile.IsResumeValidated = resumeValid;
            candidate.CandidateProfile.VerificationStatus = registrationValid ? "Verified" : "Pending Verification";
            candidate.CandidateProfile.UpdatedAt = DateTime.UtcNow;
            if (profileComplete && candidate.CandidateProfile.CompletedAt == null) candidate.CandidateProfile.CompletedAt = DateTime.UtcNow;
            return result;
        }

        public static bool IsProfileComplete(User candidate)
        {
            var undergraduate = candidate.EducationRecords.FirstOrDefault(e => e.Level == EducationLevels.Undergraduate);
            var intermediate = candidate.EducationRecords.FirstOrDefault(e => e.Level == EducationLevels.Intermediate);
            var secondary = candidate.EducationRecords.FirstOrDefault(e => e.Level == EducationLevels.Secondary);

            var required = new[]
            {
                candidate.FullName, candidate.Email, candidate.MobileNumber,
                candidate.PersonalDetail?.Gender, candidate.Address?.AddressLine, candidate.Address?.State,
                candidate.Address?.City, candidate.Address?.Country, candidate.Address?.Pincode,
                undergraduate?.DegreeOrCourse, undergraduate?.InstituteName, undergraduate?.StreamBranch,
                intermediate?.InstituteName, secondary?.InstituteName,
                candidate.ProfessionalProfile?.Skills, candidate.JobPreference?.PreferredJobLocation
            };

            return required.All(v => !string.IsNullOrWhiteSpace(v))
                && candidate.PersonalDetail?.DateOfBirth.HasValue == true
                && candidate.ProfileCompletion?.CompletionPercentage >= 90;
        }

        private void LoadCandidateRegistration(User candidate)
        {
            _db.Entry(candidate).Reference(u => u.PersonalDetail).Load();
            _db.Entry(candidate).Reference(u => u.Address).Load();
            _db.Entry(candidate).Collection(u => u.EducationRecords).Load();
            _db.Entry(candidate).Reference(u => u.ProfessionalProfile).Load();
            _db.Entry(candidate).Reference(u => u.JobPreference).Load();
            _db.Entry(candidate).Reference(u => u.ProfileCompletion).Load();
        }

        private static bool HasAnyMatch(string candidateValues, string requiredValues)
        {
            if (string.IsNullOrWhiteSpace(requiredValues)) return true;
            var candidateSet = candidateValues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var requiredSet = requiredValues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return requiredSet.Any(required => candidateSet.Any(candidate => candidate.Contains(required, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
