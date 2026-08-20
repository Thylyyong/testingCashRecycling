namespace SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

/// <summary>
/// Deployment configuration for the ITL CashDevice REST adapter.
///
/// The physical validator may report multiple currencies in the same
/// device-status stream. The configured Currency value therefore
/// represents the currencies that the kiosk permits from the device,
/// rather than selecting a currency during OpenConnection.
///
/// Supported examples:
///
///     USD
///     KHR
///     USD,KHR
///     KHR,USD
///
/// For the current Cambodia kiosk deployment use:
///
///     USD,KHR
///
/// Currency identification for an inserted note remains authoritative
/// from the ITL cash event's CountryCode field.
/// </summary>
public sealed class CashRecyclerXOptions
{
    public required string BaseUrl
    {
        get;
        init;
    }

    public required string Username
    {
        get;
        init;
    }

    public required string Password
    {
        get;
        init;
    }

    public required string ComPort
    {
        get;
        init;
    }

    /// <summary>
    /// Comma-separated currencies accepted from ITL cash events.
    ///
    /// Examples:
    /// USD
    /// KHR
    /// USD,KHR
    /// </summary>
    public string Currency
    {
        get;
        init;
    } = "USD,KHR";

    public int SspAddress
    {
        get;
        init;
    }

    public ulong? EncryptionKey
    {
        get;
        init;
    }

    public string? DeviceLogFilePath
    {
        get;
        init;
    }

    public TimeSpan PollInterval
    {
        get;
        init;
    } = TimeSpan.FromMilliseconds(
        200);

    public TimeSpan RequestTimeout
    {
        get;
        init;
    } = TimeSpan.FromSeconds(
        5);

    public int MaximumConsecutivePollFailures
    {
        get;
        init;
    } = 3;


    /*
     * ============================================================
     * Configuration Validation
     * ============================================================
     */

    internal Uri ValidateAndCreateBaseUri()
    {
        RequireText(
            BaseUrl,
            nameof(BaseUrl));

        RequireText(
            Username,
            nameof(Username));

        RequireText(
            Password,
            nameof(Password));

        RequireText(
            ComPort,
            nameof(ComPort));

        RequireText(
            Currency,
            nameof(Currency));


        /*
         * --------------------------------------------------------
         * Base URL
         * --------------------------------------------------------
         */

        if (
            !Uri.TryCreate(
                BaseUrl,
                UriKind.Absolute,
                out var baseUri) ||
            (
                baseUri.Scheme !=
                    Uri.UriSchemeHttp &&
                baseUri.Scheme !=
                    Uri.UriSchemeHttps
            )
        )
        {
            throw new ArgumentException(
                "CashDevice BaseUrl must be an absolute HTTP or HTTPS URI.",
                nameof(BaseUrl));
        }


        if (
            !string.IsNullOrEmpty(
                baseUri.Query) ||
            !string.IsNullOrEmpty(
                baseUri.Fragment)
        )
        {
            throw new ArgumentException(
                "CashDevice BaseUrl cannot contain a query or fragment.",
                nameof(BaseUrl));
        }


        /*
         * --------------------------------------------------------
         * SSP
         * --------------------------------------------------------
         */

        if (
            SspAddress <
            0
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(SspAddress),
                "The SSP address cannot be negative.");
        }


        /*
         * --------------------------------------------------------
         * Polling
         * --------------------------------------------------------
         */

        if (
            PollInterval <=
            TimeSpan.Zero
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(PollInterval),
                "The polling interval must be greater than zero.");
        }


        if (
            RequestTimeout <=
            TimeSpan.Zero
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(RequestTimeout),
                "The request timeout must be greater than zero.");
        }


        if (
            MaximumConsecutivePollFailures <=
            0
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumConsecutivePollFailures),
                "The polling failure limit must be greater than zero.");
        }


        /*
         * --------------------------------------------------------
         * Supported currencies
         * --------------------------------------------------------
         */

        var currencies =
            ParseSupportedCurrencies();


        if (
            currencies.Count ==
            0
        )
        {
            throw new ArgumentException(
                "At least one CashDevice currency must be configured.",
                nameof(Currency));
        }


        foreach (
            var currency
            in currencies
        )
        {
            if (
                currency !=
                    "USD" &&
                currency !=
                    "KHR"
            )
            {
                throw new NotSupportedException(
                    "Physical CashRecyclerX currently supports only " +
                    "USD and KHR cash events. Unsupported configured " +
                    "currency: " +
                    currency +
                    ".");
            }
        }


        /*
         * --------------------------------------------------------
         * Normalize Base URL
         * --------------------------------------------------------
         */

        var normalized =
            BaseUrl.EndsWith(
                "/",
                StringComparison.Ordinal)
                ? BaseUrl
                : BaseUrl +
                  "/";


        return new Uri(
            normalized,
            UriKind.Absolute);
    }


    /*
     * ============================================================
     * Currency Helpers
     * ============================================================
     */

    internal bool IsCurrencyAllowed(
        string? countryCode)
    {
        if (
            string.IsNullOrWhiteSpace(
                countryCode)
        )
        {
            return false;
        }


        var normalized =
            countryCode
                .Trim()
                .ToUpperInvariant();


        return ParseSupportedCurrencies()
            .Contains(
                normalized);
    }


    internal IReadOnlySet<string>
        GetSupportedCurrencies()
    {
        return ParseSupportedCurrencies();
    }


    private HashSet<string>
        ParseSupportedCurrencies()
    {
        return Currency
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(
                value =>
                    value.ToUpperInvariant())
            .Where(
                value =>
                    !string.IsNullOrWhiteSpace(
                        value))
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);
    }


    /*
     * ============================================================
     * Required Text
     * ============================================================
     */

    private static void RequireText(
        string value,
        string parameterName)
    {
        if (
            string.IsNullOrWhiteSpace(
                value)
        )
        {
            throw new ArgumentException(
                $"CashDevice {parameterName} is required.",
                parameterName);
        }
    }
}