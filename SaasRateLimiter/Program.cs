using SaasRateLimiter.Configuration;
using SaasRateLimiter.Middleware;
using SaasRateLimiter.Services.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));
builder.Services.AddSingleton<RateLimitResolver>();
builder.Services.AddSingleton<RateLimitCounter>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<RateLimitMiddleware>();

app.MapControllers();

app.Run();

// Exposes the generated Program class to SaasRateLimiter.Tests via WebApplicationFactory<Program>.
public partial class Program;
