using System.Linq;
using CursorForge.Client.Services.Security;
using Xunit;

namespace CursorForge.Tests.Unit;

public class PasswordGeneratorTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void Generate_ProducesRequestedLength(int length)
    {
        string pwd = PasswordGenerator.Generate(length);
        Assert.Equal(length, pwd.Length);
    }

    [Fact]
    public void Generate_EnforcesDiversityAndEntropy()
    {
        for (int i = 0; i < 50; i++)
        {
            string pwd = PasswordGenerator.Generate(16);
            Assert.Contains(pwd, c => "ABCDEFGHJKLMNPQRSTUVWXYZ".Contains(c));
            Assert.Contains(pwd, c => "abcdefghijkmnopqrstuvwxyz".Contains(c));
            Assert.Contains(pwd, c => "23456789".Contains(c));
            Assert.Contains(pwd, c => "!@#$%*+?=".Contains(c));

            // Verify confusing characters are excluded
            Assert.DoesNotContain('0', pwd);
            Assert.DoesNotContain('O', pwd);
            Assert.DoesNotContain('1', pwd);
            Assert.DoesNotContain('l', pwd);
            Assert.DoesNotContain('I', pwd);
        }
    }

    [Fact]
    public void Generate_GeneratesUniquePasswords()
    {
        var passwords = Enumerable.Range(0, 100)
            .Select(_ => PasswordGenerator.Generate(16))
            .ToHashSet();

        Assert.Equal(100, passwords.Count);
    }
}
