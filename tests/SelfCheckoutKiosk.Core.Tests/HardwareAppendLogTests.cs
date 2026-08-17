using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.ValueObjects;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

public sealed class HardwareAppendLogTests
{
    [Fact]
    public void AppendEscrow_WritesDeterministicUtf8Record()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        var auditId =
            Guid.Parse(
                "4f6b5270-7a4f-4db4-9348-83f34e913bc6"
            );

        var timestamp =
            new DateTimeOffset(
                year: 2026,
                month: 8,
                day: 5,
                hour: 6,
                minute: 20,
                second: 0,
                offset: TimeSpan.Zero
            );

        var log =
            new HardwareAppendLog(
                temporaryFile.FilePath,
                new SequenceTimeProvider(
                    timestamp
                ),
                () =>
                    auditId
            );

        var returnedAuditId =
            log.AppendEscrow(
                Money.Usd(
                    10m
                )
            );

        Assert.Equal(
            auditId,
            returnedAuditId
        );

        var expected =
            "ESCROW|" +
            "4f6b5270-7a4f-4db4-9348-83f34e913bc6|" +
            "2026-08-05T06:20:00.0000000+00:00|" +
            "USD|10.00\n";

        var expectedBytes =
            Encoding.UTF8.GetBytes(
                expected
            );

        var actualBytes =
            File.ReadAllBytes(
                temporaryFile.FilePath
            );

