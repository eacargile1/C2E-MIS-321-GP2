using C2E.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectTeamMember> ProjectTeamMembers => Set<ProjectTeamMember>();
    public DbSet<TimesheetLine> TimesheetLines => Set<TimesheetLine>();
    public DbSet<TimesheetWeekApproval> TimesheetWeekApprovals => Set<TimesheetWeekApproval>();
    public DbSet<ExpenseEntry> ExpenseEntries => Set<ExpenseEntry>();
    public DbSet<ClientQuote> ClientQuotes => Set<ClientQuote>();

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
            e.HasOne(x => x.DeliveryManager)
                .WithMany()
                .HasForeignKey(x => x.DeliveryManagerUserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.EngagementPartner)
                .WithMany()
                .HasForeignKey(x => x.EngagementPartnerUserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.AssignedFinanceUser)
                .WithMany()
                .HasForeignKey(x => x.AssignedFinanceUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ProjectTeamMember>(e =>
        {
            e.HasKey(x => new { x.ProjectId, x.UserId });
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.Project)
                .WithMany(p => p.TeamMembers)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.ManagerUserId);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.DisplayName).HasMaxLength(80);
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
            e.HasIndex(x => new { x.UserId, x.WorkDate, x.Client, x.Project, x.Task, x.IsDeleted }).IsUnique();

            e.Property(x => x.Client).HasMaxLength(120);
            e.Property(x => x.Project).HasMaxLength(120);
            e.Property(x => x.Task).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(1000);
        });

        modelBuilder.Entity<TimesheetWeekApproval>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.WeekStartMonday }).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ReviewedBy)
                .WithMany()
                .HasForeignKey(x => x.ReviewedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
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
            e.Property(x => x.InvoiceFileName).HasMaxLength(260);
            e.Property(x => x.InvoiceContentType).HasMaxLength(128);
        });

        modelBuilder.Entity<ClientQuote>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ReferenceNumber).IsUnique();
            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.Status);

            e.Property(x => x.ReferenceNumber).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.ScopeSummary).HasMaxLength(2000);
            e.Property(x => x.EstimatedHours).HasPrecision(18, 2);
            e.Property(x => x.HourlyRate).HasPrecision(18, 2);
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            e.HasOne(x => x.Client)
                .WithMany()
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
