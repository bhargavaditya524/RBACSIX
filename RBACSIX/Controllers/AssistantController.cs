using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using RBACSIX.Models.ViewModels;
using RBACSIX.Services;

namespace RBACSIX.Controllers;

[Authorize(Roles = "Admin,Staff,Student,Parent")]
public sealed class AssistantController(
    CoachingAssistantService assistantService,
    ILogger<AssistantController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask(
        [FromBody] AssistantQuestionViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        try
        {
            return Ok(await assistantService.AskAsync(User, model.Message.Trim(), cancellationToken));
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The coaching assistant's AI provider request failed.");
            return Problem(
                title: "Assistant unavailable",
                detail: "The assistant could not reach its AI provider. Please try again later.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogError(exception, "The coaching assistant could not prepare an answer.");
            return Problem(
                title: "Assistant unavailable",
                detail: "The assistant is not ready to answer. Please contact an administrator.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The coaching assistant encountered invalid stored or provider data.");
            return Problem(
                title: "Assistant unavailable",
                detail: "The assistant could not read its current knowledge index. Please contact an administrator.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
