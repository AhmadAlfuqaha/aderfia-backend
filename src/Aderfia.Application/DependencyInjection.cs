using Aderfia.Application.Admin;
using Aderfia.Application.Carts;
using Aderfia.Application.Catalog;
using Aderfia.Application.Customers;
using Aderfia.Application.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace Aderfia.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the use-case services. Scoped, because each one depends on
    /// the request-scoped DbContext.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IWishlistService, WishlistService>();

        // ---- Admin write side ----
        services.AddScoped<IAdminProductService, AdminProductService>();
        services.AddScoped<IAdminTaxonomyService, AdminTaxonomyService>();
        services.AddScoped<IAdminMediaService, AdminMediaService>();

        return services;
    }
}
