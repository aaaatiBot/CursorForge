using CursorForge.Client.Models;
using CursorForge.Client.Services;
using FluentAssertions;
using Microsoft.Win32;
using Moq;

namespace CursorForge.Tests.Unit;

public class RegistryFidelityTests : IDisposable
{
    private readonly string _testRoot;
    private readonly Mock<IStructuredLogger> _loggerMock;
    private readonly Mock<ICursorConverterService> _converterMock;
    private readonly LocalStorageService _storage;

    public RegistryFidelityTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"RegFidelity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _storage = new LocalStorageService(_testRoot);
        _loggerMock = new Mock<IStructuredLogger>();
        _converterMock = new Mock<ICursorConverterService>();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Ignore test cleanup
        }
    }

    [Fact]
    public void CaptureCurrentSnapshot_CapturesAll15StandardRoles()
    {
        var service = new SystemCursorService(_storage, _loggerMock.Object, _converterMock.Object);
        var snapshot = service.CaptureCurrentSnapshot();

        snapshot.Should().NotBeNull();
        snapshot.Roles.Should().HaveCount(15);

        var capturedRoleNames = snapshot.Roles.Select(r => r.ValueName).ToHashSet();
        foreach (var def in CursorRoleDefinition.StandardRoles)
        {
            capturedRoleNames.Should().Contain(def.RegistryValueName);
        }
    }

    [Fact]
    public void SnapshotModel_PreservesExactValueKindAndNullState()
    {
        var snapshot = new CursorSchemeSnapshot
        {
            DefaultSchemeName = "Custom Scheme",
            CursorBaseSize = 32,
            Roles =
            [
                new RegistryValueSnapshot
                {
                    ValueName = "Arrow",
                    Exists = true,
                    ValueKind = RegistryValueKind.ExpandString,
                    RawData = "%SystemRoot%\\Cursors\\aero_arrow.cur"
                },
                new RegistryValueSnapshot
                {
                    ValueName = "NWPen",
                    Exists = false,
                    ValueKind = RegistryValueKind.String,
                    RawData = null
                }
            ]
        };

        snapshot.DefaultSchemeName.Should().Be("Custom Scheme");
        snapshot.CursorBaseSize.Should().Be(32);
        snapshot.Roles[0].ValueKind.Should().Be(RegistryValueKind.ExpandString);
        snapshot.Roles[0].RawData.Should().Be("%SystemRoot%\\Cursors\\aero_arrow.cur");
        snapshot.Roles[1].Exists.Should().BeFalse();
        snapshot.Roles[1].RawData.Should().BeNull();
    }
}
