namespace SelfCheckoutKiosk.App.Navigation
{
    /// <summary>
    /// Central enumeration of all application routes / screens in the Kiosk system.
    /// Analogous to route definitions in modern web/SPA frameworks (e.g. app.routes.ts).
    /// </summary>
    public enum KioskRoute
    {
        /// <summary>Attract / Idle video loop screen (KioskBaseView)</summary>
        Attract,

        /// <summary>Customer Home / Landing screen with quick actions (HomeView)</summary>
        Home,

        /// <summary>Active shopping cart & scanning interface (CartView)</summary>
        Cart,

        /// <summary>Checkout payment method selection screen (PaymentSelectionView)</summary>
        PaymentSelection,

        /// <summary>Accepted payment methods overview & hardware status screen (PaymentOptionView)</summary>
        PaymentOptions,

        /// <summary>ABA / KHQR dynamic QR payment screen (QRPaymentView)</summary>
        QRPayment,

        /// <summary>Cash payment note insertion & change dispensing screen (IngestionProgressView)</summary>
        CashIngestion,

        /// <summary>Transaction complete receipt & thank you screen (SuccessView)</summary>
        Success,

        /// <summary>Attendant / Staff PIN authentication login screen (AdminLoginView)</summary>
        AdminLogin,

        /// <summary>Staff diagnostics, cash vault, hardware & system reboot controls (AdminDiagnosticsView)</summary>
        AdminDiagnostics,

        /// <summary>Store logo, branding name, tagline & store hours settings (MediaBrandingView)</summary>
        MediaBranding
    }
}
