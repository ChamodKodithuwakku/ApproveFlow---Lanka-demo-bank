using ApproveFlow.Application.Common.Models;
using ApproveFlow.Application.LoanApplications;
using ApproveFlow.Domain.Entities;

namespace ApproveFlow.Application.Abstractions.Persistence;

/// <summary>Commits the changes tracked by the repositories in the current scope as one unit.</summary>
public interface IUnitOfWork
{
    /// <exception cref="Common.Exceptions.ConcurrencyConflictException">The row was changed by someone else.</exception>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ILoanApplicationRepository
{
    /// <summary>Loads a tracked application for a state change or edit.</summary>
    Task<LoanApplication?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Loads an untracked application with customer, product, branch and approval history.</summary>
    Task<LoanApplication?> GetDetailsAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalAction>> GetHistoryAsync(int id, CancellationToken cancellationToken = default);

    Task<PagedResult<LoanApplicationListItemDto>> SearchAsync(LoanApplicationQuery query, CancellationToken cancellationToken = default);

    void Add(LoanApplication application);

    /// <summary>
    /// Sets the RowVersion the client last saw as the original value, so the UPDATE's WHERE clause fails
    /// (DbUpdateConcurrencyException) if anyone changed the row in between.
    /// </summary>
    void SetExpectedRowVersion(LoanApplication application, byte[] rowVersion);
}

public interface IReferenceDataRepository
{
    Task<IReadOnlyList<Branch>> GetBranchesAsync(bool activeOnly, CancellationToken cancellationToken = default);
    Task<Branch?> GetBranchAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> BranchCodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken = default);
    void Add(Branch branch);

    Task<IReadOnlyList<LoanProduct>> GetProductsAsync(bool activeOnly, CancellationToken cancellationToken = default);
    Task<LoanProduct?> GetProductAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ProductCodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken = default);
    void Add(LoanProduct product);
}

public interface ICustomerRepository
{
    Task<Customer?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<PagedResult<Customer>> SearchAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Customer>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<bool> NicExistsAsync(string nic, int? excludeId, CancellationToken cancellationToken = default);
    Task<string> NextCustomerNumberAsync(CancellationToken cancellationToken = default);
    void Add(Customer customer);
}

/// <summary>Generates application reference numbers in the format LDB-YYYY-000001.</summary>
public interface IReferenceNumberGenerator
{
    Task<string> NextAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
