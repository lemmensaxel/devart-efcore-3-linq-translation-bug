using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Devart.Data.Oracle;
using HibernatingRhinos.Profiler.Appender.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace devart_efcore_value_conversion_bug_repro
{

    public class Program
    {
        static async Task Main(string[] args)
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

                using (var scope = new TransactionScope(
                    TransactionScopeOption.Required,
                    new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
                    TransactionScopeAsyncFlowOption.Enabled))
                {
                    using (var context = new EntityContext())
                    {
                        context.Database.EnsureDeleted();
                        context.Database.ExecuteSqlCommand(@"
CREATE TABLE RIDER
(
    ID          NUMBER (19, 0) GENERATED ALWAYS AS IDENTITY NOT NULL,
    MOUNT       VARCHAR2 (100 CHAR)
)");

                        var rider = new Rider(EquineBeast.Mule);
                        context.Add(rider);
                        await context.SaveChangesAsync();
                    }

                    scope.Complete();
                }

                using (var context = new EntityContext())
                {
                    // This works
                    var unicornRider = context.Set<Rider>()
                        .FirstOrDefault(_ => _.Mount.Value == EquineBeast.Unicorn);

                    // This works
                    var beastsWithHorns = new EquineBeast?[] { EquineBeast.Unicorn };
                    var hornRider = context.Set<Rider>()
                        .FirstOrDefault(_ => beastsWithHorns.Contains(_.Mount));

                    // This doesn't - throws ORA-01722: invalid number exception
                    var beastsWithoutHorns = new[] { EquineBeast.Donkey, EquineBeast.Horse, EquineBeast.Mule };
                    var noHornRider = context.Set<Rider>()
                        .FirstOrDefault(_ => beastsWithoutHorns.Contains(_.Mount.Value));
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
            modelBuilder.Entity<Rider>().Property(_ => _.Mount).HasConversion<string>();
            modelBuilder.Entity<Rider>().Property(_ => _.Mount).HasColumnName("MOUNT");
        }
    }

    public class Rider
    {
        public int Id { get; private set; }
        public EquineBeast? Mount { get; private set; }

        private Rider()
        {
            // Required by EF Core
        }

        public Rider(EquineBeast? mount)
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
