using System.IO;
using ArbitrageBot.Objects.Database.Objects;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageBot.Objects.Database
{
    public class SqlContext : DbContext
    {
        public DbSet<ArbitrageInfo> ArbitrageInfos { get; set; }
        public DbSet<TradeInfo> TradeInfos { get; set; }

        private const string DbFolder = "Data";
        private const string DbPath = DbFolder + "\\ArbitrageData.sqlite3";

        public SqlContext()
        {
            if (!Directory.Exists(DbFolder))
                Directory.CreateDirectory(DbFolder);

            if (!File.Exists(DbPath))
                Database.EnsureCreated();
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data Source={DbPath};");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ArbitrageInfo>().ToTable("ArbitrageInfos");
            modelBuilder.Entity<TradeInfo>().ToTable("TradeInfos");
        }
    }
}
