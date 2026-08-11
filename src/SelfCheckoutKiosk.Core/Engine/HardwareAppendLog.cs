using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Engine;

/// <summary>
/// Represents an escrow audit record that does not yet have a
/// matching COMMITTED_TO_VAULT or REJECTED resolution.
/// </summary>
public sealed record UnresolvedEscrowRecord(
    Guid AuditId,
    DateTimeOffset EscrowedAtUtc,
    Money Note
);

/// <summary>
/// Append-only hardware audit log for physical cash events.
///
/// This class is hardware-independent. It receives ordinary data
/// from LLCoreLogicEngine and never subscribes to HAL events or
/// calls hardware directly.
/// </summary>
public sealed class HardwareAppendLog
{
    private const string
        EscrowRecordType =
            "ESCROW";

    private const string
        CommittedToVaultRecordType =
            "COMMITTED_TO_VAULT";

    private const string
        RejectedRecordType =
            "REJECTED";

    private const string
        RecordLineEnding =
            "\n";

    private static readonly UTF8Encoding
        Utf8WithoutBom =
            new(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true
            );

    private readonly string
        _filePath;

    private readonly TimeProvider
        _timeProvider;

    private readonly Func<Guid>
        _auditIdFactory;

    /*
     * Serializes:
     *
     * - audit-ID generation;
     * - timestamp generation;
     * - physical file appends;
     * - startup reconciliation reads.
     *
     * Reconciliation therefore sees one stable file snapshot when
     * it runs against this HardwareAppendLog instance.
     */
    private readonly object
        _writeSync =
            new();

    /// <summary>
    /// Creates a hardware append log using the system UTC clock
    /// and randomly generated audit identifiers.
    /// </summary>
    public HardwareAppendLog(
        string filePath)
        : this(
            filePath,
            TimeProvider.System,
            Guid.NewGuid
        )
    {
    }

    /// <summary>
    /// Testable constructor with controllable time and audit-ID
    /// generation.
    ///
    /// Internal visibility keeps production construction simple
    /// while allowing deterministic Core unit tests.
    /// </summary>
    internal HardwareAppendLog(
        string filePath,
        TimeProvider timeProvider,
        Func<Guid> auditIdFactory)
    {
        if (
            string.IsNullOrWhiteSpace(
                filePath
            )
        )
        {
            throw new ArgumentException(
                "The hardware audit-log path is required.",
                nameof(filePath)
            );
        }

        ArgumentNullException.ThrowIfNull(
            timeProvider
        );

        ArgumentNullException.ThrowIfNull(
            auditIdFactory
        );

        _filePath =
            Path.GetFullPath(
                filePath
            );

        _timeProvider =
            timeProvider;

        _auditIdFactory =
            auditIdFactory;

        var directoryPath =
            Path.GetDirectoryName(
                _filePath
            );

        if (
            !string.IsNullOrWhiteSpace(
                directoryPath
            )
        )
        {
            Directory.CreateDirectory(
                directoryPath
            );
        }
    }

    /// <summary>
    /// Full normalized path of the append-only audit file.
    /// </summary>
    public string FilePath =>
        _filePath;

    /// <summary>
    /// Synchronously appends one durable ESCROW record.
    ///
    /// The method does not return until the record has been written
    /// and Flush(flushToDisk: true) has completed.
    ///
    /// LLCoreLogicEngine must call this before performing any
    /// additional processing of the escrowed note.
    /// </summary>
    /// <returns>
    /// Audit identifier used to correlate the escrow record with its
    /// later COMMITTED_TO_VAULT or REJECTED resolution record.
    /// </returns>
    public Guid AppendEscrow(
        Money note)
    {
        ValidateNote(
            note
        );

        lock (_writeSync)
        {
            var auditId =
                _auditIdFactory();

            if (
                auditId ==
                Guid.Empty
            )
            {
                throw new InvalidOperationException(
                    "The generated hardware audit identifier " +
                    "cannot be empty."
                );
            }

            AppendRecordCore(
                FormatRecord(
                    EscrowRecordType,
                    auditId,
                    GetCurrentUtcTimestamp(),
                    note
                )
            );

            return auditId;
        }
    }

