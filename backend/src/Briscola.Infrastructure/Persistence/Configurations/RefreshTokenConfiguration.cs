using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Briscola.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshTokenEntity>
{
    public void Configure(EntityTypeBuilder<RefreshTokenEntity> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId);

        // Concurrency guard on the revocation transition. Two concurrent
        // RotateAsync calls against the same row would both load RevokedAt
        // = null and both write RevokedAt = now without this — and both
        // commit, producing two valid child tokens off one parent. With
        // IsConcurrencyToken(), EF emits `WHERE RevokedAt = @originalNull`
        // on the UPDATE, the loser raises DbUpdateConcurrencyException,
        // and the in-flight INSERT of the new token rolls back with it.
        builder.Property(t => t.RevokedAt).IsConcurrencyToken();
    }
}
