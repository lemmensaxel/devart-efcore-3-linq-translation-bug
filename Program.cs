using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Devart.Data.Oracle;
using HibernatingRhinos.Profiler.Appender.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace devart_efcore_3_discriminator_bug
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

                        context.Database.ExecuteSqlRaw(@"
CREATE TABLE BEAST_RIDER
(
    ID          NUMBER (19, 0) GENERATED ALWAYS AS IDENTITY NOT NULL,
    NAME        VARCHAR2 (50 CHAR) NOT NULL,
    DISCRIMINATOR VARCHAR2 (50 CHAR) NOT NULL
)");

                        await context.SaveChangesAsync();
                    }

                    scope.Complete();
                }

                using (var context = new EntityContext())
                {
                    // Both methods of querying derived entities generate
                    // ... WHERE "b".DISCRIMINATOR = TO_NCLOB('BirdRider')
                    // resulting in 'ORA-00932: inconsistent datatypes: expected - got NCLOB'
                    var birdRider = context
                        .Set<BirdRider>()
                        .FirstOrDefault();

                    var beastRider = context
                        .Set<BeastRider>()
                        .FirstOrDefault(_ => _ is BirdRider);
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
            modelBuilder.Entity<BeastRider>().ToTable("BEAST_RIDER");
            modelBuilder.Entity<BeastRider>().HasKey(_ => _.Id);
            modelBuilder.Entity<BeastRider>().Property(_ => _.Id).HasColumnName("ID");
            modelBuilder.Entity<BeastRider>().Property(_ => _.Name).HasColumnName("NAME");
            
            modelBuilder.Entity<BeastRider>()
                .HasDiscriminator<string>("DISCRIMINATOR")
                .HasValue<BeastRider>(nameof(BeastRider))
                .HasValue<BirdRider>(nameof(BirdRider));
            modelBuilder.Entity<BeastRider>().Property("DISCRIMINATOR").HasMaxLength(50);
        }
    }

    public class BeastRider
    {
        public long Id { get; private set; }

        public string Name { get; private set; }

        public BeastRider()
        {
            // Required by EF Core
        }

        public BeastRider(string name)
        {
            Name = name;
        }
    }

    public class BirdRider : BeastRider
    {
        public BirdRider()
        {
            
        }
    }
}
