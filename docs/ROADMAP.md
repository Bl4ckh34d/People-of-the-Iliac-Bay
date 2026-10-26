# People of the Iliac Bay — Roadmap

Stand: 25. Oktober 2026. Dieser Plan ersetzt die früheren Architektur- und Zwischenstandslisten.

Zwischenstand 26. Oktober, 16:22: Bundle `E31822DEE3D6` installiert; Core 2147/2147
und Unity-Paketprüfung bestehen. Stuhlidentität und Tresenzahlung sind korrigiert;
zwei Inn-Layouts bestehen Bezahlung, Sitzen und Kauferinnerungen. Kontrollierter
Familienlauf15 besteht auch den Ausgang und Heimweg; Ausgang im ursprünglichen
Inn131841 bleibt offen. Reisezeitbudget, Reparaturverwahrung und benannte
NPC-Übergabeerinnerungen sind installiert. Der native Banktest wird mit erhaltenem
Budget wiederholt; echte Wachverfolgung durch Türen ist noch abzunehmen.

Aktive Ergänzungen vom 26. Oktober 2026 stehen in der [Implementierungs- und Abnahmecheckliste](WORKLIST-2026-10-26.md). Diese Fehlerbehebung und Spielabnahme haben Vorrang vor neuen späteren Roadmap-Systemen.

Zwischenstand 26. Oktober, 13:45: Fünf ausdrücklich beauftragte Astra/High-Agenten
ergänzten Inn-Kauferinnerungen, den vollständigen Tempel-Skillkatalog, restaurierte
NPC-Zauberkosten, tageweise Begleiterbezahlung und einen begrenzten Dungeon-Sprungadapter.
Gemeinsamer Build und 2144 Core-Tests bestehen. Native Abnahme dieser Ergänzungen
bleibt offen; Dungeon-Follow, Klettern und Thronplattform sind damit nicht erledigt.
Die [Checkliste](WORKLIST-2026-10-26.md) enthält Einzelbelege und Grenzen.

## Leitlinie

Sichtbare Kernsysteme zuerst. Bestehende DFU-Mechaniken verwenden. Nur Zustand speichern und berechnen, der Verhalten, Begegnungen, Gespräche oder tatsächlichen Besitz beeinflusst. Kein Zusatzsystem als Voraussetzung für ein einfaches sichtbares Verhalten.

Entwicklungs-Spielstände sind entbehrliche Testdaten. Keine Rettungs- oder Migrationsprojekte für alte Entwicklungsstände. Normales Speichern/Laden und stabile NPC-Identitäten bleiben erforderlich.

Aktuelle Modelle: Gemma + Breeze. Keine zusätzliche Modellfamilie, Bildinterpretation aus NPC-Augen oder Backend-/Gerätekombinationsmatrix als Core-Voraussetzung. Spracheingabe bleibt zurückgestellt.

Kein Regex im eigenen DFU-Projekt, einschließlich Werkzeugen und Tests. Gemma interpretiert Sprache; DFU prüft reale Ziele und führt Aktionen aus. Keine Ersatz-Wortmuster mit `Contains`. Der bestehende Mischbetrieb aus Modellentscheidungen und Regex-Altwegen wird anhand [des geprüften Ersatzkonzepts](NO-REGEX-DIALOGUE.md) bereinigt. Noch offen; zuerst aktive Routing-/Ortsauskunft-Overrides, danach Aktionen und übrige Altwege.

## Spielbarer Stand — 25. Oktober 2026

Der aktuelle Entwicklungsbuild ist im lokalen Player installiert. Start und kurze Tests: [PLAYTEST.md](PLAYTEST.md). Exakter Paketstand und Nachweise: [TESTING.md](TESTING.md). „Geprüft“ bezeichnet jeweils den genannten Ablauf, keine vollständige Abnahme aller Orte und Formulierungen.

