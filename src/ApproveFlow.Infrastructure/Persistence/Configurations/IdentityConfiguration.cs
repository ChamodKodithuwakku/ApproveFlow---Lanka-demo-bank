using ApproveFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace ApproveFlow.Infrastructure.Persistence.Configurations;

internal static class IdentityConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>(user =>
        {
            user.ToTable("AspNetUsers", t => t.HasCheckConstraint("CK_AspNetUsers_ApprovalLimit", "[ApprovalLimit] IS NULL OR [ApprovalLimit] >= 0"));
            user.Property(u => u.FullName).HasMaxLength(150).IsRequired();
            user.HasOne(u => u.Branch).WithMany().HasForeignKey(u => u.BranchId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
