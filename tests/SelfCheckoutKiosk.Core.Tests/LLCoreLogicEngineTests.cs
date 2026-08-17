using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

public sealed class LLCoreLogicEngineTests
{
    private const string ValidEan13 =
        "4006381333931";

    private const decimal UsdToKhrRate =
        4100m;

    private static readonly DateTimeOffset
        AuditTimestamp =
            new(
                year: 2026,
                month: 8,
                day: 5,
                hour: 7,
                minute: 0,
                second: 0,
                offset: TimeSpan.Zero
            );

    [Fact]
    public async Task InitializeAsync_InitializesAndSubscribesExactlyOnce()
    {
        var fixture =
            CreateFixture();

        await fixture.Engine
            .InitializeAsync();

        await fixture.Engine
            .InitializeAsync();

        Assert.Equal(
            1,
            fixture.LicenseManager
                .LoadAndValidateCallCount
        );

        Assert.Single(
            fixture.LicenseManager
                .EnforcedFeatures
        );

        Assert.Equal(
            LicensedFeature.CashRecycler,
            fixture.LicenseManager
                .EnforcedFeatures[0]
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .ConnectCallCount
        );

        Assert.Equal(
            1,
            fixture.Scanner
                .ConnectCallCount
        );

        Assert.Equal(
            1,
            fixture.Printer
                .ConnectCallCount
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .NoteInEscrowSubscriptionCount
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .FaultSubscriptionCount
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .EscrowResolvedSubscriptionCount
        );

        Assert.Equal(
            1,
            fixture.Scanner
                .BarcodeSubscriptionCount
        );

        Assert.Equal(
            1,
            fixture.Printer
                .JobStatusSubscriptionCount
        );

        Assert.Equal(
            KioskState.Idle,
            fixture.Engine.CurrentState
        );
    }

    [Fact]
    public async Task InitializeAsync_ConcurrentCalls_InitializeOnce()
    {
        var fixture =
            CreateFixture();

        await Task.WhenAll(
            fixture.Engine.InitializeAsync(),
            fixture.Engine.InitializeAsync(),
            fixture.Engine.InitializeAsync(),
            fixture.Engine.InitializeAsync(),
            fixture.Engine.InitializeAsync()
        );

        Assert.Equal(
            1,
            fixture.LicenseManager
                .LoadAndValidateCallCount
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .ConnectCallCount
        );

        Assert.Equal(
            1,
            fixture.Scanner
                .ConnectCallCount
        );

        Assert.Equal(
            1,
            fixture.Printer
                .ConnectCallCount
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .NoteInEscrowSubscriptionCount
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .FaultSubscriptionCount
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .EscrowResolvedSubscriptionCount
        );

        Assert.Equal(
            1,
            fixture.Scanner
                .BarcodeSubscriptionCount
        );
    }

    [Fact]
    public async Task InitializeAsync_AfterPartialFailure_RetriesSafely()
    {
        var fixture =
            CreateFixture();

        fixture.Scanner
            .ConnectFailuresRemaining =
                1;

        await Assert.ThrowsAsync<
            InvalidOperationException
        >(
            () =>
                fixture.Engine
                    .InitializeAsync()
        );

        await fixture.Engine
            .InitializeAsync();

        Assert.Equal(
            2,
            fixture.LicenseManager
                .LoadAndValidateCallCount
        );

        /*
         * Cash connected before the scanner failed.
         * It must not connect again during retry.
         */
        Assert.Equal(
            1,
            fixture.CashRecycler
                .ConnectCallCount
        );

        Assert.Equal(
            2,
            fixture.Scanner
                .ConnectCallCount
        );

        Assert.Equal(
            1,
            fixture.Printer
                .ConnectCallCount
        );

        /*
         * Event subscriptions must still be attached only once.
         */
        Assert.Equal(
            1,
            fixture.CashRecycler
                .NoteInEscrowSubscriptionCount
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .FaultSubscriptionCount
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .EscrowResolvedSubscriptionCount
        );

        Assert.Equal(
            1,
            fixture.Scanner
                .BarcodeSubscriptionCount
        );

        Assert.Equal(
            1,
            fixture.Printer
                .JobStatusSubscriptionCount
        );
    }

    [Fact]
    public async Task SubmitScanAsync_ValidEan13_TransitionsToScanning()
    {
        var fixture =
            CreateFixture();

        KioskStateChangedEventArgs?
            stateChange =
                null;

        fixture.Engine.OnStateChanged +=
            (_, eventArgs) =>
                stateChange =
                    eventArgs;

        var result =
            await fixture.Engine
                .SubmitScanAsync(
                    ValidEan13
                );

        Assert.True(
            result.Accepted
        );

        Assert.Equal(
            ScanCategory.Ean13Product,
            result.Category
        );

        Assert.Equal(
            KioskState.Scanning,
            fixture.Engine.CurrentState
        );

        Assert.NotNull(
            stateChange
        );

        Assert.Equal(
            KioskState.Idle,
            stateChange.Previous
        );

        Assert.Equal(
            KioskState.Scanning,
            stateChange.Current
        );
    }

    [Fact]
    public async Task SubmitScanAsync_SecondEan13_DoesNotRaiseAnotherStateChange()
    {
        var fixture =
            CreateFixture();

        var stateChangeCount =
            0;

        fixture.Engine.OnStateChanged +=
            (_, _) =>
                stateChangeCount++;

        await fixture.Engine
            .SubmitScanAsync(
                ValidEan13
            );

        var secondResult =
            await fixture.Engine
                .SubmitScanAsync(
                    "5012345678900"
                );

        Assert.True(
            secondResult.Accepted
        );

        Assert.Equal(
            KioskState.Scanning,
            fixture.Engine.CurrentState
        );

        Assert.Equal(
            1,
            stateChangeCount
        );
    }

