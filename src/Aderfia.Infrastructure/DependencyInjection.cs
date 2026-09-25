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

        var blobServiceUriText = configuration["BlobStorage:ServiceUri"]?.Trim().TrimEnd('/');
        var blobContainer = configuration["BlobStorage:Container"]?.Trim();
        if (string.IsNullOrWhiteSpace(blobContainer)) blobContainer = "media";

        /* Media URLs and storage use the same blob configuration in production.
           Media:BaseUrl can still override the public URL later (for example,
           when a CDN or custom media domain is added). */
        var mediaBaseUrl = configuration["Media:BaseUrl"];

        if (!string.IsNullOrWhiteSpace(blobServiceUriText))
        {
            if (!Uri.TryCreate(blobServiceUriText, UriKind.Absolute, out var blobServiceUri))
                throw new InvalidOperationException("BlobStorage:ServiceUri must be an absolute URI.");

            services.AddSingleton<IImageStorage>(
                _ => new AzureBlobImageStorage(blobServiceUri, blobContainer));

            if (string.IsNullOrWhiteSpace(mediaBaseUrl))
                mediaBaseUrl = $"{blobServiceUriText}/{blobContainer}";
        }
        else
        {
            /* Local development fallback. Uploads land under wwwroot/media,
               where UseStaticFiles serves them immediately. */
            var mediaRoot = Path.Combine(webRootPath, "media");
            Directory.CreateDirectory(mediaRoot);
            services.AddSingleton<IImageStorage>(_ => new LocalImageStorage(mediaRoot));
        }

        services.AddSingleton<IImageUrlResolver>(
            _ => new ImageUrlResolver(mediaBaseUrl ?? "/media"));

        return services;
    }
}
