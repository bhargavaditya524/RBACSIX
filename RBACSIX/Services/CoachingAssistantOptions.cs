namespace RBACSIX.Services;

public sealed class CoachingAssistantOptions
{
    public string? ApiKey { get; set; }
    public string ChatApiUrl { get; set; } = "https://openrouter.ai/api/v1/chat/completions";
    public string EmbeddingApiUrl { get; set; } = "https://openrouter.ai/api/v1/embeddings";
    public string ChatModel { get; set; } = "deepseek/deepseek-chat";
    public string EmbeddingModel { get; set; } = "openai/text-embedding-3-small";
    public string DocumentRootPath { get; set; } = "App_Data/knowledge_base";
}

public sealed record AssistantEvidence(string Source, string Content);
public sealed record RetrievedKnowledgeChunk(string FileName, int? PageNumber, string Content, float Score);
