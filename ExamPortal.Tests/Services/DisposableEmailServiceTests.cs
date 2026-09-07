using System.Collections.Generic;
using ExamPortal.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ExamPortal.Tests.Services
{
    public class DisposableEmailServiceTests
    {
        private static DisposableEmailService BuildService(Dictionary<string, string?>? config = null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(config ?? new Dictionary<string, string?>())
                .Build();
            return new DisposableEmailService(configuration);
        }

        [Theory]
        [InlineData("someone@mailinator.com")]
        [InlineData("someone@tempmail.com")]
        [InlineData("someone@10minutemail.com")]
        [InlineData("someone@yopmail.com")]
        [InlineData("someone@guerrillamail.com")]
        public void IsDisposable_DetectsKnownProviders(string email)
        {
            var service = BuildService();
            Assert.True(service.IsDisposable(email));
        }

        [Theory]
        [InlineData("someone@MAILINATOR.COM")] // case-insensitive
        [InlineData("Someone@Mailinator.Com")]
        public void IsDisposable_IsCaseInsensitive(string email)
        {
            var service = BuildService();
            Assert.True(service.IsDisposable(email));
        }

        [Fact]
        public void IsDisposable_MatchesSubdomainsOfBlockedDomain()
        {
            var service = BuildService();
            Assert.True(service.IsDisposable("someone@sub.mailinator.com"));
        }

        [Theory]
        [InlineData("someone@gmail.com")]
        [InlineData("someone@outlook.com")]
        [InlineData("someone@company.co.in")]
        [InlineData("someone@vistawaystech.com")]
        public void IsDisposable_AllowsRealProviders(string email)
        {
            var service = BuildService();
            Assert.False(service.IsDisposable(email));
        }

        [Fact]
        public void IsDisposable_HandlesMalformedInputSafely()
        {
            var service = BuildService();
            Assert.False(service.IsDisposable(""));
            Assert.False(service.IsDisposable("not-an-email"));
            Assert.False(service.IsDisposable("trailing-at@"));
        }

        [Fact]
        public void IsDisposable_ConfigurationExtendsTheBuiltInList()
        {
            var service = BuildService(new Dictionary<string, string?>
            {
                ["DisposableEmail:BlockedDomains:0"] = "customdisposable.example",
            });

            Assert.True(service.IsDisposable("someone@customdisposable.example"));
            // Built-in defaults still work — configuration extends, doesn't replace.
            Assert.True(service.IsDisposable("someone@mailinator.com"));
        }
    }
}
