namespace ERP.Infrastructure.Persistence;

/// <summary>The branch and expiry kept on every stock movement.</summary>
public static class StockRows
{
    /// <summary>The branch the movement belongs to.</summary>
    public const string BranchId = "BranchId";

    /// <summary>The expiry date of the movement's batch.</summary>
    public const string ExpiresOn = "ExpiresOn";
}
