namespace SelfCheckoutKiosk.App.Services.Audio;

/// <summary>
/// Identifies all audio sound effects and voice prompts available in the kiosk application.
/// Maps 1-to-1 with MP3 assets located in Assets/sounds/.
/// </summary>
public enum KioskSound
{
    /// <summary>Short click feedback on general UI button taps (button-sound.mp3)</summary>
    ButtonClick,

    /// <summary>Short tactile click feedback on on-screen keypad digit taps (keypad-sound.mp3)</summary>
    KeypadClick,

    /// <summary>Positive chime/beep for valid barcode scan, item add, PIN unlock, or payment confirmation (success-beep.mp3)</summary>
    SuccessBeep,

    /// <summary>Error buzzer for incorrect PIN or failed authentication (error-password.mp3)</summary>
    ErrorPassword,

    /// <summary>Voice guidance prompt when navigating to payment selection (choose-payment-method.mp3)</summary>
    ChoosePaymentMethod,

    /// <summary>Welcome chime/greeting on startup or returning to attract screen (welcome-sound.mp3)</summary>
    Welcome,

    /// <summary>Voice guidance prompt asking the customer to scan their first item (please-scan-first-item.mp3)</summary>
    PleaseScanFirstItem,

    /// <summary>Voice prompt thanking the customer and reminding them to take their receipt (thank-you-take-receipt.mp3)</summary>
    ThankYouTakeReceipt,

    /// <summary>Voice alert when an unknown or invalid barcode is scanned (invalid-barcode.mp3)</summary>
    InvalidBarcode,

    /// <summary>Voice guidance prompt for loyalty/membership card input (input-membership-card-number.mp3)</summary>
    InputMembershipCard,

    /// <summary>Voice alert when a membership card cannot be verified (invalid-membership.mp3)</summary>
    InvalidMembership
}
