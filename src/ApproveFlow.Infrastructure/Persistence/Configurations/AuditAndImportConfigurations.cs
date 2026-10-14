using ApproveFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApproveFlow.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        // The append-only trigger (database/triggers) is declared so EF Core avoids OUTPUT clauses that SQL Server
        // disallows on tables with triggers.
        builder.ToTable("AuditLogs", t => t.HasTrigger("trg_AuditLogs_AppendOnly"));
        builder.Property(a => a.TableName).HasMaxLength(128);
        builder.Property(a => a.RecordKey).HasMaxLength(100);
        builder.Property(a => a.Action).HasMaxLength(50).IsRequired();
        builder.Property(a => a.ChangedColumns).HasMaxLength(2000);
        builder.Property(a => a.UserId).HasMaxLength(450);
        builder.Property(a => a.UserName).HasMaxLength(256);
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.CorrelationId).HasMaxLength(100);

        builder.HasIndex(a => a.TimestampUtc);
        builder.HasIndex(a => new { a.TableName, a.RecordKey });
        builder.HasIndex(a => new { a.UserId, a.TimestampUtc });
        builder.HasIndex(a => new { a.EventType, a.Action });
    }
}

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("ImportBatches");
        builder.Property(b => b.FileName).HasMaxLength(260).IsRequired();
        builder.Property(b => b.FileHash).HasMaxLength(64).IsRequired();
        StampColumns.Configure(builder);

        // The same file can only be imported successfully once.
        builder.HasIndex(b => b.FileHash).IsUnique().HasFilter("[Status] = 'Completed'");

        builder.HasMany(b => b.Errors).WithOne(e => e.ImportBatch).HasForeignKey(e => e.ImportBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(b => b.Errors).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(b => b.IsReconciled);
    }
}

internal sealed class ImportErrorConfiguration : IEntityTypeConfiguration<ImportError>
{
    public void Configure(EntityTypeBuilder<ImportError> builder)
    {
        builder.ToTable("ImportErrors");
        builder.Property(e => e.LegacyReference).HasMaxLength(50);
        builder.Property(e => e.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(e => e.RawData).HasMaxLength(2000);
        builder.HasIndex(e => new { e.ImportBatchId, e.RowNumber });
    }
}