    /// <summary>
    /// Synchronously appends a durable COMMITTED_TO_VAULT
    /// resolution record for an earlier escrow record.
    ///
    /// This method only records a resolution supplied by the
    /// caller. It does not call cash hardware and does not infer
    /// that physical vault commitment occurred.
    ///
    /// The caller must invoke this method only when an honest vault
    /// commitment trigger is available.
    /// </summary>
    public void AppendCommittedToVault(
        Guid escrowAuditId,
        Money note)
    {
        AppendResolution(
            CommittedToVaultRecordType,
            escrowAuditId,
            note
        );
    }

    /// <summary>
    /// Synchronously appends a durable REJECTED resolution record
    /// for an earlier escrow record.
    ///
    /// This method only records a resolution supplied by the
    /// caller. It does not call cash hardware and does not infer
    /// physical rejection.
    ///
    /// The caller must invoke this method only at a resolution point
    /// supported by the current hardware contract.
    /// </summary>
    public void AppendRejected(
        Guid escrowAuditId,
        Money note)
    {
        AppendResolution(
            RejectedRecordType,
            escrowAuditId,
            note
        );
    }

    /// <summary>
    /// Scans the existing append-only audit file and returns every
    /// ESCROW record that has no matching resolution.
    ///
    /// The returned records preserve their original physical file
    /// order.
    ///
    /// This method does not modify the file, call hardware, or
    /// automatically resolve an orphan.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the audit file contains malformed, duplicated,
    /// mismatched, or out-of-order lifecycle records.
    ///
    /// Audit corruption is not silently ignored.
    /// </exception>
    public IReadOnlyList<UnresolvedEscrowRecord>
        FindUnresolvedEscrows()
    {
        lock (_writeSync)
        {
            if (
                !File.Exists(
                    _filePath
                )
            )
            {
                return Array.Empty<
                    UnresolvedEscrowRecord
                >();
            }

            return FindUnresolvedEscrowsCore();
        }
    }

    /// <summary>
    /// Reads and validates one stable audit-file snapshot.
    ///
    /// The caller must hold _writeSync.
    /// </summary>
    private IReadOnlyList<UnresolvedEscrowRecord>
        FindUnresolvedEscrowsCore()
    {
        var escrowOrder =
            new List<UnresolvedEscrowRecord>();

        var pendingEscrows =
            new Dictionary<
                Guid,
                UnresolvedEscrowRecord
            >();

        var seenEscrowIds =
            new HashSet<Guid>();

        var resolvedEscrowIds =
            new HashSet<Guid>();

        using var stream =
            new FileStream(
                _filePath,
                new FileStreamOptions
                {
                    Mode =
                        FileMode.Open,

                    Access =
                        FileAccess.Read,

                    /*
                     * Prevent another writer from changing the file
                     * while reconciliation reads it.
                     */
                    Share =
                        FileShare.Read,

                    BufferSize =
                        4096,

                    Options =
                        FileOptions.SequentialScan
                }
            );

        using var reader =
            new StreamReader(
                stream,
                Utf8WithoutBom,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: false
            );

        var lineNumber =
            0;

        while (
            reader.ReadLine() is
            {
            } line
        )
        {
            lineNumber++;

            if (
                line.Length ==
                0
            )
            {
                throw CreateInvalidRecordException(
                    lineNumber,
                    "Empty audit records are not allowed."
                );
            }

            var parsedRecord =
                ParseRecord(
                    line,
                    lineNumber
                );

            if (
                parsedRecord.RecordType ==
                EscrowRecordType
            )
            {
                if (
                    !seenEscrowIds.Add(
                        parsedRecord.AuditId
                    )
                )
                {
                    throw CreateInvalidRecordException(
                        lineNumber,
                        "The escrow audit identifier is duplicated."
                    );
                }

                var unresolvedRecord =
                    new UnresolvedEscrowRecord(
                        parsedRecord.AuditId,
                        parsedRecord.TimestampUtc,
                        parsedRecord.Note
                    );

                pendingEscrows.Add(
                    parsedRecord.AuditId,
                    unresolvedRecord
                );

                escrowOrder.Add(
                    unresolvedRecord
                );

                continue;
            }

            if (
                resolvedEscrowIds.Contains(
                    parsedRecord.AuditId
                )
            )
            {
                throw CreateInvalidRecordException(
                    lineNumber,
                    "The escrow record has more than one resolution."
                );
            }

            if (
                !pendingEscrows.TryGetValue(
                    parsedRecord.AuditId,
                    out var pendingEscrow
                )
            )
            {
                throw CreateInvalidRecordException(
                    lineNumber,
                    "A resolution exists without an earlier " +
                    "matching escrow record."
                );
            }

            if (
                pendingEscrow.Note !=
                parsedRecord.Note
            )
            {
                throw CreateInvalidRecordException(
                    lineNumber,
                    "The resolution note does not match its " +
                    "escrow record."
                );
            }

            pendingEscrows.Remove(
                parsedRecord.AuditId
            );

            resolvedEscrowIds.Add(
                parsedRecord.AuditId
            );
        }

        var unresolvedEscrows =
            new List<UnresolvedEscrowRecord>(
                pendingEscrows.Count
            );

        foreach (
            var escrowRecord in
            escrowOrder
        )
        {
            if (
                pendingEscrows.ContainsKey(
                    escrowRecord.AuditId
                )
            )
            {
                unresolvedEscrows.Add(
                    escrowRecord
                );
            }
        }

        return unresolvedEscrows;
    }

