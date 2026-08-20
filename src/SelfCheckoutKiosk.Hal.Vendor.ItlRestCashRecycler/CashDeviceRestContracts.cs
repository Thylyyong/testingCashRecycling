using System.Text.Json.Serialization;

namespace SelfCheckoutKiosk.Hal.Vendor.ItlRestCashRecycler;

/// <summary>Wire shape for a single note — either escrowed (status) or
/// requested/dispensed (dispense). <see cref="CurrencyCode"/> is "Usd" or
/// "Khr"; <see cref="Denomination"/> is the face value in that currency's
/// own units (e.g. 20 for a $20 bill, 10000 for a 10,000 KHR note).</summary>
internal sealed record NoteDto(
    [property: JsonPropertyName("currencyCode")] string CurrencyCode,
    [property: JsonPropertyName("denomination")] decimal Denomination);

/// <summary>GET /api/device/status. <see cref="SequenceId"/> is a monotonic
/// counter the bridge increments on every state change — the poller only
/// reacts when it advances, so a poll that lands between two identical
/// reads is a safe no-op rather than a duplicate event.</summary>
internal sealed record DeviceStatusDto(
    [property: JsonPropertyName("state")] string State, // "Idle" | "NoteInEscrow" | "Faulted"
    [property: JsonPropertyName("escrowedNote")] NoteDto? EscrowedNote,
    [property: JsonPropertyName("faultMessage")] string? FaultMessage,
    [property: JsonPropertyName("sequenceId")] long SequenceId);

internal sealed record CassetteLevelDto(
    [property: JsonPropertyName("denominationKhr")] int DenominationKhr,
    [property: JsonPropertyName("count")] int Count);

/// <summary>GET /api/device/cassettes. KHR-denomination levels only — the
/// low-float safeguard this feeds (<c>LowFloatMonitor</c>) is KHR-only by
/// design (Blueprint §4); USD cassette levels aren't tracked there.</summary>
internal sealed record CassetteLevelsDto(
    [property: JsonPropertyName("levels")] IReadOnlyList<CassetteLevelDto> Levels);

internal sealed record DispenseNoteDto(
    [property: JsonPropertyName("denomination")] int Denomination,
    [property: JsonPropertyName("count")] int Count);

/// <summary>POST /api/device/dispense body.</summary>
internal sealed record DispenseRequestDto(
    [property: JsonPropertyName("usdNotes")] IReadOnlyList<DispenseNoteDto> UsdNotes,
    [property: JsonPropertyName("khrNotes")] IReadOnlyList<DispenseNoteDto> KhrNotes);

/// <summary>POST /api/device/dispense response.</summary>
internal sealed record DispenseResponseDto(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("dispensedUsdNotes")] IReadOnlyList<DispenseNoteDto> DispensedUsdNotes,
    [property: JsonPropertyName("dispensedKhrNotes")] IReadOnlyList<DispenseNoteDto> DispensedKhrNotes);

/// <summary>
/// Source-generated System.Text.Json context — AOT/trim-safe (no
/// reflection-based serialization) per the project's Native AOT mandate.
/// Every HttpClient call in <see cref="ItlRestCashRecycler"/> uses the
/// JsonTypeInfo-accepting overloads bound to this context, never the plain
/// generic ones (those fall back to reflection at runtime).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DeviceStatusDto))]
[JsonSerializable(typeof(CassetteLevelsDto))]
[JsonSerializable(typeof(DispenseRequestDto))]
[JsonSerializable(typeof(DispenseResponseDto))]
internal sealed partial class CashDeviceRestJsonContext : JsonSerializerContext;
