namespace SelfCheckoutKiosk.App.Services.Audio;

/// <summary>
/// Service contract for centralized audio sound effects and voice guidance playback.
/// </summary>
public interface ISoundService
{
    /// <summary>Plays the specified kiosk sound effect or voice prompt.</summary>
    void Play(KioskSound sound);

    /// <summary>Stops any currently playing voice prompt on the voice channel.</summary>
    void StopVoice();

    /// <summary>Sets the global audio playback volume (0.0 to 1.0).</summary>
    void SetVolume(double volume);

    /// <summary>Gets or sets whether all sound effects and voice prompts are muted.</summary>
    bool IsMuted { get; set; }
}
