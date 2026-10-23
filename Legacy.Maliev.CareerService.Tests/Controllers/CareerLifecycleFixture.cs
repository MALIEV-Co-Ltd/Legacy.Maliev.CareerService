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
/// <summary>Owns disposable PostgreSQL, Redis, and controlled IAM resources for lifecycle acceptance.</summary>
public sealed class CareerLifecycleFixture : IAsyncLifetime
{
    private const string Issuer = "https://career-lifecycle.example.invalid";
    private const string Audience = "career-lifecycle";
    private readonly CareerFixtureLifetime lifetime = new("lifecycle");
    private readonly PostgreSqlContainer postgres;
    private readonly IContainer redis;
    private readonly RSA key;

    /// <summary>Retains fixture resources from construction through terminal cleanup.</summary>
    public CareerLifecycleFixture()
    {
        try
        {
            key = lifetime.Create("key", 3, () => RSA.Create(2048), value => { value.Dispose(); return ValueTask.CompletedTask; });
            postgres = lifetime.Create("postgres", 4,
                () => new PostgreSqlBuilder("postgres:18-alpine").WithLabel("com.maliev.career.fixture", lifetime.FixtureId).Build(),
                value => value.DisposeAsync(), value => CareerFixtureLifetime.ObserveContainer(value, "postgres:18-alpine"));
            redis = lifetime.Create("redis", 4,
                () => new ContainerBuilder("redis:8-alpine").WithPortBinding(6379, true)
                    .WithLabel("com.maliev.career.fixture", lifetime.FixtureId)
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build(),
                value => value.DisposeAsync(), value => CareerFixtureLifetime.ObserveContainer(value, "redis:8-alpine"));
        }
        catch (Exception primary)
        {
            lifetime.DisposePreservingAsync(primary).GetAwaiter().GetResult();
            throw;
        }
    }
    private readonly string liveCredential = Guid.NewGuid().ToString("N");
    /// <summary>Gets the controlled permission decisions keyed by synthetic service principal.</summary>
    public ConcurrentDictionary<string, LifecycleAuthority> Authorities { get; } = new();
    /// <summary>Gets the default production application factory for lifecycle tests.</summary>
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    /// <summary>Creates a context connected to the fixture-owned PostgreSQL container.</summary>
    /// <returns>A new caller-owned Career database context connected to the fixture PostgreSQL container.</returns>
    public CareerDbContext Context() => lifetime.Create("context", 1,
        () => new CareerDbContext(new DbContextOptionsBuilder<CareerDbContext>().UseNpgsql(postgres.GetConnectionString()).Options),
        value => value.DisposeAsync());
    /// <summary>Starts fixture dependencies and creates the migrated Career database.</summary>
    /// <returns>A task that completes after PostgreSQL and Redis start, database migrations run, and the default host factory is created.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            await lifetime.RunAsync("initialize", async cancellation =>
            {
                await lifetime.RunAsync("postgres-start", token => postgres.StartAsync(token), TimeSpan.FromMinutes(4), cancellation);
                lifetime.ObserveOwned(postgres);
                await lifetime.RunAsync("redis-start", token => redis.StartAsync(token), TimeSpan.FromMinutes(4), cancellation);
                lifetime.ObserveOwned(redis);
                await lifetime.UseOwnedAsync(Context, (db, token) => db.Database.MigrateAsync(token), cancellation);
                Factory = NewFactory();
            }, TimeSpan.FromMinutes(4));
        }
        catch (Exception primary)
        {
            await lifetime.DisposePreservingAsync(primary);
            throw;
        }
    }
    /// <summary>Removes test offers and levels and clears controlled authority state.</summary>
    /// <returns>A task that completes after offers, levels, and recorded synthetic IAM authorities are cleared.</returns>
    public Task ResetAsync() => lifetime.RunAsync("reset", async cancellation =>
    {
        await lifetime.UseOwnedAsync(Context, async (db, token) =>
        {
            await db.Offers.ExecuteDeleteAsync(token);
            await db.Levels.ExecuteDeleteAsync(token);
        }, cancellation);
        Authorities.Clear();
    }, TimeSpan.FromSeconds(90), serialize: true);
    /// <summary>Creates an isolated host with optional IAM and save-interception controls.</summary>
    /// <param name="iam">Whether the host registers the production IAM client backed by the controlled IAM HTTP transport.</param>
    /// <param name="environment">The ASP.NET Core environment assigned to the isolated application host.</param>
    /// <param name="schedule">The optional save barrier and failure controls installed as an EF Core interceptor; null leaves saves unintercepted.</param>
    /// <returns>A caller-owned application factory configured with the fixture dependencies and requested host controls.</returns>
    public WebApplicationFactory<Program> NewFactory(bool iam = true, string environment = "Production", SaveSchedule? schedule = null) =>
        lifetime.Create("factory", 2, () => new LifecycleFactory(this, iam, environment, schedule), value => value.DisposeAsync());
    /// <summary>Creates an anonymous or synthetic RS256-authenticated client for the selected host.</summary>
    /// <param name="permission">The permission embedded in a synthetic RS256 token and registered with controlled IAM; null creates an anonymous client.</param>
    /// <param name="decision">The controlled live IAM result for the synthetic principal: allow, deny, unavailable, or malformed.</param>
    /// <param name="factory">The application factory to create the client from; null uses the fixture default factory.</param>
    /// <returns>A caller-owned HTTP client with redirects disabled and, when requested, a synthetic signed bearer token.</returns>
    public HttpClient Client(string? permission = null, string decision = "allow", WebApplicationFactory<Program>? factory = null)
    {
        var client = lifetime.Create("client", 0,
            () => (factory ?? Factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }),
            value => { value.Dispose(); return ValueTask.CompletedTask; });
        if (permission is null) return client;
        var principal = "service:career-lifecycle-" + Guid.NewGuid().ToString("N");
        Authorities[principal] = new(permission, decision);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(Issuer, Audience, [new(JwtRegisteredClaimNames.Sub, principal), new("permissions", permission)],
            now.AddMinutes(-1), now.AddMinutes(5), new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }
    /// <summary>Disposes the application factory, signing key, and fixture-owned containers.</summary>
    /// <returns>A task that completes after the default host, signing key, Redis container, and PostgreSQL container are disposed.</returns>
    public Task DisposeAsync() => lifetime.DisposeAsync();
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

