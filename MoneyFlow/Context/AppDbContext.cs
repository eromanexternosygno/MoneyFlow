using Microsoft.EntityFrameworkCore;
using MoneyFlow.Entities;

namespace MoneyFlow.Context;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    //Declare DBSets here
    public DbSet<User> User { get; set; }
    public DbSet<Service> Service { get; set; }
    public DbSet<Transaction> Transaction { get; set; }

    // Define relationships using Fluent API: https://learn.microsoft.com/en-us/ef/core/modeling/relationships
    // Note: This is optional if you follow EF Core conventions
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Define User Entity From: https://learn.microsoft.com/en-us/ef/core/modeling/entity-types
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey("UserId");
            e.Property("UserId").ValueGeneratedOnAdd();

            // Insert Data Seeding From: https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding
            // Seed a default user
            e.HasData(
                new User { UserId = 1 , FullName = "Nestor Silva", Email = "nestor@gmail.com", Password = "password123" }
                );
        });

        // Define Service Entity From: https://learn.microsoft.com/en-us/ef/core/modeling/entity-types
        modelBuilder.Entity<Service>(e => {
            e.HasKey("ServiceId");
            e.Property("ServiceId").ValueGeneratedOnAdd();

            // If Service has relationship with any other entity, define it here From: https://learn.microsoft.com/en-us/ef/core/modeling/relationships
            e.HasOne(e => e.User).WithMany(u => u.Services) // One User has many Services
             .HasForeignKey(e => e.UserId) // Foreign Key Will be UserId in Service Table
             .OnDelete(DeleteBehavior.Restrict); // When User is deleted, restrict deletion if there are related Services
        });

        // Define Transaction Entity From: https://learn.microsoft.com/en-us/ef/core/modeling/entity-types
        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasKey("TransactionId"); // Primary Key
            e.Property("TransactionId").ValueGeneratedOnAdd(); // Auto Increment
            e.Property("Date").HasColumnType("date"); // DateOnly type mapping
            e.Property("TotalAmount").HasColumnType("decimal(10,2)"); // Decimal type mapping

            e.HasOne(e => e.Service).WithMany()
            .HasForeignKey(e => e.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(e =>e.User).WithMany(u => u.Transactions)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
