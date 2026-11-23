using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trackwise.Domain.Entities;

namespace Trackwise.Infrastructure.Persistence.Configurations;

internal sealed class QuotationLinkEntityConfiguration : IEntityTypeConfiguration<QuotationLink>
{
    public void Configure(EntityTypeBuilder<QuotationLink> builder)
    {
        builder.ToTable("quotation_links");

        builder.Property(q => q.Token)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(q => q.Token).IsUnique();
        builder.HasIndex(q => q.QuotationId);
        builder.HasIndex(q => q.ExpiresAt);

        builder.HasOne(q => q.Quotation)
            .WithMany()
            .HasForeignKey(q => q.QuotationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}