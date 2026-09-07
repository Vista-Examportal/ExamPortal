using System.Security.Claims;

namespace ExamPortal.Services
{
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// Parses the signed-in user's id from the NameIdentifier claim. Only valid to call
        /// from an [Authorize]-protected action, where the claim is guaranteed to be present.
        /// </summary>
        public static int GetUserId(this ClaimsPrincipal user) =>
            int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
