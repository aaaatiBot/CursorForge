using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace CursorForge.Client.Models;

[Table("cursors")]
public sealed class DbCursor : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("owner_id")]
    public Guid OwnerId { get; set; }

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("description")]
    public string Description { get; set; } = string.Empty;

    [Column("tags")]
    public string[]? Tags { get; set; }

    [Column("preview_path")]
    public string PreviewPath { get; set; } = string.Empty;

    [Column("download_count")]
    public int DownloadCount { get; set; }

    [Column("like_count")]
    public int LikeCount { get; set; }

    [Column("status")]
    public string Status { get; set; } = "approved";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

[Table("profiles")]
public sealed class DbProfile : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("username")]
    public string Username { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
