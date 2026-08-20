using System;
using System.Collections.Generic;

namespace SelfCheckoutKiosk.App.Models
{
    public enum PaymentMethod
    {
        Cash,
        KHQR,
        Card,
        InternationalQR,
        MembershipCard,
        Coupon,
        // Card, QR, etc. can be added later — SuccessView reads this to render dynamically
    }

    public enum PaymentAttemptResult
    {
        Accepted,
        Rejected,
        Info,
        Warning,
        Error
    }

    public class PaymentAttempt
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public PaymentAttemptResult Result { get; set; }
        public string Reason { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public bool IsUsd { get; set; }

        public string DisplayTime => Timestamp.ToString("HH:mm:ss");
        public bool HasAmount => Amount > 0;
        public string DisplayAmount => HasAmount ? (IsUsd ? $"${Amount:0.00}" : $"៛{Amount:N0}") : string.Empty;
        public string StatusText => Result switch
        {
            PaymentAttemptResult.Accepted => "ACCEPTED",
            PaymentAttemptResult.Rejected => "REJECTED",
            PaymentAttemptResult.Warning => "WARNING",
            PaymentAttemptResult.Error => "ERROR",
            _ => "INFO"
        };
    }

    public class Payment
    {
        public string TransactionId { get; set; } = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        public PaymentMethod Method { get; set; }
        public DateTime CompletedAt { get; set; } = DateTime.Now;

        public decimal TotalDueUsd { get; set; }
        public decimal TotalPaidUsd { get; set; }
        public decimal ChangeDueUsd { get; set; }
        public decimal ExchangeRate { get; set; }
        public bool IsFullyPaid { get; set; }

        public List<PaymentAttempt> Attempts { get; set; } = new();

        public decimal TotalDueKhr => TotalDueUsd * ExchangeRate;
        public decimal TotalPaidKhr => TotalPaidUsd * ExchangeRate;
        public decimal ChangeDueKhr => ChangeDueUsd * ExchangeRate;
    }
}