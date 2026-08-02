using System.Security.Cryptography.X509Certificates;
using FluentValidation;
using idnetityServiceWedApi.Data;
using idnetityServiceWedApi.Features.Auth.Login;
using idnetityServiceWedApi.Features.Auth.Register;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Scalar.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using idnetityServiceWedApi.Messaging;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "IdentityService")
    .WriteTo.Console()
    .WriteTo.Seq(builder.Configuration["Seq:ServerUrl"] ?? "http://localhost:15341")
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddOpenApi();

builder.Services.AddDbContext<AuthDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("AuthDb"));
    options.UseOpenIddict();
});

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = true;

        // Kept in sync with RegisterRequestValidator so FluentValidation is the single
        // source of user-facing errors and Identity's own check never disagrees.
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization();
builder.Services.AddHostedService<OutboxDispatcher>();

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services
    .AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore().UseDbContext<AuthDbContext>();
    })
    .AddServer(options =>
    {
        options.SetTokenEndpointUris("/api/auth/token");

        // Pinned so resource servers can validate against a stable issuer instead of
        // one inferred from the request host.
        options.SetIssuer(new Uri(builder.Configuration["OpenIddict:Issuer"] ?? "http://localhost:5066/"));

        options.AllowPasswordFlow();
        options.AllowRefreshTokenFlow();

        options.AcceptAnonymousClients();

        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
        options.SetRefreshTokenLifetime(TimeSpan.FromDays(7));

        if (builder.Environment.IsDevelopment())
        {
            // Ephemeral keys: regenerated per restart, so tokens do not survive a restart.
            options.AddDevelopmentEncryptionCertificate()
                   .AddDevelopmentSigningCertificate();
        }
        else
        {
            // A stable certificate is required outside Development so tokens remain valid
            // across restarts and can be validated by every instance and resource server.
            string certificatePath = builder.Configuration["OpenIddict:CertificatePath"]
                ?? throw new InvalidOperationException("OpenIddict:CertificatePath is required outside Development.");
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                builder.Configuration["OpenIddict:CertificatePassword"]);
            options.AddEncryptionCertificate(certificate)
                   .AddSigningCertificate(certificate);
        }

        options.DisableAccessTokenEncryption();

        options.RegisterScopes(Scopes.Email, Scopes.Profile, Scopes.Roles, Scopes.OfflineAccess);

        options.UseAspNetCore()
               .EnableTokenEndpointPassthrough();
    });

var app = builder.Build();

app.UseSerilogRequestLogging();

try
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    app.UseHttpMethodOverride();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapRegisterEndpoint();
    app.MapLoginEndpoint();

    await app.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}