| Bereich | Geprüfter Stand | Noch offen |
| --- | --- | --- |
| Alltag und Familie | Mehrere Tagesziele; kontrollierter Familienausflug mit gemeinsamem Weg, Inn, Tisch und Heimkehr; Partner im Gesprächskontext; Tür geschlossen, wenn nur das Kind zu Hause ist. | Vollständigen freien Tagesablauf, dichte Städte und weitere Gebäude beobachten. |
| Navigation und Sitzen | Vorgerechnete zwölf Gothway-RMB-Varianten, normale Laufwege, Terrasse/Treppe/Sitzplatz und Aufstehen; 40cm Wegpunkttoleranz und Paar-Kollisionsausnahme. | Weltweite Blockvarianten, weitere Innenräume und Dungeons; Springen, Klettern und bewegliche Plattformen. |
| Gespräch und Ortsauskunft | Tatsächlicher Aufenthalts-/Wegkontext; Persönlichkeit und Beziehung beeinflussen Reaktionen; bekannte native Orte nennen, markieren und hinführen. | Breite freie Formulierungen, zusätzliche Questadapter und zuverlässige lange Aufträge. |
| NPC-Aktionen | Follow/Wait/Stop, Hinführen mit Abstandswarten, echte Itemübergaben; Follow durch Inn-Türen; ein tatsächlicher Einkauf mit Übergabe an den Spieler. | Enter→Wait in weiteren Gebäuden, Einkauf für andere NPCs, breitere Aktionsketten. |
| Gesundheit | Native Tränke und Selbstzauber, begrenzte HP-Tranknutzung, Schlafheilung; selbstständiger Tempel-/Gildenbesuch mit Zahlung, Behandlung und Rückkehr; Begleiter erhält und nutzt Gegengift. | Shield und weitere Begleiterzauber; zusätzliche Kampf-/Dungeon-Situationen. |
| Wachen | Kontrollierte körperliche/magische Angriffe lösen passende native Reaktionen aus; echter Schaden; Leichenentfernung, Zeugen, Sichtkontakt und Suche; maximal sechs Wachen. | Freie Täterentscheidungen und Flucht; Festnahme nur mit wirklich nutzbarer Zelle; weitere Innenraumfälle. |
| Erinnerung und Beziehung | Gespeicherte Persönlichkeit, Stimmung, Angst/Groll und relevante Ereignisse; bekannter Vergiftungsgrund im Gespräch; native Streetwise/Etikette/Mercantile-Praxis. Kontrollierte Begegnung mit einem stationären Markt-NPC: Partnerblick, beidseitiger Kontext/Erinnerung und Weiterlauf zum vorherigen Ziel nativ geprüft. | Spontane Begegnungshäufigkeit, weiterer stationärer Sicht-/Schlafkontext, zusätzliche Erlebnisse und allgemeine Dialogqualität. |
| Persönlicher Handel | Mobile Bewohner: Vorschau/Abbruch, Teilstapel-Kauf, Verkauf, exaktes Gold, Feilschgrenze und Stimmung; Leihen/Rückgabe mit echter UID, auch nach Vertrauensverlust, ohne Follow abzubrechen. Statischer Ladenhändler: Fensterübergang, Abbruch, Kauf und Rückverkauf nativ geprüft; Regalbestand bleibt getrennt. | Weitere statische Rollen, gemischter Tausch und freie Gemma-Handelsanfragen benötigen weitere Spielabnahme. |
| Besitz und Begleiter | Native Rollen-/Geschlechtspools, persönliches Geld und belegter Hausschlüssel sind integriert. Erste Erzeugung und Persistenz werden beim Build geprüft. | Breite Ingame-Abnahme der Pools, bezahltes Mitreisen, Fortschritt, Reparatur und Training. |

Handel setzt Bereitschaft voraus; Mercantile beeinflusst Preise und Gegenangebote, ist keine Zugangssperre. Das NPC-Geld bleibt internes Kaufbudget. Schlechte eingereichte Angebote verschlechtern die Stimmung, vorteilhafte Abschlüsse verbessern sie. Leihen verlangt bekanntes Vertrauen ab75; die Rückgabe eines belegten Gegenstands verlangt kein erneutes Vertrauen. Schlüssel, ausgerüstete und geschützte Gegenstände bleiben unverkäuflich.

