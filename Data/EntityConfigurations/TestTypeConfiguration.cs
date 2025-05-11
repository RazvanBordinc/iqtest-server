using IqTest_server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IqTest_server.Data.EntityConfigurations
{
    public class TestTypeConfiguration : IEntityTypeConfiguration<TestType>
    {
        public void Configure(EntityTypeBuilder<TestType> builder)
        {
            builder.HasKey(t => t.Id);

            builder.HasIndex(t => t.TypeId)
                .IsUnique();

            builder.Property(t => t.TypeId)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(t => t.Title)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(t => t.Description)
                .HasMaxLength(500);

            builder.Property(t => t.LongDescription)
                .HasMaxLength(1000);

            builder.Property(t => t.Icon)
                .HasMaxLength(50);

            builder.Property(t => t.Color)
                .HasMaxLength(255);

            builder.Property(t => t.TimeLimit)
                .HasMaxLength(50);

            builder.Property(t => t.Difficulty)
                .HasMaxLength(50);
        }
    }
}