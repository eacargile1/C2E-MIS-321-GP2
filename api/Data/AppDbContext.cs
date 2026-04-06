using C2E.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<TimesheetLine> TimesheetLines => Set<TimesheetLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.PasswordHash).HasMaxLength(500);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<TimesheetLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.WorkDate });
            e.HasIndex(x => new { x.UserId, x.WorkDate, x.Client, x.Project, x.Task }).IsUnique();

            e.Property(x => x.Client).HasMaxLength(120);
            e.Property(x => x.Project).HasMaxLength(120);
            e.Property(x => x.Task).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(1000);
        });
    }
}
