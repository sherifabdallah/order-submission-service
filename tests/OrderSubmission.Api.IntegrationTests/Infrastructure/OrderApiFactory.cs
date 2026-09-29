using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using OrderSubmission.Application.Abstractions.Notifications;
using OrderSubmission.Infrastructure.Notifications;
using OrderSubmission.Infrastructure.Persistence;

namespace OrderSubmission.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real application in memory against its own, freshly migrated database: a SQLite file by
/// default, or a throwaway SQL Server database when <c>ORDER_TESTS_SQLSERVER</c> holds a connection string.
/// By default the background worker is off and time is a <see cref="FakeTimeProvider"/>, so tests drive
/// notification delivery step by step.
/// </summary>
public class OrderApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string? SqlServerConnectionString = Environment.GetEnvironmentVariable("ORDER_TESTS_SQLSERVER");

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"order-submission-tests-{Guid.NewGuid():N}.db");
    private readonly Dictionary<string, string> _settings = new(StringComparer.Ordinal);

    public OrderApiFactory()
    {
        if (string.IsNullOrWhiteSpace(SqlServerConnectionString))
        {
            UseSetting("Database:Provider", "Sqlite");
            UseSetting("ConnectionStrings:Orders", $"Data Source={_databasePath}");
        }
        else
        {
            var database = new SqlConnectionStringBuilder(SqlServerConnectionString) { InitialCatalog = $"OrderSubmissionTests_{Guid.NewGuid():N}" };
            UseSetting("Database:Provider", "SqlServer");
            UseSetting("ConnectionStrings:Orders", database.ConnectionString);
        }

        UseSetting("RateLimiting:Enabled", "false");
        UseSetting("Notifications:Dispatcher:Enabled", "false");
        UseSetting("Notifications:FakeService:Latency", "00:00:00");
        UseSetting("Notifications:Delivery:JitterRatio", "0");
        UseSetting("Notifications:Delivery:InitialRetryDelay", "00:00:02");
        UseSetting("Notifications:Delivery:MaxRetryDelay", "00:01:00");
        UseSetting("Notifications:Delivery:MaxAttempts", "8");
    }

    /// <summary>Controls time for everything in the app. Ignored when <see cref="UseRealTime"/> is set.</summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero));

    public bool UseRealTime { get; init; }

    public FaultInjectionInterceptor Faults { get; } = new();

    public FakeNotificationSender NotificationService => Services.GetRequiredService<FakeNotificationSender>();

    public INotificationServiceSimulator Simulator => Services.GetRequiredService<INotificationServiceSimulator>();

    public OrderApiFactory UseSetting(string key, string value)
    {
        _settings[key] = value;
        return this;
    }

    /// <summary>Runs one dispatch cycle, exactly as the background worker would.</summary>
    public Task<int> DispatchAsync() =>
        Services.GetRequiredService<NotificationDispatcher>().DispatchDueAsync(TestContext.Current.CancellationToken);

    public void SetNotificationService(NotificationServiceMode mode) =>
        Simulator.Apply(new NotificationServiceSettings(mode, FailureRate: 1, Latency: TimeSpan.Zero));

    public async Task<T> QueryAsync<T>(Func<OrderSubmissionDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderSubmissionDbContext>();
        return await query(db);
    }

    public Task<int> CountOrdersAsync(string customerReference) =>
        QueryAsync(db => db.Orders.CountAsync(order => order.CustomerReference == customerReference));

    ValueTask IAsyncLifetime.InitializeAsync()
    {
        _ = Server; // Start the host (runs migrations) before the test body.
        return ValueTask.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(SqlServerConnectionString))
        {
            await QueryAsync(db => db.Database.EnsureDeletedAsync());
        }

        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best effort; the OS temp folder is cleaned eventually.
            }
        }

        GC.SuppressFinalize(this);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IInterceptor>(Faults);
            if (!UseRealTime)
            {
                services.AddSingleton<TimeProvider>(Time);
            }
        });
    }
}

/// <summary>Fails a database command after it has executed, to prove the surrounding transaction rolls back.</summary>
public sealed class FaultInjectionInterceptor : DbCommandInterceptor
{
    public volatile bool FailNotificationInserts;

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfTargeted(command);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfTargeted(command);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    private void ThrowIfTargeted(DbCommand command)
    {
        var insertsNotification = command.CommandText.Contains("INSERT INTO \"Notifications\"", StringComparison.Ordinal)
            || command.CommandText.Contains("INSERT INTO [Notifications]", StringComparison.Ordinal);

        if (FailNotificationInserts && insertsNotification)
        {
            throw new InvalidOperationException("Injected failure after inserting the notification.");
        }
    }
}

public static class HttpExtensions
{
    public static Task<HttpResponseMessage> PlaceOrderAsync(this HttpClient client, string? idempotencyKey, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(OrderApiFactory.Json, TestContext.Current.CancellationToken))!;
}
