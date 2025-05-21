using IqTest_server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IqTest_server.Data.EntityConfigurations
{
    public class QuestionConfiguration : IEntityTypeConfiguration<Question>
    {
        public void Configure(EntityTypeBuilder<Question> builder)
        {
            builder.HasKey(q => q.Id);

            builder.Property(q => q.Type)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(q => q.Text)
                .IsRequired()
                .HasMaxLength(1000);

            builder.Property(q => q.Category)
                .HasMaxLength(200);

            builder.HasOne(q => q.TestType)
                .WithMany(t => t.Questions)
                .HasForeignKey(q => q.TestTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}