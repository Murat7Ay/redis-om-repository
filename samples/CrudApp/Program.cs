using System.Threading.RateLimiting;
using CrudApp.Auth;
using CrudApp.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using RedisCrud.Endpoints;
using RedisCrud.Hosting;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// --- Persistence: framework
builder.Services
    .AddRedisCrud(builder.Configuration.GetSection(CrudOptions.SectionName))
    .AddEntity<UserEntity>();

// --- Authentication: application concern, not framework
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .Validate(o => o.SigningKeyBytes.Length >= 32, "Auth:SigningKey must be at least 32 bytes. Set it via user-secrets or the Auth__SigningKey environment variable.")
    .Validate(o => builder.Environment.IsDevelopment() || o.SigningKey != AuthOptions.DevelopmentSigningKey,
        "The development signing key must not be used outside the Development environment.")
    .Validate(o => o.TokenLifetimeMinutes is > 0 and <= 24 * 60, "Auth:TokenLifetimeMinutes must be between 1 and 1440.")
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthOptions>>((jwt, auth) =>
    {
        jwt.MapInboundClaims = false;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = auth.Value.Issuer,
            ValidAudience = auth.Value.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(auth.Value.SigningKeyBytes),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            NameClaimType = "name",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization(Roles.AddPolicies);
builder.Services.AddScoped<UserAccounts>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddHostedService<BootstrapAdminService>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = http.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.AuthRequestsPerMinute,
            Window = TimeSpan.FromMinutes(1)
        }));
});

// --- Cross-cutting
builder.Services.AddValidation(); // .NET 10 built-in DataAnnotations validation for minimal API parameters (auth DTOs)
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(), name: "redis");
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, _, _) =>
{
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT"
    };
    document.Security ??= [];
    document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
    return Task.CompletedTask;
}));

var app = builder.Build();

// Malformed request bodies surface as BadHttpRequestException (thrown in Development): keep their 4xx status.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError
});
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "CrudApp"));
}

app.MapHealthChecks("/health");
app.MapAuthEndpoints();

app.MapCrud<ProductEntity>("/products", o =>
{
    o.ReadPolicy = Roles.Reader;
    o.WritePolicy = Roles.Moderator;
    o.AdminPolicy = Roles.Root;
});

app.MapCrud<RoseEntity>("/roses", o =>
{
    o.AllowAnonymousRead = true;
    o.WritePolicy = Roles.Root;
    o.AdminPolicy = Roles.Root;
});

app.Run();

public partial class Program;
