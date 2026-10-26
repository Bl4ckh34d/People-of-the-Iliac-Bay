# Spieltest — 26. Oktober 2026

Spielbarer Zwischenbuild `7BB6C4BB878D`, um 08:57 installiert. Paket: `dist/peopleoftheiliacbay.dfmod`. Unity-Paketprüfung und 2121 Core-Tests bestehen. Neu: Auch der separate Spielerangriffs-Pfad prüft die Blickrichtung. Wer den Spieler nicht sieht, wird nicht automatisch Augenzeuge; bloßes Hören verrät den Täter nicht. Je neun native Fälle für mobile und stationäre Körper bestehen. Vollständige Kampf-/Todesszenen und tatsächliche Inn-Angriffe bleiben separat offen; Details im [Testbericht](audits/2026-10-26-player-attack-perception.md).

Der vorherige Fix für NPC-/Gegnerangriffe bleibt enthalten: Nicht gesehene Angreifer bleiben unbekannt; tatsächliche NPC-Namen ersetzen leere Entity-Namen. Die zwölf zugehörigen Prüffälle bestehen auch im neuen Lauf.

Der vorherige Gesprächsfix bleibt enthalten: „Wache suchen und Mord melden“ wird als ein Auftrag verstanden. Ein längeres Gespräch lässt den anschließenden Wachengang nicht mehr fälschlich als blockiert scheitern. Der native Test nutzt freie Texteingabe und das lokale Modell, wartet nach der Zusage weitere 25 Sekunden im Gespräch und beobachtet anschließend 18,63 m Laufweg sowie beide Erinnerungen. Freundliche Ausgangslage kontrolliert; andere Formulierungen und Ablehnungen bleiben mögliche Varianten.

Der vorherige Ladefix bleibt enthalten: Die Wohnraumerfassung verwendet DFUs vorhandene Gebäudedaten; der gemessene Ladeblock sinkt von 3.097 ms auf 8,92 ms. Alle 408 Hausprofile stimmen exakt überein. Der separate Editor-Zeitsprung-Hänger stammt überwiegend aus virtueller Asset-Suche; die Ursache sämtlicher gemeldeter Player-Freezes ist damit nicht geklärt.

Der vorherige Zeugenfix bleibt enthalten: Stationäre Außen-NPCs werden bei Angriffen nicht mehr im Schlaf oder ohne Sichtkontakt als Augenzeugen eingetragen. Zehn kontrollierte native Fälle prüfen Zeugenstatus und Erinnerung; vollständige Kampfszenen bleiben separat offen. Die bisherigen Gesundheits- und Restore-Prüfungen für mobile und stationäre Beobachter bestehen weiterhin.

Kurze Begegnungen mit einem stationären Markt-NPC bestehen den kontrollierten Test: Blick zum Partner, Kontext und Erinnerung für beide sowie Weiterlaufen zum bisherigen Ziel. Die anfängliche Nähe wurde dabei arrangiert. Der vorherige Fix für ein bereits zerstörtes Inn-Vermessungsobjekt bleibt enthalten.

Der anonyme Mord-Meldeauftrag bleibt Hörensagen; die Wache erhält weder eine erfundene Täteridentität noch einen erfundenen Tatort. Das endliche Wachsystem muss aktiviert sein; die Paket-Vorgabe ist bereits eingeschaltet. Der vollständige freundliche Gesprächsdurchlauf besteht jetzt mit Gemma und passenden Breeze-Sprachdiensten. Details und Grenzen stehen im [Dialogtest](audits/2026-10-26-murder-report-dialogue.md).

Der Inn-Fehler besteht den gezielten nativen Test: Tresen, stabiler Sitzplatz, Speicherstand und Spieler-Ausgang. Freie Fluchtbewegung erreicht im nativen Test 13,16 km/h statt zuvor 5,54 km/h. Vollständiger Hash und Prüfgrenzen stehen in [TESTING.md](TESTING.md); die [Arbeitsliste](WORKLIST-2026-10-26.md) enthält die übrigen offenen Punkte.

