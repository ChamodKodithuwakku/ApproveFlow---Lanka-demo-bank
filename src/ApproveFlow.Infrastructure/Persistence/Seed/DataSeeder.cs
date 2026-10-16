using System.Globalization;
using ApproveFlow.Application.Abstractions.Persistence;
using ApproveFlow.Application.Common.Security;
using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Entities;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApproveFlow.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds a realistic, fully fictional data set for demonstrations. Applications are driven through the real domain
/// workflow (submit, approve, reject, return) with back-dated timestamps, so every record has a consistent approval
/// history. Deterministic (fixed random seed) so screenshots and UAT scripts are repeatable.
/// </summary>
public sealed class DataSeeder
{
    public static readonly IReadOnlyList<DemoUser> DemoUsers = new[]
    {
        new DemoUser("admin@demo.lk", "Dilini Jayawardena", AppRoles.Admin, "COL", null),
        new DemoUser("maker1@demo.lk", "Kasun Fernando", AppRoles.Maker, "COL", null),
        new DemoUser("maker2@demo.lk", "Tharushi Wickramasinghe", AppRoles.Maker, "KDY", null),
        new DemoUser("checker1@demo.lk", "Ruwan Senanayake", AppRoles.Checker, "COL", 2_000_000m),
        new DemoUser("checker2@demo.lk", "Shanika Gunasekara", AppRoles.Checker, "GLE", 10_000_000m),
        new DemoUser("auditor@demo.lk", "Mohamed Rizwan", AppRoles.Auditor, "COL", null),
    };

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IReferenceNumberGenerator _references;
    private readonly TimeProvider _clock;
    private readonly SeedOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(ApplicationDbContext db, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager,
        IReferenceNumberGenerator references, TimeProvider clock, IOptions<SeedOptions> options, IHostEnvironment environment,
        ILogger<DataSeeder> logger)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _references = references;
        _clock = clock;
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Roles are reference data required in every environment.
        foreach (var role in AppRoles.All)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                EnsureSucceeded(await _roleManager.CreateAsync(new IdentityRole(role)), $"create role {role}");
            }
        }

        await BootstrapFirstAdminAsync(cancellationToken);

        if (!_options.Enabled)
        {
            return;
        }

        if (_environment.IsProduction())
        {
            _logger.LogWarning("Seed:Enabled is ignored in Production; demo data is never seeded there");
            return;
        }

        if (await _db.Branches.AnyAsync(cancellationToken))
        {
            _logger.LogInformation("Database already contains data; demo seed skipped");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.DemoPassword))
        {
            throw new InvalidOperationException(
                "Seed:DemoPassword is not configured. Run scripts/setup-dev.ps1, or " +
                "'dotnet user-secrets set \"Seed:DemoPassword\" \"<password>\" --project src/ApproveFlow.Web'.");
        }

        _logger.LogInformation("Seeding demo data (branches, products, users, customers, applications)");
        var now = _clock.GetUtcNow().UtcDateTime;

        var branches = SeedBranches();
        var products = SeedProducts();
        await _db.SaveChangesAsync(cancellationToken);

        var users = await SeedUsersAsync(branches, now);
        var customers = SeedCustomers(branches);
        await _db.SaveChangesAsync(cancellationToken);

        await SeedApplicationsAsync(customers, branches, products, users, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Demo data seeded");
    }

    /// <summary>
    /// Production bootstrap: when Bootstrap:AdminEmail and Bootstrap:AdminPassword are supplied (environment variables)
    /// and no administrator exists yet, creates the first Admin account so the bank can sign in and create real users.
    /// Remove the settings after first sign-in; the password must be changed immediately.
    /// </summary>
    private async Task BootstrapFirstAdminAsync(CancellationToken cancellationToken)
    {
        var email = _options.BootstrapAdminEmail;
        var password = _options.BootstrapAdminPassword;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)
            || (await _userManager.GetUsersInRoleAsync(AppRoles.Admin)).Count > 0)
        {
            return;
        }

        var branch = await _db.Branches.OrderBy(b => b.Id).FirstOrDefaultAsync(cancellationToken);
        if (branch is null)
        {
            branch = new Branch("HO", "Head Office", "Colombo");
            _db.Branches.Add(branch);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = "System Administrator",
            BranchId = branch.Id,
            IsActive = true,
            CreatedAtUtc = _clock.GetUtcNow().UtcDateTime,
        };
        EnsureSucceeded(await _userManager.CreateAsync(admin, password), "create the bootstrap administrator");
        EnsureSucceeded(await _userManager.AddToRoleAsync(admin, AppRoles.Admin), "assign the Admin role");
        _logger.LogWarning("Bootstrap administrator {Email} created. Remove the Bootstrap settings and change the password now", email);
    }

    private Dictionary<string, Branch> SeedBranches()
    {
        var branches = new[]
        {
            new Branch("COL", "Colombo Main Branch", "Colombo"),
            new Branch("KDY", "Kandy City Branch", "Kandy"),
            new Branch("GLE", "Galle Fort Branch", "Galle"),
        };
        _db.Branches.AddRange(branches);
        return branches.ToDictionary(b => b.Code, StringComparer.Ordinal);
    }

    private Dictionary<string, LoanProduct> SeedProducts()
    {
        var products = new[]
        {
            new LoanProduct("PL", "Personal Loan", "Unsecured personal financing for salaried customers.", 50_000m, 5_000_000m, 6, 84, 14.50m),
            new LoanProduct("HL", "Housing Loan", "Purchase, construction or renovation of residential property.", 1_000_000m, 50_000_000m, 12, 300, 11.50m),
            new LoanProduct("VL", "Vehicle Loan", "Financing for registered and brand-new vehicles.", 300_000m, 15_000_000m, 12, 84, 13.00m),
            new LoanProduct("SME", "SME Business Loan", "Working capital and capital expenditure for small and medium enterprises.", 250_000m, 25_000_000m, 6, 120, 12.75m),
        };
        _db.LoanProducts.AddRange(products);
        return products.ToDictionary(p => p.Code, StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, Actor>> SeedUsersAsync(Dictionary<string, Branch> branches, DateTime now)
    {
        var actors = new Dictionary<string, Actor>(StringComparer.Ordinal);
        foreach (var demo in DemoUsers)
        {
            var user = await _userManager.FindByEmailAsync(demo.Email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = demo.Email,
                    Email = demo.Email,
                    EmailConfirmed = true,
                    FullName = demo.FullName,
                    BranchId = branches[demo.BranchCode].Id,
                    ApprovalLimit = demo.ApprovalLimit,
                    IsActive = true,
                    CreatedAtUtc = now,
                };
                EnsureSucceeded(await _userManager.CreateAsync(user, _options.DemoPassword!), $"create user {demo.Email}");
                EnsureSucceeded(await _userManager.AddToRoleAsync(user, demo.Role), $"assign role to {demo.Email}");
            }

            actors[demo.Email] = new Actor(user.Id, user.FullName);
        }

        return actors;
    }

    private List<Customer> SeedCustomers(Dictionary<string, Branch> branches)
    {
        string[] firstNames =
        {
            "Amal", "Nadeesha", "Chaminda", "Ishara", "Sanjeewa", "Dinithi", "Pradeep", "Hiruni", "Mahesh", "Sachini",
            "Ajith", "Kavindi", "Roshan", "Nethmi", "Fazal", "Ayesha", "Suresh", "Priyanka", "Lahiru", "Thilini",
            "Nuwan", "Malsha", "Rajiv", "Anjali", "Gayan", "Imesha", "Arjun", "Shehani", "Isuru", "Dulani",
        };
        string[] lastNames =
        {
            "Perera", "Silva", "Bandara", "Herath", "Rajapaksha", "Dissanayake", "Ranasinghe", "Abeysekara", "Kumara", "Weerasinghe",
            "Ekanayake", "Samarasinghe", "Liyanage", "Hameed", "Nazeer", "Karunaratne", "Wijesinghe", "Pathirana", "Gamage", "Jayasuriya",
            "Wickremaratne", "Fonseka", "Rathnayake", "Selvaraj", "Kumarasamy", "Munasinghe", "Hettiarachchi", "Aluthge", "Peiris", "de Alwis",
        };
        string[] streets = { "Galle Road", "Kandy Road", "Temple Road", "Station Road", "Lake Drive", "Hill Street", "Church Lane", "Main Street" };
        var branchList = branches.Values.ToArray();
        var random = new Random(30);
        var customers = new List<Customer>();

        for (var i = 0; i < 30; i++)
        {
            var branch = branchList[i % branchList.Length];
            var birthYear = 1965 + random.Next(0, 36);
            var dayOfYear = random.Next(1, 366) + (i % 2 == 1 ? 500 : 0);
            var nic = string.Create(CultureInfo.InvariantCulture, $"{birthYear}{dayOfYear:D3}{random.Next(1000, 9999):D4}{random.Next(0, 10)}");
            var firstName = firstNames[i];
            var lastName = lastNames[(i * 7) % lastNames.Length];
            var customer = new Customer(
                string.Create(CultureInfo.InvariantCulture, $"CIF{i + 1:D6}"),
                $"{firstName} {lastName}",
                nic,
                branch.Id,
                new DateOnly(birthYear, 1, 1).AddDays(random.Next(0, 364)),
                string.Create(CultureInfo.InvariantCulture, $"0700000{i + 101:D3}"),
                $"{firstName}.{lastName.Replace(" ", string.Empty, StringComparison.Ordinal)}@example.com".ToLowerInvariant(),
                string.Create(CultureInfo.InvariantCulture, $"No. {random.Next(1, 250)}, {streets[i % streets.Length]}, {branch.City}"));
            customers.Add(customer);
        }

        _db.Customers.AddRange(customers);
        return customers;
    }

    private async Task SeedApplicationsAsync(List<Customer> customers, Dictionary<string, Branch> branches,
        Dictionary<string, LoanProduct> products, Dictionary<string, Actor> users, DateTime now, CancellationToken cancellationToken)
    {
        var random = new Random(20261014);
        var branchById = branches.Values.ToDictionary(b => b.Id);
        var maker1 = users["maker1@demo.lk"];
        var maker2 = users["maker2@demo.lk"];
        var checker1 = users["checker1@demo.lk"];
        var checker2 = users["checker2@demo.lk"];
        const decimal checker1Limit = 2_000_000m;
        const decimal checker2Limit = 10_000_000m;

        // Target distribution: 60 applications across every status.
        var plan = new List<SeedPlan>();
        void AddPlans(SeedOutcome outcome, int count)
        {
            for (var i = 0; i < count; i++)
            {
                plan.Add(new SeedPlan(outcome, i));
            }
        }

        AddPlans(SeedOutcome.Draft, 9);
        AddPlans(SeedOutcome.Submitted, 12);
        AddPlans(SeedOutcome.PendingSecondApproval, 5);
        AddPlans(SeedOutcome.Approved, 15);
        AddPlans(SeedOutcome.ApprovedAfterReturn, 3);
        AddPlans(SeedOutcome.Rejected, 8);
        AddPlans(SeedOutcome.Returned, 8);

        // Submitted items get spread ages so the 0-2 / 3-5 / 6+ day ageing buckets are all populated.
        int[] submittedAgeDays = { 0, 0, 1, 2, 2, 3, 4, 5, 6, 8, 11, 15 };
        int[] pendingAgeDays = { 1, 3, 4, 7, 9 };

        var drafts = new List<(SeedPlan Plan, DateTime CreatedAt)>();
        foreach (var item in plan)
        {
            DateTime createdAt = item.Outcome switch
            {
                SeedOutcome.Draft => now.AddDays(-random.Next(0, 10)).AddHours(-random.Next(1, 8)),
                SeedOutcome.Submitted => now.AddDays(-submittedAgeDays[item.Index]).AddHours(-random.Next(3, 20)),
                SeedOutcome.PendingSecondApproval => now.AddDays(-pendingAgeDays[item.Index]).AddHours(-random.Next(12, 30)),
                _ => now.AddDays(-random.Next(6, 45)).AddHours(-random.Next(1, 20)),
            };
            drafts.Add((item, createdAt));
        }

        foreach (var (item, createdAt) in drafts.OrderBy(d => d.CreatedAt))
        {
            var customer = customers[random.Next(customers.Count)];
            var branch = branchById[customer.BranchId];
            var maker = random.Next(2) == 0 ? maker1 : maker2;
            var (product, amount, tenure) = PickProductAndAmount(item.Outcome, item.Index, products, random);
            var terms = BuildTerms(product, amount, tenure, random);

            var reference = await _references.NextAsync(createdAt, cancellationToken);
            var application = LoanApplication.CreateDraft(reference, customer, product, branch, terms, maker, createdAt);

            var submittedAt = Clamp(createdAt.AddHours(random.Next(1, 6)), now);
            var decidedAt = Clamp(submittedAt.AddHours(random.Next(10, 96)), now);

            switch (item.Outcome)
            {
                case SeedOutcome.Draft:
                    break;

                case SeedOutcome.Submitted:
                    application.Submit(maker, submittedAt);
                    break;

                case SeedOutcome.PendingSecondApproval:
                    application.Submit(maker, submittedAt);
                    application.Approve(checker1, checker1Limit, "Repayment capacity verified; recommended for approval.", decidedAt);
                    break;

                case SeedOutcome.Approved:
                    application.Submit(maker, submittedAt);
                    ApproveFully(application, preferDual: item.Index % 3 == 0, decidedAt);
                    break;

                case SeedOutcome.ApprovedAfterReturn:
                    application.Submit(maker, submittedAt);
                    application.ReturnForCorrection(checker2, ReturnRemarks[item.Index % ReturnRemarks.Length], decidedAt);
                    var resubmittedAt = Clamp(decidedAt.AddHours(random.Next(4, 30)), now);
                    application.Submit(maker, resubmittedAt);
                    ApproveFully(application, preferDual: false, Clamp(resubmittedAt.AddHours(random.Next(3, 40)), now));
                    break;

                case SeedOutcome.Rejected:
                    application.Submit(maker, submittedAt);
                    application.Reject(item.Index % 2 == 0 ? checker1 : checker2, RejectRemarks[item.Index % RejectRemarks.Length], decidedAt);
                    break;

                case SeedOutcome.Returned:
                    application.Submit(maker, submittedAt);
                    application.ReturnForCorrection(item.Index % 2 == 0 ? checker2 : checker1, ReturnRemarks[item.Index % ReturnRemarks.Length], decidedAt);
                    break;
            }

            _db.LoanApplications.Add(application);
        }

        // Drives a submitted application to Approved, using dual approval whenever the amount requires it.
        void ApproveFully(LoanApplication application, bool preferDual, DateTime decidedAt)
        {
            var amount = application.Amount;
            if (amount <= checker1Limit && !preferDual)
            {
                application.Approve(checker1, checker1Limit, "Documents complete and policy criteria met.", decidedAt);
            }
            else if (amount > checker2Limit || (amount > checker1Limit && preferDual))
            {
                application.Approve(checker1, checker1Limit, "Within policy; forwarding for second approval.", decidedAt);
                application.Approve(checker2, checker2Limit, "Second approval granted.", Clamp(decidedAt.AddHours(random.Next(2, 30)), now));
            }
            else
            {
                application.Approve(checker2, checker2Limit, "Approved. Security documentation verified.", decidedAt);
            }
        }
    }

    private static (LoanProduct Product, decimal Amount, int Tenure) PickProductAndAmount(SeedOutcome outcome, int index,
        Dictionary<string, LoanProduct> products, Random random)
    {
        decimal Round(decimal value) => Math.Round(value / 10_000m, MidpointRounding.AwayFromZero) * 10_000m;

        // Pending second approval must exceed checker1's LKR 2,000,000 limit; a few submitted items do too, so the
        // dual-approval path can be demonstrated live.
        var mustExceedLimit = outcome == SeedOutcome.PendingSecondApproval || (outcome == SeedOutcome.Submitted && index % 3 == 0);
        if (mustExceedLimit)
        {
            var code = index % 2 == 0 ? "HL" : "SME";
            return (products[code], Round(2_500_000m + random.Next(0, 650) * 10_000m), code == "HL" ? 180 : 60);
        }

        return (index % 4) switch
        {
            0 => (products["PL"], Round(150_000m + random.Next(0, 185) * 10_000m), new[] { 24, 36, 48, 60 }[random.Next(4)]),
            1 => (products["VL"], Round(900_000m + random.Next(0, 500) * 10_000m), new[] { 36, 48, 60, 72 }[random.Next(4)]),
            2 => (products["HL"], Round(2_000_000m + random.Next(0, 1100) * 10_000m), new[] { 120, 180, 240 }[random.Next(3)]),
            _ => (products["SME"], Round(500_000m + random.Next(0, 800) * 10_000m), new[] { 24, 36, 60 }[random.Next(3)]),
        };
    }

    private static LoanTerms BuildTerms(LoanProduct product, decimal amount, int tenure, Random random)
    {
        var rate = product.DefaultInterestRate + new[] { 0m, 0.25m, 0.50m, -0.25m }[random.Next(4)];
        var installment = Domain.Rules.LoanCalculator.MonthlyInstallment(amount, rate, tenure);

        // Choose a target debt-burden ratio between 20% and 74% and derive income and existing obligations from it.
        var targetDbr = random.Next(20, 75) / 100m;
        var obligationShare = random.Next(0, (int)(targetDbr * 40)) / 100m;
        var income = Math.Ceiling(installment / (targetDbr - obligationShare) / 1_000m) * 1_000m;
        var obligations = Math.Round(income * obligationShare / 500m, MidpointRounding.AwayFromZero) * 500m;

        return new LoanTerms(amount, tenure, rate, Purposes[product.Code][random.Next(Purposes[product.Code].Length)], income, obligations);
    }

    private static DateTime Clamp(DateTime value, DateTime now) => value < now.AddMinutes(-5) ? value : now.AddMinutes(-5);

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Seeding failed to {operation}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }

    private static readonly Dictionary<string, string[]> Purposes = new(StringComparer.Ordinal)
    {
        ["PL"] = new[]
        {
            "Higher education expenses for a postgraduate degree",
            "Medical expenses for a planned surgical procedure",
            "Consolidation of existing credit card balances",
            "Wedding and household setup expenses",
        },
        ["HL"] = new[]
        {
            "Construction of a two-storey family house",
            "Purchase of a completed apartment unit",
            "Renovation of existing house including roof replacement",
            "Purchase of a residential land plot with a single-storey house",
        },
        ["VL"] = new[]
        {
            "Purchase of a registered used car for daily commuting",
            "Purchase of a brand-new hybrid car",
            "Purchase of a three-wheeler for a hiring business",
            "Purchase of a small lorry for vegetable transport",
        },
        ["SME"] = new[]
        {
            "Working capital for expanding a retail grocery business",
            "Purchase of industrial sewing machines for a garment workshop",
            "Cold-room installation for a seafood distribution business",
            "Expansion of a bakery including a new deck oven",
        },
    };

    private static readonly string[] RejectRemarks =
    {
        "Debt-burden ratio exceeds the bank's policy threshold of 60 percent.",
        "Repayment capacity not established; income documents are insufficient.",
        "Credit bureau report shows recent arrears on existing facilities.",
        "Requested tenure exceeds the applicant's remaining years of service.",
    };

    private static readonly string[] ReturnRemarks =
    {
        "Please attach the latest three months' salary slips and resubmit.",
        "Valuation report for the property is missing; upload and resubmit.",
        "Purpose is too vague; specify the asset and attach the supplier quotation.",
        "Existing obligations do not match the credit bureau report; please verify.",
    };

    public sealed record DemoUser(string Email, string FullName, string Role, string BranchCode, decimal? ApprovalLimit);

    private sealed record SeedPlan(SeedOutcome Outcome, int Index);

    private enum SeedOutcome
    {
        Draft,
        Submitted,
        PendingSecondApproval,
        Approved,
        ApprovedAfterReturn,
        Rejected,
        Returned,
    }
}
