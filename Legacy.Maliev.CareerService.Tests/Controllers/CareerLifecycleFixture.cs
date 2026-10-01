using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Legacy.Maliev.CareerService.Data;
using Maliev.Aspire.ServiceDefaults.IAM;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

// Actual Production auth/application/repository/PG18/Redis. Only remote IAM HTTP
// is controlled; no deployed grant, source-data or provider-readiness claim.
public sealed class CareerLifecycleFixture : IAsyncLifetime
{
    private const string Issuer = "https://career-lifecycle.example.invalid";
    private const string Audience = "career-lifecycle";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly IContainer redis = new ContainerBuilder("redis:8-alpine").WithPortBinding(6379, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
    private readonly RSA key = RSA.Create(2048);
    private readonly string liveCredential = Guid.NewGuid().ToString("N");
    public ConcurrentDictionary<string, LifecycleAuthority> Authorities { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public CareerDbContext Context() => new(new DbContextOptionsBuilder<CareerDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
    public async Task InitializeAsync()
    {
        await postgres.StartAsync(); await redis.StartAsync();
        await using var db = Context(); await db.Database.MigrateAsync();
        Factory = NewFactory();
    }
    public async Task ResetAsync()
    {
        await using var db = Context(); await db.Offers.ExecuteDeleteAsync(); await db.Levels.ExecuteDeleteAsync();
        Authorities.Clear();
    }
    public WebApplicationFactory<Program> NewFactory(bool iam = true, string environment = "Production", SaveSchedule? schedule = null) =>
        new LifecycleFactory(this, iam, environment, schedule);
    public HttpClient Client(string? permission = null, string decision = "allow", WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? Factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (permission is null) return client;
        var principal = "service:career-lifecycle-" + Guid.NewGuid().ToString("N");
        Authorities[principal] = new(permission, decision);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(Issuer, Audience, [new(JwtRegisteredClaimNames.Sub, principal), new("permissions", permission)],
            now.AddMinutes(-1), now.AddMinutes(5), new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }
    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        key.Dispose(); await redis.DisposeAsync(); await postgres.DisposeAsync();
    }
    private sealed class LifecycleFactory(CareerLifecycleFixture fixture, bool iam, string environment, SaveSchedule? schedule) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:CareerDbContext"] = fixture.postgres.GetConnectionString(),
                ["ConnectionStrings:redis"] = $"{fixture.redis.Hostname}:{fixture.redis.GetMappedPublicPort(6379)}",
                ["Cache:RedisEnabled"] = "true",
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(fixture.key.ExportSubjectPublicKeyInfoPem())),
                ["Logging:LogLevel:Default"] = "Warning",
                ["IAM:LivePermissionChecks:Credential"] = fixture.liveCredential,
            };
            foreach (var item in settings) builder.UseSetting(item.Key, item.Value);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services =>
            {
                if (iam)
                {
                    services.AddScoped<IIamServiceClient, IamServiceClient>();
                    services.AddHttpClient("IAMService", client => client.BaseAddress = new Uri("https://controlled-iam.example.invalid"))
                        .ConfigurePrimaryHttpMessageHandler(() => new LifecycleIamTransport(fixture));
                }
                if (schedule is not null) services.ConfigureDbContext<CareerDbContext>(options => options.AddInterceptors(new ScheduledSave(schedule)));
            });
        }
    }
    private sealed class LifecycleIamTransport(CareerLifecycleFixture fixture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/iam/v1/auth/check-permission", request.RequestUri!.AbsolutePath);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var json = document.RootElement;
            Assert.True(fixture.Authorities.TryGetValue(json.GetProperty("principalId").GetString()!, out var authority));
            Assert.Equal(authority!.Permission, json.GetProperty("permissionId").GetString());
            Assert.Equal("global", json.GetProperty("resourcePath").GetString());
            var live = json.GetProperty("bypassCache").GetBoolean();
            Interlocked.Increment(ref authority.Calls);
            if (live)
            {
                Interlocked.Increment(ref authority.LiveCalls);
                Assert.True(request.Headers.TryGetValues("X-Maliev-IAM-Live-Check-Key", out var header) && header.Single() == fixture.liveCredential);
            }
            else Assert.False(request.Headers.Contains("X-Maliev-IAM-Live-Check-Key"));
            if (live && authority.Decision == "unavailable") return new(HttpStatusCode.ServiceUnavailable);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(live && authority.Decision == "malformed" ? "not-json"
                    : live && authority.Decision == "allow" ? "{\"allowed\":true}" : "{\"allowed\":false}", Encoding.UTF8, "application/json"),
            };
        }
    }
}

public sealed class LifecycleAuthority(string permission, string decision)
{
    public string Permission { get; } = permission;
    public string Decision { get; } = decision;
    public int Calls;
    public int LiveCalls;
}

public sealed class SaveSchedule
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Saves;
    public bool SawCallerCancellation;
    public Exception? Failure { get; init; }
    public Action? OnConcurrencyFailure { get; init; }
}

internal sealed class ScheduledSave(SaveSchedule schedule) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref schedule.Saves) == 1)
        {
            schedule.Entered.TrySetResult();
            try { await schedule.Release.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                schedule.SawCallerCancellation = true; throw;
            }
            finally { schedule.Exited.TrySetResult(); }
            if (schedule.Failure is not null) throw schedule.Failure;
        }
        return result;
    }

    public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(ConcurrencyExceptionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default)
    {
        schedule.OnConcurrencyFailure?.Invoke();
        return ValueTask.FromResult(result);
    }
}
