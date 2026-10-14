using System.Globalization;
using ApproveFlow.Application.Abstractions.Persistence;
using ApproveFlow.Application.Common.Models;
using ApproveFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApproveFlow.Infrastructure.Persistence.Repositories;

internal sealed class ReferenceDataRepository : IReferenceDataRepository
{
    private readonly ApplicationDbContext _db;

    public ReferenceDataRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Branch>> GetBranchesAsync(bool activeOnly, CancellationToken cancellationToken = default) =>
        await _db.Branches.AsNoTracking().Where(b => !activeOnly || b.IsActive).OrderBy(b => b.Name).ToListAsync(cancellationToken);

    public Task<Branch?> GetBranchAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public Task<bool> BranchCodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken = default)
    {
        var normalised = code.Trim().ToUpperInvariant();
        return _db.Branches.AnyAsync(b => b.Code == normalised && (excludeId == null || b.Id != excludeId), cancellationToken);
    }

    public void Add(Branch branch) => _db.Branches.Add(branch);

    public async Task<IReadOnlyList<LoanProduct>> GetProductsAsync(bool activeOnly, CancellationToken cancellationToken = default) =>
        await _db.LoanProducts.AsNoTracking().Where(p => !activeOnly || p.IsActive).OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public Task<LoanProduct?> GetProductAsync(int id, CancellationToken cancellationToken = default) =>
        _db.LoanProducts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> ProductCodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken = default)
    {
        var normalised = code.Trim().ToUpperInvariant();
        return _db.LoanProducts.AnyAsync(p => p.Code == normalised && (excludeId == null || p.Id != excludeId), cancellationToken);
    }

    public void Add(LoanProduct product) => _db.LoanProducts.Add(product);
}

internal sealed class CustomerRepository : ICustomerRepository
{
    private readonly ApplicationDbContext _db;

    public CustomerRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<Customer?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Customers.Include(c => c.Branch).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<PagedResult<Customer>> SearchAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Normalise(page, pageSize);
        var source = _db.Customers.AsNoTracking().Include(c => c.Branch).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            source = source.Where(c => c.FullName.Contains(term) || c.Nic.Contains(term) || c.CustomerNumber.Contains(term));
        }

        var total = await source.CountAsync(cancellationToken);
        var items = await source.OrderBy(c => c.FullName).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<Customer>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<Customer>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        await _db.Customers.AsNoTracking().Include(c => c.Branch).Where(c => c.IsActive).OrderBy(c => c.FullName).ToListAsync(cancellationToken);

    public Task<bool> NicExistsAsync(string nic, int? excludeId, CancellationToken cancellationToken = default)
    {
        var normalised = Customer.NormaliseNic(nic);
        return _db.Customers.AnyAsync(c => c.Nic == normalised && (excludeId == null || c.Id != excludeId), cancellationToken);
    }

    public async Task<string> NextCustomerNumberAsync(CancellationToken cancellationToken = default)
    {
        var numbers = await _db.Customers.Select(c => c.CustomerNumber).ToListAsync(cancellationToken);
        var tracked = _db.ChangeTracker.Entries<Customer>().Select(e => e.Entity.CustomerNumber);
        var max = numbers.Concat(tracked)
            .Select(n => n.Length > 3 && int.TryParse(n.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .DefaultIfEmpty(0)
            .Max();
        return string.Create(CultureInfo.InvariantCulture, $"CIF{max + 1:D6}");
    }

    public void Add(Customer customer) => _db.Customers.Add(customer);
}
