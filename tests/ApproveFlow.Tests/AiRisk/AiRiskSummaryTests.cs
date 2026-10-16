using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ApproveFlow.Application.AiRisk;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Infrastructure.Ai;
using ApproveFlow.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ApproveFlow.Tests.AiRisk;

public sealed class AiRiskSummaryTests
{
    private static RiskSummaryInput Input(decimal dbr = 45m, decimal lti = 2.5m) =>
        new("VL", "Vehicle Loan", 3_500_000m, 60, 13m, 250_000m, 30_000m, 79_635.76m, dbr, lti, 84, 13m, "Submitted");

    [Fact]
    public void Input_type_cannot_carry_personal_data()
    {
        // Privacy by construction: only these fields can ever be sent to the LLM.
        var allowed = new[]
        {
            "ProductCode", "ProductName", "Amount", "TenureMonths", "InterestRatePercent", "MonthlyIncome", "ExistingMonthlyObligations",
            "MonthlyInstallment", "DebtBurdenRatioPercent", "LoanToAnnualIncome", "ProductMaxTenureMonths", "ProductDefaultInterestRatePercent",
            "CurrentStatus",
        };
        Assert.Equal(allowed.OrderBy(x => x), typeof(RiskSummaryInput).GetProperties().Select(p => p.Name).OrderBy(x => x));
    }

    [Theory]
    [InlineData(35, 2, RiskLevel.Low)]
    [InlineData(50, 2, RiskLevel.Moderate)]
    [InlineData(72, 2, RiskLevel.High)]
    [InlineData(30, 7, RiskLevel.High)]
    public void Rule_based_assessment_is_deterministic(decimal dbr, decimal lti, RiskLevel expected)
    {
        var input = Input(dbr, lti) with { MonthlyInstallment = 50_000m, ExistingMonthlyObligations = 10_000m };
        var result = RuleBasedRiskAssessor.Assess(input, DateTime.UtcNow);
        Assert.Equal(expected, result.RiskLevel);
        Assert.True(result.IsFallback);
        Assert.NotEmpty(result.Factors);
    }

    [Fact]
    public async Task Without_an_api_key_the_rule_based_summary_is_returned_and_the_request_is_audited()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var target = await host.QueryAsync(db => db.LoanApplications.Include(a => a.Customer)
            .Where(a => a.Status == LoanStatus.Submitted).OrderBy(a => a.Id).FirstAsync());
        await host.SignInAsync("checker1@demo.lk");

        var result = await host.WithScopeAsync(sp => sp.GetRequiredService<IRiskSummaryAppService>().GenerateAsync(target.Id));

        Assert.True(result.IsFallback);
        Assert.Contains("No AI API key", result.FallbackReason, StringComparison.Ordinal);

        var key = target.Id.ToString(CultureInfo.InvariantCulture);
        var audit = await host.QueryAsync(db => db.AuditLogs.SingleAsync(a => a.EventType == AuditEventType.AiRequest && a.RecordKey == key));
        Assert.Equal("AiRiskSummaryGenerated", audit.Action);
        Assert.DoesNotContain(target.Customer!.FullName, audit.NewValues!, StringComparison.Ordinal);
        Assert.DoesNotContain(target.Customer.Nic, audit.NewValues!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Makers_cannot_request_an_ai_summary()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var id = await host.QueryAsync(db => db.LoanApplications.Select(a => a.Id).FirstAsync());
        await host.SignInAsync("maker1@demo.lk");

        await Assert.ThrowsAsync<ApproveFlow.Application.Common.Exceptions.ForbiddenAccessException>(() =>
            host.WithScopeAsync(sp => sp.GetRequiredService<IRiskSummaryAppService>().GenerateAsync(id)));
    }

    [Fact]
    public async Task Llm_request_uses_structured_output_and_parses_the_answer()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5",
             "content":[{"type":"thinking","thinking":"","signature":"sig"},
                        {"type":"text","text":"{\"riskLevel\":\"Moderate\",\"summary\":\"Within policy but above comfort level.\",\"keyFactors\":[\"DBR 45%\",\"Loan is 2.5x income\"]}"}],
             "stop_reason":"end_turn","usage":{"input_tokens":120,"output_tokens":60}}
            """);
        var service = CreateService(handler);

        var result = await service.SummariseAsync(Input());

        Assert.False(result.IsFallback);
        Assert.Equal(RiskLevel.Moderate, result.RiskLevel);
        Assert.Equal(2, result.Factors.Count);
        Assert.Equal("claude-opus-5-5", result.Model);

        var request = handler.Request!;
        Assert.Equal("test-key", request.Headers.GetValues("x-api-key").Single());
        Assert.Equal(LlmRiskSummaryService.AnthropicVersion, request.Headers.GetValues("anthropic-version").Single());
        Assert.Equal(LlmRiskSummaryService.FallbackBeta, request.Headers.GetValues("anthropic-beta").Single());

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("claude-opus-5-5", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_schema", body.RootElement.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString());
        Assert.Equal("default", body.RootElement.GetProperty("fallbacks").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"model":"claude-opus-5-5","content":[],"stop_reason":"refusal","stop_details":{"type":"refusal","category":null}}""", "declined")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"type":"error","error":{"type":"rate_limit_error","message":"slow down"}}""", "HTTP 429")]
    [InlineData(HttpStatusCode.OK, """{"model":"claude-opus-5-5","content":[{"type":"text","text":"not json"}],"stop_reason":"end_turn"}""", "unavailable")]
    public async Task Llm_failures_fall_back_to_the_rule_based_summary(HttpStatusCode status, string response, string expectedReason)
    {
        var result = await CreateService(new StubHandler(status, response)).SummariseAsync(Input());

        Assert.True(result.IsFallback);
        Assert.Contains(expectedReason, result.FallbackReason, StringComparison.Ordinal);
    }

    private static LlmRiskSummaryService CreateService(StubHandler handler) =>
        new(new HttpClient(handler), Options.Create(new AiRiskSummaryOptions { ApiKey = "test-key" }), TimeProvider.System,
            NullLogger<LlmRiskSummaryService>.Instance);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _response;

        public StubHandler(HttpStatusCode status, string response)
        {
            _status = status;
            _response = response;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_response, Encoding.UTF8, "application/json") };
        }
    }
}
