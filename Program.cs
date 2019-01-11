using System;
using System.IO;
using System.Linq;
using Devart.Data.Oracle;
using HibernatingRhinos.Profiler.Appender.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace devart_efcore_value_conversion_bug_repro
{
    public class Program
    {
        static void Main(string[] args)
        {
            try
            {
                EntityFrameworkProfiler.Initialize();

                var config = Devart.Data.Oracle.Entity.Configuration.OracleEntityProviderConfig.Instance;
                config.CodeFirstOptions.UseNonUnicodeStrings = true;
                config.CodeFirstOptions.UseNonLobStrings = true;

                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.development.json", optional: false, reloadOnChange: true);
                var configuration = builder.Build();
                EntityContext.ConnectionString = ComposeConnectionString(configuration);

                using (var context = new EntityContext())
                {
                    context.Database.EnsureDeleted();
                    context.Database.ExecuteSqlCommand(@"
CREATE TABLE RIDER
(
    ID           NUMBER (19, 0) GENERATED ALWAYS AS IDENTITY NOT NULL,
    MOUNT        VARCHAR2 (100 CHAR) NOT NULL
)
");
                    var entity = new Rider(EquineBeast.Mule);
                    context.Add(entity);
                    context.SaveChanges();
                }

                using (var context = new EntityContext())
                {
                    var parameter = EquineBeast.Mule;
                    var rider = context.Set<Rider>().Where(_ => _.Mount == parameter).FirstOrDefault();
                }

                Console.WriteLine("Finished.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            Console.ReadKey();
        }

        private static string ComposeConnectionString(IConfiguration configuration)
        {
            var builder = new OracleConnectionStringBuilder
            {
                Server = configuration["DatabaseServer"],
                UserId = configuration["UserId"],
                Password = configuration["Password"],
                ServiceName = configuration["ServiceName"],
                Port = int.Parse(configuration["Port"]),
                Direct = true,
                Pooling = true,
                LicenseKey = configuration["DevartLicenseKey"]
            };
            return builder.ToString();
        }
    }

    public class EntityContext : DbContext
    {
        public static string ConnectionString;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => 
            optionsBuilder.UseOracle(ConnectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Rider>().ToTable("RIDER");
            modelBuilder.Entity<Rider>().HasKey(_ => _.Id);
            modelBuilder.Entity<Rider>().Property(_ => _.Id).HasColumnName("ID");
            modelBuilder.Entity<Rider>().Property(e => e.Mount).HasConversion<string>();
            modelBuilder.Entity<Rider>().Property(_ => _.Mount).HasColumnName("MOUNT");
        }
    }

    public class Rider
    {
        public int Id { get; private set; }
        public EquineBeast Mount { get; private set; }

        private Rider()
        {
            // Required by EF Core
        }

        public Rider(EquineBeast mount)
        {
            Mount = mount;
        }
    }

    public enum EquineBeast
    {
        Donkey,
        Mule,
        Horse,
        Unicorn
    }
}
