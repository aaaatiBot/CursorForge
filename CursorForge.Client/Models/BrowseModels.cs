using System.Text.Json.Serialization;

namespace CursorForge.Client.Models;

public enum BrowseSort
{
    Newest,
    MostLiked,
    MostDownloaded
}

public sealed class CursorRoleData
{
    [JsonPropertyName("storage_path")]
    public string StoragePath { get; set; } = string.Empty;

    [JsonPropertyName("hotspot_x")]
    public int HotspotX { get; set; }

    [JsonPropertyName("hotspot_y")]
    public int HotspotY { get; set; }
}

public sealed class CursorItem
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("owner_id")]
    public Guid OwnerId { get; set; }

    [JsonPropertyName("owner_username")]
    public string OwnerUsername { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("preview_path")]
    public string PreviewPath { get; set; } = string.Empty;

    [JsonPropertyName("preview_url")]
    public string? PreviewUrl { get; set; }

    [JsonPropertyName("download_count")]
    public int DownloadCount { get; set; }

    [JsonPropertyName("like_count")]
    public int LikeCount { get; set; }

    [JsonPropertyName("is_liked")]
    public bool IsLikedByMe { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "approved";

    [JsonPropertyName("roles")]
    public Dictionary<string, CursorRoleData>? Roles { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public bool HasMore { get; set; }
    public DateTime? NextTimestamp { get; set; }
    public Guid? NextId { get; set; }
}

public sealed record HotspotCoord(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y
);

public sealed class UploadSessionResult
{
    [JsonPropertyName("session_id")]
    public Guid SessionId { get; set; }

    [JsonPropertyName("preview_path")]
    public string PreviewPath { get; set; } = string.Empty;

    [JsonPropertyName("role_paths")]
    public Dictionary<string, string> RolePaths { get; set; } = [];

    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; }
}
