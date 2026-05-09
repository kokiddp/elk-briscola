using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Briscola.Infrastructure.Persistence.Configurations;

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessageEntity>
{
    public void Configure(EntityTypeBuilder<ChatMessageEntity> builder)
    {
        builder.ToTable("ChatMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Scope).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(m => m.Text).HasMaxLength(500).IsRequired();

        builder.HasIndex(m => new { m.GameId, m.CreatedAt });
    }
}
