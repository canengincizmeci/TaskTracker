using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskTracker.Core.Entities.Concrete;

namespace TaskTracker.Core.Entities.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.Property(token => token.TokenDigest).HasMaxLength(64);
        builder.HasIndex(token => token.TokenDigest).IsUnique();
        builder.HasIndex(token => new { token.SessionId, token.IsRevoked });
        builder.Property(token => token.IsRevoked).IsConcurrencyToken();
    }
}
