using System.Text.Json.Serialization;
using Legacy.Maliev.CareerService.Application.Interfaces;
using Legacy.Maliev.CareerService.Application.Services;
using Legacy.Maliev.CareerService.Data;
using Maliev.Aspire.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDefaultApiVersioning();
builder.AddPostgresDbContext<CareerDbContext>(connectionName: "CareerDbContext");
builder.AddStandardCache("legacy:career:");
builder.AddStandardCors();
builder.AddJwtAuthentication();
builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
builder.AddStandardOpenApi(
    title: "Legacy MALIEV JobOffer Service API",
    description: "Temporary .NET 10 compatibility service preserving the legacy Jobs and jobs/Levels API contracts.");

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICareerRepository, CareerRepository>();
builder.Services.AddScoped<ICareerService, CareerApplicationService>();

var app = builder.Build();

app.UseStandardMiddleware();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints("Jobs");
app.MapControllers();
app.MapApiDocumentation(servicePrefix: "Jobs");

await app.RunAsync();

/// <summary>Legacy Career Service entry point.</summary>
public partial class Program;
