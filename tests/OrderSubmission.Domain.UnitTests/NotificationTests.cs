using OrderSubmission.Domain.Common;
using OrderSubmission.Domain.Notifications;
using OrderSubmission.Domain.Orders;

namespace OrderSubmission.Domain.UnitTests;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_order_confirmation_is_pending_and_due_immediately()
    {
        var order = Order.Place("CUST-1", [new("SKU", 2, 5m)], Now);

        var notification = Notification.OrderConfirmation(order, Now);

        Assert.Equal(order.Id, notification.OrderId);
        Assert.Equal("CUST-1", notification.Recipient);
        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Equal(Now, notification.NextAttemptAt);
        Assert.Empty(notification.Attempts);
        Assert.Contains("10.00", notification.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_with_a_retry_time_stays_pending_and_is_rescheduled()
    {
        var notification = NewNotification();
        var retryAt = Now.AddSeconds(2);

        notification.RecordFailure("503 Service Unavailable", Now, retryAt);

        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Equal(retryAt, notification.NextAttemptAt);
        Assert.Equal(1, notification.ConsecutiveFailures);
        Assert.Equal("503 Service Unavailable", notification.LastError);
        var attempt = Assert.Single(notification.Attempts);
        Assert.False(attempt.Succeeded);
        Assert.Equal(1, attempt.Number);
    }

    [Fact]
    public void A_failure_without_a_retry_time_marks_the_notification_failed()
    {
        var notification = NewNotification();

        notification.RecordFailure("boom", Now, retryAt: null);

        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Null(notification.NextAttemptAt);
    }

    [Fact]
    public void Delivery_after_failures_records_the_full_attempt_history()
    {
        var notification = NewNotification();
        notification.RecordFailure("first", Now, Now.AddSeconds(1));
        notification.RecordFailure("second", Now.AddSeconds(1), Now.AddSeconds(3));

        notification.RecordDelivery(Now.AddSeconds(3));

        Assert.Equal(NotificationStatus.Delivered, notification.Status);
        Assert.Equal(Now.AddSeconds(3), notification.DeliveredAt);
        Assert.Null(notification.NextAttemptAt);
        Assert.Null(notification.LastError);
        Assert.Equal([false, false, true], notification.Attempts.Select(attempt => attempt.Succeeded));
        Assert.Equal([1, 2, 3], notification.Attempts.Select(attempt => attempt.Number));
    }

    [Fact]
    public void Retrying_a_failed_notification_requeues_it_with_a_fresh_retry_budget()
    {
        var notification = NewNotification();
        notification.RecordFailure("down", Now, retryAt: null);

        notification.RetryNow(Now.AddMinutes(5));

        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Equal(0, notification.ConsecutiveFailures);
        Assert.Equal(Now.AddMinutes(5), notification.NextAttemptAt);
        Assert.Single(notification.Attempts);
    }

    [Fact]
    public void Retrying_a_pending_notification_skips_the_remaining_backoff()
    {
        var notification = NewNotification();
        notification.RecordFailure("down", Now, Now.AddMinutes(10));

        notification.RetryNow(Now.AddSeconds(1));

        Assert.Equal(Now.AddSeconds(1), notification.NextAttemptAt);
        Assert.Equal(1, notification.ConsecutiveFailures);
    }

    [Fact]
    public void A_delivered_notification_cannot_be_retried_or_attempted_again()
    {
        var notification = NewNotification();
        notification.RecordDelivery(Now);

        Assert.Equal("notification.already_delivered", Assert.Throws<DomainException>(() => notification.RetryNow(Now)).Code);
        Assert.Equal("notification.not_pending", Assert.Throws<DomainException>(() => notification.RecordDelivery(Now)).Code);
        Assert.Equal("notification.not_pending", Assert.Throws<DomainException>(() => notification.RecordFailure("x", Now, null)).Code);
    }

    [Fact]
    public void Long_error_messages_are_truncated()
    {
        var notification = NewNotification();

        notification.RecordFailure(new string('x', Notification.ErrorMaxLength * 2), Now, Now.AddSeconds(1));

        Assert.Equal(Notification.ErrorMaxLength, notification.LastError!.Length);
    }

    private static Notification NewNotification() =>
        Notification.OrderConfirmation(Order.Place("CUST-1", [new("SKU", 1, 1m)], Now), Now);
}
