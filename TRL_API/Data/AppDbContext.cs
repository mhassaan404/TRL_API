using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using TRL_API.Models;

namespace TRL_API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // Users and RefreshTokens live in each client's database, so the context is created for one client's database
        public static AppDbContext ForDatabase(string connectionString) =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
        public DbSet<UserPermission> UserPermissions { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<UserPermission>().HasKey(p => new { p.UserId, p.Permission });
    }
}