Das vereinfachte Handelsfenster zeigt NPC-Items links und Spieler-Items rechts, nur OFFER/CLEAR/EXIT sowie helle Felder für NPC-Preis und eigenes Angebot. Beide Warenrichtungen werden zu einem Goldsaldo verrechnet. Native DFU-Preise mit Mercantile/Personality bleiben die Basis; NPC-Persönlichkeit und Disposition sowie bekanntes Vertrauen und aktuelle Stimmung beeinflussen die Konditionen. Vertrauen/Stimmung wirken gleitend, erst Goldbeträge werden gerundet. Streetwise/Etiquette beeinflussen keine Handelspreise. Die Preisgrenze bleibt pro unverändertem Warenkorb fest; Stimmung und Vertrauen beeinflussen weitere Gegenangebote. Neue Hintergrundsysteme sind dafür nicht erforderlich.

<a id="delivery-plan"></a>
## Nächste Umsetzungsschritte

1. **Diesen Build im normalen Spiel prüfen:** Bewegung, Tagesleben, Gespräche, Ortsauskunft und persönlicher Handel. Gemeldete Fehler haben Vorrang.
2. **Handel abschließen:** korrigierten statischen Fensterübergang bei weiteren Rollen prüfen; freien Gesprächseinstieg und gemischten Tausch durchgehend prüfen.
3. **Vorhandene Aktionsadapter fertig prüfen:** Enter→Wait, bezahlte Ortswechsel, Tempeltraining, Reparatur, Shield und Übergaben an andere Personen.
4. **Gesundheit und Wachreaktionen abrunden:** freie Täterreaktionen und Verfolgung durch beobachtete Türen; Festnahme erst nach Nachweis nutzbarer Gefängnisgeometrie.
5. **Navigation erweitern:** zusätzliche Innen-/Dungeonblöcke, Springen, Klettern und die Thronplattform in Privateer's Hold.
6. **Erinnerung und Beziehung verbreitern:** weitere tatsächliche Erlebnisse und Zusagen im Gespräch; keine zusätzliche Hintergrundsimulation.

Der Hauptagent integriert und prüft die elf am 26. Oktober ausdrücklich beauftragten Astra/High-Arbeitspakete. Die zwei neuesten Aufgaben betreffen Dungeon-Follow durch echte native Eingänge und Erinnerungen an tatsächlich beobachtete Diebstähle bei mobilen und stationären Personen. Die vorherige Zweiergruppe bearbeitet native Reparatur einschließlich Abholung und Gegenstandsübergaben an andere NPC-Empfänger. Beauftragung bedeutet keine bestandene Spielabnahme. Keine unbegrenzte oder verschachtelte Delegation. Je Schritt einen sichtbaren Ablauf abschließen; keine weiteren Debug-Kamera- oder Nebensysteme.

<a id="detailed-design"></a>
## Verbindlicher Funktionsumfang

### Dungeon-Navigation: Sprünge, Klettern und Plattformen

NPCs müssen erreichbare Höhenunterschiede durch tatsächliches Springen und Klettern überwinden können. Native Geometrie und vorhandene Fähigkeiten begrenzen die Ausführung; kein Versetzen an unbekannte oder unerreichbare Ziele. Bewegliche native Plattformen behalten ihre tatsächliche Position. Ein NPC kann auf eine bekannte Plattform springen, dort warten und bei Aktivierung durch den Spieler mitfahren. Konkrete Abnahme: Thronplattform in Privateer's Hold, Spieler bittet den NPC aufzuspringen und betätigt danach selbst den Hebel. Dialog und Aktion müssen dieselbe tatsächlich identifizierte Plattform verwenden. Diese Erweiterung ist beauftragt, noch nicht als implementiert oder im Spiel geprüft abgenommen.

### NPC-Festnahme, Fahndung und Haft — beauftragt, noch nicht abgenommen

