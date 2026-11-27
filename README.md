# NovaLab Deck

Windows-Host und Browser-Fernbedienung von NovaLab Studios. Windows 10 Version 2004 oder neuer, x64. Die EXE enthält die .NET-Laufzeit, den Webserver, die Bedienoberfläche und die Icons.

## In fünf Schritten starten

1. NovaLabDeck-Setup-x64.exe installieren oder NovaLabDeck.exe direkt starten.
2. Am PC unter **Netzwerk & Geräte** die private WLAN-/LAN-IP auswählen und **Übernehmen** anklicken. HTTP ist im Heimnetz unverschlüsselt. Für HTTPS ein lokal vertrauenswürdiges PFX mit passendem IP-SAN und privatem Schlüssel wählen. Für den PC-Browser auch 127.0.0.1 im Zertifikat berücksichtigen.
3. Das Handy mit demselben Heimnetz verbinden. Am Host **QR-Code erneuern** und binnen zwei Minuten scannen. Falls Windows fragt, den Host im privaten Netzwerk zulassen. Gast-WLAN mit Geräteisolierung verhindert die Verbindung.
4. Auf dem Handy **Mit PC koppeln** drücken. Am PC **Nur steuern** oder **Auch bearbeiten** wählen.
5. Auf dem Handy eine Taste drücken. Unter **Bearbeiten** lassen sich Aktion, Bild, Gestaltung und Makro ändern. **Deck auf dem PC öffnen** öffnet das Host-Dashboard. Das Schließen des Hostfensters minimiert in den Infobereich. **Beenden** im Tray-Menü beendet den Server.

## Integration einrichten

- **OBS** unter Werkzeuge den WebSocket-Server aktivieren. Port und Passwort im Host unter **OBS & Host** speichern. Bei Verbindung erscheinen echte Szenen, Quellen und Zustände. Streamwechsel erfordern eine zusätzliche Bestätigung am PC. „Clip“ speichert den laufenden Replay Buffer.
- **Spotify** am PC abspielen. Im Audio-Mixer den Spotify-Kanal wählen. Ein Pegel von 30 % gilt für die Spotify-Sitzungen. „Windows Master“ ist ein separater Kanal. Unter **Now Playing** erscheinen Titel, Cover und die vom jeweiligen Player angebotenen Transportfunktionen.
- **TikTok LIVE Studio und Discord** am PC passende Hotkeys einrichten und unter **Programme & Hotkeys → Hotkey aufnehmen** freigeben. Optional den Zielprozess ohne .exe angeben. Im Editor die Aktion Hotkey und das freigegebene Ziel wählen. Für Discord Push-to-Talk die Funktion „Solange gedrückt“ aktivieren. Windows kann Eingaben an höher privilegierte Fenster ablehnen.
- **Programme** werden über Startmenü, AppsFolder und Steam eingelesen. Fehlende EXEs oder lokale Ziele am Host hinzufügen, danach im Deck **Ziele aktualisieren**. Vor dem ersten Start eines automatisch gefundenen Ziels erscheint eine Freigabe am PC.
- **Soundboard** MP3, OGG und WAV am Host importieren. Ausgang unter **OBS & Host** einstellen. Für Discord oder OBS ein bereits eingerichtetes virtuelles Audiogerät wählen und in der Zielanwendung routen.
- **Backup** Einstellungen → Backup. JSON enthält Deck, Profile, Designs und eingebettete Benutzerbilder. Für einen vollständigen Umzug zusätzlich bei beendetem Host den Datenordner sichern. DPAPI-Geheimnisse auf einem anderen PC neu einrichten. Audio- und Medienziele können dort andere IDs haben.

## Builds und Tests

GitHub Actions → **Windows build and checks** → erfolgreicher Lauf → **NovaLab-Deck-Windows-x64**. Das Archiv enthält Setup, portable EXE und die native Diagnose. Lokaler Build mit .NET 8 SDK und NSIS:

```powershell
dotnet run -c Release --project Tests
dotnet publish Host/Host.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o out/portable
cd packaging
makensis installer.nsi
```

Der Setup installiert pro Benutzer. Persönliche Konfiguration liegt unter %LOCALAPPDATA%\NovaLab Deck\Data. Die portable EXE speichert ebenfalls dort. Start mit --diagnose und einem lokalen JSON-Ausgabepfad prüft die native Laufzeit ohne LAN-Steuerung. Einzelheiten stehen in TEST_REPORT.md, SECURITY.md und PROJECT_STATE.md.
