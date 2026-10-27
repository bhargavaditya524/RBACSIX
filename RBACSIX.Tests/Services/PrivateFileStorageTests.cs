using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RBACSIX.Services;

namespace RBACSIX.Tests.Services;

public class PrivateFileStorageTests
{
    [Fact]
    public async Task SavePdfMaterialAsync_StoresUnderRandomNameOutsideWebRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var storage = CreateStorage(root);
        var upload = CreateFormFile("lecture.pdf", "%PDF-1.7\ncontent"u8.ToArray());

        var stored = await storage.SavePdfMaterialAsync(upload, CancellationToken.None);

        Assert.NotEqual("lecture.pdf", stored.StoredFileName);
        Assert.EndsWith(".pdf", stored.StoredFileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("lecture.pdf", stored.OriginalFileName);
        Assert.Equal("application/pdf", stored.ContentType);
        Assert.True(File.Exists(Path.Combine(root, stored.StoredFileName)));
        Assert.Equal("%PDF-1.7\ncontent", await File.ReadAllTextAsync(Path.Combine(root, stored.StoredFileName)));

        await storage.DeleteAsync(stored.StoredFileName);
        Assert.False(File.Exists(Path.Combine(root, stored.StoredFileName)));
        Directory.Delete(root);
    }

    [Fact]
    public async Task SaveSubmissionAsync_RejectsUnsupportedExtensionAndMismatchedSignature()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var storage = CreateStorage(root);

        await Assert.ThrowsAsync<InvalidUploadException>(() =>
            storage.SaveSubmissionAsync(CreateFormFile("run.exe", "MZ executable"u8.ToArray()), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidUploadException>(() =>
            storage.SaveSubmissionAsync(CreateFormFile("answer.pdf", "not a pdf"u8.ToArray()), CancellationToken.None));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task SaveQuizAnswerAsync_AllowsPdfAndImagesButRejectsOtherSubmissionTypes()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var storage = CreateStorage(root);

        var pdf = await storage.SaveQuizAnswerAsync(
            CreateFormFile("answer.pdf", "%PDF-1.7\ncontent"u8.ToArray()),
            CancellationToken.None);
        var image = await storage.SaveQuizAnswerAsync(
            CreateFormFile("answer.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]),
            CancellationToken.None);

        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.Equal("image/png", image.ContentType);
        await Assert.ThrowsAsync<InvalidUploadException>(() =>
            storage.SaveQuizAnswerAsync(
                CreateFormFile("answer.docx", "PK\x03\x04"u8.ToArray()),
                CancellationToken.None));

        await storage.DeleteAsync(pdf.StoredFileName);
        await storage.DeleteAsync(image.StoredFileName);
        Directory.Delete(root);
    }

    [Fact]
    public void Constructor_RejectsStorageConfiguredUnderPublicWebRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(root, "wwwroot");
        var environment = CreateEnvironment(root, webRoot);

        Assert.Throws<InvalidOperationException>(() => new PrivateFileStorage(
            environment,
            Options.Create(new PrivateFileStorageOptions { RootPath = Path.Combine(webRoot, "uploads") }),
            NullLogger<PrivateFileStorage>.Instance));
    }

    private static PrivateFileStorage CreateStorage(string root)
    {
        return new PrivateFileStorage(
            CreateEnvironment(Path.GetDirectoryName(root)!, Path.Combine(Path.GetDirectoryName(root)!, "wwwroot")),
            Options.Create(new PrivateFileStorageOptions { RootPath = root }),
            NullLogger<PrivateFileStorage>.Instance);
    }

    private static IFormFile CreateFormFile(string fileName, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, stream.Length, "File", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream"
        };
    }

    private static IWebHostEnvironment CreateEnvironment(string contentRoot, string webRoot)
    {
        return new TestWebHostEnvironment
        {
            ContentRootPath = contentRoot,
            WebRootPath = webRoot,
            ContentRootFileProvider = new NullFileProvider(),
            WebRootFileProvider = new NullFileProvider(),
            ApplicationName = "RBACSIX.Tests",
            EnvironmentName = "Development"
        };
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
