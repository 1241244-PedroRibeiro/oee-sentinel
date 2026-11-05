using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using CsvHelper;
using OEESentinel.Application.Ports.Outbound;
using OEESentinel.Domain.Records;
using OEESentinel.Domain.ValueObjects;

namespace OEESentinel.Infrastructure.Parsing;

public sealed class CsvProductionLogParser : IProductionLogParser
{
    private static readonly CsvReaderOptions CsvOptions = new()
    {
        HasHeader = false,
        TrimWhitespace = true,
        FormatProvider = CultureInfo.InvariantCulture
    };

    public async IAsyncEnumerable<ProductionEvent> ParseAsync(
        Stream stream,
        Action<RejectedRow> onRejected,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(onRejected);

        using var csv = CsvReader.Create(stream, CsvOptions, leaveOpen: true);

        var isFirstRow = true;
        while (await csv.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var lineNumber = (int)csv.RowNumber;
            var rawLine = csv.Current.ToString().TrimEnd('\r', '\n', ' ');

            if (isFirstRow)
            {
                isFirstRow = false;
                var firstField = csv.GetString(0);
                if (string.IsNullOrWhiteSpace(firstField) || !LooksLikeTimestamp(firstField))
                    continue;
            }

            var timestampRaw = csv.GetString(0);
            var machineCodeRaw = csv.GetString(1);
            var quantityRaw = csv.GetString(2);

            if (string.IsNullOrWhiteSpace(timestampRaw) ||
                string.IsNullOrWhiteSpace(machineCodeRaw) ||
                string.IsNullOrWhiteSpace(quantityRaw))
            {
                onRejected(new RejectedRow(lineNumber, rawLine, RejectedReasons.MissingField));
                continue;
            }

            if (!csv.TryParse<DateTimeOffset>(0, out var timestamp) ||
                timestamp == default)
            {
                onRejected(new RejectedRow(lineNumber, rawLine, RejectedReasons.InvalidTimestamp));
                continue;
            }

            MachineCode machineCode;
            try
            {
                machineCode = new MachineCode(machineCodeRaw);
            }
            catch (ArgumentException)
            {
                onRejected(new RejectedRow(lineNumber, rawLine, RejectedReasons.InvalidMachineCode));
                continue;
            }

            if (!csv.TryParse<int>(2, out var quantity))
            {
                onRejected(new RejectedRow(lineNumber, rawLine, RejectedReasons.QuantityNotInteger));
                continue;
            }

            if (quantity < 0)
            {
                onRejected(new RejectedRow(lineNumber, rawLine, RejectedReasons.QuantityNegative));
                continue;
            }

            var errorCode = csv.GetString(3);
            if (string.IsNullOrWhiteSpace(errorCode))
                errorCode = null;
            else
                errorCode = errorCode.Trim();

            // AQUI: Adicionado o 'rawLine' no fim
            yield return new ProductionEvent(timestamp, machineCode, quantity, errorCode, lineNumber, rawLine);
        }
    }

    private static bool LooksLikeTimestamp(string value) =>
        DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out _);
}