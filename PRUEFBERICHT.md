# VereinMember – Prüfbericht

Stand: 10.11.2026. Prüfung von ausgewähltem Code, synthetischen InMemory-Tests und Sicherheitsprüfung; kein Zugriff auf echte Mitglieder, produktive Datenbank oder Betriebsumgebung. Keine umfassende Sicherheitsfreigabe.

## Änderung und Tests

- `src/VereinMember/Services/MemberService.cs`: In `UpdateOwnProfileAsync` wird der Audit-Vorherwert vor der Mutation serialisiert. `tests/VereinMember.Tests/Services/MemberServiceTests.cs` enthält einen gezielten Regressionstest mit synthetischen InMemory-Daten. `coder-1` implementierte, `coder-2` prüfte unabhängig, `qa` testete und `security` prüfte statisch.
- Vor der Korrektur schlug der neue Regressionstest erwartungsgemäß fehl; danach bestand er. QA: **63 Tests bestanden, 0 fehlgeschlagen**, gezielter Wiederholungslauf 1/1 (kein zusätzlicher Test); Build 0 Warnungen/0 Fehler. SQL Server und echte Concurrency wurden nicht getestet.
- Das Solution-Build meldete zuvor `Unable to find a project to restore`; direkte Builds der Projektdateien funktionierten. Solution-Zuordnung separat prüfen.

## Sicherheits- und Qualitätsbefunde

1. **Hoch – flüchtiger Verschlüsselungsschlüssel:** `src/VereinMember/Extensions/ServiceCollectionExtensions.cs:82–91` erzeugt bei fehlender Schlüsselkonfiguration einen Zufallsschlüssel. Bei Neustart/mehreren Instanzen können zuvor mit anderem Schlüssel verschlüsselte Bankdaten unlesbar werden. Produktionsstart ohne stabilen, gültigen Schlüssel verweigern; Schlüssel sicher verwalten und Backup/Rotation definieren. Tatsächliche Produktionskonfiguration wurde nicht geprüft.
2. **Hoch – Aktivierungslink im Log:** `Services/DefaultMemberAccountService.cs:71–85` protokolliert die URL einschließlich Token. Wer Zugriff auf Logs hat, könnte ein noch gültiges Token zur Kontoaktivierung nutzen. Token/URL nicht loggen, Logzugriff und Aufbewahrung prüfen; potenziell kompromittierte Tokens risikobasiert widerrufen.
3. **Hoch – sensible Daten in Audit-Snapshots:** `Services/MemberService.cs:276,318–322,523–529`, `Services/AuditService.cs` und `Data/EntityConfigurations/AuditEntryConfiguration.cs` speichern vollständige Member-JSON-Werte, darunter entschlüsselte Bankdaten; auch bei Löschung können alte Werte in Audits verbleiben. Der korrigierte Vorher-Snapshot erweitert die verfügbare Historie. Datenminimierung, Löschkonzept, Verschlüsselung/Berechtigungen und Aufbewahrung prüfen.
4. **Mittel – Login-Lockout nicht aktiviert:** `Areas/Membership/Pages/Login.cshtml.cs:69` nutzt `lockoutOnFailure: false` trotz konfigurierter Sperrwerte. Bei extern erreichbarem Login ohne wirksames externes Rate-Limit können Passwortversuche ungebremst bleiben. Lockout, Rate-Limits und Monitoring prüfen.
5. **Verdacht auf defekten Profil-POST:** Review von `Areas/Membership/Pages/Profile.cshtml`, `Profile.cshtml.cs` und `MemberService.cs:290–305`: gesperrte Eigenschaften werden offenbar nicht mitgepostet, während der Service sie mit den gespeicherten Werten vergleicht. Ein normales Profilupdate könnte dadurch abgelehnt werden. Statisch abgeleitet, nicht im HTTP-Flow reproduziert; gezielt mit Integrationstest prüfen.
6. **Audit-Genauigkeit:** `ChangedFields` wird pauschal gefüllt, auch wenn nicht alle aufgeführten Felder geändert wurden. Abweichung zwischen tatsächlichen und behaupteten Änderungen prüfen.
7. **Mail-Abhängigkeit:** Das Projekt referenziert `VereinMail 1.0.0-dev`; der parallel geprüfte VereinMail-Quellstand ist 1.0.1-dev. Die tatsächlich verwendete Paketversion und ihre TLS-Einstellungen wurden nicht verifiziert.

## Ergebnis

Der Audit-Snapshot-Fix und seine synthetischen Tests sind bestanden. Die vorbestehenden bzw. durch vollständigere Audit-Historie berührten Datenschutz- und Sicherheitsrisiken sind gesondert zu behandeln; keine Produktivfreigabe aus diesem Test ableiten.
