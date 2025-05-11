using IqTest_server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IqTest_server.Data.EntityConfigurations
{
    public class TestResultConfiguration : IEntityTypeConfiguration<TestResult>
    {
        public void Configure(EntityTypeBuilder<TestResult> builder)
        {
            builder.HasKey(t => t.Id);

            builder.HasOne(t => t.User)
                .WithMany(u => u.TestResults)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(t => t.TestType)
                .WithMany(tt => tt.TestResults)
                .HasForeignKey(t => t.TestTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}