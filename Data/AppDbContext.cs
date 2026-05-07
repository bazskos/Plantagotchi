using Microsoft.EntityFrameworkCore;
using Plantagotchi.Models;

namespace Plantagotchi.Data;

public class AppDbContext : DbContext
{
    public DbSet<Plant> Plants { get; set; }
    public DbSet<PlantImage> PlantImages { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=plants.db");
    }
}