using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CursorForge.Client.Models;
using FluentAssertions;

namespace CursorForge.Tests.Unit;

public class StarterThemesGeneratorTests
{
    private static void GenerateVectorTheme(string themeName, string themeId, Color fillColor, Color strokeColor, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        var rolesMap = new Dictionary<string, object>();

        foreach (var role in CursorRoleDefinition.StandardRoles)
        {
            string pngFileName = $"{role.RoleId}.png";
            string pngPath = Path.Combine(outputDir, pngFileName);

            int hx = 2;
            int hy = 2;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var brush = new SolidColorBrush(fillColor);
                var pen = new Pen(new SolidColorBrush(strokeColor), 3);

                // Render distinct geometry per cursor role
                switch (role.RoleId)
                {
                    case "Crosshair":
                        hx = 64; hy = 64;
                        dc.DrawEllipse(null, pen, new Point(64, 64), 30, 30);
                        dc.DrawLine(pen, new Point(64, 20), new Point(64, 108));
                        dc.DrawLine(pen, new Point(20, 64), new Point(108, 64));
                        break;

                    case "IBeam":
                        hx = 64; hy = 64;
                        dc.DrawLine(pen, new Point(44, 24), new Point(84, 24));
                        dc.DrawLine(pen, new Point(44, 104), new Point(84, 104));
                        dc.DrawLine(pen, new Point(64, 24), new Point(64, 104));
                        break;

                    case "Wait":
                        hx = 64; hy = 64;
                        dc.DrawEllipse(brush, pen, new Point(64, 64), 28, 28);
                        break;

                    case "Hand":
                        hx = 48; hy = 16;
                        var handGeo = new StreamGeometry();
                        using (var ctx = handGeo.Open())
                        {
                            ctx.BeginFigure(new Point(48, 16), true, true);
                            ctx.LineTo(new Point(60, 40), true, false);
                            ctx.LineTo(new Point(80, 56), true, false);
                            ctx.LineTo(new Point(70, 96), true, false);
                            ctx.LineTo(new Point(36, 96), true, false);
                            ctx.LineTo(new Point(28, 56), true, false);
                        }
                        dc.DrawGeometry(brush, pen, handGeo);
                        break;

                    default: // Arrow / Resizing / Other
                        hx = 4; hy = 4;
                        var arrowGeo = new StreamGeometry();
                        using (var ctx = arrowGeo.Open())
                        {
                            ctx.BeginFigure(new Point(12, 12), true, true);
                            ctx.LineTo(new Point(12, 88), true, false);
                            ctx.LineTo(new Point(34, 66), true, false);
                            ctx.LineTo(new Point(54, 98), true, false);
                            ctx.LineTo(new Point(68, 88), true, false);
                            ctx.LineTo(new Point(48, 56), true, false);
                            ctx.LineTo(new Point(74, 56), true, false);
                        }
                        dc.DrawGeometry(brush, pen, arrowGeo);
                        break;
                }
            }

            var rtb = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(pngPath))
            {
                encoder.Save(fs);
            }

            rolesMap[role.RoleId] = new
            {
                file = pngFileName,
                hotspot_x = hx,
                hotspot_y = hy
            };
        }

        // Preview image
        string previewPath = Path.Combine(outputDir, "preview.png");
        File.Copy(Path.Combine(outputDir, "Arrow.png"), previewPath, overwrite: true);

        // theme.json
        var themeDoc = new
        {
            id = themeId,
            name = themeName,
            description = $"Official {themeName} offline cursor scheme for CursorForge.",
            author = "CursorForge Official",
            preview = "preview.png",
            roles = rolesMap
        };

        string json = JsonSerializer.Serialize(themeDoc, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outputDir, "theme.json"), json);
    }

    [Fact]
    public void GenerateAllStarterThemes_ProducesValidThemeFiles()
    {
        string projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "CursorForge.Client", "Assets", "StarterThemes"));

        GenerateVectorTheme("Neon Cyan", "neon-cyan", Color.FromRgb(0, 229, 255), Color.FromRgb(11, 14, 20), Path.Combine(projectDir, "Neon"));
        GenerateVectorTheme("Minimalist Slate", "slate", Color.FromRgb(148, 163, 184), Color.FromRgb(248, 250, 252), Path.Combine(projectDir, "Slate"));
        GenerateVectorTheme("Cyberpunk Gold", "cyber-gold", Color.FromRgb(245, 158, 11), Color.FromRgb(139, 92, 246), Path.Combine(projectDir, "Cyber"));

        foreach (string theme in new[] { "Neon", "Slate", "Cyber" })
        {
            string dir = Path.Combine(projectDir, theme);
            File.Exists(Path.Combine(dir, "theme.json")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "preview.png")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "Arrow.png")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "Hand.png")).Should().BeTrue();
        }
    }
}
