using Aderfia.Application.Common;
using Aderfia.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aderfia.Persistence;

public static class DependencyInjection
{
    /// <summary>
    /// Assembly holding the SQL Server migrations. Kept as a constant so the
    /// one place that has to change on a rename is findable.
    /// </summary>
    public const string SqlServerMigrationsAssembly = "Aderfia.Persistence.SqlServer";

    /// <summary>
    /// Registers the DbContext and exposes it through the Application layer's
    /// <see cref="IAderfiaDbContext"/> abstraction — use cases never see the
    /// concrete context type.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("Default")
                               ?? "Data Source=aderfia.db";

        services.AddDbContext<AderfiaDbContext>(options =>
        {
            switch (provider.ToLowerInvariant())
            {
                case "sqlserver":
                    options.UseSqlServer(connectionString, sql =>
                    {
                        /* SQL Server's migrations live in their own assembly.
                           EF finds migrations by scanning ONE assembly for
                           [Migration] types and cannot tell providers apart,
                           so the SQLite set and this set have to be in
                           separate assemblies or EF would try to apply both.

                           A string rather than typeof(...).Assembly: this
                           project cannot reference Aderfia.Persistence.SqlServer,
                           which references it. Renaming that assembly means
                           changing this line. */
                        sql.MigrationsAssembly(SqlServerMigrationsAssembly);
                        // Transient network faults should not surface as 500s.
                        sql.EnableRetryOnFailure(maxRetryCount: 3);
                    });
                    break;

                default:
                    // SQLite is the default so a fresh clone runs with no
                    // database server installed.
                    options.UseSqlite(connectionString, sqlite =>
                        sqlite.MigrationsAssembly(typeof(AderfiaDbContext).Assembly.FullName));
                    break;
            }
        });

        services.AddScoped<IAderfiaDbContext>(sp => sp.GetRequiredService<AderfiaDbContext>());

        return services;
    }

    /// <summary>
    /// Applies pending migrations and seeds development data.
    /// Call on startup outside production, where migrations should be run as
    /// a deliberate deployment step instead.
    /// </summary>
    public static async Task InitialiseDatabaseAsync(
        this IServiceProvider services,
        bool seed,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AderfiaDbContext>();

        await db.Database.MigrateAsync(ct);

        if (seed) await CatalogSeeder.SeedAsync(db, ct);
    }
}
