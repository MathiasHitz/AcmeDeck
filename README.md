# AcmeDeck

A graphical front end for [win-acme](https://www.win-acme.com/) to manage Let's Encrypt
certificates for IIS on Windows Server.

win-acme does the actual work: ordering, validation, storing, binding and the daily
renewal task. AcmeDeck gives it a point-and-click interface and adds the checks that
are missing when something silently goes wrong.

> AcmeDeck is an independent project and not affiliated with win-acme or Let's Encrypt.

## Features

- **Overview** of all win-acme renewals with expiry, next renewal, last run and status
- **New certificate wizard**: pick an IIS site, tick host names, check, request.
  Always stores an explicit site id and host list, so a binding added later cannot
  sneak into the next order.
- **Staging test**: runs the full validation against the Let's Encrypt staging
  environment without touching IIS and without counting against rate limits.
- **IIS bindings view**: every HTTPS binding with the certificate it actually serves.
  Certificates from other tools or purchased ones can be
  **taken over** by win-acme with one click.
- **Health checks** on start:
  - wacs.exe running from a user profile folder (Desktop, Downloads)
  - scheduled task missing, disabled, failing or pointing to another wacs.exe
  - no e-mail notification configured
  - several renewals managing the same certificate or the same host name
  - renewals without site or host filter
  - host names Let's Encrypt will reject (`localhost`, IP addresses, `.local` …)
- **Binding check**: connects to each HTTPS binding with SNI and compares the served
  certificate with the one win-acme issued last.
- **Public DNS check** via DNS over HTTPS, independent of local resolvers, Pi-hole or
  hosts entries. Detects names that only resolve through a hoster's wildcard record.
- **Notification settings** for win-acme's settings.json with a test e-mail
- **Log viewer** with a warnings-and-errors filter

## Requirements

- Windows Server 2016 or later (or Windows 10/11) with IIS
- win-acme 2.2.x, x64 (bundled with the installer or installed separately)
- Administrator rights (IIS configuration, machine certificate store, Task Scheduler)

No .NET runtime is needed, the application is published self-contained.

## How it works

AcmeDeck never writes win-acme's renewal files. It reads them from
`%ProgramData%\win-acme\<ACME server>\*.renewal.json` and runs `wacs.exe` unattended
for every change (`--source iis`, `--renew --id`, `--cancel --id`, ...). The output is
shown live. Whether a run succeeded is judged by what win-acme recorded in the renewal
history, not only by the exit code.

The only file AcmeDeck edits is the `Notification` section of win-acme's
`settings.json` (with a timestamped backup), and its own settings in
`%ProgramData%\AcmeDeck\settings.json`.

Online checks send host names to `dns.google` and ask `api.ipify.org` for the public
IP of the server. They can be switched off in the settings.

## Building

Requires the .NET 10 SDK and, for the installer, Inno Setup 6.

```powershell
dotnet build AcmeDeck.sln
```

Installer (self-contained single file, optionally with win-acme bundled):

```powershell
.\setup\build.ps1 -WinAcmeVersion v2.2.9.1701
```

When win-acme is bundled, it is installed to `C:\Program Files\AcmeDeck\win-acme` and
deliberately left in place on uninstall, because the scheduled task points to it.
win-acme is licensed under the Apache License 2.0; its license file is part of the
release archive and is installed with it.

## Author

AcmeDeck was created by Mathias Hitz.

## Support

If AcmeDeck saves you time or a license fee, you can buy me a coffee via
[PayPal](https://www.paypal.com/paypalme/usterix). Thank you!

## License

MIT, see [LICENSE](LICENSE).