    [Fact]
    public async Task SubmitScanAsync_UnknownScan_IsRejected()
    {
        var fixture =
            CreateFixture();

        var result =
            await fixture.Engine
                .SubmitScanAsync(
                    "not-a-valid-scan"
                );

        Assert.False(
            result.Accepted
        );

        Assert.Equal(
            ScanCategory.Unknown,
            result.Category
        );

        Assert.Equal(
            KioskState.Idle,
            fixture.Engine.CurrentState
        );
    }

    [Fact]
    public async Task SubmitScanAsync_TrimsScannerLineTerminators()
    {
        var fixture =
            CreateFixture();

        var result =
            await fixture.Engine
                .SubmitScanAsync(
                    ValidEan13 +
                    "\r\n"
                );

        Assert.True(
            result.Accepted
        );

        Assert.Equal(
            ScanCategory.Ean13Product,
            result.Category
        );

        Assert.Equal(
            KioskState.Scanning,
            fixture.Engine.CurrentState
        );
    }

    [Fact]
    public async Task SubmitScanAsync_WhileAwaitingPayment_Throws()
    {
        var fixture =
            CreateFixture();

        await fixture.Engine
            .SubmitScanAsync(
                ValidEan13
            );

        await fixture.Engine
            .SelectPaymentMethodAsync(
                PaymentMethod.KhqrDigital
            );

        await Assert.ThrowsAsync<
            InvalidOperationException
        >(
            () =>
                fixture.Engine
                    .SubmitScanAsync(
                        "5012345678900"
                    )
        );
    }

    [Fact]
    public async Task SelectPaymentMethodAsync_Cash_ArmsRecyclerAndTransitions()
    {
        var fixture =
            CreateFixture();

        var states =
            new List<KioskState>();

        fixture.Engine.OnStateChanged +=
            (_, eventArgs) =>
                states.Add(
                    eventArgs.Current
                );

        await fixture.Engine
            .SubmitScanAsync(
                ValidEan13
            );

        await fixture.Engine
            .SelectPaymentMethodAsync(
                PaymentMethod.Cash
            );

        Assert.Equal(
            KioskState.ProcessingCash,
            fixture.Engine.CurrentState
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .ArmAcceptanceCallCount
        );

        Assert.Equal(
            new[]
            {
                KioskState.Scanning,
                KioskState.AwaitingPayment,
                KioskState.ProcessingCash
            },
            states
        );
    }

    [Fact]
    public async Task SelectPaymentMethodAsync_Khqr_RemainsAwaitingPayment()
    {
        var fixture =
            CreateFixture();

        await fixture.Engine
            .SubmitScanAsync(
                ValidEan13
            );

        await fixture.Engine
            .SelectPaymentMethodAsync(
                PaymentMethod.KhqrDigital
            );

        Assert.Equal(
            KioskState.AwaitingPayment,
            fixture.Engine.CurrentState
        );

        Assert.Equal(
            0,
            fixture.CashRecycler
                .ArmAcceptanceCallCount
        );
    }

    [Fact]
    public async Task SelectPaymentMethodAsync_FromIdle_Throws()
    {
        var fixture =
            CreateFixture();

        await Assert.ThrowsAsync<
            InvalidOperationException
        >(
            () =>
                fixture.Engine
                    .SelectPaymentMethodAsync(
                        PaymentMethod.Cash
                    )
        );
    }

    [Fact]
    public async Task SelectPaymentMethodAsync_InvalidValue_Throws()
    {
        var fixture =
            CreateFixture();

        await Assert.ThrowsAsync<
            ArgumentOutOfRangeException
        >(
            () =>
                fixture.Engine
                    .SelectPaymentMethodAsync(
                        (PaymentMethod)999
                    )
        );
    }

    [Fact]
    public async Task BeginCashPaymentAsync_StartsExactCashSession()
    {
        var fixture =
            CreateFixture();

        await fixture.Engine
            .InitializeAsync();

        await fixture.Engine
            .SubmitScanAsync(
                ValidEan13
            );

        var balanceEventCount =
            0;

        fixture.Engine.OnBalanceChanged +=
            (_, _) =>
                balanceEventCount++;

        await fixture.Engine
            .BeginCashPaymentAsync(
                Money.Usd(
                    2m
                ),
                UsdToKhrRate
            );

        Assert.Equal(
            KioskState.ProcessingCash,
            fixture.Engine.CurrentState
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .ArmAcceptanceCallCount
        );

        /*
         * Starting the session publishes the initial balance.
         */
        Assert.Equal(
            1,
            balanceEventCount
        );

        Assert.Equal(
            0,
            fixture.CashRecycler
                .CommitEscrowedNoteCallCount
        );

        Assert.Equal(
            0,
            fixture.CashRecycler
                .RejectEscrowedNoteCallCount
        );
    }

    [Fact]
    public async Task BeginCashPaymentAsync_FromIdle_Throws()
    {
        var fixture =
            CreateFixture();

        await fixture.Engine
            .InitializeAsync();

        await Assert.ThrowsAsync<
            InvalidOperationException
        >(
            () =>
                fixture.Engine
                    .BeginCashPaymentAsync(
                        Money.Usd(
                            2m
                        ),
                        UsdToKhrRate
                    )
        );
    }

    [Fact]
    public async Task BeginCashPaymentAsync_NonUsdTotal_Throws()
    {
        var fixture =
            CreateFixture();

        await fixture.Engine
            .InitializeAsync();

        await fixture.Engine
            .SubmitScanAsync(
                ValidEan13
            );

        var exception =
            await Assert.ThrowsAsync<
                ArgumentException
            >(
                () =>
                    fixture.Engine
                        .BeginCashPaymentAsync(
                            Money.Khr(
                                8200m
                            ),
                            UsdToKhrRate
                        )
            );

        Assert.Equal(
            "transactionTotalUsd",
            exception.ParamName
        );
    }

