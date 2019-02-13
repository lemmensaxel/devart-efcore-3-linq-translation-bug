using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Devart.Data.Oracle;
using HibernatingRhinos.Profiler.Appender.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SESVdh.Data.Ado;

namespace devart_efcore_value_conversion_bug_repro
{
    public class MyContext : Context
    {
        public MyContext(string connectionString) : base(connectionString)
        {
        }
    }

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
                    new TransactionOptions {IsolationLevel = IsolationLevel.ReadCommitted},
                    TransactionScopeAsyncFlowOption.Enabled))
                {
                    using (var context = new EntityContext())
                    {
                        context.Database.EnsureDeleted();
                        context.Database.ExecuteSqlCommand(@"
CREATE TABLE RIDER
(
    ID          NUMBER (19, 0) GENERATED ALWAYS AS IDENTITY NOT NULL,
    MOUNT       VARCHAR2 (100 CHAR) NOT NULL,
    COMMENT2    CLOB
)");

                        context.Database.ExecuteSqlCommand(@"
CREATE TABLE SWORD
(
    ID              NUMBER (19, 0) GENERATED ALWAYS AS IDENTITY NOT NULL,
    RIDER_ID        NUMBER (19, 0) NOT NULL,
    SWORD_TYPE      VARCHAR2 (100 CHAR) NOT NULL
)");

                        var rider = new Rider(EquineBeast.Mule);
                        rider.Comment = string.Join("", Enumerable.Range(1, 5000).Select(_ => "a"));
                        context.Add(rider);
                        await context.SaveChangesAsync();

                        rider.PickUpSword(new Sword(SwordType.Katana));
                        rider.PickUpSword(new Sword(SwordType.Longsword));
                        await context.SaveChangesAsync();
                    }

                    scope.Complete();
                }

                using (var context = new EntityContext())
                {
                    var parameter = EquineBeast.Mule;
                    var rider = context.Set<Rider>()
                        .Where(_ => _.Mount == parameter &&
                                    _.Swords.Any(sword => sword.SwordType == SwordType.Longsword))
                        .Include(_ => _.Swords).FirstOrDefault();
                    //var rider = context.Set<Rider>().Where(_ => _.Mount == EquineBeast.Mule).FirstOrDefault();
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
            modelBuilder.Entity<Rider>().Property(_ => _.Comment).HasColumnName("COMMENT2");//.HasMaxLength(11000);
            modelBuilder.Entity<Rider>().HasMany(_ => _.Swords).WithOne();
            modelBuilder.Entity<Rider>().Metadata.FindNavigation($"{nameof(Rider.Swords)}").SetPropertyAccessMode(PropertyAccessMode.Field);

            modelBuilder.Entity<Sword>().ToTable("SWORD");
            modelBuilder.Entity<Sword>().HasKey(_ => _.Id);
            modelBuilder.Entity<Sword>().Property(_ => _.Id).HasColumnName("ID");
            modelBuilder.Entity<Sword>().Property(_ => _.SwordType).HasColumnName("SWORD_TYPE");
            modelBuilder.Entity<Sword>().Property("RiderId").HasColumnName("RIDER_ID");
            modelBuilder.Entity<Sword>().Property(_ => _.SwordType).HasConversion<string>();   
        }
    }

    public class Rider
    {
        public int Id { get; private set; }
        public EquineBeast Mount { get; private set; }
        public string Comment { get; set; }
        private readonly List<Sword> _swords = new List<Sword>();
        public IReadOnlyList<Sword> Swords => _swords.AsReadOnly();

        private Rider()
        {
            // Required by EF Core
        }

        public Rider(EquineBeast mount)
        {
            Mount = mount;
        }

        public void PickUpSword(Sword sword)
        {
            _swords.Add(sword);
        }
    }

    public class Sword
    {
        public int Id { get; private set; }
        public SwordType SwordType { get; private set;}

        private Sword()
        {
            // Required by EF Core
        }

        public Sword(SwordType type)
        {
            SwordType = type;
        }
    }

    public enum EquineBeast
    {
        Donkey,
        Mule,
        Horse,
        Unicorn
    }

    public enum SwordType
    {
        Katana,
        Longsword,
        Falx
    }
}
