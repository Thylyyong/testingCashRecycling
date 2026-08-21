namespace SelfCheckoutKiosk.App.Services.Audio;

/// <summary>
/// Global static facade providing ergonomic 1-line audio playback across all Views, ViewModels, and services.
/// </summary>
public static class AppSound
{
    private static ISoundService Service => SoundService.Instance;

    /// <summary>Plays any specified KioskSound effect or voice prompt.</summary>
    public static void Play(KioskSound sound) => Service.Play(sound);

    /// <summary>Plays tactile UI button click sound (button-sound.mp3).</summary>
    public static void ButtonClick() => Service.Play(KioskSound.ButtonClick);

    /// <summary>Plays on-screen keypad digit tap sound (keypad-sound.mp3).</summary>
    public static void KeypadClick() => Service.Play(KioskSound.KeypadClick);

    /// <summary>Plays positive success confirmation chime/beep (success-beep.mp3).</summary>
    public static void SuccessBeep() => Service.Play(KioskSound.SuccessBeep);

    /// <summary>Plays error buzzer sound for failed PIN/auth (error-password.mp3).</summary>
    public static void ErrorPassword() => Service.Play(KioskSound.ErrorPassword);

    /// <summary>Plays voice prompt instructing customer to choose payment method (choose-payment-method.mp3).</summary>
    public static void ChoosePaymentMethod() => Service.Play(KioskSound.ChoosePaymentMethod);

    /// <summary>Plays welcome chime/greeting on attract screen (welcome-sound.mp3).</summary>
    public static void Welcome() => Service.Play(KioskSound.Welcome);

    /// <summary>Plays voice prompt requesting first item scan (please-scan-first-item.mp3).</summary>
    public static void PleaseScanFirstItem() => Service.Play(KioskSound.PleaseScanFirstItem);

    /// <summary>Plays receipt reminder voice prompt on transaction success (thank-you-take-receipt.mp3).</summary>
    public static void ThankYouTakeReceipt() => Service.Play(KioskSound.ThankYouTakeReceipt);

    /// <summary>Plays voice alert for unregistered or invalid barcode (invalid-barcode.mp3).</summary>
    public static void InvalidBarcode() => Service.Play(KioskSound.InvalidBarcode);

    /// <summary>Stops any currently playing voice prompt.</summary>
    public static void StopVoice() => Service.StopVoice();

    /// <summary>Sets global audio volume (0.0 to 1.0).</summary>
    public static void SetVolume(double volume) => Service.SetVolume(volume);

    /// <summary>Gets or sets global audio mute state.</summary>
    public static bool IsMuted
    {
        get => Service.IsMuted;
        set => Service.IsMuted = value;
    }
}
