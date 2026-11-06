using OEESentinel.Domain.Enums;
using OEESentinel.Domain.Records;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Domain.Entities;

public sealed class LogIngestion
{
    public Guid Id { get; private set; }
    public MachineCode MachineCode { get; private set; } = default!;
    public string OriginalFileName { get; private set; } = default!;
    public IngestionStatus Status { get; private set; }
    public int RowsProcessed { get; private set; }
    public int RowsRejected { get; private set; }
    public IReadOnlyList<RejectedRow> RejectedSample { get; private set; } = Array.Empty<RejectedRow>();
    public DateTimeOffset UploadedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }

    private LogIngestion() { }

    public static LogIngestion CreatePending(
        MachineCode machineCode,
        string originalFileName,
        DateTimeOffset uploadedAt)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new ArgumentException("Original file name is required.", nameof(originalFileName));

        return new LogIngestion
        {
            Id = Guid.NewGuid(),
            MachineCode = machineCode ?? throw new ArgumentNullException(nameof(machineCode)),
            OriginalFileName = originalFileName,
            Status = IngestionStatus.PENDING,
            UploadedAt = uploadedAt
        };
    }

    public void MarkProcessing() => Status = IngestionStatus.PROCESSING;

    public void MarkCompleted(
        int rowsProcessed,
        int rowsRejected,
        IReadOnlyList<RejectedRow> rejectedSample,
        DateTimeOffset completedAt)
    {
        if (rowsProcessed < 0)
            throw new ArgumentOutOfRangeException(nameof(rowsProcessed));
        if (rowsRejected < 0)
            throw new ArgumentOutOfRangeException(nameof(rowsRejected));
        if (rowsRejected > rowsProcessed)
            throw new ArgumentOutOfRangeException(
                nameof(rowsRejected),
                "RowsRejected cannot exceed RowsProcessed.");

        Status = IngestionStatus.COMPLETED;
        RowsProcessed = rowsProcessed;
        RowsRejected = rowsRejected;
        RejectedSample = rejectedSample ?? Array.Empty<RejectedRow>();
        CompletedAt = completedAt;
        FailureReason = null;
    }

    public void MarkFailed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Failure reason is required.", nameof(reason));

        Status = IngestionStatus.FAILED;
        FailureReason = reason;
    }
}