/// <summary>Stores a synthetic principal's permission, decision, and observed IAM call counts.</summary>
/// <param name="permission">The permission identifier expected in IAM requests for this synthetic principal.</param>
/// <param name="decision">The controlled live IAM outcome used to allow, deny, fail, or return malformed authorization responses.</param>
public sealed class LifecycleAuthority(string permission, string decision)
{
    /// <summary>Gets the permission identifier expected by the controlled IAM transport.</summary>
    public string Permission { get; } = permission;
    /// <summary>Gets the controlled live authorization decision.</summary>
    public string Decision { get; } = decision;
    /// <summary>Counts all observed IAM permission requests.</summary>
    public int Calls;
    /// <summary>Counts IAM requests that explicitly bypass the permission cache.</summary>
    public int LiveCalls;
}

/// <summary>Coordinates the first intercepted save and controlled concurrency or cancellation outcomes.</summary>
public sealed class SaveSchedule
{
    /// <summary>Signals that the first save reached the interception barrier.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Releases the intercepted save so its test can control write ordering.</summary>
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Signals that the intercepted save left its wait barrier.</summary>
    public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Counts intercepted save attempts.</summary>
    public int Saves;
    /// <summary>Records whether cancellation interrupted the save barrier.</summary>
    public bool SawCallerCancellation;
    /// <summary>Gets the controlled exception to throw after releasing the save barrier.</summary>
    public Exception? Failure { get; init; }
    /// <summary>Gets the callback invoked when EF observes a concurrency failure.</summary>
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
