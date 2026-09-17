using Aderfia.Application.Common;
using Aderfia.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aderfia.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string webRootPath)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<ISlugGenerator, SlugGenerator>();

        // Reads Media:BaseUrl. Point it at a CDN in production and no other
        // code changes — image records store keys, never URLs.
        services.AddSingleton<IImageUrlResolver>(_ =>
            new ImageUrlResolver(configuration["Media:BaseUrl"] ?? "/media"));

        /* Uploads land under the web root's media folder, which is exactly
           where the static file middleware serves from — so a freshly
           uploaded image is reachable on the very next request. Swap this
           registration for an S3 or Blob implementation to move to a CDN. */
        var mediaRoot = Path.Combine(webRootPath, "media");
        Directory.CreateDirectory(mediaRoot);

        services.AddSingleton<IImageStorage>(_ => new LocalImageStorage(mediaRoot));

        return services;
    }
}
