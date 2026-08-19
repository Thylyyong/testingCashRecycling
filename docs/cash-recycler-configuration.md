# Cash Recycler local configuration

Real hardware is disabled by default. Enable it only on the kiosk connected to
the device, using machine environment variables:

```powershell
$env:SELFCHECKOUT_CASH_RECYCLER_USE_REAL_API  = "true"
$env:SELFCHECKOUT_CASH_RECYCLER_API_BASE_URL  = "http://localhost:5000"
$env:SELFCHECKOUT_CASH_RECYCLER_COM_PORT      = "COM8"
$env:SELFCHECKOUT_CASH_RECYCLER_JWT_ISSUER    = "INNOVATIVETECHNOLOGY"
$env:SELFCHECKOUT_CASH_RECYCLER_JWT_AUDIENCE  = "INNOVATIVETECHNOLOGY"
```

Store the API key in the ignored `api_key.secret` file or set
`SELFCHECKOUT_CASH_RECYCLER_API_KEY`. Do not commit an API key, a workstation
COM port, or vendor JWT settings. Confirm the port in Windows Device Manager
and obtain the JWT issuer/audience values from the vendor REST API configuration.

Run read-only workstation discovery with:

```powershell
dotnet run --project src/SelfCheckoutKiosk.App -- --detect-hardware
```

It verifies a cash-recycler candidate using the vendor status API, lists Epson
printer queues, and lists serial-port candidates for a Datalogic scanner. A
scanner requires a test scan for final confirmation; a keyboard-wedge scanner
does not have a COM port to detect.
