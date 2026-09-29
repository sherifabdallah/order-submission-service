using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using OrderSubmission.Application.Abstractions.Notifications;
using OrderSubmission.Application.Abstractions.Persistence;
using OrderSubmission.Application.Common.Results;
using OrderSubmission.Application.Idempotency;
using OrderSubmission.Application.Orders;
using OrderSubmission.Application.Orders.PlaceOrder;
using OrderSubmission.Domain.Notifications;
using OrderSubmission.Domain.Orders;

namespace OrderSubmission.Application.UnitTests.Orders;

/// <summary>
/// Exercises the handler's decision logic against an in-memory store that behaves like a database:
/// staged changes become visible only on commit, and a duplicate key fails the whole commit.
/// </summary>
public sealed class PlaceOrderCommandHandlerTests
{
    private static readonly PlaceOrderCommand Command = new("key-1", "CUST-1", [new("SKU-1", 2, 10m)]);

    private readonly InMemoryStore _store = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task A_new_key_commits_order_notification_and_idempotency_record_together()
    {
        var result = await Handler().HandleAsync(Command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsReplay);
        Assert.Equal(1, _store.Commits);
        var order = Assert.Single(_store.Orders);
        var notification = Assert.Single(_store.Notifications);
        var record = Assert.Single(_store.Records);
        Assert.Equal(order.Id, notification.OrderId);
        Assert.Equal(order.Id, record.OrderId);
        Assert.Equal(20m, result.Value.Order.Total);
        Assert.Equal(NotificationStatus.Pending, result.Value.Order.Notification.Status);
        Assert.Equal(1, _store.Signals);
    }

    [Fact]
    public async Task Repeating_a_committed_request_replays_it_without_writing()
    {
        var first = await Handler().HandleAsync(Command, CancellationToken.None);

        var second = await Handler().HandleAsync(Command, CancellationToken.None);

        Assert.True(second.Value.IsReplay);
        Assert.Equal(first.Value.Order.Id, second.Value.Order.Id);
        Assert.Equal(1, _store.Commits);
        Assert.Single(_store.Orders);
    }

    [Fact]
    public async Task Reusing_a_key_with_a_different_payload_is_a_conflict()
    {
        await Handler().HandleAsync(Command, CancellationToken.None);

        var result = await Handler().HandleAsync(Command with { Items = [new("SKU-1", 3, 10m)] }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("idempotency.key_reused", result.Error.Code);
        Assert.Single(_store.Orders);
    }

    [Fact]
    public async Task Losing_a_race_to_a_concurrent_duplicate_replays_the_winner()
    {
        // Another request with the same key commits between our lookup and our commit.
        var winner = await Handler().HandleAsync(Command, CancellationToken.None);
        _store.HideRecordsFromNextLookup = true;

        var loser = await Handler().HandleAsync(Command, CancellationToken.None);

        Assert.True(loser.IsSuccess);
        Assert.True(loser.Value.IsReplay);
        Assert.Equal(winner.Value.Order.Id, loser.Value.Order.Id);
        Assert.Single(_store.Orders);
        Assert.Single(_store.Notifications);
        Assert.Equal(1, _store.RejectedCommits);
    }

    [Fact]
    public async Task Losing_a_race_to_a_different_payload_is_a_conflict()
    {
        await Handler().HandleAsync(Command, CancellationToken.None);
        _store.HideRecordsFromNextLookup = true;

        var loser = await Handler().HandleAsync(Command with { CustomerReference = "CUST-2" }, CancellationToken.None);

        Assert.Equal("idempotency.key_reused", loser.Error?.Code);
        Assert.Single(_store.Orders);
    }

    private PlaceOrderCommandHandler Handler() => new(
        _store, _store, _store, _store, _store, _store, _time, NullLogger<PlaceOrderCommandHandler>.Instance);

    private sealed class InMemoryStore :
        IIdempotencyStore, IOrderRepository, INotificationRepository, IOrderReadService, IUnitOfWork, INotificationDispatchSignal
    {
        private readonly List<object> _staged = [];

        public List<Order> Orders { get; } = [];

        public List<Notification> Notifications { get; } = [];

        public List<IdempotencyRecord> Records { get; } = [];

        public int Commits { get; private set; }

        public int RejectedCommits { get; private set; }

        public int Signals { get; private set; }

        /// <summary>Simulates a concurrent commit that landed after this request's initial lookup.</summary>
        public bool HideRecordsFromNextLookup { get; set; }

        public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken)
        {
            if (HideRecordsFromNextLookup)
            {
                HideRecordsFromNextLookup = false;
                return Task.FromResult<IdempotencyRecord?>(null);
            }

            return Task.FromResult(Records.SingleOrDefault(record => record.Key == key));
        }

        public void Add(IdempotencyRecord record) => _staged.Add(record);

        public void Add(Order order) => _staged.Add(order);

        public void Add(Notification notification) => _staged.Add(notification);

        public Task<Notification?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(Notifications.SingleOrDefault(notification => notification.OrderId == orderId));

        public Task<OrderDetails?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)
        {
            var order = Orders.SingleOrDefault(candidate => candidate.Id == orderId);
            var notification = Notifications.SingleOrDefault(candidate => candidate.OrderId == orderId);
            return Task.FromResult(order is null || notification is null ? null : OrderDetails.From(order, notification));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            var staged = _staged.ToList();
            _staged.Clear();

            var incomingKeys = staged.OfType<IdempotencyRecord>().Select(record => record.Key);
            if (incomingKeys.Any(key => Records.Any(record => record.Key == key)))
            {
                RejectedCommits++;
                throw new UniqueConstraintViolationException("Duplicate idempotency key.");
            }

            Orders.AddRange(staged.OfType<Order>());
            Notifications.AddRange(staged.OfType<Notification>());
            Records.AddRange(staged.OfType<IdempotencyRecord>());
            Commits++;
            return Task.CompletedTask;
        }

        public void Notify() => Signals++;
    }
}
