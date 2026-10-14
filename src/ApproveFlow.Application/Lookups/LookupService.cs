using ApproveFlow.Application.Abstractions.Persistence;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Application.LoanApplications;

namespace ApproveFlow.Application.Lookups;

/// <summary>Reference data for drop-downs and filters.</summary>
public interface ILookupService
{
    Task<IReadOnlyList<LookupItem>> GetBranchesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductLookupItem>> GetProductsAsync(bool activeOnly = true, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerLookupItem>> GetCustomersAsync(CancellationToken cancellationToken = default);
}

internal sealed class LookupService : ILookupService
{
    private readonly IReferenceDataRepository _referenceData;
    private readonly ICustomerRepository _customers;
    private readonly ICurrentUser _currentUser;

    public LookupService(IReferenceDataRepository referenceData, ICustomerRepository customers, ICurrentUser currentUser)
    {
        _referenceData = referenceData;
        _customers = customers;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<LookupItem>> GetBranchesAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.RequireActor();
        var branches = await _referenceData.GetBranchesAsync(activeOnly: false, cancellationToken);
        return branches.Select(b => new LookupItem(b.Id, b.Code, b.Name)).ToList();
    }

    public async Task<IReadOnlyList<ProductLookupItem>> GetProductsAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireActor();
        var products = await _referenceData.GetProductsAsync(activeOnly, cancellationToken);
        return products.Select(p => new ProductLookupItem(p.Id, p.Code, p.Name, p.MinAmount, p.MaxAmount, p.MinTenureMonths,
            p.MaxTenureMonths, p.DefaultInterestRate)).ToList();
    }

    public async Task<IReadOnlyList<CustomerLookupItem>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.RequireActor();
        var customers = await _customers.GetActiveAsync(cancellationToken);
        return customers.Select(c => new CustomerLookupItem(c.Id, c.CustomerNumber, c.FullName, c.Nic, c.BranchId, c.Branch?.Name ?? string.Empty))
            .ToList();
    }
}
