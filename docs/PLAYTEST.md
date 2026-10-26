# Spieltest — 26. Oktober 2026

Aktuell installiert: **`D3CA68C3D919`**; Distribution und Player identisch.
Unity-Paketprüfung besteht; die enthaltene Core-Basis besteht2162 Tests.
Kontrollierte Wachtests bestehen beobachteten und unbeobachteten Türdurchgang,
Wiedererkennung innen und Zielverlust.22 native Diebstahls-Erinnerungsfälle bestehen.
Dungeon-Follow samt Kontext und Rundreiseprobe ist enthalten, aber noch nicht
nativ abgenommen: der erste Lauf scheitert vor Eintritt an der Reisenden-Fixture.
Frühere Begleiter-, Tempel- und Inn-Ergänzungen bleiben enthalten.
10 g je tatsächlichem NPC-Level buchen einen begonnenen 24-Stunden-Tag
(Level5:50g, Level12:120g). Frühe Kündigung erstattet keinen
Teil des Tages, gescheiterte Aufnahme erstattet vollständig, keine automatische
Verlängerung. Gewöhnliche generierte Begleiter haben derzeit meist das native
Level1 und kosten damit10g; diese Änderung erzeugt keine neue Levelverteilung.
Die native Prüfung dieser Konditionen steht noch aus.

Neu enthalten: Erfolgreiche Kampf-/Zauberhilfe berechtigter Begleiter verbessert
die tatsächlich genutzte Fertigkeit. Abgelaufene oder pausierte Verträge erlauben
keinen Fortschritts-Umweg. Trauer um Partner, Eltern und Kinder benötigt jetzt
wirkliche Kenntnis des Todes: Beobachtung, Leichenfund oder überbrachte Nachricht.
Unbekannte Täter werden nicht automatisch dem Spieler gleichgesetzt. Erinnerungen
und tägliche Begrüßungsversuche überstehen die Speicherung.49 gezielte Trauer-
und22 Fertigkeitsprüfungen bestehen; vollständige Spielabnahme bleibt offen.

Für den kurzen Test: Begleiterlohn im Angebot prüfen, tatsächliche Hilfe leisten
lassen und Familien nach bekannten Ereignissen erneut ansprechen. Die jüngsten
Ergänzungen sind Testfunktionen, keine vollständig abgenommene Veröffentlichung.

Familien erreichen in zwei geprüften Inns ihre Plätze, zahlen und sitzen stabil;
ihre tatsächlichen Käufe erscheinen korrekt in Erinnerungen. Im ursprünglichen
Inn131841 klappt der anschließende Ausgang noch nicht. Im kontrollierten Lauf15
mit Inn197127 ist der gesamte Familienausflug einschließlich Heimkehr bestanden.
Der Bank-Enter→Wait-Test bindet die richtige Tür. Blockierende Testposition und
zu kurzes Testbudget sind korrigiert. Der Folgelauf zeigt aber, dass die
Aufgabensteuerung das längere Budget verwirft. Der Quellfix besteht gezielte
Prüfungen und ist installiert. Der erneute Banklauf besteht den tatsächlichen
Eintritt und anschließendes Warten bei draußen gebliebenem Spieler. Identität,
Inventar und Vitalwerte bleiben unverändert; Innenwege sind damit nicht geprüft.
Reparaturverwahrung und benannte Übergabeerinnerungen beider NPC-Teilnehmer sind
ebenfalls enthalten; ihre vollständige Spielabnahme steht noch aus.
Dungeon-Follow, Klettern und Thronplattform bleiben ebenfalls offen.
Details und weitere offene Punkte: [Arbeitsliste](WORKLIST-2026-10-26.md).

Vorheriger Zwischenbuild `7EC048CD49C7`, um 12:21 installiert; Distribution und Player identisch. Unity-Paketprüfung und 2131 Core-Tests bestehen. Korrigierte native Paarwege erlauben gemeinsame Familienausflüge. Der isolierte friedliche Test besteht morgendlichen Bankservice, einmalige Auszahlung, echte Heimkehr und anschließenden gemeinsamen Aufbruch. Vollständige Inn-Ankunft/Bezahlung und allgemeine Straßendichte bleiben offen. Details: [Familien-Banktest](audits/2026-10-26-family-cash-visit.md). Der folgende 11:05-Stand ist damit ersetzt.

Aktueller Zwischenbuild `4F4349E44CF4`, um 11:05 installiert. Unity-Paketprüfung und 2131 Core-Tests bestehen. Einzelne Kurzbesuche blockieren weitere Tagesziele nicht mehr. Die native Nachmittagsstichprobe steigt von drei auf elf Bewohner draußen; die Stadt bleibt stellenweise noch zu leer. Allein untätige Ladenwartende geben nach Öffnung binnen einer Spielminute frei. Geplante gemeinsame Inn-Finanzierung ist enthalten, der morgendliche Familien-Bankgang aber noch nicht nativ abgenommen. Details: [Dichtemessung](audits/2026-10-26-street-density.md).

Früherer Zwischenbuild `7BB6C4BB878D`, um 08:57 installiert (vom aktuellen Build ersetzt). Paket: `dist/peopleoftheiliacbay.dfmod`. Unity-Paketprüfung und 2121 Core-Tests bestehen. Neu: Auch der separate Spielerangriffs-Pfad prüft die Blickrichtung. Wer den Spieler nicht sieht, wird nicht automatisch Augenzeuge; bloßes Hören verrät den Täter nicht. Je neun native Fälle für mobile und stationäre Körper bestehen. Vollständige Kampf-/Todesszenen und tatsächliche Inn-Angriffe bleiben separat offen; Details im [Testbericht](audits/2026-10-26-player-attack-perception.md).

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
