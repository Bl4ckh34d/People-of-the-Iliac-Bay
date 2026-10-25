# Spieltest — 26. Oktober 2026

Spielbarer Zwischenbuild `DE1428CD851D`, um 05:07 installiert. Paket: `dist/peopleoftheiliacbay.dfmod`. Unity-Paketprüfung und 2117 Core-Tests bestehen. Der gemeldete Inn-Fehler besteht den gezielten nativen Test: Tresen, stabiler Sitzplatz, Speicherstand und Spieler-Ausgang. Fluchtbewegung erreicht im freien nativen Test 13,16 km/h statt der zuvor begrenzten 5,54 km/h. Korrigierte Handels-Ablehnungstexte, soziale Preisgrenzen und Erinnerungsreparaturen sind enthalten. Vollständiger Hash und Prüfgrenzen stehen in [TESTING.md](TESTING.md). Die [Arbeitsliste](WORKLIST-2026-10-26.md) enthält offene Implementierungen und Spieltests; insbesondere ist der neue anonyme Mord-Meldeauftrag noch nicht implementiert.

Für diesen kurzen Test besonders hilfreich: um 09:10 beim Kleiderladen warten, später tatsächlichen Eintritt/Rückkehr beobachten; persönliche Trankpreise und Null-Gold-Pergamenthandel prüfen; stationäre Markt-NPCs nach wirklich beobachteten Ereignissen erneut ansprechen. Die neue stationäre Wahrnehmung und kurze Begegnungen sind noch nicht nativ abgenommen. Automatische Marktrollen sind noch nicht implementiert.

## Start

`DFU/Play DFU.lnk` wie gewohnt starten. Der Launcher startet Gemma und Breeze. Alle vorhandenen Spielstände sind Testdaten. Die automatisierten Prüfungen sind beendet; das Spiel ist für deinen eigenen Start frei.

## Sinnvolle kurze Tests

1. **Markt und Wege:** Tagsüber durch Daggerfall gehen. Auf stockende NPCs, Türansammlungen und Familienpaare achten.
2. **Kontext und Ortsauskunft:** Einen laufenden Bewohner fragen, was er vorhat. Nach einem Rüstungsladen fragen, anschließend um Kartenmarkierung oder Hinführen bitten. Begleitung hängt von Bereitschaft und Pflichten ab.
3. **Persönlicher Handel:** Einen normalen mobilen Bewohner um Handel bitten. Spieler-/NPC-Items prüfen, erst abbrechen, dann kaufen oder verkaufen. Einen niedrigeren Preis anbieten und das Gegenangebot beobachten.
4. **Beziehung:** Freundlich sprechen, später erneut ansprechen. Nach einer Kränkung bleibt der NPC ablehnend; eine Entschuldigung löscht Groll nicht sofort.
5. **Gesundheit und Wachen:** Vorhandene Behandlung, Hilfe und Wachreaktionen ausprobieren. Beobachtete Orte und konkrete Ereignisse beim Feedback nennen.

Leihen benötigt bekanntes Vertrauen ab75 und eignet sich erst für vertraute NPCs. Rückgabe desselben geliehenen Gegenstands bleibt auch bei gesunkenem Vertrauen möglich.

## Noch offen

- Persönlicher Handel mit statischen Laden-/Gilden-NPCs: Fensterübergang scheitert im aktuellen gezielten Test.
- Gemischter Tausch und sämtliche freien Handelsformulierungen sind noch nicht vollständig im Spiel geprüft.
- Bezahltes Mitreisen, Enter→Wait in weiteren Gebäuden, Shield, Training und neue Kampffunktionen haben offene Abnahmen.
- Springen/Klettern, bewegliche Dungeonplattformen und vollständige NPC-Haft sind noch nicht fertig.

Für Fehler reichen NPC/Ort, letzte Aktion und sichtbares Ergebnis; ein Screenshot hilft bei Darstellung oder Bewegung.
