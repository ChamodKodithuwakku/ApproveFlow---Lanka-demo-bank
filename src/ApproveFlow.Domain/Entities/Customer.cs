using System.Text.RegularExpressions;
using ApproveFlow.Domain.Common;

namespace ApproveFlow.Domain.Entities;

public partial class Customer : AuditableEntity
{
    private Customer()
    {
    }

    public Customer(string customerNumber, string fullName, string nic, int branchId,
        DateOnly? dateOfBirth = null, string? phone = null, string? email = null, string? address = null)
    {
        CustomerNumber = Guard.Required(customerNumber, nameof(customerNumber)).ToUpperInvariant();
        IsActive = true;
        Update(fullName, nic, branchId, dateOfBirth, phone, email, address);
    }

    public string CustomerNumber { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;

    /// <summary>Sri Lankan National Identity Card number (old 9 digits + V/X, or new 12 digits).</summary>
    public string Nic { get; private set; } = string.Empty;

    public DateOnly? DateOfBirth { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public int BranchId { get; private set; }
    public Branch? Branch { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string fullName, string nic, int branchId, DateOnly? dateOfBirth, string? phone, string? email, string? address)
    {
        var normalisedNic = NormaliseNic(nic);
        if (!IsValidNic(normalisedNic))
        {
            throw new BusinessRuleViolationException("invalid_nic", "NIC must be 9 digits followed by V/X, or 12 digits.");
        }

        FullName = Guard.Required(fullName, nameof(fullName));
        Nic = normalisedNic;
        BranchId = branchId;
        DateOfBirth = dateOfBirth;
        Phone = Guard.Optional(phone);
        Email = Guard.Optional(email);
        Address = Guard.Optional(address);
    }

    public static string NormaliseNic(string? nic) => (nic ?? string.Empty).Trim().ToUpperInvariant();

    public static bool IsValidNic(string? nic) => !string.IsNullOrEmpty(nic) && NicPattern().IsMatch(nic);

    [GeneratedRegex(@"^(\d{9}[VX]|\d{12})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex NicPattern();
}