    /// <summary>
    /// Appends one validated resolution record.
    /// </summary>
    private void AppendResolution(
        string recordType,
        Guid escrowAuditId,
        Money note)
    {
        if (
            escrowAuditId ==
            Guid.Empty
        )
        {
            throw new ArgumentException(
                "The escrow audit identifier cannot be empty.",
                nameof(escrowAuditId)
            );
        }

        ValidateNote(
            note
        );

        lock (_writeSync)
        {
            AppendRecordCore(
                FormatRecord(
                    recordType,
                    escrowAuditId,
                    GetCurrentUtcTimestamp(),
                    note
                )
            );
        }
    }

    /// <summary>
    /// Returns the current timestamp normalized to UTC.
    /// </summary>
    private DateTimeOffset GetCurrentUtcTimestamp()
    {
        return _timeProvider
            .GetUtcNow()
            .ToUniversalTime();
    }

    /// <summary>
    /// Creates the deterministic text representation of one
    /// hardware audit record.
    /// </summary>
    private static string FormatRecord(
        string recordType,
        Guid auditId,
        DateTimeOffset timestamp,
        Money note)
    {
        return string.Join(
            '|',
            recordType,
            auditId.ToString(
                "D",
                CultureInfo.InvariantCulture
            ),
            timestamp.ToString(
                "O",
                CultureInfo.InvariantCulture
            ),
            FormatCurrency(
                note.Currency
            ),
            FormatAmount(
                note
            )
        );
    }

