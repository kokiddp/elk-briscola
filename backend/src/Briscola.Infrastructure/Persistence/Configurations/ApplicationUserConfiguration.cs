using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Briscola.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.DisplayName).HasMaxLength(32).IsRequired();
        builder.Property(u => u.ActiveCardSetId).HasMaxLength(64).HasDefaultValue("placeholder").IsRequired();
        builder.Property(u => u.CreatedAt).IsRequired();
    }
}
