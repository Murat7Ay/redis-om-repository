using CrudApp;
using CrudApp.Entity;
using CrudApp.Repository;
using CrudApp.Service;
using CrudApp.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Redis.OM;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
string redisConnectionString = builder.Configuration["Redis:ConnectionString"]
    ?? builder.Configuration["Redis:Port"]
    ?? "localhost:6379";

ApiSettings apiSettings = builder.Configuration.GetSection(ApiSettings.SectionName).Get<ApiSettings>()
    ?? new ApiSettings();
var secretKey = apiSettings.GetSecretBytes();

builder.Services.Configure<ApiSettings>(builder.Configuration.GetSection(ApiSettings.SectionName));

builder.Services.AddAuthentication(config =>
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
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("reader", policy => policy.RequireRole("reader"));
    options.AddPolicy("moderator", policy => policy.RequireRole("moderator"));
    options.AddPolicy("root", policy => policy.RequireRole("root"));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Internal Generator Apis", Version = "v1" });
    OpenApiSecurityScheme securityDefinition = new OpenApiSecurityScheme()
    {
        Name = "Authorization",
        BearerFormat = "JWT",
        Scheme = "Bearer",
        Description = "Specify the authorization token",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
    };
    c.AddSecurityDefinition("Bearer", securityDefinition);
    OpenApiSecurityRequirement securityRequirement = new OpenApiSecurityRequirement();
    OpenApiSecurityScheme secondSecurityDefinition = new OpenApiSecurityScheme
    {
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };
    securityRequirement.Add(secondSecurityDefinition, new string[] { });
    c.AddSecurityRequirement(securityRequirement);
});

var redisOptions = ConfigurationOptions.Parse(redisConnectionString, true);
builder.Services.AddSingleton(new RedisConnectionProvider(redisOptions));
builder.Services.AddSingleton<IDatabase>(cfg =>
{
    IConnectionMultiplexer multiplexer = ConnectionMultiplexer.Connect(redisOptions);
    return multiplexer.GetDatabase();
});
builder.Services.AddHostedService<CreateIndexHostedService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddHttpContextAccessor();
RegisterEntities.AddRepositories(builder);
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI();
RegisterEntities.AddApis(app);
app.Run();
