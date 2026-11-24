using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Entities.FreightForwarding;

namespace Trackwise.Infrastructure.Persistence.Configurations.FreightForwarding;

public class BookingEventConfiguration : IEntityTypeConfiguration<BookingEvent>
{
    public void Configure(EntityTypeBuilder<BookingEvent> builder)
    {
        builder.ToTable("bookingevents");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(e => e.FromStatus).HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.ToStatus).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(e => e.Actor).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Details).HasMaxLength(2000);

        builder.HasIndex(e => new { e.BookingId, e.OccurredAt });

        builder.HasOne<Booking>()
            .WithMany(b => b.Events)
            .HasForeignKey(e => e.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
