namespace OrderSubmission.Domain.Notifications;

public enum NotificationStatus
{
    /// <summary>Waiting for its first delivery attempt or for a scheduled retry.</summary>
    Pending = 0,

    /// <summary>Delivered successfully. Terminal.</summary>
    Delivered = 1,

    /// <summary>Automatic retries are exhausted. It can still be re-queued manually.</summary>
    Failed = 2,
}
