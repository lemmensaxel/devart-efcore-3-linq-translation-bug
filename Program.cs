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

namespace devart_efcore_owned_type_bug_repro
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
    BEAST_NAME VARCHAR2 (50 CHAR) NOT NULL,
    BEAST_TYPE VARCHAR2 (50 CHAR) NOT NULL,
    DISCRIMINATOR VARCHAR2 (50 CHAR) NOT NULL
)");

                        context.Database.ExecuteSqlCommand(@"
CREATE TABLE OTHER_RIDER
(
    ID          NUMBER (19, 0) GENERATED ALWAYS AS IDENTITY NOT NULL,
    BEAST_NAME VARCHAR2 (50 CHAR) NOT NULL,
    BEAST_TYPE VARCHAR2 (50 CHAR) NOT NULL
)");

                        var beast = new Beast("Drogo", EquineBeast.Horse);
                        var rider = new BeastRider( beast);
                        context.Add(rider);

                        var otherBeast = new Beast("Viscerion", EquineBeast.Donkey);
                        var otherRider = new OtherBeastRider(otherBeast);
                        context.Add(otherRider);

                        await context.SaveChangesAsync();
                    }

                    scope.Complete();
                }

                using (var context = new EntityContext())
                {
                    // Works as expected, clean SQL
                    var otherRider = context.Set<OtherBeastRider>()
                        .FirstOrDefault();

                    // Works, but generates unnecessary left join in SQL
                    var rider = context.Set<BeastRider>()
                        .FirstOrDefault();
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
            modelBuilder.Entity<Rider>().HasDiscriminator<string>("DISCRIMINATOR")
                .HasValue<BeastRider>("BeastRider");

            modelBuilder.Entity<BeastRider>().OwnsOne(
                o => o.Beast,
                sa =>
                {
                    sa.Property<long>("Id").HasColumnName("ID");
                    sa.Property(p => p.Name).HasColumnName("BEAST_NAME");
                    sa.Property(p => p.Type).HasColumnName("BEAST_TYPE").HasConversion<string>();
                });

            modelBuilder.Entity<OtherBeastRider>().ToTable("OTHER_RIDER");
            modelBuilder.Entity<OtherBeastRider>().HasKey(_ => _.Id);
            modelBuilder.Entity<OtherBeastRider>().Property(_ => _.Id).HasColumnName("ID");
            modelBuilder.Entity<OtherBeastRider>().OwnsOne(
                o => o.Beast,
                sa =>
                {
                    sa.Property<long>("Id").HasColumnName("ID");
                    sa.Property(p => p.Name).HasColumnName("BEAST_NAME");
                    sa.Property(p => p.Type).HasColumnName("BEAST_TYPE").HasConversion<string>();
                });
        }
    }

    public abstract class Rider
    {
        public long Id { get; private set; }

        protected Rider()
        {
            // Required by EF Core
        }
    }

    public class BeastRider : Rider
    {
        public Beast Beast { get; private set; }

        public BeastRider() : base()
        {
            // Required by EF Core
        }

        public BeastRider(Beast beast) : base()
        {
            Beast = beast;
        }
    }

    public class OtherBeastRider
    {
        public long Id { get; private set; }

        public Beast Beast { get; private set; }

        public OtherBeastRider()
        {
            // Required by EF Core
        }

        public OtherBeastRider(Beast beast)
        {
            Beast = beast;
        }
    }

    public class Beast : ValueObject
    {
        public string Name { get; private set; }

        public EquineBeast Type { get; private set; }

        public Beast(string name, EquineBeast type)
        {
            Name = name;
            Type = type;
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Name;
            yield return Type;
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
