using ApproveFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApproveFlow.Infrastructure.Persistence.Configurations;

internal static class StampColumns
{
    /// <summary>Common column sizes for <see cref="Domain.Common.AuditableEntity"/> stamps.</summary>
    public static void Configure<T>(EntityTypeBuilder<T> builder) where T : Domain.Common.AuditableEntity
    {
        builder.Property(e => e.CreatedById).HasMaxLength(450).IsRequired();
        builder.Property(e => e.CreatedByName).HasMaxLength(256).IsRequired();
        builder.Property(e => e.UpdatedById).HasMaxLength(450);
        builder.Property(e => e.UpdatedByName).HasMaxLength(256);
    }
}

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");
        builder.Property(b => b.Code).HasMaxLength(10).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(100).IsRequired();
        builder.Property(b => b.City).HasMaxLength(100).IsRequired();
        builder.HasIndex(b => b.Code).IsUnique();
        StampColumns.Configure(builder);
    }
}

internal sealed class LoanProductConfiguration : IEntityTypeConfiguration<LoanProduct>
{
    public void Configure(EntityTypeBuilder<LoanProduct> builder)
    {
        builder.ToTable("LoanProducts", t =>
        {
            t.HasCheckConstraint("CK_LoanProducts_AmountRange", "[MinAmount] > 0 AND [MaxAmount] >= [MinAmount]");
            t.HasCheckConstraint("CK_LoanProducts_TenureRange", "[MinTenureMonths] >= 1 AND [MaxTenureMonths] <= 360 AND [MaxTenureMonths] >= [MinTenureMonths]");
            t.HasCheckConstraint("CK_LoanProducts_InterestRate", "[DefaultInterestRate] >= 0 AND [DefaultInterestRate] <= 100");
        });
        builder.Property(p => p.Code).HasMaxLength(10).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.DefaultInterestRate).HasPrecision(5, 2);
        builder.HasIndex(p => p.Code).IsUnique();
        StampColumns.Configure(builder);
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.Property(c => c.CustomerNumber).HasMaxLength(20).IsRequired();
        builder.Property(c => c.FullName).HasMaxLength(150).IsRequired();
        builder.Property(c => c.Nic).HasMaxLength(12).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.Address).HasMaxLength(300);

        builder.HasIndex(c => c.CustomerNumber).IsUnique();
        builder.HasIndex(c => c.Nic).IsUnique();
        builder.HasIndex(c => c.FullName);

        builder.HasOne(c => c.Branch).WithMany().HasForeignKey(c => c.BranchId).OnDelete(DeleteBehavior.Restrict);
        StampColumns.Configure(builder);
    }
}
