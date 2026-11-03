namespace CursorForge.Client.Models;

public sealed class LocalCursorRoleItem
{
    public required string FilePath { get; set; }
    public int HotspotX { get; set; }
    public int HotspotY { get; set; }
}

public sealed class LocalTheme
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? PreviewPath { get; set; }
    public int? CursorBaseSize { get; set; }
    public Dictionary<string, LocalCursorRoleItem> Roles { get; set; } = [];
}
