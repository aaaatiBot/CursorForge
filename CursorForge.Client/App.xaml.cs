using System.Windows;
using CursorForge.Client.Services;
using CursorForge.Client.ViewModels;
using CursorForge.Client.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CursorForge.Client;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                var logger = Services?.GetService<IStructuredLogger>();
                logger?.LogError("app.dispatcher_unhandled", args.Exception.Message, args.Exception);
            }
            catch { }
            MessageBox.Show(args.Exception.Message, "CursorForge Notification", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try
            {
                var logger = Services?.GetService<IStructuredLogger>();
                if (args.ExceptionObject is Exception ex)
                {
                    logger?.LogError("app.domain_unhandled", ex.Message, ex);
                }
            }
            catch { }
        };

        try
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            Services = serviceCollection.BuildServiceProvider();

            var logger = Services.GetRequiredService<IStructuredLogger>();
            logger.LogInformation("app.startup", "CursorForge application starting up");

            var storage = Services.GetRequiredService<ILocalStorageService>();
            storage.CleanupStaleTempFiles();

            var systemCursors = Services.GetRequiredService<ISystemCursorService>();
            systemCursors.CheckAndRecoverPendingApply();

            _ = Services.GetRequiredService<CursorForge.Client.Services.Supabase.ISupabaseClientService>().InitializeAsync();

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "CursorForge Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core Singletons
        services.AddSingleton<IStructuredLogger, StructuredLogger>();
        services.AddSingleton<ILocalStorageService, LocalStorageService>();
        services.AddSingleton<IDpapiTokenStorage, DpapiTokenStorage>();
        services.AddSingleton<CursorForge.Client.Services.Supabase.ISupabaseClientService, CursorForge.Client.Services.Supabase.SupabaseClientService>();
        services.AddSingleton<ICursorConverterService, CursorConverterService>();
        services.AddSingleton<ISystemCursorService, SystemCursorService>();
        services.AddSingleton<IThemeManagerService, ThemeManagerService>();

        // ViewModels
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<BrowseViewModel>();
        services.AddSingleton<UploadViewModel>();
        services.AddSingleton<MyCursorsViewModel>();
        services.AddSingleton<AccountViewModel>();
        services.AddSingleton<MainViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
    }
}

