namespace OEESentinel.Domain.Records;

public static class RejectedReasons
{
    // Parser-level (AC3.2)
    public const string MissingField = "MISSING_FIELD";
    public const string InvalidDecimalLocale = "INVALID_DECIMAL_LOCALE";
    public const string InvalidTimestamp = "INVALID_TIMESTAMP";
    public const string QuantityNotInteger = "QUANTITY_NOT_INTEGER";
    public const string QuantityNegative = "QUANTITY_NEGATIVE";

    // Handler-level
    public const string UnknownErrorCode = "UNKNOWN_ERROR_CODE";
    public const string InvalidMachineCode = "INVALID_MACHINE_CODE";
    public const string MachineCodeMismatch = "MACHINE_CODE_MISMATCH";
}