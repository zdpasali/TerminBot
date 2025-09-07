using Microsoft.EntityFrameworkCore;
using TerminBot.Models;

namespace TerminBot.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<ServiceRequest> ServiceRequests { get; set; }

        public DbSet<Appointment> Appointments { get; set; }
        public DbSet<TerminBot.Models.AdminUser> AdminUsers { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AdminUser>()
                .HasIndex(u => u.Username)
                .IsUnique();

        }
    }
}