- Festnahme gibt es ausschließlich bei einem tatsächlich vorhandenen Kerker mit einer freien, erreichbaren Zelle. Nur dann ergeben sich gewöhnliche beziehungsweise nicht sehr mächtige Täter angesichts mehrerer tatsächlich anwesender Wachen. Reale Kampfwerte und bestehende Rollen-/Dienstregeln bestimmen die Einschätzung. Überlegene oder widerstandsfähige unabhängige NPCs können kämpfen oder fliehen.
- Eine Wache führt den ergebenen NPC körperlich neben sich zum tatsächlich vorhandenen Schloss-/Gefängniszugang und, soweit die native Geometrie es wirklich hergibt, in eine echte Zelle. Beide sind während der Abführung nicht ansprechbar. Keine erfundenen Kerker oder unsichtbaren Begleitwege. Die genaue verfügbare Gefängnisgeometrie von Daggerfall und anderen Orten wird erst verifiziert.
- Für dasselbe tatsächliche Vergehen gelten ungefähr die nativen Spieler-Strafzeiten. Native Strafberechnung und eigener NPC-Geldbeutel verwenden; den Spieler weder bezahlen noch Zeit absitzen lassen. Kein pauschales weltweites Vorspulen wegen NPC-Haft.
- Während seiner Haft kennt der NPC diesen tatsächlichen Zustand im Gesprächskontext. Nach der Abführung darf ein erreichbarer Gefangener durch echte Gitter angesprochen werden, sofern die Geometrie das ermöglicht.
- Fluchtziele können Stadtausgang, eigenes erreichbares Haus oder ein anderes wirklich zugängliches Gebäude sein. Sieht die Wache den Eintritt, folgt sie durch diese konkrete Tür. Ohne diese Beobachtung erhält sie keine Innenraumposition.
- Nach verlorener Spur sucht die Wache ungefähr30 Spielminuten und beendet danach die aktive Suche. Die bekannte Täteridentität und Fahndung bleiben bestehen; späterer tatsächlicher Sichtkontakt kann erneut eine Festnahme auslösen. Suchfrist und dauerhafte Fahndung sind getrennte Zustände.
- Später: Befreiung eines mit dem Spieler verbundenen Gefangenen, Gespräche über unterlassene Hilfe und passende Vertrauens-/Grollfolgen. Keine automatische Kenntnis des NPCs über unbekannte Handlungen des Spielers. Das ist noch kein implementiertes Befreiungssystem.
- Ohne nutzbaren Kerker entfällt die Festnahmeoption vollständig: Die Wache bekämpft den Täter bis zu dessen Tod; er kann sich zum Kampf stellen oder fliehen/verstecken. Das gilt auch in Dorf, Homestead, Farm oder Tempel ohne belegten Kerker. Keine Dorf-zu-Stadt-Transporte, provisorischen Zellen oder erfundenen Verwahrorte ergänzen. Ein bloßes Schloss- oder Stadtmauergebäude beweist noch keine freie Zelle.

Kleinster Umsetzungsschritt: vorhandene Wachreaktion, tatsächliche Täteridentität, nativen Satz für die aktuelle Straftat und den vorhandenen Bewegungs-/Gesprächskontext verbinden. Keine zusätzliche Gerichts-, Wirtschafts- oder globale Polizeisimulation.

| Bereich | Kleinste benötigte Umsetzung | Bezüge |
| --- | --- | --- |
| Identität und Familie | Stabile Person, drei Charakterzüge, Rolle, Zuhause und wirkliche Familienbeziehungen. Kernbiografie kompakt; bekannte Fakten nicht neu würfeln. | LIFE-01–08 |
| Bevölkerung | Große Mehrheit gewöhnliche Einwohner mit einfachen Routinen. Wenige beständige Abenteurer; bevorzugte Aufenthaltsorte nach Klasse. Gewöhnliche Bürger werden nicht durch freundliche Gespräche zu Abenteurern. | LIFE-02,08,21,34 |
| Tagesleben | Rollenpassende Arbeits-, Besorgungs-, Ruhe- und Freizeitblöcke; gemeinsame Ausflüge benutzen dieselben Bewegungsregeln. Kein eigener Berufs-Simulator. | LIFE-15–22,36 |
| Soziales | Stabile Persönlichkeit; veränderliche Stimmung, Vertrauen, Angst und Groll. Etikette/Streetwise/Mercantile und native Reputation haben tatsächlichen Einfluss. | LIFE-03,10–16,35,38,41 |
| Wahrnehmung | Native Wahrnehmung, Entfernung, Blickrichtung und Hindernisse. Eigene Beobachtung, Behauptung und unbekannte Information unterscheiden. Kein zusätzliches visuelles Modell. | LIFE-11–14 |
| Gesundheit und Kampf | Native Werte, Effekte, Heilmittel und Kampfsteuerung. Rollen- und Dienstregeln vor allgemeinen Fluchtentscheidungen. | LIFE-17–20,30–33 |
| Besitz und Handel | Persönliches Geld und tatsächliche Items bei relevanten Käufen, Verkäufen, Geschenken, Diebstählen und Beute behalten. Native Dienste und Ladenangebote nutzen. | LIFE-22–26,29 |
| Inn | Native Menüwahl und Preis; Käufer zahlt. Kein Warenbestand oder Inventar des Wirts als Kaufvoraussetzung. Danach tatsächlicher Tisch-/Ruheaufenthalt. Paarabrechnungen nicht weiter verkomplizieren. | LIFE-17,21,26 |
| Einkommen | Wenn benötigt, einfacher rollenabhängiger Tageslohn bei tatsächlicher Beschäftigung. Keine Abrechnung jedes Arbeitsschritts, Arbeitgeberkonten oder Hintergrund-Lieferketten. | LIFE-26 |
| Erinnerung | Relevante Begegnungen und Folgen plus kompakter Abruf. Später einfache Zusammenfassungen bei Bedarf. Keine zusätzliche Archiv-, Export- oder Altsave-Plattform als Core-Blocker. | LIFE-39–41 |
| Weltzustand | Geladene Akteure bewegen; bekannte Reisende, Familienausflüge und konkrete Zusagen bei Zeitablauf fortschreiben. Unbesuchte Orte aus Seed und bereits bekannten Fakten erzeugen. | LIFE-43,49,51,52 |
| Darstellung | Gemma-Text, Breeze-Stimme und Portrait aus demselben NPC-Zustand. Audio, wortweise Anzeige und Mundanimation zusammen starten und synchron bleiben. | LIFE-42 |

