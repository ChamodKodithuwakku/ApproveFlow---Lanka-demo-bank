using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApproveFlow.Application.Common.Models;
using ApproveFlow.Application.LoanApplications;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Infrastructure.Identity;
using ApproveFlow.Infrastructure.Persistence;
using ApproveFlow.Tests.TestInfrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApproveFlow.Tests.Api;

/// <summary>End-to-end tests of the REST API through the real HTTP pipeline (authentication, authorization, filters).</summary>
public sealed class LoanApplicationsApiTests : IClassFixture<ApproveFlowApiFactory>
{
    private readonly ApproveFlowApiFactory _factory;

    public LoanApplicationsApiTests(ApproveFlowApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Api_returns_401_with_problem_details_when_no_token_is_sent()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/loan-applications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_returns_401_for_a_tampered_token()
    {
        using var client = _factory.CreateClient();
        var token = await _factory.GetTokenAsync("checker1@demo.lk");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token[..^4] + "AAAA");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/loan-applications")).StatusCode);
    }

    [Fact]
    public async Task Api_returns_403_when_a_maker_tries_to_approve()
    {
        var submitted = await CreateSubmittedAsync("maker1@demo.lk", 800_000m);
        using var maker2 = await _factory.CreateClientForAsync("maker2@demo.lk");

        var response = await maker2.PostAsJsonAsync($"/api/v1/loan-applications/{submitted.Id}/approve", new { rowVersion = submitted.RowVersion });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Api_returns_403_when_an_auditor_tries_to_create()
    {
        using var auditor = await _factory.CreateClientForAsync("auditor@demo.lk");
        var response = await auditor.PostAsJsonAsync("/api/v1/loan-applications", await NewRequestAsync(500_000m));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Token_endpoint_rejects_wrong_password_with_401()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/token", new { email = "checker2@demo.lk", password = "Wrong-Password#1" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_supports_filtering_paging_and_sorting()
    {
        using var checker = await _factory.CreateClientForAsync("checker2@demo.lk");

        var page = await checker.GetFromJsonAsync<PagedResult<LoanApplicationListItemDto>>(
            "/api/v1/loan-applications?status=Submitted&pageSize=5&sortBy=amount&sortDirection=desc", ApproveFlowApiFactory.Json);

        Assert.NotNull(page);
        Assert.InRange(page.Items.Count, 1, 5);
        Assert.All(page.Items, i => Assert.Equal(LoanStatus.Submitted, i.Status));
        Assert.Equal(page.Items.OrderByDescending(i => i.Amount).Select(i => i.Id), page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Invalid_query_parameters_return_400()
    {
        using var checker = await _factory.CreateClientForAsync("checker2@demo.lk");
        Assert.Equal(HttpStatusCode.BadRequest, (await checker.GetAsync("/api/v1/loan-applications?pageSize=500")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await checker.GetAsync("/api/v1/loan-applications?sortBy=Amount;DROP TABLE")).StatusCode);
    }

    [Fact]
    public async Task Full_workflow_over_the_api_and_approved_record_is_immutable()
    {
        using var maker = await _factory.CreateClientForAsync("maker1@demo.lk");
        var request = await NewRequestAsync(1_500_000m);

        var create = await maker.PostAsJsonAsync("/api/v1/loan-applications", request);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(create.Headers.Location);
        var draft = (await create.Content.ReadFromJsonAsync<LoanApplicationDetailsDto>(ApproveFlowApiFactory.Json))!;
        Assert.Matches(@"^LDB-\d{4}-\d{6}$", draft.ReferenceNumber);

        var submit = await maker.PostAsJsonAsync($"/api/v1/loan-applications/{draft.Id}/submit", new { rowVersion = draft.RowVersion });
        var submitted = (await submit.Content.ReadFromJsonAsync<WorkflowResult>(ApproveFlowApiFactory.Json))!;
        Assert.Equal(LoanStatus.Submitted, submitted.Status);

        using var checker = await _factory.CreateClientForAsync("checker2@demo.lk");
        var approve = await checker.PostAsJsonAsync($"/api/v1/loan-applications/{draft.Id}/approve", new { rowVersion = submitted.RowVersion });
        var approved = (await approve.Content.ReadFromJsonAsync<WorkflowResult>(ApproveFlowApiFactory.Json))!;
        Assert.Equal(LoanStatus.Approved, approved.Status);

        var edit = await maker.PutAsJsonAsync($"/api/v1/loan-applications/{draft.Id}", new UpdateLoanApplicationRequest
        {
            CustomerId = request.CustomerId,
            LoanProductId = request.LoanProductId,
            Amount = 1_600_000m,
            TenureMonths = request.TenureMonths,
            InterestRate = request.InterestRate,
            Purpose = request.Purpose,
            MonthlyIncome = request.MonthlyIncome,
            ExistingObligations = request.ExistingObligations,
            RowVersion = approved.RowVersion,
        });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal("immutable_record", await ProblemCodeAsync(edit));

        var history = await checker.GetFromJsonAsync<List<ApprovalActionDto>>($"/api/v1/loan-applications/{draft.Id}/history", ApproveFlowApiFactory.Json);
        Assert.Equal(new[] { ApprovalActionType.Created, ApprovalActionType.Submitted, ApprovalActionType.Approved }, history!.Select(h => h.ActionType));
    }

    [Fact]
    public async Task Reject_without_remarks_returns_422_with_field_errors()
    {
        var submitted = await CreateSubmittedAsync("maker1@demo.lk", 700_000m);
        using var checker = await _factory.CreateClientForAsync("checker1@demo.lk");

        var response = await checker.PostAsJsonAsync($"/api/v1/loan-applications/{submitted.Id}/reject", new { rowVersion = submitted.RowVersion, remarks = "no" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("Remarks", out _));
    }

    [Fact]
    public async Task Stale_row_version_returns_409_conflict()
    {
        var submitted = await CreateSubmittedAsync("maker2@demo.lk", 700_000m);
        using var checker1 = await _factory.CreateClientForAsync("checker1@demo.lk");
        using var checker2 = await _factory.CreateClientForAsync("checker2@demo.lk");

        var first = await checker2.PostAsJsonAsync($"/api/v1/loan-applications/{submitted.Id}/return",
            new { rowVersion = submitted.RowVersion, remarks = "Please attach the salary slips." });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await checker1.PostAsJsonAsync($"/api/v1/loan-applications/{submitted.Id}/approve", new { rowVersion = submitted.RowVersion });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("concurrency_conflict", await ProblemCodeAsync(second));
    }

    [Fact]
    public async Task Unknown_application_returns_404()
    {
        using var checker = await _factory.CreateClientForAsync("checker1@demo.lk");
        var response = await checker.GetAsync("/api/v1/loan-applications/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Token_is_revoked_when_the_user_is_deactivated()
    {
        using var client = await _factory.CreateClientForAsync("auditor@demo.lk");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/loan-applications?pageSize=1")).StatusCode);

        await _factory.WithServicesAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync("auditor@demo.lk"))!;
            user.IsActive = false;
            await users.UpdateAsync(user);
            await users.UpdateSecurityStampAsync(user);
            return true;
        });

        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/loan-applications?pageSize=1")).StatusCode);
        }
        finally
        {
            await _factory.WithServicesAsync(async sp =>
            {
                var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
                var user = (await users.FindByEmailAsync("auditor@demo.lk"))!;
                user.IsActive = true;
                await users.UpdateAsync(user);
                return true;
            });
        }
    }

    [Fact]
    public async Task Reports_summary_is_available_to_auditors_but_not_makers()
    {
        using var auditor = await _factory.CreateClientForAsync("auditor@demo.lk");
        var response = await auditor.GetAsync("/api/v1/reports/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(report.RootElement.GetProperty("overall").GetProperty("totalApplications").GetInt32() > 0);

        using var maker = await _factory.CreateClientForAsync("maker1@demo.lk");
        Assert.Equal(HttpStatusCode.Forbidden, (await maker.GetAsync("/api/v1/reports/summary")).StatusCode);
    }

    [Fact]
    public async Task Swagger_document_describes_the_versioned_api_with_bearer_security()
    {
        using var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("/api/v1/loan-applications/{id}/approve", json, StringComparison.Ordinal);
        Assert.Contains("/api/v1/auth/token", json, StringComparison.Ordinal);
        Assert.Contains("\"bearer\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/LoanApplications/Details", json, StringComparison.Ordinal);
    }

    private async Task<CreateLoanApplicationRequest> NewRequestAsync(decimal amount) =>
        await _factory.WithServicesAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            return new CreateLoanApplicationRequest
            {
                CustomerId = await db.Customers.OrderBy(c => c.Id).Select(c => c.Id).FirstAsync(),
                LoanProductId = await db.LoanProducts.Where(p => p.Code == "SME").Select(p => p.Id).FirstAsync(),
                Amount = amount,
                TenureMonths = 48,
                InterestRate = 12.75m,
                Purpose = "Working capital for an API integration test",
                MonthlyIncome = 400_000m,
                ExistingObligations = 20_000m,
            };
        });

    private async Task<WorkflowResult> CreateSubmittedAsync(string makerEmail, decimal amount)
    {
        using var maker = await _factory.CreateClientForAsync(makerEmail);
        var created = await maker.PostAsJsonAsync("/api/v1/loan-applications", await NewRequestAsync(amount));
        created.EnsureSuccessStatusCode();
        var draft = (await created.Content.ReadFromJsonAsync<LoanApplicationDetailsDto>(ApproveFlowApiFactory.Json))!;
        var submit = await maker.PostAsJsonAsync($"/api/v1/loan-applications/{draft.Id}/submit", new { rowVersion = draft.RowVersion });
        submit.EnsureSuccessStatusCode();
        return (await submit.Content.ReadFromJsonAsync<WorkflowResult>(ApproveFlowApiFactory.Json))!;
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
