using ApproveFlow.Domain.Common;

namespace ApproveFlow.Domain.Entities;

public class Branch : AuditableEntity
{
    private Branch()
    {
    }

    public Branch(string code, string name, string city)
    {
        Update(code, name, city, isActive: true);
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    public void Update(string code, string name, string city, bool isActive)
    {
        Code = Guard.Required(code, nameof(code)).ToUpperInvariant();
        Name = Guard.Required(name, nameof(name));
        City = Guard.Required(city, nameof(city));
        IsActive = isActive;
    }
}
