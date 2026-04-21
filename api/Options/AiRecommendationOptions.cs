namespace C2E.Api.Options;

public sealed class AiRecommendationOptions
{
    public const string SectionName = "AIRecommendations";

    /// <summary>deterministic | openai | hybrid</summary>
    public string Provider { get; set; } = "deterministic";
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com";
    public string OpenAiModel { get; set; } = "gpt-4o-mini";
    public string OpenAiApiKey { get; set; } = "";
    public int OpenAiTimeoutSeconds { get; set; } = 6;
    public int OpenAiMaxCandidates { get; set; } = 15;
    public decimal OpenAiTemperature { get; set; } = 0.1m;
}
