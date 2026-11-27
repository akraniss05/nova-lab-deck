# Prüfbericht · 27. November 2026

22 automatisierte Integrationsprüfungen mit echtem Kestrel, lokalen HTTP-/WebSocket-Clients, temporärem Datenordner und einem separaten OBS-v5-Testserver. Abgedeckt sind vollständige Asset-Auflösung, 15er-Layout, gespeicherte Einstellungen nach Neustart, Konflikt, ungültige Layouts, Einmalcode, Origin, Geräteentzug, Steuerrolle, abgelehnte kritische Aktion, einmalige Weiterleitung eines Tastendrucks sowie OBS-Authentifizierung und Fehlerantwort. Die Plattformaktionen im plattformübergreifenden Test laufen über einen Testadapter.

Chromium mit 393 × 852, 852 × 393 und 1440 × 900 geprüft. 15 Tasten, drei bzw. fünf Spalten, kein horizontaler Überlauf im Hochformat, Button umbenennen und speichern, Einstellungssuche und fehlerfreie JavaScript-Ausführung. Das ist eine Viewportprüfung. Physisches Safari steht aus.

Der Windows-Runner hat die native EXE ausgeführt. Eine Diagnose auf Windows Build 20348 fand 221 Programmziele und lieferte einen RAM-Messwert. Der Runner meldete null Audio- und Mediensitzungen und keinen NVIDIA-Messwert. Die Testmethode bestätigt daher weder Spotify-Wiedergabe noch die Lautstärkeisolation auf einem realen Streaming-PC.

Der Windows-Workflow erzeugt die portable EXE und den NSIS-Installer, installiert in ein isoliertes Verzeichnis, vergleicht den SHA-256 der installierten EXE und prüft die Deinstallation. Der konkrete Laufstatus ist in GitHub Actions sichtbar. Für OBS mit echter Aufnahme, Hotkeys, Mikrofon und physische Geräte gilt die Abnahmeliste in TODO.md.
