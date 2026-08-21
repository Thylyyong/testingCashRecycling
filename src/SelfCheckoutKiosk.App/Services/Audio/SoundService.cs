using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace SelfCheckoutKiosk.App.Services.Audio;

/// <summary>
/// Centralized high-performance sound engine managing audio feedback and voice prompts.
/// Provides dual-channel playback (SFX + Voice Prompts) so UI clicks do not interrupt voice prompts,
/// and pre-loads media sources for instantaneous zero-latency response.
/// </summary>
public sealed class SoundService : ISoundService
{
    private static readonly Lazy<SoundService> _lazy = new(() => new SoundService());
    public static SoundService Instance => _lazy.Value;

    private static readonly Dictionary<KioskSound, string> SoundFileMap = new()
    {
        [KioskSound.ButtonClick] = "button-sound.mp3",
        [KioskSound.KeypadClick] = "keypad-sound.mp3",
        [KioskSound.SuccessBeep] = "success-beep.mp3",
        [KioskSound.ErrorPassword] = "error-password.mp3",
        [KioskSound.ChoosePaymentMethod] = "choose-payment-method.mp3",
        [KioskSound.Welcome] = "welcome-sound.mp3",
        [KioskSound.PleaseScanFirstItem] = "please-scan-first-item.mp3",
        [KioskSound.ThankYouTakeReceipt] = "thank-you-take-receipt.mp3",
        [KioskSound.InvalidBarcode] = "invalid-barcode.mp3",
        [KioskSound.InputMembershipCard] = "input-membership-card-number.mp3",
        [KioskSound.InvalidMembership] = "invalid-membership.mp3"
    };

    private static readonly HashSet<KioskSound> VoiceSounds = new()
    {
        KioskSound.ChoosePaymentMethod,
        KioskSound.Welcome,
        KioskSound.PleaseScanFirstItem,
        KioskSound.ThankYouTakeReceipt,
        KioskSound.InvalidBarcode,
        KioskSound.InputMembershipCard,
        KioskSound.InvalidMembership
    };

    private readonly ConcurrentDictionary<KioskSound, Uri> _soundUris = new();
    private readonly MediaPlayer _voicePlayer = new();
    private readonly MediaPlayer _sfxPlayer = new();
    private readonly object _lock = new();

    public bool IsMuted { get; set; } = false;

    private SoundService()
    {
        try
        {
            _voicePlayer.Volume = 1.0;
            _sfxPlayer.Volume = 0.85;

            _voicePlayer.MediaFailed += (s, e) => Debug.WriteLine($"[SoundService Voice Error] {e.ErrorMessage}");
            _sfxPlayer.MediaFailed += (s, e) => Debug.WriteLine($"[SoundService SFX Error] {e.ErrorMessage}");

            // Pre-resolve asset file paths
            string baseDir = AppContext.BaseDirectory;
            foreach (var kvp in SoundFileMap)
            {
                string soundPath = Path.Combine(baseDir, "Assets", "sounds", kvp.Value);
                if (File.Exists(soundPath))
                {
                    _soundUris[kvp.Key] = new Uri(soundPath, UriKind.Absolute);
                }
                else
                {
                    Debug.WriteLine($"[SoundService] Sound asset not found on disk: {soundPath}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SoundService] Initialization warning: {ex.Message}");
        }
    }

    /// <summary>
    /// Plays the requested sound effect or voice prompt asynchronously with zero UI stutter.
    /// </summary>
    public void Play(KioskSound sound)
    {
        if (IsMuted) return;

        try
        {
            if (!_soundUris.TryGetValue(sound, out var uri))
            {
                // Fallback attempt to resolve file if dynamic
                if (SoundFileMap.TryGetValue(sound, out var fileName))
                {
                    string fallbackPath = Path.Combine(AppContext.BaseDirectory, "Assets", "sounds", fileName);
                    if (File.Exists(fallbackPath))
                    {
                        uri = new Uri(fallbackPath, UriKind.Absolute);
                        _soundUris[sound] = uri;
                    }
                }
            }

            if (uri == null)
            {
                Debug.WriteLine($"[SoundService] Skipping sound {sound}: URI unresolved.");
                return;
            }

            lock (_lock)
            {
                var source = MediaSource.CreateFromUri(uri);
                if (VoiceSounds.Contains(sound))
                {
                    // Voice prompt channel
                    _voicePlayer.Source = source;
                    _voicePlayer.Play();
                }
                else
                {
                    // Sound effects channel
                    _sfxPlayer.Source = source;
                    _sfxPlayer.Play();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SoundService] Error playing sound {sound}: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops any voice prompt currently in progress on the voice channel.
    /// </summary>
    public void StopVoice()
    {
        try
        {
            lock (_lock)
            {
                _voicePlayer.Pause();
                _voicePlayer.Source = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SoundService] Error stopping voice prompt: {ex.Message}");
        }
    }

    /// <summary>
    /// Adjusts playback volume across both SFX and voice prompt channels.
    /// </summary>
    public void SetVolume(double volume)
    {
        double clamped = Math.Clamp(volume, 0.0, 1.0);
        lock (_lock)
        {
            _voicePlayer.Volume = clamped;
            _sfxPlayer.Volume = clamped * 0.85;
        }
    }
}
