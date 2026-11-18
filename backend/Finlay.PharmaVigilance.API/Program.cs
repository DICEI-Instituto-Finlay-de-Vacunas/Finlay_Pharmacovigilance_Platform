

using Finlay.PharmaVigilance.Infrastructure.Initializer;
using Finlay.PharmaVigilance.Api;
using Finlay.PharmaVigilance.Application;
using Finlay.PharmaVigilance.Infrastructure;
using Finlay.PharmaVigilance.Api.Middleware;
using Microsoft.AspNetCore.Mvc;
using Serilog;


var builder = WebApplication.CreateBuilder(args);


var services = builder.Services;


services.AddPresentation(builder.Configuration);
services.AddAplication(builder.Configuration);
services.AddInfrastructure(builder.Configuration);

services.AddRateLimitingConfiguration();

services.AddHttpClient();

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(x => x.Value.Errors.Count > 0)
            .Select(x => new
            {
                Field = x.Key,
                Errors = x.Value.Errors.Select(e => e.ErrorMessage)
            });

        return new BadRequestObjectResult(new
        {
            success = false,
            status = 400,
            message = "Validation failed",
            errors
        });
    };
});


// The API listens on all network interfaces. The port comes from the PORT variable
// (injected by most hosting platforms) and defaults to 5137 for local development.
var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var parsedPort) ? parsedPort : 5137;
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(System.Net.IPAddress.Any, port);
});

// Behind a reverse proxy (Render, Cloudflare, ...) the real client IP and scheme arrive in X-Forwarded-* headers.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});


// 🔥 CONFIGURACIÓN SERILOG AQUÍ
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/app-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();


var app = builder.Build();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Apply EF Core migrations at startup in Development, or in any environment when Database__AutoMigrate=true.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await DatabaseInitializer.InitializeAsync(app.Services);
}

app.UseForwardedHeaders();

app.UseCors("Frontend");
//app.UseCors("LocalhostPolicy");

app.UseMiddleware<ErrorHandlingMiddleware>();

// Opt-in: TLS is normally terminated by the hosting platform, so the redirect is off unless requested.
if (app.Configuration.GetValue<bool>("Https:Redirect"))
{
    app.UseHttpsRedirection();
}
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Lightweight liveness probe for hosting platforms (does not touch the database).
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();


app.Run();