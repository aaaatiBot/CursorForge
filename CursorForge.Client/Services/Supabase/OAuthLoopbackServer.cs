using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CursorForge.Client.Services.Supabase;

public sealed class OAuthCallbackResult
{
    public bool Success { get; init; }
    public string? Code { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class OAuthLoopbackServer : IDisposable
{
    private readonly TcpListener _listener;
    public int Port { get; }
    public string RedirectUri => $"http://127.0.0.1:{Port}/callback";

    public OAuthLoopbackServer(int preferredPort = 54321)
    {
        // Try preferred port first, fall back to ephemeral port
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, preferredPort);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }
        catch
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }
    }

    public async Task<OAuthCallbackResult> WaitForCallbackAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() =>
        {
            try { _listener.Stop(); } catch { }
        });

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch when (cancellationToken.IsCancellationRequested)
                {
                    return new OAuthCallbackResult { Success = false, ErrorMessage = "Sign in was cancelled." };
                }

                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                {
                    string? requestLine = await reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrWhiteSpace(requestLine)) continue;

                    // Example: GET /callback?code=12345 HTTP/1.1
                    string[] parts = requestLine.Split(' ');
                    if (parts.Length < 2) continue;

                    string fullPath = parts[1];
                    var uri = new Uri(new Uri(RedirectUri), fullPath);
                    var query = ParseQueryString(uri.Query);

                    // Check for errors
                    if (query.TryGetValue("error", out var error))
                    {
                        query.TryGetValue("error_description", out var desc);
                        string errText = !string.IsNullOrEmpty(desc) ? desc : error;
                        await WriteHtmlResponseAsync(writer, isSuccess: false, errText);
                        return new OAuthCallbackResult { Success = false, ErrorMessage = errText };
                    }

                    // Check for auth code (PKCE flow)
                    if (query.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code))
                    {
                        await WriteHtmlResponseAsync(writer, isSuccess: true, "Authentication successful! Returning to CursorForge...");
                        return new OAuthCallbackResult { Success = true, Code = code };
                    }

                    // Check for token callback (implicit flow forwarded via JS fetch)
                    if (query.TryGetValue("access_token", out var accessToken) && !string.IsNullOrWhiteSpace(accessToken))
                    {
                        query.TryGetValue("refresh_token", out var refreshToken);
                        await WriteHtmlResponseAsync(writer, isSuccess: true, "Authentication successful! Returning to CursorForge...");
                        return new OAuthCallbackResult
                        {
                            Success = true,
                            AccessToken = accessToken,
                            RefreshToken = refreshToken
                        };
                    }

                    // Initial page load without code in query (e.g. hash fragment in browser)
                    // Serve HTML that inspects window.location.hash and forwards it
                    await WriteHashForwardingHtmlAsync(writer);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new OAuthCallbackResult { Success = false, ErrorMessage = "Sign in was cancelled or timed out." };
        }
        catch (Exception ex)
        {
            return new OAuthCallbackResult { Success = false, ErrorMessage = ex.Message };
        }

        return new OAuthCallbackResult { Success = false, ErrorMessage = "Authentication timed out." };
    }

    private static async Task WriteHtmlResponseAsync(StreamWriter writer, bool isSuccess, string message)
    {
        string title = isSuccess ? "Authentication Successful" : "Authentication Failed";
        string color = isSuccess ? "#818cf8" : "#f87171";
        string html = $@"<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"">
  <title>CursorForge - {title}</title>
  <style>
    body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #18181b; color: #f4f4f5; text-align: center; padding: 60px 20px; }}
    .card {{ max-width: 440px; margin: 0 auto; background: #27272a; padding: 32px; border-radius: 12px; box-shadow: 0 8px 24px rgba(0,0,0,0.4); border: 1px solid #3f3f46; }}
    h1 {{ color: {color}; font-size: 22px; margin-bottom: 12px; }}
    p {{ color: #a1a1aa; font-size: 14px; line-height: 1.5; }}
    .badge {{ display: inline-block; padding: 6px 14px; background: #312e81; color: #c7d2fe; border-radius: 6px; font-weight: 600; margin-top: 16px; font-size: 13px; }}
  </style>
</head>
<body>
  <div class=""card"">
    <h1>{title}</h1>
    <p>{message}</p>
    {(isSuccess ? @"<p>You can now close this tab and return to <strong>CursorForge</strong>.</p><div class=""badge"">Ready</div>" : "")}
  </div>
  {(isSuccess ? "<script>setTimeout(() => window.close(), 2500);</script>" : "")}
</body>
</html>";

        byte[] bytes = Encoding.UTF8.GetBytes(html);
        await writer.WriteLineAsync("HTTP/1.1 200 OK");
        await writer.WriteLineAsync("Content-Type: text/html; charset=utf-8");
        await writer.WriteLineAsync($"Content-Length: {bytes.Length}");
        await writer.WriteLineAsync("Connection: close");
        await writer.WriteLineAsync();
        await writer.WriteAsync(html);
    }

    private static async Task WriteHashForwardingHtmlAsync(StreamWriter writer)
    {
        string html = @"<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"">
  <title>CursorForge - Completing Authentication...</title>
  <style>
    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #18181b; color: #f4f4f5; text-align: center; padding: 60px 20px; }
  </style>
</head>
<body>
  <h2>Connecting to CursorForge...</h2>
  <script>
    if (window.location.hash && window.location.hash.length > 1) {
      window.location.replace('/callback?' + window.location.hash.substring(1));
    } else {
      setTimeout(() => {
        if (window.location.hash && window.location.hash.length > 1) {
          window.location.replace('/callback?' + window.location.hash.substring(1));
        }
      }, 500);
    }
  </script>
</body>
</html>";

        byte[] bytes = Encoding.UTF8.GetBytes(html);
        await writer.WriteLineAsync("HTTP/1.1 200 OK");
        await writer.WriteLineAsync("Content-Type: text/html; charset=utf-8");
        await writer.WriteLineAsync($"Content-Length: {bytes.Length}");
        await writer.WriteLineAsync("Connection: close");
        await writer.WriteLineAsync();
        await writer.WriteAsync(html);
    }

    private static Dictionary<string, string> ParseQueryString(string queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(queryString)) return result;

        string clean = queryString.TrimStart('?');
        string[] pairs = clean.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            string[] kv = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(kv[0]);
            string val = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
            result[key] = val;
        }

        return result;
    }

    public void Dispose()
    {
        try { _listener.Stop(); } catch { }
    }
}