### Feste Rollenregeln

- Adel und belegte hohe Ränge werden gegenüber dem Spieler nicht unterwürfig oder verängstigt.
- Palastwachen und Soldaten kämpfen bis zum Tod. Stadtwachen kämpfen zu98% weiter; die übrigen2% dürfen bei schwerer Verletzung in erreichbare Stadtmauerquartiere fliehen.
- Unabhängige Rogues, Assassinen und ähnliche Kämpfer können bei schwerer Verletzung fliehen. Belegter Palast-/Adelsdienst hat Vorrang vor dem unabhängigen Standard.
- Diese Regeln ändern weder native Feindschaft noch Questschutz und verhindern keine Heilung.

## Später, nach funktionierenden Kernabläufen

- **Stationäre Menschen und Marktplatzrollen:** Auch durch andere Mods als Dekoration platzierte NPCs sind gleichwertige Personen mit eigener Identität, Wahrnehmung, bewerteten Erinnerungen und Gesprächskontext. Dazu gehören Marktstandhändler ohne Handelsmenü, Bedienungen innen und außen, der Bühnenunterhalter, Schauspielerinnen und Obdachlose. Später Rollen möglichst aus tatsächlicher Platzierung, Stand/Tisch und Waren-Props bestimmen; wiederverwendete Sprites allein beweisen keinen Beruf. Für mehrdeutige Szenen gezielte Layout-/Ortszuordnungen erlauben. Verifizierte Rolle, Arbeitsplatz und gegenwärtige Tätigkeit ins Kontextfenster aufnehmen; stationäre Arbeit ist zunächst ihr Alltag, ohne erfundene mobile Tagespläne. Handelsmenüs, Warenbestände und zusätzliche Routinen folgen erst nach zuverlässiger Rollenbestimmung. Die laufende Erinnerungs-/Begegnungsanbindung ist bereits Teil der aktiven Checkliste, diese automatische Rollenzuweisung noch nicht.

