using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Entities.FreightForwarding;

namespace Trackwise.Infrastructure.Persistence.Configurations.FreightForwarding;

public class QuotationEventConfiguration : IEntityTypeConfiguration<QuotationEvent>
{
    public void Configure(EntityTypeBuilder<QuotationEvent> builder)
    {
        builder.ToTable("quotationevents");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(e => e.FromStatus).HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.ToStatus).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(e => e.Actor).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Details).HasMaxLength(2000);

        builder.HasIndex(e => new { e.QuotationId, e.OccurredAt });

        builder.HasOne<Quotation>()
            .WithMany(q => q.Events)
            .HasForeignKey(e => e.QuotationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
