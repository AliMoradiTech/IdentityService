using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace idnetityServiceWedApi.Data;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema("IAM");
        builder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessage");
            entity.HasKey(message => message.Id);
            entity.HasIndex(message => message.EventId).IsUnique();
            entity.Property(message => message.Type).HasMaxLength(256).IsRequired();
            entity.Property(message => message.Payload).IsRequired();
            entity.Property(message => message.TraceParent).HasMaxLength(256);
            entity.Property(message => message.TraceState).HasMaxLength(512);
            entity.Property(message => message.Error).HasMaxLength(2048);
            entity.HasIndex(message => new { message.DispatchedAt, message.DeadLetteredAt, message.LockedUntil, message.CreatedAt })
                .HasDatabaseName("IX_OutboxMessage_Pending");
        });
    }
}