    [Fact]
    public async Task ExactCash_KhrNote_UpdatesBalanceOnlyAfterPhysicalCommit()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        var auditId =
            Guid.Parse(
                "7503775a-ed59-4971-a2ae-bbf9c776de01"
            );

        var hardwareAppendLog =
            CreateHardwareAppendLog(
                temporaryFile.FilePath,
                auditId
            );

        var fixture =
            CreateFixture(
                hardwareAppendLog
            );

        await fixture.Engine
            .InitializeAsync();

        await fixture.Engine
            .SubmitScanAsync(
                ValidEan13
            );

        var balanceEventCount =
            0;

        var secondBalanceEvent =
            new TaskCompletionSource<object?>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously
            );

        fixture.Engine.OnBalanceChanged +=
            (_, _) =>
            {
                balanceEventCount++;

                if (
                    balanceEventCount ==
                    2
                )
                {
                    secondBalanceEvent
                        .TrySetResult(
                            null
                        );
                }
            };

        await fixture.Engine
            .BeginCashPaymentAsync(
                Money.Usd(
                    2m
                ),
                UsdToKhrRate
            );

        /*
         * $2.00 × 4,100 = 8,200 KHR target.
         */
        fixture.CashRecycler
            .RaiseNoteInEscrow(
                Money.Khr(
                    5000m
                )
            );

        await fixture.CashRecycler
            .WaitForCommitCommandAsync();

        Assert.Equal(
            1,
            fixture.CashRecycler
                .CommitEscrowedNoteCallCount
        );

        /*
         * The note has only received a commit command.
         *
         * The accepted balance must not change before the hardware
         * confirms physical vault commitment.
         */
        Assert.Equal(
            1,
            balanceEventCount
        );

        Assert.Equal(
            KioskState.ProcessingCash,
            fixture.Engine.CurrentState
        );

        fixture.CashRecycler
            .RaiseEscrowResolved(
                Money.Khr(
                    5000m
                ),
                CashEscrowResolution
                    .CommittedToVault
            );

        await secondBalanceEvent.Task
            .WaitAsync(
                TimeSpan.FromSeconds(
                    2
                )
            );

        Assert.Equal(
            2,
            balanceEventCount
        );

        Assert.Equal(
            KioskState.ProcessingCash,
            fixture.Engine.CurrentState
        );

        var contents =
            File.ReadAllText(
                temporaryFile.FilePath
            );

        Assert.Contains(
            "ESCROW|" +
            auditId.ToString(
                "D"
            ) +
            "|",
            contents
        );

        Assert.Contains(
            "COMMITTED_TO_VAULT|" +
            auditId.ToString(
                "D"
            ) +
            "|",
            contents
        );
    }

    [Fact]
    public async Task ExactCash_Overpayment_RejectsAndKeepsAcceptedBalance()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        var firstAuditId =
            Guid.Parse(
                "7503775a-ed59-4971-a2ae-bbf9c776de02"
            );

        var secondAuditId =
            Guid.Parse(
                "7503775a-ed59-4971-a2ae-bbf9c776de03"
            );

        var hardwareAppendLog =
            CreateHardwareAppendLog(
                temporaryFile.FilePath,
                firstAuditId,
                secondAuditId
            );

        var fixture =
            CreateFixture(
                hardwareAppendLog
            );

        await fixture.Engine
            .InitializeAsync();

        await fixture.Engine
            .SubmitScanAsync(
                ValidEan13
            );

        var balanceEventCount =
            0;

        CashNoteRejectedEventArgs? rejectedEventArgs =
            null;

        fixture.Engine.OnCashNoteRejected +=
            (_, e) =>
                rejectedEventArgs = e;

        var committedBalanceEvent =
            new TaskCompletionSource<object?>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously
            );

        var rejectedBalanceEvent =
            new TaskCompletionSource<object?>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously
            );

        fixture.Engine.OnBalanceChanged +=
            (_, _) =>
            {
                balanceEventCount++;

                if (
                    balanceEventCount ==
                    2
                )
                {
                    committedBalanceEvent
                        .TrySetResult(
                            null
                        );
                }

                if (
                    balanceEventCount ==
                    3
                )
                {
                    rejectedBalanceEvent
                        .TrySetResult(
                            null
                        );
                }
            };

        /*
         * $1.30 × 4,100 = 5,330 KHR.
         */
        await fixture.Engine
            .BeginCashPaymentAsync(
                Money.Usd(
                    1.30m
                ),
                UsdToKhrRate
            );

        /*
         * First note:
         *
         * 5,000 KHR < 5,330 KHR
         * → commit and continue.
         */
        fixture.CashRecycler
            .RaiseNoteInEscrow(
                Money.Khr(
                    5000m
                )
            );

        await fixture.CashRecycler
            .WaitForCommitCommandAsync();

        fixture.CashRecycler
            .RaiseEscrowResolved(
                Money.Khr(
                    5000m
                ),
                CashEscrowResolution
                    .CommittedToVault
            );

        await committedBalanceEvent.Task
            .WaitAsync(
                TimeSpan.FromSeconds(
                    2
                )
            );

        Assert.Equal(
            2,
            balanceEventCount
        );

        /*
         * Second note:
         *
         * 5,000 + 1,000 = 6,000 KHR
         * 6,000 > 5,330 (Overpayment by 670 > 500 tolerance)
         * → reject overpayment.
         */
        fixture.CashRecycler
            .RaiseNoteInEscrow(
                Money.Khr(
                    1000m
                )
            );

        await fixture.CashRecycler
            .WaitForRejectCommandAsync();

        Assert.Equal(
            1,
            fixture.CashRecycler
                .RejectEscrowedNoteCallCount
        );

        /*
         * The rejected note must not update the accepted balance
         * before physical rejection confirmation.
         */
        Assert.Equal(
            2,
            balanceEventCount
        );

        fixture.CashRecycler
            .RaiseEscrowResolved(
                Money.Khr(
                    1000m
                ),
                CashEscrowResolution
                    .Rejected
            );

        await rejectedBalanceEvent.Task
            .WaitAsync(
                TimeSpan.FromSeconds(
                    2
                )
            );

        Assert.Equal(
            3,
            balanceEventCount
        );

        Assert.NotNull(
            rejectedEventArgs
        );

        Assert.Equal(
            Money.Khr(
                1000m
            ),
            rejectedEventArgs!.Note
        );

        Assert.Equal(
            KioskState.ProcessingCash,
            fixture.Engine.CurrentState
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .CommitEscrowedNoteCallCount
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .RejectEscrowedNoteCallCount
        );

        Assert.Equal(
            0,
            fixture.CashRecycler
                .DisarmAcceptanceCallCount
        );

        Assert.Equal(
            0,
            fixture.CashRecycler
                .DispenseCallCount
        );

        var contents =
            File.ReadAllText(
                temporaryFile.FilePath
            );

        Assert.Contains(
            "COMMITTED_TO_VAULT|" +
            firstAuditId.ToString(
                "D"
            ) +
            "|",
            contents
        );

        Assert.Contains(
            "REJECTED|" +
            secondAuditId.ToString(
                "D"
            ) +
            "|",
            contents
        );
    }

    [Fact]
    public async Task ExactCash_MixedUsdAndKhr_ExactPaymentCompletesWithoutDispensing()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        var firstAuditId =
            Guid.Parse(
                "7503775a-ed59-4971-a2ae-bbf9c776de04"
            );

        var secondAuditId =
            Guid.Parse(
                "7503775a-ed59-4971-a2ae-bbf9c776de05"
            );

        var hardwareAppendLog =
            CreateHardwareAppendLog(
                temporaryFile.FilePath,
                firstAuditId,
                secondAuditId
            );

        var fixture =
            CreateFixture(
                hardwareAppendLog
            );

        await fixture.Engine
            .InitializeAsync();

        await fixture.Engine
            .SubmitScanAsync(
                ValidEan13
            );

        var balanceEventCount =
            0;

        var firstAcceptedBalance =
            new TaskCompletionSource<object?>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously
            );

        var transactionCompleted =
            new TaskCompletionSource<object?>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously
            );

        fixture.Engine.OnBalanceChanged +=
            (_, _) =>
            {
                balanceEventCount++;

                if (
                    balanceEventCount ==
                    2
                )
                {
                    firstAcceptedBalance
                        .TrySetResult(
                            null
                        );
                }
            };

        fixture.Engine.OnStateChanged +=
            (_, eventArgs) =>
            {
                if (
                    eventArgs.Current ==
                    KioskState.TransactionComplete
                )
                {
                    transactionCompleted
                        .TrySetResult(
                            null
                        );
                }
            };

        /*
         * $2.00 × 4,100 = 8,200 KHR target.
         */
        await fixture.Engine
            .BeginCashPaymentAsync(
                Money.Usd(
                    2m
                ),
                UsdToKhrRate
            );

        /*
         * First currency:
         *
         * $1.00 = 4,100 KHR.
         */
        fixture.CashRecycler
            .RaiseNoteInEscrow(
                Money.Usd(
                    1m
                )
            );

        await fixture.CashRecycler
            .WaitForCommitCommandAsync();

        fixture.CashRecycler
            .RaiseEscrowResolved(
                Money.Usd(
                    1m
                ),
                CashEscrowResolution
                    .CommittedToVault
            );

        await firstAcceptedBalance.Task
            .WaitAsync(
                TimeSpan.FromSeconds(
                    2
                )
            );

        Assert.Equal(
            KioskState.ProcessingCash,
            fixture.Engine.CurrentState
        );

        /*
         * Second currency:
         *
         * 4,100 KHR.
         *
         * This test verifies currency normalization and engine
         * workflow. Supported physical denominations will later be
         * supplied by the actual device configuration.
         */
        fixture.CashRecycler
            .RaiseNoteInEscrow(
                Money.Khr(
                    4100m
                )
            );

        await fixture.CashRecycler
            .WaitForCommitCommandAsync();

        fixture.CashRecycler
            .RaiseEscrowResolved(
                Money.Khr(
                    4100m
                ),
                CashEscrowResolution
                    .CommittedToVault
            );

        await transactionCompleted.Task
            .WaitAsync(
                TimeSpan.FromSeconds(
                    2
                )
            );

        Assert.Equal(
            KioskState.TransactionComplete,
            fixture.Engine.CurrentState
        );

        Assert.Equal(
            2,
            fixture.CashRecycler
                .CommitEscrowedNoteCallCount
        );

        Assert.Equal(
            0,
            fixture.CashRecycler
                .RejectEscrowedNoteCallCount
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .DisarmAcceptanceCallCount
        );

        /*
         * Acceptor-only exact payment skips DispensingChange and
         * never calls DispenseAsync.
         */
        Assert.Equal(
            0,
            fixture.CashRecycler
                .DispenseCallCount
        );

        Assert.Equal(
            3,
            balanceEventCount
        );

        var contents =
            File.ReadAllText(
                temporaryFile.FilePath
            );

        Assert.Contains(
            "ESCROW|" +
            firstAuditId.ToString(
                "D"
            ) +
            "|",
            contents
        );

        Assert.Contains(
            "COMMITTED_TO_VAULT|" +
            firstAuditId.ToString(
                "D"
            ) +
            "|",
            contents
        );

        Assert.Contains(
            "ESCROW|" +
            secondAuditId.ToString(
                "D"
            ) +
            "|",
            contents
        );

        Assert.Contains(
            "COMMITTED_TO_VAULT|" +
            secondAuditId.ToString(
                "D"
            ) +
            "|",
            contents
        );
    }

    [Fact]
    public async Task ExactCash_UnexpectedPhysicalResolution_FaultsEngine()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        var auditId =
            Guid.Parse(
                "7503775a-ed59-4971-a2ae-bbf9c776de06"
            );

        var hardwareAppendLog =
            CreateHardwareAppendLog(
                temporaryFile.FilePath,
                auditId
            );

        var fixture =
            CreateFixture(
                hardwareAppendLog
            );

        await fixture.Engine
            .InitializeAsync();

        await fixture.Engine
            .SubmitScanAsync(
                ValidEan13
            );

        await fixture.Engine
            .BeginCashPaymentAsync(
                Money.Usd(
                    2m
                ),
                UsdToKhrRate
            );

        var faultRaised =
            new TaskCompletionSource<
                HardwareFaultEventArgs
            >(
                TaskCreationOptions
                    .RunContinuationsAsynchronously
            );

        fixture.Engine.OnHardwareFault +=
            (_, eventArgs) =>
                faultRaised.TrySetResult(
                    eventArgs
                );

        /*
         * 5,000 KHR is below the 8,200-KHR target, so the engine
         * requests commitment.
         */
        fixture.CashRecycler
            .RaiseNoteInEscrow(
                Money.Khr(
                    5000m
                )
            );

        await fixture.CashRecycler
            .WaitForCommitCommandAsync();

        /*
         * The device reports rejection even though Core requested
         * commitment.
         */
        fixture.CashRecycler
            .RaiseEscrowResolved(
                Money.Khr(
                    5000m
                ),
                CashEscrowResolution
                    .Rejected
            );

        var fault =
            await faultRaised.Task
                .WaitAsync(
                    TimeSpan.FromSeconds(
                        2
                    )
                );

        Assert.Equal(
            KioskState.Faulted,
            fixture.Engine.CurrentState
        );

        Assert.Contains(
            "physical escrow outcome did not match",
            fault.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task ResetToIdleAsync_FromScanning_ReturnsToIdle()
    {
        var fixture =
            CreateFixture();

        await fixture.Engine
            .SubmitScanAsync(
                ValidEan13
            );

        await fixture.Engine
            .ResetToIdleAsync();

        Assert.Equal(
            KioskState.Idle,
            fixture.Engine.CurrentState
        );
    }

    [Fact]
    public async Task ResetToIdleAsync_FromAwaitingPayment_ReturnsToIdle()
    {
        var fixture =
            CreateFixture();

        await fixture.Engine
            .SubmitScanAsync(
                ValidEan13
            );

        await fixture.Engine
            .SelectPaymentMethodAsync(
                PaymentMethod.KhqrDigital
            );

        await fixture.Engine
            .ResetToIdleAsync();

        Assert.Equal(
            KioskState.Idle,
            fixture.Engine.CurrentState
        );
    }

    [Fact]
    public async Task ResetToIdleAsync_FromProcessingCash_Throws()
    {
        var fixture =
            CreateFixture();

        await fixture.Engine
            .SubmitScanAsync(
                ValidEan13
            );

        await fixture.Engine
            .SelectPaymentMethodAsync(
                PaymentMethod.Cash
            );

        await Assert.ThrowsAsync<
            InvalidOperationException
        >(
            () =>
                fixture.Engine
                    .ResetToIdleAsync()
        );

        Assert.Equal(
            KioskState.ProcessingCash,
            fixture.Engine.CurrentState
        );
    }

    [Fact]
    public async Task LowFloatTriggered_StopsCashAndEntersLockout()
    {
        var fixture =
            CreateFixture();

        /*
         * Calling twice also proves that initialization does not
         * duplicate the LowFloatMonitor subscription.
         */
        await fixture.Engine
            .InitializeAsync();

        await fixture.Engine
            .InitializeAsync();

        var triggered =
            new TaskCompletionSource<object?>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously
            );

        fixture.Engine.LowFloatStateTriggered +=
            (_, _) =>
                triggered.TrySetResult(
                    null
                );

        fixture.LowFloatMonitor
            .UpdateCount(
                denominationKhr: 100,
                count: 14
            );

        await triggered.Task
            .WaitAsync(
                TimeSpan.FromSeconds(
                    2
                )
            );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .StopAcceptingCashCallCount
        );

        Assert.Equal(
            KioskState.ExactCashOnlyLockout,
            fixture.Engine.CurrentState
        );
    }

    [Fact]
    public async Task LowFloatCleared_RaisesEventButRemainsLocked()
    {
        var fixture =
            CreateFixture();

        await fixture.Engine
            .InitializeAsync();

        var triggered =
            new TaskCompletionSource<object?>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously
            );

        var cleared =
            new TaskCompletionSource<object?>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously
            );

        fixture.Engine.LowFloatStateTriggered +=
            (_, _) =>
                triggered.TrySetResult(
                    null
                );

        fixture.Engine.LowFloatStateCleared +=
            (_, _) =>
                cleared.TrySetResult(
                    null
                );

        fixture.LowFloatMonitor
            .UpdateCount(
                denominationKhr: 100,
                count: 14
            );

        await triggered.Task
            .WaitAsync(
                TimeSpan.FromSeconds(
                    2
                )
            );

        fixture.LowFloatMonitor
            .UpdateCount(
                denominationKhr: 100,
                count: 15
            );

        await cleared.Task
            .WaitAsync(
                TimeSpan.FromSeconds(
                    2
                )
            );

        Assert.Equal(
            KioskState.ExactCashOnlyLockout,
            fixture.Engine.CurrentState
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .StopAcceptingCashCallCount
        );

        Assert.Equal(
            0,
            fixture.CashRecycler
                .ArmAcceptanceCallCount
        );
    }

    [Fact]
    public async Task CashRecyclerFault_TransitionsToFaultedAndForwardsEvent()
    {
        var fixture =
            CreateFixture();

        await fixture.Engine
            .InitializeAsync();

        var forwardedFault =
            new TaskCompletionSource<
                HardwareFaultEventArgs
            >(
                TaskCreationOptions
                    .RunContinuationsAsynchronously
            );

        fixture.Engine.OnHardwareFault +=
            (_, eventArgs) =>
                forwardedFault.TrySetResult(
                    eventArgs
                );

        var raisedFault =
            fixture.CashRecycler
                .RaiseFault(
                    device: "CashRecycler",
                    message: "Transport jam."
                );

        var receivedFault =
            await forwardedFault.Task
                .WaitAsync(
                    TimeSpan.FromSeconds(
                        2
                    )
                );

        Assert.Same(
            raisedFault,
            receivedFault
        );

        Assert.Equal(
            KioskState.Faulted,
            fixture.Engine.CurrentState
        );

        Assert.Equal(
            "CashRecycler",
            receivedFault.Device
        );

        Assert.Equal(
            "Transport jam.",
            receivedFault.Message
        );
    }

    [Fact]
    public async Task CashArmFailure_TransitionsToFaultedAndRaisesFault()
    {
        var fixture =
            CreateFixture();

        fixture.CashRecycler
            .ArmAcceptanceException =
                new InvalidOperationException(
                    "Arm command failed."
                );

        var raisedFault =
            new TaskCompletionSource<
                HardwareFaultEventArgs
            >(
                TaskCreationOptions
                    .RunContinuationsAsynchronously
            );

        fixture.Engine.OnHardwareFault +=
            (_, eventArgs) =>
                raisedFault.TrySetResult(
                    eventArgs
                );

        await fixture.Engine
            .SubmitScanAsync(
                ValidEan13
            );

        var exception =
            await Assert.ThrowsAsync<
                InvalidOperationException
            >(
                () =>
                    fixture.Engine
                        .SelectPaymentMethodAsync(
                            PaymentMethod.Cash
                        )
            );

        var fault =
            await raisedFault.Task
                .WaitAsync(
                    TimeSpan.FromSeconds(
                        2
                    )
                );

        Assert.Equal(
            "Arm command failed.",
            exception.Message
        );

        Assert.Equal(
            KioskState.Faulted,
            fixture.Engine.CurrentState
        );

        Assert.Equal(
            "CashRecycler",
            fault.Device
        );

        Assert.Contains(
            "Failed to arm cash acceptance",
            fault.Message
        );

        Assert.Contains(
            "Arm command failed.",
            fault.Message
        );
    }

    [Fact]
    public async Task LowFloatStopFailure_TransitionsToFaulted()
    {
        var fixture =
            CreateFixture();

        fixture.CashRecycler
            .StopAcceptingCashException =
                new InvalidOperationException(
                    "Stop command failed."
                );

        await fixture.Engine
            .InitializeAsync();

        var faultRaised =
            new TaskCompletionSource<
                HardwareFaultEventArgs
            >(
                TaskCreationOptions
                    .RunContinuationsAsynchronously
            );

        var lowFloatEventCount =
            0;

        fixture.Engine.OnHardwareFault +=
            (_, eventArgs) =>
                faultRaised.TrySetResult(
                    eventArgs
                );

        fixture.Engine.LowFloatStateTriggered +=
            (_, _) =>
                lowFloatEventCount++;

        fixture.LowFloatMonitor
            .UpdateCount(
                denominationKhr: 100,
                count: 14
            );

        var fault =
            await faultRaised.Task
                .WaitAsync(
                    TimeSpan.FromSeconds(
                        2
                    )
                );

        Assert.Equal(
            KioskState.Faulted,
            fixture.Engine.CurrentState
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .StopAcceptingCashCallCount
        );

        Assert.Equal(
            0,
            lowFloatEventCount
        );

        Assert.Contains(
            "Failed to stop cash acceptance",
            fault.Message
        );

        Assert.Contains(
            "Stop command failed.",
            fault.Message
        );
    }

    [Fact]
    public async Task NoteInEscrow_WritesAuditRecordBeforeEventReturns()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        var auditId =
            Guid.Parse(
                "4f6b5270-7a4f-4db4-9348-83f34e913bc6"
            );

        var hardwareAppendLog =
            new HardwareAppendLog(
                temporaryFile.FilePath,
                new FixedTimeProvider(
                    AuditTimestamp
                ),
                () =>
                    auditId
            );

        var fixture =
            CreateFixture(
                hardwareAppendLog
            );

        await fixture.Engine
            .InitializeAsync();

        fixture.CashRecycler
            .RaiseNoteInEscrow(
                Money.Usd(
                    10m
                )
            );

        /*
         * RaiseNoteInEscrow invokes the engine event handler
         * synchronously.
         *
         * The durable audit record must already exist when the
         * synchronous event invocation returns.
         */
        var contents =
            File.ReadAllText(
                temporaryFile.FilePath
            );

        Assert.Equal(
            "ESCROW|" +
            "4f6b5270-7a4f-4db4-9348-83f34e913bc6|" +
            "2026-08-05T07:00:00.0000000+00:00|" +
            "USD|10.00\n",
            contents
        );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .NoteInEscrowSubscriptionCount
        );
    }

    [Fact]
    public async Task NoteInEscrow_WhenAuditWriteFails_StopsCashAndFaultsEngine()
    {
        using var temporaryFile =
            new TemporaryAuditFile();

        /*
         * An existing directory cannot be opened as an append-only
         * audit file.
         */
        Directory.CreateDirectory(
            temporaryFile.DirectoryPath
        );

        var hardwareAppendLog =
            new HardwareAppendLog(
                temporaryFile.DirectoryPath,
                new FixedTimeProvider(
                    AuditTimestamp
                ),
                () =>
                    Guid.Parse(
                        "4f6b5270-7a4f-4db4-9348-83f34e913bc6"
                    )
            );

        var fixture =
            CreateFixture(
                hardwareAppendLog
            );

        await fixture.Engine
            .InitializeAsync();

        var faultRaised =
            new TaskCompletionSource<
                HardwareFaultEventArgs
            >(
                TaskCreationOptions
                    .RunContinuationsAsynchronously
            );

        fixture.Engine.OnHardwareFault +=
            (_, eventArgs) =>
                faultRaised.TrySetResult(
                    eventArgs
                );

        fixture.CashRecycler
            .RaiseNoteInEscrow(
                Money.Usd(
                    10m
                )
            );

        var fault =
            await faultRaised.Task
                .WaitAsync(
                    TimeSpan.FromSeconds(
                        2
                    )
                );

        Assert.Equal(
            1,
            fixture.CashRecycler
                .StopAcceptingCashCallCount
        );

        Assert.Equal(
            KioskState.Faulted,
            fixture.Engine.CurrentState
        );

        Assert.Equal(
            "HardwareAppendLog",
            fault.Device
        );

        Assert.Contains(
            "Failed to durably record an escrowed note",
            fault.Message
        );
    }

    private static HardwareAppendLog
        CreateHardwareAppendLog(
            string filePath,
            params Guid[] auditIds)
    {
        var remainingAuditIds =
            new Queue<Guid>(
                auditIds
            );

        return new HardwareAppendLog(
            filePath,
            new FixedTimeProvider(
                AuditTimestamp
            ),
            () =>
            {
                if (
                    remainingAuditIds.Count ==
                    0
                )
                {
                    throw new InvalidOperationException(
                        "No deterministic audit ID remains for this test."
                    );
                }

                return remainingAuditIds
                    .Dequeue();
            }
        );
    }

    private static TestFixture CreateFixture(
        HardwareAppendLog? hardwareAppendLog = null)
    {
        var cashRecycler =
            new FakeCashRecycler();

        var scanner =
            new FakeBarcodeScanner();

        var printer =
            new FakeReceiptPrinter();

        var lowFloatMonitor =
            new LowFloatMonitor();

        var licenseManager =
            new FakeOfflineLicenseManager();

        var engine =
            new LLCoreLogicEngine(
                cashRecycler,
                scanner,
                printer,
                new DualCurrencyCalculator(),
                lowFloatMonitor,
                licenseManager,
                hardwareAppendLog,
                new CashAcceptancePolicy()
            );

        return new TestFixture(
            engine,
            cashRecycler,
            scanner,
            printer,
            lowFloatMonitor,
            licenseManager
        );
    }

    private sealed class TestFixture(
        LLCoreLogicEngine engine,
        FakeCashRecycler cashRecycler,
        FakeBarcodeScanner scanner,
        FakeReceiptPrinter printer,
        LowFloatMonitor lowFloatMonitor,
        FakeOfflineLicenseManager licenseManager)
    {
        public LLCoreLogicEngine Engine
        {
            get;
        } = engine;

        public FakeCashRecycler CashRecycler
        {
            get;
        } = cashRecycler;

        public FakeBarcodeScanner Scanner
        {
            get;
        } = scanner;

        public FakeReceiptPrinter Printer
        {
            get;
        } = printer;

        public LowFloatMonitor LowFloatMonitor
        {
            get;
        } = lowFloatMonitor;

        public FakeOfflineLicenseManager LicenseManager
        {
            get;
        } = licenseManager;
    }

    private sealed class FakeOfflineLicenseManager
        : IOfflineLicenseManager
    {
        public int LoadAndValidateCallCount
        {
            get;
            private set;
        }

        public List<LicensedFeature> EnforcedFeatures
        {
            get;
        } = new();

        public Task LoadAndValidateAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            LoadAndValidateCallCount++;

            return Task.CompletedTask;
        }

        public void EnforceFeatureAccess(
            LicensedFeature feature)
        {
            EnforcedFeatures.Add(
                feature
            );
        }
    }

    private sealed class FakeCashRecycler
        : ICashRecycler,
          ICashEscrowController
    {
        private readonly SemaphoreSlim
            _commitCommandSignal =
                new(
                    initialCount: 0
                );

        private readonly SemaphoreSlim
            _rejectCommandSignal =
                new(
                    initialCount: 0
                );

        private EventHandler<NoteInEscrowEventArgs>?
            _onNoteInEscrow;

        private EventHandler<HardwareFaultEventArgs>?
            _onFault;

        private EventHandler<CashEscrowResolvedEventArgs>?
            _onEscrowResolved;

        public int NoteInEscrowSubscriptionCount
        {
            get;
            private set;
        }

        public int FaultSubscriptionCount
        {
            get;
            private set;
        }

        public int EscrowResolvedSubscriptionCount
        {
            get;
            private set;
        }

        public int ConnectCallCount
        {
            get;
            private set;
        }

        public int ArmAcceptanceCallCount
        {
            get;
            private set;
        }

        public int DisarmAcceptanceCallCount
        {
            get;
            private set;
        }

        public int StopAcceptingCashCallCount
        {
            get;
            private set;
        }

        public int CommitEscrowedNoteCallCount
        {
            get;
            private set;
        }

        public int RejectEscrowedNoteCallCount
        {
            get;
            private set;
        }

        public int DispenseCallCount
        {
            get;
            private set;
        }

        public Exception?
            ArmAcceptanceException
        {
            get;
            set;
        }

        public Exception?
            DisarmAcceptanceException
        {
            get;
            set;
        }

        public Exception?
            StopAcceptingCashException
        {
            get;
            set;
        }

        public Exception?
            CommitEscrowedNoteException
        {
            get;
            set;
        }

        public Exception?
            RejectEscrowedNoteException
        {
            get;
            set;
        }

        public event EventHandler<NoteInEscrowEventArgs>?
            OnNoteInEscrow
        {
            add
            {
                _onNoteInEscrow +=
                    value;

                NoteInEscrowSubscriptionCount++;
            }

            remove
            {
                _onNoteInEscrow -=
                    value;
            }
        }

        public event EventHandler<HardwareFaultEventArgs>?
            OnFault
        {
            add
            {
                _onFault +=
                    value;

                FaultSubscriptionCount++;
            }

            remove
            {
                _onFault -=
                    value;
            }
        }

        public event EventHandler<CashEscrowResolvedEventArgs>?
            OnEscrowResolved
        {
            add
            {
                _onEscrowResolved +=
                    value;

                EscrowResolvedSubscriptionCount++;
            }

            remove
            {
                _onEscrowResolved -=
                    value;
            }
        }

        public Task ConnectAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            ConnectCallCount++;

            return Task.CompletedTask;
        }

        public Task ArmAcceptanceAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            ArmAcceptanceCallCount++;

            if (
                ArmAcceptanceException is
                not null
            )
            {
                throw ArmAcceptanceException;
            }

            return Task.CompletedTask;
        }

        public Task DisarmAcceptanceAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            DisarmAcceptanceCallCount++;

            if (
                DisarmAcceptanceException is
                not null
            )
            {
                throw DisarmAcceptanceException;
            }

            return Task.CompletedTask;
        }

        public Task StopAcceptingCashAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            StopAcceptingCashCallCount++;

            if (
                StopAcceptingCashException is
                not null
            )
            {
                throw StopAcceptingCashException;
            }

            return Task.CompletedTask;
        }

        public Task<DispenseResult> DispenseAsync(
            ChangeBreakdown change,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            DispenseCallCount++;

            return Task.FromResult(
                new DispenseResult(
                    Success: true,
                    Dispensed: change
                )
            );
        }

        public Task CommitEscrowedNoteAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            CommitEscrowedNoteCallCount++;

            _commitCommandSignal
                .Release();

            if (
                CommitEscrowedNoteException is
                not null
            )
            {
                throw CommitEscrowedNoteException;
            }

            return Task.CompletedTask;
        }

        public Task RejectEscrowedNoteAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            RejectEscrowedNoteCallCount++;

            _rejectCommandSignal
                .Release();

            if (
                RejectEscrowedNoteException is
                not null
            )
            {
                throw RejectEscrowedNoteException;
            }

            return Task.CompletedTask;
        }

        public async Task WaitForCommitCommandAsync()
        {
            var received =
                await _commitCommandSignal
                    .WaitAsync(
                        TimeSpan.FromSeconds(
                            2
                        )
                    );

            if (!received)
            {
                throw new TimeoutException(
                    "The expected commit-escrow command was not received."
                );
            }
        }

        public async Task WaitForRejectCommandAsync()
        {
            var received =
                await _rejectCommandSignal
                    .WaitAsync(
                        TimeSpan.FromSeconds(
                            2
                        )
                    );

            if (!received)
            {
                throw new TimeoutException(
                    "The expected reject-escrow command was not received."
                );
            }
        }

        public void RaiseNoteInEscrow(
            Money note)
        {
            _onNoteInEscrow?.Invoke(
                this,
                new NoteInEscrowEventArgs(
                    note
                )
            );
        }

        public void RaiseEscrowResolved(
            Money note,
            CashEscrowResolution resolution)
        {
            _onEscrowResolved?.Invoke(
                this,
                new CashEscrowResolvedEventArgs(
                    note,
                    resolution
                )
            );
        }

        public HardwareFaultEventArgs RaiseFault(
            string device,
            string message)
        {
            var eventArgs =
                new HardwareFaultEventArgs(
                    device,
                    message
                );

            _onFault?.Invoke(
                this,
                eventArgs
            );

            return eventArgs;
        }
    }

    private sealed class FakeBarcodeScanner
        : IBarcodeScanner
    {
        private EventHandler<BarcodeScannedEventArgs>?
            _onBarcodeScanned;

        public int BarcodeSubscriptionCount
        {
            get;
            private set;
        }

        public int ConnectCallCount
        {
            get;
            private set;
        }

        public int DisconnectCallCount
        {
            get;
            private set;
        }

        public int ConnectFailuresRemaining
        {
            get;
            set;
        }

        public event EventHandler<BarcodeScannedEventArgs>?
            OnBarcodeScanned
        {
            add
            {
                _onBarcodeScanned +=
                    value;

                BarcodeSubscriptionCount++;
            }

            remove
            {
                _onBarcodeScanned -=
                    value;
            }
        }

        public Task ConnectAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            ConnectCallCount++;

            if (
                ConnectFailuresRemaining >
                0
            )
            {
                ConnectFailuresRemaining--;

                throw new InvalidOperationException(
                    "Scanner connection failed."
                );
            }

            return Task.CompletedTask;
        }

        public Task DisconnectAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            DisconnectCallCount++;

            return Task.CompletedTask;
        }
    }

    private sealed class FakeReceiptPrinter
        : IReceiptPrinter
    {
        private EventHandler<PrintJobStatusEventArgs>?
            _onJobStatusChanged;

        public int JobStatusSubscriptionCount
        {
            get;
            private set;
        }

        public int ConnectCallCount
        {
            get;
            private set;
        }

        public int PrintRawCallCount
        {
            get;
            private set;
        }

        public event EventHandler<PrintJobStatusEventArgs>?
            OnJobStatusChanged
        {
            add
            {
                _onJobStatusChanged +=
                    value;

                JobStatusSubscriptionCount++;
            }

            remove
            {
                _onJobStatusChanged -=
                    value;
            }
        }

        public Task ConnectAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            ConnectCallCount++;

            return Task.CompletedTask;
        }

        public Task<bool> IsPaperPresentAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            return Task.FromResult(
                true
            );
        }

        public Task PrintRawAsync(
            ReadOnlyMemory<byte> escPosPayload,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            PrintRawCallCount++;

            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset timestamp)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return timestamp;
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