Für diesen kurzen Test besonders hilfreich: um 09:10 beim Kleiderladen warten, später tatsächlichen Eintritt/Rückkehr beobachten; persönliche Trankpreise und Null-Gold-Pergamenthandel prüfen; stationäre Markt-NPCs nach wirklich beobachteten Ereignissen erneut ansprechen. Breitere stationäre Wahrnehmung und spontane Begegnungshäufigkeit sind noch nicht nativ abgenommen. Automatische Marktrollen sind noch nicht implementiert.

## Start

Neu im Build 06:04: persönlicher Handel mit einem statischen Ladenhändler besteht Fensterübergang, Abbruch, Teilstapel-Kauf und Rückverkauf. Ursache war eine ungültig erzeugte Trankflasche im separaten Händler-Inventar. Regalwaren bleiben davon getrennt. Weitere statische Rollen und freie Handelsformulierungen bleiben offen.

`DFU/Play DFU.lnk` wie gewohnt starten. Der Launcher startet Gemma und Breeze. Alle vorhandenen Spielstände sind Testdaten. Die automatisierten Prüfungen sind beendet; das Spiel ist für deinen eigenen Start frei.

## Sinnvolle kurze Tests

1. **Markt und Wege:** Tagsüber durch Daggerfall gehen. Auf stockende NPCs, Türansammlungen und Familienpaare achten.
2. **Kontext und Ortsauskunft:** Einen laufenden Bewohner fragen, was er vorhat. Nach einem Rüstungsladen fragen, anschließend um Kartenmarkierung oder Hinführen bitten. Begleitung hängt von Bereitschaft und Pflichten ab.
3. **Persönlicher Handel:** Einen normalen mobilen Bewohner um Handel bitten. Spieler-/NPC-Items prüfen, erst abbrechen, dann kaufen oder verkaufen. Einen niedrigeren Preis anbieten und das Gegenangebot beobachten.
4. **Beziehung:** Freundlich sprechen, später erneut ansprechen. Nach einer Kränkung bleibt der NPC ablehnend; eine Entschuldigung löscht Groll nicht sofort.
5. **Gesundheit und Wachen:** Vorhandene Behandlung, Hilfe und Wachreaktionen ausprobieren. Beobachtete Orte und konkrete Ereignisse beim Feedback nennen.
6. **Mord melden lassen:** Draußen einen erwachsenen Bewohner bitten: `I saw a murder. Please find a guard and report it anonymously.` Nach Zustimmung das Gespräch schließen und den tatsächlichen Weg beobachten. Später nachfragen. Die Wache erhält keinen erfundenen Täter oder Tatort; eine Verhaftung oder Untersuchung wird dadurch nicht automatisch ausgelöst.

Leihen benötigt bekanntes Vertrauen ab75 und eignet sich erst für vertraute NPCs. Rückgabe desselben geliehenen Gegenstands bleibt auch bei gesunkenem Vertrauen möglich.

## Noch offen

- Persönlicher Handel mit weiteren statischen Rollen und Gilden-NPCs: Der Ladenhändler besteht jetzt Fensterübergang, Abbruch, Kauf und Rückverkauf; diese weiteren Rollen und freie LLM-Formulierungen bleiben separat zu prüfen.
- Gemischter Tausch und sämtliche freien Handelsformulierungen sind noch nicht vollständig im Spiel geprüft.
- Bezahltes Mitreisen, Enter→Wait in weiteren Gebäuden, Shield, Training und neue Kampffunktionen haben offene Abnahmen.
- Springen/Klettern, bewegliche Dungeonplattformen und vollständige NPC-Haft sind noch nicht fertig.

Für Fehler reichen NPC/Ort, letzte Aktion und sichtbares Ergebnis; ein Screenshot hilft bei Darstellung oder Bewegung.