- **Spracheingabe-Modelltest (STT):** Auf Nutzerwunsch vom25. Oktober2026 **Underdog Bark 3.0 Plus** als Kandidaten testen (korrigiert von Pro). Erwarteter Nutzen laut Nutzer: kleiner und ähnlich gut; noch nicht verifiziert. Zuerst genaue Modellidentität und tatsächliche STT-Eignung bestätigen, danach Speicherbedarf, CPU-/GPU-Latenz, Erkennungsqualität und Verhalten bei Stille/Hintergrundgeräuschen messen. Modellentscheidung und Integration bleiben offen; dieser Eintrag startet keine STT-Implementierung.
- **Begleiter:** Rekrutierung, Gruppenverwaltung, Mitreisen und Kampfhilfe. Einfache tageweise Bezahlung mit klaren Kündigungsregeln; keine minutengenaue Treuhand-/Erstattungsarchitektur. Bei Stadtaufenthalten wenige begründete Erledigungen und ein klarer Treffpunkt. | LIFE-19,24,25,47 |
- **Begleiter-Fortschritt:** Tatsächlich benutzte Skills verbessern sich. Gewöhnliche Einwohner behalten ihr erzeugtes Profil; kein laufendes Berufstraining der gesamten Bevölkerung und keine allgemeine Persönlichkeitsentwicklung. | LIFE-09 |
- **NPC-Gruppen und Expeditionen:** Sichtbare Gruppen, Reise und ein kompaktes Ergebnis bei Abwesenheit. Keine unbeobachtete Ausführung einzelner Dungeonkämpfe oder Containerleerungen. | LIFE-47,48 |
- **Weitere Orte und Berufe:** Weitere native Ortstypen schrittweise anbinden. Saisonale Tätigkeiten als Routine-/Darstellungswechsel; keine landwirtschaftliche Ressourcenproduktion. | LIFE-34,45,57 |
- **Weitere native Aktionen:** Enter/Exit im Inn und körperliche Routine-Rückkehr aus einem fremden öffentlichen Gebäude sind geprüft. Weitere Tür-/Questgeometrien und gemeinsame Familienbesuche weiter anbinden. Kampfunterstützung, Zauber, Diebstahl, Suche, Reparatur und Training jeweils als vollständiger kleiner Ablauf. | LIFE-20,23,30,32,38 |
- **Lokale Folgen:** Trauer, ein geschlossener Laden, ein Nachfolger oder ein bekanntes örtliches Ereignis. Kein allgemeines Erb-/Schuldrecht und keine flächendeckende Ereigniserzeugung. | LIFE-31,37,54 |
- **Veröffentlichung:** Installation, verständliche Einstellungen und normales Übertragen von Spielständen erst für den Release bearbeiten; funktionierende bestehende Speicherwege nutzen. |

## Aus dem aktiven Plan gestrichen

- Autonomer Händler-zu-Händler-Handel, Preisrecherche, Lieferketten und allgemeines Schuldenmanagement. Frühere LIFE-27/28.
- Quantitative Tierhaltung, Futter-/Ertragsketten und Ersatzbetreuung als Wirtschaftssystem. Frühere LIFE-46; sichtbare Tätigkeiten bleiben unter LIFE-45.
- Eine zusätzliche weltweite Ereigniswarteschlange, rückwirkende Lebensgeschichte unbekannter Orte und regionale Produktions-/Wachstumsbudgets. Frühere LIFE-50/53/55.
- Eine weltweite Mehrjahresabnahme als Voraussetzung für normale lokale Funktionen. Frühere LIFE-56; gezielte Szenarien bleiben unter LIFE-44.
- Weiterer Ausbau von NPC-Augenbildern, Export-/Archivplattformen, Entwicklungs-Save-Migrationen, dauernder Alltags-Skillentwicklung und permanentem Trait-Umbau.

Die früheren IDs bleiben zur Nachverfolgung in historischen Dokumenten. Sie begründen keine Implementierungspflicht. Zusätzliche Mechaniken benötigen einen konkreten sichtbaren Nutzen.

## Prüfungen und Dokumentation

- Kompilieren und betroffene vorhandene Tests ausführen. Für sichtbare Änderungen einen passenden normalen Spielablauf prüfen.
- Stabile Identität, keine doppelten tatsächlichen Items/Zahlungen und funktionierende Rückkehr nach Unterbrechung bleiben nötig. Keine kombinatorische Absicherung hypothetischer Sonderfälle.
- Keine neue GPU-Arbeit außerhalb von Gemma/Breeze ohne konkrete Notwendigkeit und Koordination.
- Die Mindmap zeigt denselben reduzierten Umfang. Details zum aktuellen Nachweis stehen in [TESTING.md](TESTING.md).
- [Historische Roadmap vor der Vereinfachung](archive/ROADMAP-2026-10-24-before-simplification.md): Nachschlagequelle, kein zweiter aktiver Arbeitsplan.
