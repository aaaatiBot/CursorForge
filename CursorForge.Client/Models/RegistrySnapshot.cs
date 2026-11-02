using Microsoft.Win32;

namespace CursorForge.Client.Models;

public sealed class RegistryValueSnapshot
{
    public required string ValueName { get; init; }
    public required bool Exists { get; init; }
    public RegistryValueKind ValueKind { get; init; } = RegistryValueKind.String;
    public string? RawData { get; init; }
}

public sealed class CursorSchemeSnapshot
{
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;
    public string? DefaultSchemeName { get; init; }
    public int? CursorBaseSize { get; init; }
    public List<RegistryValueSnapshot> Roles { get; init; } = [];
}
