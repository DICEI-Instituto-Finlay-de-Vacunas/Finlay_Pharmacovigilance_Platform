
using Finlay.PharmaVigilance.Api.Common;
using Finlay.PharmaVigilance.Application.IServices.Common;
using System.Text.Json.Serialization;

namespace Finlay.PharmaVigilance.Api;


public static class DependencyInjection
{
    /// <summary>
    /// Configures services for the presentation layer, which includes setting up API controllers, 
    /// Swagger documentation, and Cross-Origin Resource Sharing (CORS) policies for the application.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which services are added.</param>
    /// <returns>The modified <see cref="IServiceCollection"/> with the presentation layer services registered.</returns>
    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        // Add controllers to handle API requests
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter()
                );
            });

        // Add Swagger for API documentation
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        // Allowed browser origins. Extra origins (e.g. the Cloudflare frontend URL) are provided
        // through configuration: Cors__AllowedOrigins="https://a.example.com,https://b.example.com"
        var allowedOrigins = new List<string>
        {
            "http://localhost:5173",
            "https://frontend-five-sepia-10.vercel.app"
        };

        allowedOrigins.AddRange(
            (configuration["Cors:AllowedOrigins"] ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(o => o.TrimEnd('/')));

        services.AddCors(options =>
        {
            options.AddPolicy("Frontend", builder =>
            {
                builder
                    .WithOrigins(allowedOrigins.Distinct().ToArray())
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        services.AddScoped<IUserContextService, UserContextService>();

        return services;
    }
}