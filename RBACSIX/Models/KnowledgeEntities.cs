using System.ComponentModel.DataAnnotations;

namespace RBACSIX.Models;

public class KnowledgeDocument
{
    public int Id { get; set; }

    [Required, StringLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string StoredFileName { get; set; } = string.Empty;

    [Required, StringLength(64)]
    public string Sha256 { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string Status { get; set; } = "Processing";

    [StringLength(1000)]
    public string? Error { get; set; }

    [Required]
    public string UploadedByUserId { get; set; } = string.Empty;

    public DateTime UploadedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<KnowledgeChunk> Chunks { get; set; } = new List<KnowledgeChunk>();
}

public class KnowledgeChunk
{
    public int Id { get; set; }

    [Required]
    public int KnowledgeDocumentId { get; set; }
    public KnowledgeDocument KnowledgeDocument { get; set; } = null!;

    public int ChunkIndex { get; set; }
    public int? PageNumber { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;

    [Required]
    public string EmbeddingJson { get; set; } = string.Empty;
}
