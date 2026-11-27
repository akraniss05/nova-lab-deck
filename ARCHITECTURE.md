# Architektur

Host ist ein WinForms-Prozess mit Tray, lokaler Freigabe, QR-Erzeugung und einem eingebetteten Kestrel-Server. Die Desktop-Bedienoberfläche öffnet im Standardbrowser. Beide Browseroberflächen verwenden native JavaScript-Module und CSS. Dadurch entfallen WebView-Installation, Node-Server und Frontend-Build auf dem Windows-PC.

Core/Server.cs prüft Herkunft, Geräteidentität, Rolle und Revision. Einmalcodes werden am PC erzeugt. Der WebSocket liefert Anfangszustand, Telemetrie und Änderungen. Tastendrücke laufen über denselben Dispatcher wie JSON-Befehle. Der Host serialisiert Aktionen, begrenzt Makros und überprüft kritische Aktionen am PC. PTT endet bei Loslassen, Sitzungsabbruch oder nach 30 Sekunden.

Core/Model.cs verwaltet versionierte Profile mit genau 15 Tasten pro Seite. Änderungen werden unter einem Lock atomar ersetzt. Gleichzeitige Änderungen mit älterer Revision werden zurückgewiesen. Der vorherige Stand bleibt als .bak erhalten.

Host/Audio.cs gruppiert WASAPI-Sitzungen nach Prozess und trennt sie von Aufnahme- und Wiedergabe-Endpunkten. EndpointPolicy kapselt den versionsabhängigen Windows-Standardgerätewechsel und liest das Ergebnis zurück. Host/Media.cs nutzt GSMTC. Host/Windows.cs verwaltet freigegebene Ziele. Monitoring nutzt Windows-Zähler, Netzwerkstatistik und optional das lokal installierte nvidia-smi. Core/Obs.cs spricht OBS-WebSocket v5 über Loopback und korreliert Antworten anhand der requestId.

Web/ enthält Deck, Dashboard, Editor und Einstellungen. Artwork/Masters enthält die 16 großen Rastermotive. Die übrigen SVG-Master sind skalierbar und liegen unter Web/assets. ASSET_MANIFEST.json dokumentiert die Herkunft. Namen und Marken identifizieren die jeweilige Anwendung. Lizenztexte stehen in licenses/.
