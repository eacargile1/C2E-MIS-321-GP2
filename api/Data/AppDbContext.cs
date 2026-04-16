using C2E.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TimesheetLine> TimesheetLines => Set<TimesheetLine>();
    public DbSet<ExpenseEntry> ExpenseEntries => Set<ExpenseEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.IsActive);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.ContactName).HasMaxLength(200);
            e.Property(x => x.ContactEmail).HasMaxLength(320);
            e.Property(x => x.ContactPhone).HasMaxLength(50);
            e.Property(x => x.DefaultBillingRate).HasPrecision(18, 2);
            e.Property(x => x.Notes).HasMaxLength(2000);
        });

        modelBuilder.Entity<Project>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.IsActive);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.BudgetAmount).HasPrecision(18, 2);
            e.HasOne(x => x.Client)
                .WithMany(c => c.Projects)
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.ManagerUserId);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.PasswordHash).HasMaxLength(500);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Manager)
                .WithMany(x => x.DirectReports)
                .HasForeignKey(x => x.ManagerUserId)
                .OnDelete(DeleteBehavior.SetNull);
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

        modelBuilder.Entity<ExpenseEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.ExpenseDate });
            e.HasIndex(x => x.Status);

            e.Property(x => x.Client).HasMaxLength(120);
            e.Property(x => x.Project).HasMaxLength(120);
            e.Property(x => x.Category).HasMaxLength(80);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        });
    }
}
