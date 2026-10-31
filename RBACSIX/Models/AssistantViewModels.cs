using System.ComponentModel.DataAnnotations;

namespace RBACSIX.Models.ViewModels;

public class AssistantQuestionViewModel
{
    [Required, StringLength(4000)]
    public string Message { get; set; } = string.Empty;
}

public class KnowledgeDocumentListViewModel
{
    public IReadOnlyList<KnowledgeDocument> Documents { get; set; } = [];
}

public sealed record AssistantSourceViewModel(
    string Reference,
    string Source,
    int? PageNumber,
    string Type);

public sealed record AssistantAnswerViewModel(
    string Answer,
    IReadOnlyList<AssistantSourceViewModel> Sources);
