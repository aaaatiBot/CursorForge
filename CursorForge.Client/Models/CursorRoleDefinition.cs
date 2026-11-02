using Microsoft.Win32;

namespace CursorForge.Client.Models;

public sealed class CursorRoleDefinition
{
    public required string RoleId { get; init; }
    public required string RegistryValueName { get; init; }
    public required string DisplayName { get; init; }
    public required string DefaultFileName { get; init; }

    public static readonly IReadOnlyList<CursorRoleDefinition> StandardRoles = new List<CursorRoleDefinition>
    {
        new() { RoleId = "Arrow", RegistryValueName = "Arrow", DisplayName = "Normal Select", DefaultFileName = "aero_arrow.cur" },
        new() { RoleId = "Help", RegistryValueName = "Help", DisplayName = "Help Select", DefaultFileName = "aero_helpsel.cur" },
        new() { RoleId = "AppStarting", RegistryValueName = "AppStarting", DisplayName = "Working in Background", DefaultFileName = "aero_working.ani" },
        new() { RoleId = "Wait", RegistryValueName = "Wait", DisplayName = "Busy", DefaultFileName = "aero_busy.ani" },
        new() { RoleId = "Crosshair", RegistryValueName = "Cross", DisplayName = "Precision Select", DefaultFileName = "aero_cross.cur" },
        new() { RoleId = "IBeam", RegistryValueName = "IBeam", DisplayName = "Text Select", DefaultFileName = "aero_beam.cur" },
        new() { RoleId = "NWPen", RegistryValueName = "NWPen", DisplayName = "Handwriting", DefaultFileName = "aero_pen.cur" },
        new() { RoleId = "No", RegistryValueName = "No", DisplayName = "Unavailable", DefaultFileName = "aero_unavail.cur" },
        new() { RoleId = "SizeNS", RegistryValueName = "SizeNS", DisplayName = "Vertical Resize", DefaultFileName = "aero_ns.cur" },
        new() { RoleId = "SizeWE", RegistryValueName = "SizeWE", DisplayName = "Horizontal Resize", DefaultFileName = "aero_we.cur" },
        new() { RoleId = "SizeNWSE", RegistryValueName = "SizeNWSE", DisplayName = "Diagonal Resize 1", DefaultFileName = "aero_nwse.cur" },
        new() { RoleId = "SizeNESW", RegistryValueName = "SizeNESW", DisplayName = "Diagonal Resize 2", DefaultFileName = "aero_nesw.cur" },
        new() { RoleId = "SizeAll", RegistryValueName = "SizeAll", DisplayName = "Move", DefaultFileName = "aero_move.cur" },
        new() { RoleId = "UpArrow", RegistryValueName = "UpArrow", DisplayName = "Alternate Select", DefaultFileName = "aero_up.cur" },
        new() { RoleId = "Hand", RegistryValueName = "Hand", DisplayName = "Link Select", DefaultFileName = "aero_link.cur" }
    };
}
