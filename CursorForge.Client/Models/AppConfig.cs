namespace CursorForge.Client.Models;

public sealed class AppConfig
{
    public string SupabaseUrl { get; set; } = "https://your-project-ref.supabase.co";
    public string SupabaseAnonKey { get; set; } = "your-supabase-anon-key";
    public int CacheLimitMb { get; set; } = 200;
    public string LogLevel { get; set; } = "Information";
}

