using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Models.ViewModels;
using RBACSIX.Services;
using System.Security.Claims;

namespace RBACSIX.Controllers;

[Authorize(Roles = "Admin")]
public sealed class KnowledgeDocumentsController(
    CoachingKnowledgeService knowledgeService,
    ILogger<KnowledgeDocumentsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new KnowledgeDocumentListViewModel
        {
            Documents = await knowledgeService.GetDocumentsAsync(cancellationToken)
        });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(CoachingKnowledgeService.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = CoachingKnowledgeService.MaxUploadBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            TempData["Error"] = "Select a document to upload.";
            return RedirectToAction(nameof(Index));
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        try
        {
            var document = await knowledgeService.IndexUploadAsync(file, userId, cancellationToken);
            TempData[document.Status == "Indexed" ? "Success" : "Error"] =
                document.Status == "Indexed"
                    ? $"“{document.FileName}” was indexed and is available to authenticated users."
                    : $"“{document.FileName}” could not be indexed: {document.Error}";
        }
        catch (InvalidDataException exception)
        {
            TempData["Error"] = exception.Message;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The knowledge document could not be indexed by the AI provider.");
            TempData["Error"] = "The AI provider could not be reached. The document was not indexed.";
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "The knowledge document upload failed.");
            TempData["Error"] = "The document could not be saved to the knowledge index.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await knowledgeService.DeleteAsync(id, cancellationToken);
        TempData["Success"] = "The knowledge document and its indexed content were deleted.";
        return RedirectToAction(nameof(Index));
    }
}
