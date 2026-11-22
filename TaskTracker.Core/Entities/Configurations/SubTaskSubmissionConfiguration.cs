using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

public class SubTaskSubmissionConfiguration : IEntityTypeConfiguration<SubTaskSubmission>
{
    public void Configure(EntityTypeBuilder<SubTaskSubmission> builder)
    {
        builder.ToTable("SubTaskSubmissions", table =>
        {
            table.HasCheckConstraint("CK_SubTaskSubmissions_RevisionNumber", "\"RevisionNumber\" > 0");
            table.HasCheckConstraint("CK_SubTaskSubmissions_Delivery",
                $"(\"ClientRequestId\" IS NULL AND \"DeliveryFingerprint\" IS NULL AND \"DeliveryFileCount\" = 0) OR " +
                $"(\"ClientRequestId\" IS NOT NULL AND \"DeliveryFingerprint\" IS NOT NULL AND length(\"DeliveryFingerprint\") = 64 " +
                $"AND \"DeliveryFileCount\" BETWEEN 0 AND {SubTaskSubmission.MaxDeliveryFileCount})");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.TaskRequestId });
        builder.HasAlternateKey(x => new { x.Id, x.SubTaskId });
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Content).IsRequired().HasMaxLength(SubTaskSubmission.MaxContentLength);
        builder.Property(x => x.DeliveryFingerprint).HasMaxLength(64);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => new { x.SubTaskId, x.RevisionNumber }).IsUnique();
        builder.HasIndex(x => new { x.SubTaskId, x.SubmittedByUserId, x.ClientRequestId }).IsUnique();
        builder.HasOne(x => x.SubTask).WithMany()
            .HasForeignKey(x => new { x.SubTaskId, x.TaskRequestId })
            .HasPrincipalKey(x => new { x.Id, x.TaskRequestId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SubmittedByUser).WithMany().HasForeignKey(x => x.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
