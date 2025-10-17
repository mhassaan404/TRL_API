using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using TRL_API.Models;

namespace TRL_API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
        public DbSet<Dashboard> Dashboard { get; set; } = default!;
        public DbSet<Payments> Payments { get; set; } = default!;
        public DbSet<Tenants> Tenants { get; set; } = default!;
        public DbSet<Buildings> Buildings { get; set; } = default!;
        public DbSet<Floors> Floors { get; set; } = default!;
        public DbSet<Units> Units { get; set; } = default!;
    }
}