using ExamPortal.Services;
using Xunit;

namespace ExamPortal.Tests.Services
{
    public class TextUtilsTests
    {
        [Fact]
        public void Clamp_NullValue_ReturnsEmptyString()
        {
            Assert.Equal("", TextUtils.Clamp(null, 10));
        }

        [Fact]
        public void Clamp_ShorterThanMax_ReturnsUnchanged()
        {
            Assert.Equal("hello", TextUtils.Clamp("hello", 10));
        }

        [Fact]
        public void Clamp_ExactlyAtMax_ReturnsUnchanged()
        {
            Assert.Equal("hello", TextUtils.Clamp("hello", 5));
        }

        [Fact]
        public void Clamp_LongerThanMax_Truncates()
        {
            Assert.Equal("hello", TextUtils.Clamp("hello world", 5));
        }

        [Fact]
        public void Clamp_ZeroMaxLength_ReturnsEmptyString()
        {
            Assert.Equal("", TextUtils.Clamp("hello", 0));
        }

        [Fact]
        public void Clamp_TrimFalse_DoesNotTrimWhitespace()
        {
            // Default (trim: false) — exam-answer free text/code deliberately keeps
            // leading/trailing whitespace, since it can be meaningful in submitted code.
            Assert.Equal("  hi  ", TextUtils.Clamp("  hi  ", 10, trim: false));
        }

        [Fact]
        public void Clamp_TrimTrue_TrimsBeforeClamping()
        {
            Assert.Equal("hi", TextUtils.Clamp("  hi  ", 10, trim: true));
        }

        [Fact]
        public void Clamp_TrimTrue_TrimsThenTruncates()
        {
            // "  hello world  " trimmed -> "hello world" (11 chars) -> clamped to 5 -> "hello"
            Assert.Equal("hello", TextUtils.Clamp("  hello world  ", 5, trim: true));
        }
    }
}
