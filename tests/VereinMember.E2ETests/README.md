# Browser-Integrationstests

Diese Suite startet die **echten VereinMember-Razor-Pages** in einem minimalen
ASP.NET-Core-Host. ClubSite und Piranha sind weder Projekt- noch Testabhängigkeiten.
Die Tests sind nicht Bestandteil des `VereinMember`-NuGet-Pakets.

- relationale SQLite-In-Memory-Datenbank, exklusiv pro Test-Host
- E-Mail-Versand über den vorhandenen `FileEmailSender` in ein temporäres Verzeichnis
- Chromium über Playwright for .NET
- synthetische Testdaten, kein SMTP und keine externe Datenbank

Die Tests prüfen den Modulvertrag, nicht die konkrete Piranha-Konfiguration von ClubSite
(zum Beispiel dessen Menü-Redirect). Der Cookie-Login und ein kleines Layout gehören zum
Host-Vertrag und werden daher vom Test-Host bereitgestellt. SQL-Server-spezifisches
Verhalten wie Migrationen und Datenbankindizes bleibt bei den vorhandenen Integrationstests.

## Ausführen

Einmalig nach einem Paketupdate den Testbrowser installieren:

```sh
dotnet build tests/VereinMember.E2ETests/VereinMember.E2ETests.csproj
pwsh tests/VereinMember.E2ETests/bin/Debug/net8.0/playwright.ps1 install chromium
```

Danach:

```sh
dotnet test tests/VereinMember.E2ETests/VereinMember.E2ETests.csproj --no-build
```

Nur die bisherigen Tests ohne Browser ausführen:

```sh
dotnet test tests/VereinMember.Tests/VereinMember.Tests.csproj
```
