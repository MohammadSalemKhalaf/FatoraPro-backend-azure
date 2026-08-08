namespace Fatora.BL.DTOs.Responses;

public class AdminDashboardResponse
{
    // Null when the caller is a SubAdmin - only the top Admin ever sees
    // platform-wide numbers (see AdminReportService.GetDashboardAsync).
    public PlatformStatsResponse? Platform { get; set; }

    // The full list (every active SubAdmin) for the top Admin; a
    // single-entry list containing only its own slice for a SubAdmin
    // caller - never another SubAdmin's data either way.
    public List<SubAdminBreakdownResponse> SubAdminBreakdown { get; set; } = [];
}

public class PlatformStatsResponse
{
    public int TotalSubscribers { get; set; }
    public Dictionary<string, int> AccountStatusBreakdown { get; set; } = [];
    public Dictionary<string, int> SubscriptionTypeBreakdown { get; set; } = [];
    public int NewSignupsInPeriod { get; set; }
    public int ActivationsInPeriod { get; set; }
}

public class SubAdminBreakdownResponse
{
    public Guid SubAdminId { get; set; }
    public string SubAdminName { get; set; } = string.Empty;
    public int ManagedSubscriberCount { get; set; }
    public Dictionary<string, int> SubscriptionTypeBreakdown { get; set; } = [];

    // Always the literal last 10, regardless of the selected period - a
    // distinct concept from Platform.ActivationsInPeriod.
    public List<RecentActivationResponse> RecentActivations { get; set; } = [];

    // Also always the literal last/soonest 10, independent of the period
    // filter - both projected off the same already-loaded managed-subscriber
    // list (see AdminReportService.BuildSubAdminBreakdownAsync), no extra
    // query.
    public List<RecentSubscriberResponse> RecentlyJoined { get; set; } = [];
    public List<ExpiringSubscriberResponse> ExpiringSoon { get; set; } = [];
}

public class RecentActivationResponse
{
    public Guid Id { get; set; }
    public Guid SubscriberId { get; set; }
    public string SubscriberUserName { get; set; } = string.Empty;
    public string SubscriptionType { get; set; } = string.Empty;
    public int? CustomMonths { get; set; }
    public DateTime ActivatedAt { get; set; }
}

public class RecentSubscriberResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ExpiringSubscriberResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime SubscriptionEnd { get; set; }
}
