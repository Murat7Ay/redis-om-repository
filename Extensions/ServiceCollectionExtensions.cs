using CrudApp.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Redis.OM;
using StackExchange.Redis;

namespace CrudApp.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRedisInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string redisConnectionString = configuration["Redis:ConnectionString"]
            ?? configuration["Redis:Port"]
            ?? "localhost:6379";

        var redisOptions = ConfigurationOptions.Parse(redisConnectionString, true);
        var multiplexer = ConnectionMultiplexer.Connect(redisOptions);

        services.AddSingleton<IConnectionMultiplexer>(multiplexer);
        services.AddSingleton(new RedisConnectionProvider(multiplexer));
        services.AddSingleton<IDatabase>(_ => multiplexer.GetDatabase());

        services.AddHealthChecks()
            .AddRedis(redisConnectionString, name: "redis", failureStatus: HealthStatus.Unhealthy);

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ApiSettings apiSettings = configuration.GetSection(ApiSettings.SectionName).Get<ApiSettings>()
            ?? new ApiSettings();
        var secretKey = apiSettings.GetSecretBytes();

        services.Configure<ApiSettings>(configuration.GetSection(ApiSettings.SectionName));

        services.AddAuthentication(config =>
        {
            config.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            config.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(config =>
        {
            config.RequireHttpsMetadata = false;
            config.SaveToken = true;
            config.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(secretKey),
                ValidateIssuer = false,
                ValidateAudience = false
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("reader", policy => policy.RequireRole("reader"));
            options.AddPolicy("moderator", policy => policy.RequireRole("moderator"));
            options.AddPolicy("root", policy => policy.RequireRole("root"));
        });

        return services;
    }

    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Redis OM Repository API", Version = "v1" });

            var securityScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                BearerFormat = "JWT",
                Scheme = "Bearer",
                Description = "Specify the authorization token",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
            };
            c.AddSecurityDefinition("Bearer", securityScheme);

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }
}
