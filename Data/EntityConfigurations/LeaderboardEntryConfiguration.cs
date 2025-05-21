using IqTest_server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IqTest_server.Data.EntityConfigurations
{
    public class LeaderboardEntryConfiguration : IEntityTypeConfiguration<LeaderboardEntry>
    {
        public void Configure(EntityTypeBuilder<LeaderboardEntry> builder)
        {
            builder.HasKey(l => l.Id);

            builder.HasOne(l => l.User)
                .WithMany(u => u.LeaderboardEntries)
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(l => l.TestType)
                .WithMany(tt => tt.LeaderboardEntries)
                .HasForeignKey(l => l.TestTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Create unique constraint for user and test type combination
            builder.HasIndex(l => new { l.UserId, l.TestTypeId })
                .IsUnique();
        }
    }
}