    /// <summary>
    /// Parses and strictly validates one deterministic audit line.
    /// </summary>
    private static ParsedAuditRecord ParseRecord(
        string line,
        int lineNumber)
    {
        var fields =
            line.Split(
                '|'
            );

        if (
            fields.Length !=
            5
        )
        {
            throw CreateInvalidRecordException(
                lineNumber,
                "An audit record must contain exactly five fields."
            );
        }

        var recordType =
            fields[0];

        if (
            recordType !=
                EscrowRecordType &&
            recordType !=
                CommittedToVaultRecordType &&
            recordType !=
                RejectedRecordType
        )
        {
            throw CreateInvalidRecordException(
                lineNumber,
                "The audit record type is unsupported."
            );
        }

        if (
            !Guid.TryParseExact(
                fields[1],
                "D",
                out var auditId
            ) ||
            auditId ==
                Guid.Empty ||
            auditId.ToString(
                "D",
                CultureInfo.InvariantCulture
            ) !=
                fields[1]
        )
        {
            throw CreateInvalidRecordException(
                lineNumber,
                "The audit identifier is invalid or non-canonical."
            );
        }

        if (
            !DateTimeOffset.TryParseExact(
                fields[2],
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var timestamp
            ) ||
            timestamp.Offset !=
                TimeSpan.Zero ||
            timestamp.ToString(
                "O",
                CultureInfo.InvariantCulture
            ) !=
                fields[2]
        )
        {
            throw CreateInvalidRecordException(
                lineNumber,
                "The timestamp is invalid, non-UTC, or non-canonical."
            );
        }

        var currency =
            fields[3] switch
            {
                "USD" =>
                    CurrencyCode.Usd,

                "KHR" =>
                    CurrencyCode.Khr,

                _ =>
                    throw CreateInvalidRecordException(
                        lineNumber,
                        "The currency field is unsupported."
                    )
            };

        if (
            !decimal.TryParse(
                fields[4],
                NumberStyles.AllowLeadingSign |
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var amount
            )
        )
        {
            throw CreateInvalidRecordException(
                lineNumber,
                "The note amount is invalid."
            );
        }

        Money note;

        try
        {
            note =
                new Money(
                    amount,
                    currency
                );

            ValidateNote(
                note
            );
        }
        catch (
            Exception exception
        ) when (
            exception is
                ArgumentException ||
            exception is
                ArgumentOutOfRangeException
        )
        {
            throw CreateInvalidRecordException(
                lineNumber,
                "The note amount is not valid for its currency.",
                exception
            );
        }

        if (
            FormatAmount(
                note
            ) !=
            fields[4]
        )
        {
            throw CreateInvalidRecordException(
                lineNumber,
                "The note amount is not canonically formatted."
            );
        }

        return new ParsedAuditRecord(
            recordType,
            auditId,
            timestamp,
            note
        );
    }

    /// <summary>
    /// Appends one UTF-8 record and forces it to durable storage.
    ///
    /// The caller must hold _writeSync.
    /// </summary>
    private void AppendRecordCore(
        string record)
    {
        var recordBytes =
            Utf8WithoutBom.GetBytes(
                record +
                RecordLineEnding
            );

        using var stream =
            new FileStream(
                _filePath,
                new FileStreamOptions
                {
                    Mode =
                        FileMode.Append,

                    Access =
                        FileAccess.Write,

                    Share =
                        FileShare.Read,

                    /*
                     * Avoid retaining audit records in a large
                     * managed userspace buffer.
                     */
                    BufferSize =
                        1,

                    Options =
                        FileOptions.WriteThrough
                }
            );

        stream.Write(
            recordBytes
        );

        /*
         * Force the operating system to flush the record to the
         * underlying storage device before this method returns.
         */
        stream.Flush(
            flushToDisk: true
        );
    }

    /// <summary>
    /// Validates that the audit value represents a physical note.
    /// </summary>
    private static void ValidateNote(
        Money note)
    {
        if (note.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(note),
                note.Amount,
                "A hardware audit note amount must be greater than zero."
            );
        }
    }

    private static string FormatCurrency(
        CurrencyCode currency)
    {
        return currency switch
        {
            CurrencyCode.Usd =>
                "USD",

            CurrencyCode.Khr =>
                "KHR",

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(currency),
                    currency,
                    "Unsupported hardware audit currency."
                )
        };
    }

    private static string FormatAmount(
        Money note)
    {
        return note.Currency switch
        {
            CurrencyCode.Usd =>
                note.Amount.ToString(
                    "F2",
                    CultureInfo.InvariantCulture
                ),

            CurrencyCode.Khr =>
                note.Amount.ToString(
                    "F0",
                    CultureInfo.InvariantCulture
                ),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(note),
                    note.Currency,
                    "Unsupported hardware audit currency."
                )
        };
    }

    private static InvalidDataException
        CreateInvalidRecordException(
            int lineNumber,
            string reason)
    {
        return new InvalidDataException(
            $"Invalid hardware audit record at line " +
            $"{lineNumber}: {reason}"
        );
    }

    private static InvalidDataException
        CreateInvalidRecordException(
            int lineNumber,
            string reason,
            Exception innerException)
    {
        return new InvalidDataException(
            $"Invalid hardware audit record at line " +
            $"{lineNumber}: {reason}",
            innerException
        );
    }

    /// <summary>
    /// Internal parsed representation used only during
    /// reconciliation.
    /// </summary>
    private sealed record ParsedAuditRecord(
        string RecordType,
        Guid AuditId,
        DateTimeOffset TimestampUtc,
        Money Note
    );
}