using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApproveFlow.Application.AiRisk;
using ApproveFlow.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApproveFlow.Infrastructure.Ai;

public sealed class AiRiskSummaryOptions
{
    public const string SectionName = "AiRiskSummary";

    /// <summary>LLM provider. "Anthropic" (Claude Messages API) is supported; anything else uses the rule-based fallback.</summary>
    public string Provider { get; set; } = "Anthropic";

    public string Model { get; set; } = "claude-opus-5-5";

    public string Endpoint { get; set; } = "https://api.anthropic.com/v1/messages";

    /// <summary>Secret. Supplied via user-secrets or the AiRiskSummary__ApiKey environment variable; never committed or logged.</summary>
    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 60;

    public int MaxTokens { get; set; } = 16000;

    /// <summary>Reasoning effort (low | medium | high | xhigh | max).</summary>
    public string Effort { get; set; } = "medium";

    /// <summary>Retry a safety-classifier decline on Anthropic's recommended fallback model, server-side (Claude API only).</summary>
    public bool UseServerSideFallback { get; set; } = true;
}

/// <summary>
/// Calls the Claude Messages API with HttpClient to produce an advisory risk summary as schema-constrained JSON.
/// Falls back to <see cref="RuleBasedRiskAssessor"/> when no key is configured, the provider is unsupported, or the
/// call fails, is declined or cannot be parsed - so the feature always produces a result.
/// </summary>
internal sealed class LlmRiskSummaryService : IAiRiskSummaryService
{
    internal const string AnthropicVersion = "2023-06-01";
    internal const string FallbackBeta = "server-side-fallback-2026-07-01";

    private const string SystemPrompt =
        "You are a credit-risk analyst assistant at a retail bank in Sri Lanka. You review anonymised loan figures and write a short, " +
        "factual risk summary for the checker who will make the decision. Base every statement only on the figures provided and the " +
        "bank policy given; do not assume facts that are not there. Bank policy: a debt-burden ratio above 60% is high risk and above 40% " +
        "is moderate. Amounts are in Sri Lankan rupees (LKR). Keep the summary under 120 words and give 3 to 5 key factors, most important " +
        "first. Your output is advisory only: the checker makes the final decision.";

    private static readonly JsonObject OutputSchema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["riskLevel"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Low", "Moderate", "High") },
            ["summary"] = new JsonObject { ["type"] = "string" },
            ["keyFactors"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
        },
        ["required"] = new JsonArray("riskLevel", "summary", "keyFactors"),
        ["additionalProperties"] = false,
    };

    private readonly HttpClient _http;
    private readonly AiRiskSummaryOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<LlmRiskSummaryService> _logger;

    public LlmRiskSummaryService(HttpClient http, IOptions<AiRiskSummaryOptions> options, TimeProvider clock, ILogger<LlmRiskSummaryService> logger)
    {
        _http = http;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RiskSummaryResult> SummariseAsync(RiskSummaryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var now = _clock.GetUtcNow().UtcDateTime;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return RuleBasedRiskAssessor.Assess(input, now, "No AI API key is configured, so the deterministic rule-based assessment is shown.");
        }

        if (!string.Equals(_options.Provider, "Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("AI provider {Provider} is not supported; using rule-based assessment", _options.Provider);
            return RuleBasedRiskAssessor.Assess(input, now, $"AI provider '{_options.Provider}' is not supported.");
        }

        try
        {
            using var request = BuildRequest(input, RuleBasedRiskAssessor.Assess(input, now));
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // The response body is not logged: it may echo request content.
                _logger.LogWarning("AI service returned HTTP {Status}; using rule-based assessment", (int)response.StatusCode);
                return RuleBasedRiskAssessor.Assess(input, now, $"The AI service returned HTTP {(int)response.StatusCode}.");
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return Interpret(document.RootElement, input, now);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                                   && ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                                       or KeyNotFoundException)
        {
            _logger.LogWarning(ex, "AI risk summary request failed; using rule-based assessment");
            return RuleBasedRiskAssessor.Assess(input, now, "The AI service is unavailable or returned an unexpected response.");
        }
    }

    internal HttpRequestMessage BuildRequest(RiskSummaryInput input, RiskSummaryResult indicators)
    {
        var figures = JsonSerializer.Serialize(input);
        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["max_tokens"] = _options.MaxTokens,
            ["system"] = SystemPrompt,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] =
                    "Assess the credit risk of this loan application.\n\n" +
                    $"<loan_figures currency=\"{RiskSummaryInput.Currency}\">\n{figures}\n</loan_figures>\n\n" +
                    "<rule_based_indicators>\n" + string.Join("\n", indicators.Factors) + "\n</rule_based_indicators>",
            }),
            ["output_config"] = new JsonObject
            {
                ["effort"] = _options.Effort,
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = OutputSchema.DeepClone() },
            },
        };

        if (_options.UseServerSideFallback)
        {
            body["fallbacks"] = "default";
        }

        var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("x-api-key", _options.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        if (_options.UseServerSideFallback)
        {
            request.Headers.Add("anthropic-beta", FallbackBeta);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private RiskSummaryResult Interpret(JsonElement message, RiskSummaryInput input, DateTime now)
    {
        var stopReason = message.TryGetProperty("stop_reason", out var stop) ? stop.GetString() : null;
        if (stopReason == "refusal")
        {
            _logger.LogInformation("AI service declined the risk summary request");
            return RuleBasedRiskAssessor.Assess(input, now, "The AI service declined this request.");
        }

        if (stopReason == "max_tokens")
        {
            return RuleBasedRiskAssessor.Assess(input, now, "The AI response was incomplete.");
        }

        // Thinking blocks (and any fallback markers) are skipped; the schema-constrained answer is in the text blocks.
        var text = string.Concat(message.GetProperty("content").EnumerateArray()
            .Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "text")
            .Select(block => block.GetProperty("text").GetString()));

        using var answer = JsonDocument.Parse(text);
        var root = answer.RootElement;
        if (!Enum.TryParse<RiskLevel>(root.GetProperty("riskLevel").GetString(), ignoreCase: true, out var level))
        {
            throw new JsonException("Unexpected riskLevel value.");
        }

        var summary = Truncate(root.GetProperty("summary").GetString() ?? string.Empty, 1500);
        var factors = root.GetProperty("keyFactors").EnumerateArray().Select(f => Truncate(f.GetString() ?? string.Empty, 300))
            .Where(f => f.Length > 0).Take(8).ToList();
        var model = message.TryGetProperty("model", out var m) ? m.GetString() : _options.Model;

        return new RiskSummaryResult(level, summary, factors, IsFallback: false,
            string.Create(CultureInfo.InvariantCulture, $"Anthropic {model}"), model, null, now);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value.Trim() : value[..max].Trim() + "…";
}
