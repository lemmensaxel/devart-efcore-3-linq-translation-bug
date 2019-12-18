using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using CSharpFunctionalExtensions;
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
    RIDER_NAME        VARCHAR2 (50 CHAR) NOT NULL,
    BEAST_NAME        VARCHAR2 (50 CHAR)
)");
                        context.Database.ExecuteSqlRaw(@"INSERT INTO BEAST_RIDER (RIDER_NAME) VALUES ('Khal Drogo')");

                        await context.SaveChangesAsync();
                    }

                    scope.Complete();
                }

                await using (var context = new EntityContext())
                {
                    // This works
                    var khalDrogo = await context.Set<BeastRider>()
                        .FirstOrDefaultAsync(_ => _.Beast != null && _.Beast.Name == "Khal drogo");

                    // This fails with 'System.InvalidOperationException: Null TypeMapping in Sql Tree'
                    var khals = await context.Set<BeastRider>()
                        .Where(_ => _.Beast != null && _.Beast.Name.StartsWith("Khal"))
                        .ToArrayAsync();

                    Console.WriteLine($"Found {khals.Length} khals.");
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
            modelBuilder.Entity<BeastRider>().Property(_ => _.RiderName).HasColumnName("RIDER_NAME");
            modelBuilder.Entity<BeastRider>().OwnsOne(_ => _.Beast,
                ba =>
                {
                    ba.Property(beast => beast.Name).HasColumnName("BEAST_NAME");
                });
        }
    }

    public class BeastRider
    {
        public long Id { get; private set; }

        public string RiderName { get; private set; }

        public Beast? Beast { get; private set; }

        // ReSharper disable once UnusedMember.Global
#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        public BeastRider()
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        {
            // Required by EF Core
        }

        public BeastRider(string riderName, Beast beast)
        {
            RiderName = riderName;
            Beast = beast;
        }
    }

    public class Beast : ValueObject
    {
        public string Name { get; private set; }

        public Beast(string name)
        {
            Name = name;
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Name;
        }
    }
}
