using CrudApp;
using CrudApp.Extensions;
using CrudApp.Middleware;
using CrudApp.Service;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRedisInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddSwaggerDocumentation();
builder.Services.AddHostedService<CreateIndexHostedService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddHttpContextAccessor();
RegisterEntities.AddRepositories(builder);

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health");
RegisterEntities.AddApis(app);

app.Run();
