# Entwicklungsprotokoll

| Phase | Dateien und Ergebnis | Prüfung und Anschluss |
|---|---|---|
| 1–2 | Host/Program, Core/Server, Core/Model. Windows-Host, Kopplung, Rollen, WebSockets | Windows-Crossbuild und Kestrel-Integration. Anschließend Persistenz und UI angebunden. |
| 3 | Web/app, editor, ui.css. Deck, Revisionen, Seiten und Profile | 15 Slots, Konfliktprüfung, mobiler Editor und Querformat im Browser geprüft. |
| 4–5 | Host/Windows, Audio, Media, EndpointPolicy. Ziele, lokale Hotkeys, Mixer und GSMTC | Windows-Build und native Diagnose. Hardware-Abnahme offen. |
| 6 | Core/Obs, Host/Windows. OBS-v5-Client und lokale Shortcuts | Authentifizierung, Szenenauswahl und Fehlerantworten gegen Protokoll-Testserver geprüft. |
| 7–8 | Host/Monitoring, Audio, Web/views, editor. Soundboard, Makros, Einstellungen | Browser-Suche und Speichern geprüft. Zielgeräte und externe Programme vor Ort prüfen. |
| 9 | Web/assets, Artwork/Masters, licenses, ASSET_MANIFEST | 263 Katalogeinträge. Jeder Asset-Pfad wird im Integrationstest abgerufen. |
| 10 | Tests, packaging, Windows-Workflow | Fehler durch gemischte Pfadtrenner im Windows-Ressourcenmanifest korrigiert. NSIS-Pfade für Windows angepasst. Prüfstand siehe TEST_REPORT. |