        /*
         * Exact byte equality verifies:
         *
         * - deterministic field order;
         * - UTF-8 without BOM;
         * - invariant formatting;
         * - fixed LF line ending.
         */
        Assert.Equal(
            expectedBytes,
            actualBytes
        );
    }

    [Fact]
    public void AppendResolutionRecords_AppendsSupportedTypesInOrder()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        var firstAuditId =
            Guid.Parse(
                "11111111-1111-1111-1111-111111111111"
            );

        var secondAuditId =
            Guid.Parse(
                "22222222-2222-2222-2222-222222222222"
            );

        var auditIds =
            new Queue<Guid>(
                new[]
                {
                    firstAuditId,
                    secondAuditId
                }
            );

        var log =
            new HardwareAppendLog(
                temporaryFile.FilePath,
                new SequenceTimeProvider(
                    UtcTime(
                        minute: 20
                    ),
                    UtcTime(
                        minute: 21
                    ),
                    UtcTime(
                        minute: 22
                    ),
                    UtcTime(
                        minute: 23
                    )
                ),
                () =>
                    auditIds.Dequeue()
            );

        var usdNote =
            Money.Usd(
                10m
            );

        var khrNote =
            Money.Khr(
                10_000m
            );

        var createdFirstId =
            log.AppendEscrow(
                usdNote
            );

        var createdSecondId =
            log.AppendEscrow(
                khrNote
            );

        log.AppendCommittedToVault(
            createdFirstId,
            usdNote
        );

        log.AppendRejected(
            createdSecondId,
            khrNote
        );

        var lines =
            File.ReadAllLines(
                temporaryFile.FilePath
            );

        Assert.Equal(
            new[]
            {
                "ESCROW|" +
                "11111111-1111-1111-1111-111111111111|" +
                "2026-08-05T06:20:00.0000000+00:00|" +
                "USD|10.00",

                "ESCROW|" +
                "22222222-2222-2222-2222-222222222222|" +
                "2026-08-05T06:21:00.0000000+00:00|" +
                "KHR|10000",

                "COMMITTED_TO_VAULT|" +
                "11111111-1111-1111-1111-111111111111|" +
                "2026-08-05T06:22:00.0000000+00:00|" +
                "USD|10.00",

                "REJECTED|" +
                "22222222-2222-2222-2222-222222222222|" +
                "2026-08-05T06:23:00.0000000+00:00|" +
                "KHR|10000"
            },
            lines
        );
    }

    [Fact]
    public void FindUnresolvedEscrows_ReturnsOnlyOrphansInEscrowOrder()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        var firstAuditId =
            Guid.Parse(
                "11111111-1111-1111-1111-111111111111"
            );

        var secondAuditId =
            Guid.Parse(
                "22222222-2222-2222-2222-222222222222"
            );

        var thirdAuditId =
            Guid.Parse(
                "33333333-3333-3333-3333-333333333333"
            );

        var auditIds =
            new Queue<Guid>(
                new[]
                {
                    firstAuditId,
                    secondAuditId,
                    thirdAuditId
                }
            );

        var firstTime =
            UtcTime(
                minute: 20
            );

        var secondTime =
            UtcTime(
                minute: 21
            );

        var thirdTime =
            UtcTime(
                minute: 22
            );

        var log =
            new HardwareAppendLog(
                temporaryFile.FilePath,
                new SequenceTimeProvider(
                    firstTime,
                    secondTime,
                    thirdTime,
                    UtcTime(
                        minute: 23
                    )
                ),
                () =>
                    auditIds.Dequeue()
            );

        var firstNote =
            Money.Usd(
                5m
            );

        var secondNote =
            Money.Usd(
                10m
            );

        var thirdNote =
            Money.Khr(
                20_000m
            );

        log.AppendEscrow(
            firstNote
        );

        var resolvedAuditId =
            log.AppendEscrow(
                secondNote
            );

        log.AppendEscrow(
            thirdNote
        );

        log.AppendCommittedToVault(
            resolvedAuditId,
            secondNote
        );

        var unresolved =
            log.FindUnresolvedEscrows();

        Assert.Equal(
            2,
            unresolved.Count
        );

        Assert.Equal(
            firstAuditId,
            unresolved[0].AuditId
        );

        Assert.Equal(
            firstTime,
            unresolved[0].EscrowedAtUtc
        );

        Assert.Equal(
            firstNote,
            unresolved[0].Note
        );

        Assert.Equal(
            thirdAuditId,
            unresolved[1].AuditId
        );

        Assert.Equal(
            thirdTime,
            unresolved[1].EscrowedAtUtc
        );

        Assert.Equal(
            thirdNote,
            unresolved[1].Note
        );
    }

    [Fact]
    public void FindUnresolvedEscrows_WhenFileDoesNotExist_ReturnsEmpty()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        var log =
            new HardwareAppendLog(
                temporaryFile.FilePath
            );

        var unresolved =
            log.FindUnresolvedEscrows();

        Assert.Empty(
            unresolved
        );

        Assert.False(
            File.Exists(
                temporaryFile.FilePath
            )
        );
    }

    [Fact]
    public void FindUnresolvedEscrows_ResolutionWithoutEscrow_Throws()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        var log =
            CreateLog(
                temporaryFile.FilePath,
                Guid.Parse(
                    "11111111-1111-1111-1111-111111111111"
                ),
                requiredTimestampCount: 1
            );

        log.AppendRejected(
            Guid.Parse(
                "22222222-2222-2222-2222-222222222222"
            ),
            Money.Usd(
                10m
            )
        );

        var exception =
            Assert.Throws<InvalidDataException>(
                () =>
                    log.FindUnresolvedEscrows()
            );

        Assert.Contains(
            "without an earlier matching escrow record",
            exception.Message
        );
    }

    [Fact]
    public void FindUnresolvedEscrows_DuplicateEscrowIdentifier_Throws()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        var duplicateAuditId =
            Guid.Parse(
                "11111111-1111-1111-1111-111111111111"
            );

        var log =
            new HardwareAppendLog(
                temporaryFile.FilePath,
                new SequenceTimeProvider(
                    UtcTime(
                        minute: 20
                    ),
                    UtcTime(
                        minute: 21
                    )
                ),
                () =>
                    duplicateAuditId
            );

        log.AppendEscrow(
            Money.Usd(
                5m
            )
        );

        log.AppendEscrow(
            Money.Usd(
                10m
            )
        );

        var exception =
            Assert.Throws<InvalidDataException>(
                () =>
                    log.FindUnresolvedEscrows()
            );

        Assert.Contains(
            "audit identifier is duplicated",
            exception.Message
        );
    }

    [Fact]
    public void FindUnresolvedEscrows_MultipleResolutions_Throws()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        var auditId =
            Guid.Parse(
                "11111111-1111-1111-1111-111111111111"
            );

        var log =
            CreateLog(
                temporaryFile.FilePath,
                auditId,
                requiredTimestampCount: 3
            );

        var note =
            Money.Usd(
                10m
            );

        var createdAuditId =
            log.AppendEscrow(
                note
            );

        log.AppendRejected(
            createdAuditId,
            note
        );

        log.AppendCommittedToVault(
            createdAuditId,
            note
        );

        var exception =
            Assert.Throws<InvalidDataException>(
                () =>
                    log.FindUnresolvedEscrows()
            );

        Assert.Contains(
            "more than one resolution",
            exception.Message
        );
    }

    [Fact]
    public void FindUnresolvedEscrows_ResolutionNoteMismatch_Throws()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        var auditId =
            Guid.Parse(
                "11111111-1111-1111-1111-111111111111"
            );

        var log =
            CreateLog(
                temporaryFile.FilePath,
                auditId,
                requiredTimestampCount: 2
            );

        var createdAuditId =
            log.AppendEscrow(
                Money.Usd(
                    10m
                )
            );

        log.AppendRejected(
            createdAuditId,
            Money.Usd(
                20m
            )
        );

        var exception =
            Assert.Throws<InvalidDataException>(
                () =>
                    log.FindUnresolvedEscrows()
            );

        Assert.Contains(
            "does not match its escrow record",
            exception.Message
        );
    }

    [Fact]
    public void FindUnresolvedEscrows_MalformedRecord_Throws()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        Directory.CreateDirectory(
            temporaryFile.DirectoryPath
        );

        File.WriteAllText(
            temporaryFile.FilePath,
            "ESCROW|broken\n",
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false
            )
        );

        var log =
            new HardwareAppendLog(
                temporaryFile.FilePath
            );

        var exception =
            Assert.Throws<InvalidDataException>(
                () =>
                    log.FindUnresolvedEscrows()
            );

        Assert.Contains(
            "exactly five fields",
            exception.Message
        );
    }

    [Fact]
    public void AppendMethods_InvalidValues_ThrowWithoutWritingRecord()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        var log =
            CreateLog(
                temporaryFile.FilePath,
                Guid.Parse(
                    "11111111-1111-1111-1111-111111111111"
                ),
                requiredTimestampCount: 1
            );

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                log.AppendEscrow(
                    Money.Usd(
                        0m
                    )
                )
        );

        Assert.Throws<ArgumentException>(
            () =>
                log.AppendRejected(
                    Guid.Empty,
                    Money.Usd(
                        1m
                    )
                )
        );

        Assert.False(
            File.Exists(
                temporaryFile.FilePath
            )
        );
    }

    private static HardwareAppendLog CreateLog(
        string filePath,
        Guid auditId,
        int requiredTimestampCount)
    {
        var timestamps =
            new DateTimeOffset[
                requiredTimestampCount
            ];

        for (
            var index = 0;
            index <
                timestamps.Length;
            index++
        )
        {
            timestamps[index] =
                UtcTime(
                    minute:
                        20 +
                        index
                );
        }

        return new HardwareAppendLog(
            filePath,
            new SequenceTimeProvider(
                timestamps
            ),
            () =>
                auditId
        );
    }

    private static DateTimeOffset UtcTime(
        int minute)
    {
        return new DateTimeOffset(
            year: 2026,
            month: 8,
            day: 5,
            hour: 6,
            minute: minute,
            second: 0,
            offset: TimeSpan.Zero
        );
    }

    private sealed class SequenceTimeProvider
        : TimeProvider
    {
        private readonly Queue<DateTimeOffset>
            _timestamps;

        public SequenceTimeProvider(
            params DateTimeOffset[] timestamps)
        {
            ArgumentNullException.ThrowIfNull(
                timestamps
            );

            _timestamps =
                new Queue<DateTimeOffset>(
                    timestamps
                );
        }

        public override DateTimeOffset GetUtcNow()
        {
            if (
                _timestamps.Count ==
                0
            )
            {
                throw new InvalidOperationException(
                    "No test timestamp remains."
                );
            }

            return _timestamps
                .Dequeue();
        }
    }

    private sealed class TemporaryAuditFile
        : IDisposable
    {
        public TemporaryAuditFile()
        {
            DirectoryPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "SelfCheckoutKiosk.Core.Tests",
                    Guid.NewGuid()
                        .ToString(
                            "N"
                        )
                );

            FilePath =
                Path.Combine(
                    DirectoryPath,
                    "hardware-append.log"
                );
        }

        public string DirectoryPath
        {
            get;
        }

        public string FilePath
        {
            get;
        }

        public void Dispose()
        {
            if (
                Directory.Exists(
                    DirectoryPath
                )
            )
            {
                Directory.Delete(
                    DirectoryPath,
                    recursive: true
                );
            }
        }
    }
}
