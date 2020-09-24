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

namespace devart_efcore_3_linq_translation_bug
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
CREATE TABLE CAR
(
    ID          NUMBER (19, 0) GENERATED ALWAYS AS IDENTITY NOT NULL,
    COLOR       VARCHAR2 (50 CHAR) NOT NULL,
    OWNER_ID    NUMBER (19, 0)
)");
                        context.Database.ExecuteSqlRaw(@"
CREATE TABLE OWNER
(
    ID              NUMBER (19, 0) GENERATED ALWAYS AS IDENTITY NOT NULL,
    NAME            VARCHAR2 (50 CHAR) NOT NULL
)");

                        await context.SaveChangesAsync();
                    }

                    scope.Complete();
                }

                await using (var context = new EntityContext())
                {

                    var owners = new List<Owner>();
                    // Create 3000 owners
                    for (var i = 0; i < 3000; i++)
                    {
                        var newOwner = new Owner("Owner " + i);
                        context.Add(newOwner);
                        owners.Add(newOwner);
                    }
                    context.SaveChanges();

                    // Create the first car
                    var car1 = new Car("red", owners[0]);
                    context.Add(car1);
                    context.SaveChanges();
                    context.Entry(car1).State = EntityState.Detached;

                    // Create the second car
                    var car2 = new Car("red", owners[600]);
                    context.Add(car2);
                    context.SaveChanges();
                    context.Entry(car1).State = EntityState.Detached;

                    // Create the third car
                    var car3 = new Car("blue", owners[1000]);
                    context.Add(car3);
                    context.SaveChanges();
                    context.Entry(car1).State = EntityState.Detached;

                    // Create the fourth car
                    var car4 = new Car("blue", owners[2000]);
                    context.Add(car4);
                    context.SaveChanges();
                    context.Entry(car1).State = EntityState.Detached;

                    // This doesn't work, should return the first and second car
                    var carsOfSpecificOwnerWithRedColor = await context.Set<Car>()
                        .Where(_ => owners.Contains(_.Owner))
                        .Where(_ => _.Color == "red")
                        .ToArrayAsync();

                    foreach (var car in carsOfSpecificOwnerWithRedColor)
                    {
                        Console.WriteLine($"Found {car.Id}.");
                    }
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
            modelBuilder.Entity<Owner>().ToTable("OWNER");
            modelBuilder.Entity<Owner>().HasKey(_ => _.Id);
            modelBuilder.Entity<Owner>().Property(_ => _.Id).HasColumnName("ID");
            modelBuilder.Entity<Owner>().Property(_ => _.Name).HasColumnName("NAME");
            modelBuilder.Entity<Owner>().HasMany(_ => _.Cars).WithOne(_ => _.Owner);

            modelBuilder.Entity<Car>().ToTable("CAR");
            modelBuilder.Entity<Car>().Property<long>("OWNER_ID");
            modelBuilder.Entity<Car>().HasKey(_ => _.Id);
            modelBuilder.Entity<Car>().Property(_ => _.Id).HasColumnName("ID");
            modelBuilder.Entity<Car>().Property(_ => _.Color).HasColumnName("COLOR");
            modelBuilder.Entity<Car>().HasOne(_ => _.Owner).WithMany(_ => _.Cars).HasForeignKey("OWNER_ID");
        }
    }

    public class Car
    {
        public long Id { get; private set; }

        public string Color { get; private set; }

        public Owner Owner { get; private set; }

        // ReSharper disable once UnusedMember.Global
#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        public Car()
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        {
            // Required by EF Core
        }

        public Car(string color, Owner owner)
        {
            Color = color;
            Owner = owner;
        }
    }

    public class Owner
    {
        public long Id { get; private set; }

        public string Name { get; private set; }

        public List<Car> Cars { get; private set; }

        public Owner(string name)
        {
            Name = name;
        }
    }
}
