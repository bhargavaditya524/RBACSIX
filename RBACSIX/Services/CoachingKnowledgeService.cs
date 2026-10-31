using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RBACSIX.Data;
using RBACSIX.Models;
using UglyToad.PdfPig;

namespace RBACSIX.Services;

public sealed class CoachingKnowledgeService(
    AppDbContext db,
    CoachingAiProvider aiProvider,
    IOptions<CoachingAssistantOptions> options,
    IWebHostEnvironment environment,
    ILogger<CoachingKnowledgeService> logger)
{
    public const long MaxUploadBytes = 25 * 1024 * 1024;
    private const int ChunkLength = 1400;
    private const int ChunkOverlap = 200;
    private const int EmbeddingBatchSize = 32;
    private const int RetrievalCount = 5;
    private const float MinimumSimilarity = 0.2f;
    private readonly CoachingAssistantOptions _options = options.Value;

    public Task<List<KnowledgeDocument>> GetDocumentsAsync(CancellationToken cancellationToken) =>
        db.KnowledgeDocuments
            .OrderByDescending(document => document.UploadedUtc)
            .ToListAsync(cancellationToken);

    public async Task<KnowledgeDocument> IndexUploadAsync(
        IFormFile file,
        string uploaderUserId,
        CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaxUploadBytes)
            throw new InvalidDataException("Choose a non-empty PDF, DOCX, or TXT file up to 25 MB.");

        var extension = Path.GetExtension(file.FileName);
        if (!new[] { ".pdf", ".docx", ".txt" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Only PDF, DOCX, and TXT documents are supported.");

        byte[] fileBytes;
        await using (var buffer = new MemoryStream())
        {
            await file.CopyToAsync(buffer, cancellationToken);
            fileBytes = buffer.ToArray();
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(fileBytes));
        if (await db.KnowledgeDocuments.AnyAsync(document => document.Sha256 == sha256, cancellationToken))
            throw new InvalidDataException("This document has already been uploaded.");

        var document = new KnowledgeDocument
        {
            FileName = Path.GetFileName(file.FileName)[..Math.Min(Path.GetFileName(file.FileName).Length, 255)],
            StoredFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}",
            Sha256 = sha256,
            Status = "Processing",
            UploadedByUserId = uploaderUserId
        };
        db.KnowledgeDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var filePath = GetFilePath(document.StoredFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllBytesAsync(filePath, fileBytes, cancellationToken);

            var pages = ExtractText(fileBytes, extension);
            var chunks = CreateChunks(pages);
            if (chunks.Count == 0)
                throw new InvalidDataException("No readable text was found in the document.");

            var embeddings = new List<float[]>(chunks.Count);
            for (var index = 0; index < chunks.Count; index += EmbeddingBatchSize)
            {
                var batch = chunks.Skip(index).Take(EmbeddingBatchSize).ToArray();
                embeddings.AddRange(await aiProvider.EmbedAsync(
                    batch.Select(chunk => chunk.Content).ToArray(),
                    cancellationToken));
            }

            document.Chunks = chunks.Select((chunk, index) => new KnowledgeChunk
            {
                ChunkIndex = index,
                PageNumber = chunk.PageNumber,
                Content = chunk.Content,
                EmbeddingJson = JsonSerializer.Serialize(embeddings[index])
            }).ToList();
            document.Status = "Indexed";
            document.Error = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            document.Status = "Failed";
            document.Error = "Indexing was canceled before it completed.";
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            document.Status = "Failed";
            document.Error = exception.Message.Length <= 1000
                ? exception.Message
                : exception.Message[..1000];
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogError(exception, "Knowledge document {DocumentId} could not be indexed.", document.Id);
        }

        return document;
    }

    public async Task DeleteAsync(int documentId, CancellationToken cancellationToken)
    {
        var document = await db.KnowledgeDocuments
            .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        if (document is null)
            return;

        var filePath = GetFilePath(document.StoredFileName);
        if (File.Exists(filePath))
            File.Delete(filePath);

        db.KnowledgeDocuments.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RetrievedKnowledgeChunk>> SearchAsync(
        string question,
        CancellationToken cancellationToken)
    {
        if (!await db.KnowledgeChunks.AnyAsync(
                chunk => chunk.KnowledgeDocument.Status == "Indexed",
                cancellationToken))
            return [];

        var queryVector = (await aiProvider.EmbedAsync([question], cancellationToken))[0];
        var chunks = await db.KnowledgeChunks
            .Where(chunk => chunk.KnowledgeDocument.Status == "Indexed")
            .Select(chunk => new
            {
                chunk.Content,
                chunk.PageNumber,
                chunk.EmbeddingJson,
                chunk.KnowledgeDocument.FileName
            })
            .ToListAsync(cancellationToken);

        return chunks
            .Select(chunk =>
            {
                var vector = JsonSerializer.Deserialize<float[]>(chunk.EmbeddingJson)
                    ?? throw new InvalidOperationException("A stored knowledge embedding is invalid.");
                return new RetrievedKnowledgeChunk(
                    chunk.FileName,
                    chunk.PageNumber,
                    chunk.Content,
                    CosineSimilarity(queryVector, vector));
            })
            .Where(chunk => chunk.Score >= MinimumSimilarity)
            .OrderByDescending(chunk => chunk.Score)
            .Take(RetrievalCount)
            .ToArray();
    }

    private string GetFilePath(string storedFileName)
    {
        var root = Path.IsPathRooted(_options.DocumentRootPath)
            ? _options.DocumentRootPath
            : Path.Combine(environment.ContentRootPath, _options.DocumentRootPath);
        return Path.Combine(Path.GetFullPath(root), storedFileName);
    }

    private static IReadOnlyList<(string Text, int? PageNumber)> ExtractText(byte[] fileBytes, string extension)
    {
        using var stream = new MemoryStream(fileBytes, writable: false);
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            using var pdf = PdfDocument.Open(stream);
            return pdf.GetPages()
                .Select(page => (page.Text, (int?)page.Number))
                .ToArray();
        }

        if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
        {
            using var wordDocument = WordprocessingDocument.Open(stream, false);
            var text = wordDocument.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
            return [(text, null)];
        }

        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            return [(Encoding.UTF8.GetString(fileBytes), null)];

        throw new InvalidDataException("Unsupported document format.");
    }

    private static List<(string Content, int? PageNumber)> CreateChunks(
        IReadOnlyList<(string Text, int? PageNumber)> pages)
    {
        var chunks = new List<(string Content, int? PageNumber)>();
        foreach (var (text, pageNumber) in pages)
        {
            var start = 0;
            while (start < text.Length)
            {
                var end = Math.Min(start + ChunkLength, text.Length);
                if (end < text.Length)
                {
                    var boundary = text.LastIndexOfAny([' ', '\n', '\r', '\t'], end - 1, end - start);
                    if (boundary > start + ChunkLength / 2)
                        end = boundary + 1;
                }

                var content = text[start..end].Trim();
                if (!string.IsNullOrWhiteSpace(content))
                    chunks.Add((content, pageNumber));

                if (end >= text.Length)
                    break;
                start = Math.Max(end - ChunkOverlap, start + 1);
            }
        }

        return chunks;
    }

    private static float CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count == 0 || left.Count != right.Count)
            throw new InvalidOperationException("Stored knowledge embeddings do not match the configured embedding model.");

        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (var index = 0; index < left.Count; index++)
        {
            if (!float.IsFinite(left[index]) || !float.IsFinite(right[index]))
                throw new InvalidOperationException("A stored knowledge embedding contains an invalid value.");
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        if (leftMagnitude == 0 || rightMagnitude == 0)
            return 0;
        return (float)(dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude)));
    }
}
