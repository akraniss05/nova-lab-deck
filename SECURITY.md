# Sicherheit

Loopback ist die Voreinstellung. LAN bindet an die gewählte private IPv4-Adresse. Keine UPnP-Freigabe und keine Befehls-Shell. HTTP im LAN erlaubt das Mitlesen von Sitzungscookies durch einen Angreifer mit Netzwerkzugriff. HTTPS benötigt ein passendes, auf den Geräten vertrauenswürdiges Zertifikat. Der Service Worker registriert sich nur im sicheren Kontext.

Ein QR-Geheimnis gilt zwei Minuten und für eine Kopplung. Der Host verlangt lokale Zustimmung. Das Host-Dashboard verlangt einen eigenen kurzlebigen Loopback-Code. Sitzungen nutzen zufällige 256-Bit-Secrets, HttpOnly/SameSite-Cookies, Secure unter HTTPS und serverseitig gespeicherte Hashes. Remote-Geräte laufen nach 30 Tagen ab und sind am PC widerrufbar. OBS- und PFX-Passwörter werden mit Windows-DPAPI für den Benutzer gespeichert.

HTTP-Schreibzugriffe und WebSocket-Upgrades verlangen den eigenen Origin. Der Host prüft Hostname, Port, Rolle, Aktion, Feldlängen und Nutzlastgrenzen. Eigene Bilder akzeptieren begrenzte Rasterformate. Programme, Parameter und Hotkeys werden lokal freigegeben. OBS-Streamwechsel, Sperren, Herunterfahren, Neustart und kontrolliertes Schließen verlangen PC-Bestätigung.

Die Tests decken definierte Sicherheitsgrenzen ab. Ein unabhängiges Audit, Code-Signing und Tests in feindlichen Netzwerken stehen aus.
