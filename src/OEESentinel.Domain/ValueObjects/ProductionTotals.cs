namespace OEESentinel.Domain.ValueObjects;

public record ProductionTotals
{
    public int TotalParts { get; }
    public int ApprovedParts { get; }
    public int RejectedParts => TotalParts - ApprovedParts;

    public ProductionTotals(int totalParts, int approvedParts)
    {
        if (totalParts < 0) throw new ArgumentException("Total parts cannot be negative.");
        if (approvedParts < 0) throw new ArgumentException("Approved parts cannot be negative.");
        if (approvedParts > totalParts) throw new ArgumentException("Approved parts cannot exceed total.");
        TotalParts = totalParts;
        ApprovedParts = approvedParts;
    }
}