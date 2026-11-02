using System.IO;
using CursorForge.Client.Services;
using Velopack;

namespace CursorForge.Client;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnFirstRun(v => LocalStorageService.InitializeStorage())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}

