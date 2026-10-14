using ApproveFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApproveFlow.Infrastructure.Persistence.Configurations;

internal sealed class LoanApplicationConfiguration : IEntityTypeConfiguration<LoanApplication>
{
    public void Configure(EntityTypeBuilder<LoanApplication> builder)
    {
        builder.ToTable("LoanApplications", t =>
        {
            t.HasCheckConstraint("CK_LoanApplications_Amount", "[Amount] > 0");
            t.HasCheckConstraint("CK_LoanApplications_Tenure", "[TenureMonths] BETWEEN 1 AND 360");
            t.HasCheckConstraint("CK_LoanApplications_InterestRate", "[InterestRate] >= 0 AND [InterestRate] <= 100");
            t.HasCheckConstraint("CK_LoanApplications_MonthlyIncome", "[MonthlyIncome] > 0");
            t.HasCheckConstraint("CK_LoanApplications_Obligations", "[ExistingObligations] >= 0");
            t.HasCheckConstraint("CK_LoanApplications_Status",
                "[Status] IN ('Draft','Submitted','PendingSecondApproval','Approved','Rejected','ReturnedForCorrection')");
        });

        builder.Property(a => a.ReferenceNumber).HasMaxLength(30).IsRequired();
        builder.Property(a => a.Purpose).HasMaxLength(500).IsRequired();
        builder.Property(a => a.InterestRate).HasPrecision(5, 2);
        builder.Property(a => a.DebtBurdenRatio).HasPrecision(9, 2);
        builder.Property(a => a.LastEditedById).HasMaxLength(450).IsRequired();
        builder.Property(a => a.LastEditedByName).HasMaxLength(256).IsRequired();
        builder.Property(a => a.SubmittedById).HasMaxLength(450);
        builder.Property(a => a.FirstApproverId).HasMaxLength(450);
        builder.Property(a => a.FirstApproverName).HasMaxLength(256);
        builder.Property(a => a.DecidedById).HasMaxLength(450);
        builder.Property(a => a.DecidedByName).HasMaxLength(256);
        builder.Property(a => a.DeletedById).HasMaxLength(450);
        StampColumns.Configure(builder);

        // Indexes chosen for the main access paths: lookup by reference, work queues by status/date,
        // customer history, maker's own items and the pending-ageing view.
        builder.HasIndex(a => a.ReferenceNumber).IsUnique();
        builder.HasIndex(a => new { a.Status, a.CreatedAtUtc });
        builder.HasIndex(a => new { a.Status, a.SubmittedAtUtc });
        builder.HasIndex(a => a.CustomerId);
        builder.HasIndex(a => a.CreatedById);

        builder.HasOne(a => a.Customer).WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.LoanProduct).WithMany().HasForeignKey(a => a.LoanProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Branch).WithMany().HasForeignKey(a => a.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ImportBatch>().WithMany().HasForeignKey(a => a.ImportBatchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.Actions).WithOne(x => x.LoanApplication).HasForeignKey(x => x.LoanApplicationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(a => a.Actions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(a => a.IsFinal);
        builder.Ignore(a => a.IsPendingDecision);
        builder.Ignore(a => a.IsEditable);

        // Soft-deleted applications never appear in normal queries.
        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}

internal sealed class ApprovalActionConfiguration : IEntityTypeConfiguration<ApprovalAction>
{
    public void Configure(EntityTypeBuilder<ApprovalAction> builder)
    {
        builder.ToTable("ApprovalActions");
        builder.Property(a => a.PerformedById).HasMaxLength(450).IsRequired();
        builder.Property(a => a.PerformedByName).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Remarks).HasMaxLength(1000);

        builder.HasIndex(a => new { a.LoanApplicationId, a.PerformedAtUtc });
        builder.HasIndex(a => new { a.PerformedById, a.PerformedAtUtc });
        builder.HasIndex(a => new { a.ActionType, a.PerformedAtUtc });

        // Matches the parent's soft-delete filter so history of a deleted draft is hidden consistently.
        builder.HasQueryFilter(a => !a.LoanApplication!.IsDeleted);
    }
}
