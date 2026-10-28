# Vereinbarter Kernumfang und Detailregeln

Stand: 27. Oktober 2026. Vom Nutzer ausdrücklich zur Umsetzung durch sechs GPT-6.1-Sol-Agenten mit High freigegeben. Dieses Dokument konkretisiert die aktuelle ROADMAP; vorhandener Code ist zuerst zu prüfen und weiterzuverwenden. Keine neuen Grafiken, Sprites oder Animationen.

## Maßstab und Umfang

Einfachheit gewinnt. Native DFU-Regeln verwenden, nur sichtbare oder tatsächlich folgenreiche Mechaniken ergänzen. Die zwanzig Hauptbereiche der Restliste umfassen Markt, stationäre Personen, Wachen, Tatortermittlung, NPC-Haft, Dungeon-Navigation, semantische Dialoge, Biografie, Alltag, Familie, Reisende, Bettenkapazität, Auftragsketten, Begleiter, Gesundheit/Dienste, Handel, Erinnerung, Nacht/Gefahr, Performance und aktuelle Speicherung. Vorhandene Funktionen mit fehlender Spielprüfung sind keine fehlenden Systeme.

Zurückgestellt bleiben autonome Expeditionen, Laden-Nachfolger, Gefangenenbefreiung, Spracheingabe und Veröffentlichung. Keine Lieferketten, Hungerberechnung, Raumvermessung, unbeobachtete Einzelkämpfe, neue Zufallsverbrechen oder Altsave-Migrationen. Ein LLM (Gemma) und ein TTS-Modell (Breeze); keine zusätzliche Modellfamilie. Kontext und Erinnerung erhalten.

## Markt, Personen und Besitz

- Alle stationären NPCs sind ebenso tötbar wie mobile NPCs, unabhängig davon, ob sie nativ oder vom Mod hinzugefügt wurden. Ihr tatsächliches persönliches Inventar ist nach dem Tod lootbar. Bestehende Identität, native Todesereignisse und Item-UIDs weiterverwenden; keine zweite Inventarverwaltung und keine doppelte Beute. Diese spätere ausdrückliche Nutzeranforderung erweitert die vollständige Typabdeckung.
- Gewerbliche NPC-Käufe verändern das verfügbare Händlerangebot auch am Markt nicht. Der Käufer zahlt echtes Geld und erhält einen eigenen Gegenstand mit eigener UID. Spielerkäufe vermindern Verkaufsbestand. Privater Handel bleibt tatsächliche Besitzübertragung.
- Gewöhnliche gekaufte Lebensmittel und Verbrauchswaren gelten bei Heimkehr als verbraucht. Ausrüstung, Geschenke und Auftragsgegenstände bleiben erhalten. Nicht pauschal beliebige Heiltränke oder andere persönliche Ausrüstung löschen; nur den eindeutig zugehörigen gewöhnlichen Einkauf behandeln.
- Bühnenpublikum setzt einen real vorhandenen Darsteller voraus. Gewöhnlicher Aufenthalt ohne Darsteller behauptet keine Aufführung.
- Marktpersonal behält Identität, echten Haushalt, Bett, Besitz und Erinnerung. Bestehende Arbeitszeiten 06:00–06:30 und Feierabend 17:45–18:15 nach tatsächlicher Kundschaft gelten weiter. Laufende Gespräche/Transaktionen beenden, dann körperlich heimkehren.

## Wachen und Haft

- Sicht auf Spielerverfolgung genügt zum Beitritt. Tatsächlicher Halt-Ruf erreicht Wachen ohne Sicht innerhalb 200 m vom Rufer. Endlicher Wachbestand bleibt erhalten.
- Meldende Person und Wache sowie Wache und befragte Person stehen zwei bis fünf echte, nicht pausierte Sekunden einander zugewandt. In Spielernähe kurze sachlich passende Textzeile; kein neuer LLM-Aufruf pro Hintergrundbefragung und keine neue Sprachproduktion.
- Die flache Sprite-Fläche bleibt immer zum Spieler ausgerichtet. Bei mobilen NPCs wählt die logische Blickrichtung aus den vorhandenen Richtungsbildern. Stationäre NPCs erhalten solche Bilder später; bis dahin bleiben Gesprächsfokus und Stillstand wirksam, ohne ihre Grafikfläche vom Spieler wegzudrehen. Fehlende Richtungsbilder sind keine fehlende Gesprächslogik.
- Verschiedene erreichbare Tatortpunkte und physische Befragung; Zielauswahl vermittelt kein neues Täterwissen.
- NPC-Festnahme nur bei tatsächlich vorhandenem nutzbarem Gefängnis und freier erreichbarer Zelle. Reale Dienst-/Kampfregeln, körperliche Abführung und ungefähr native Strafzeit; keine weltweite Zeitbeschleunigung oder Spielerzahlung wegen NPC-Haft. Ohne nutzbaren Kerker gelten bestehende Kampf-/Fluchtregeln.
- Waffen und belegtes Diebesgut beschlagnahmen. Diebesgut zuerst dem tatsächlich bekannten bestohlenen Shop/NPC zurückgeben; gestohlene Waffen fallen ebenfalls darunter. Übrige Waffen in einen tatsächlich vorhandenen Stadtmauercontainer legen.
- Nutzerentscheidung für fehlenden Container: Waffen und nicht zurückgebbares Diebesgut zerstören, nicht im Wachinventar verwahren. Ist der Eigentümer tot, Diebesgut zerstören oder in einen vorhandenen Stadtmauercontainer legen. Einfacher Standard: vorhandenen geeigneten Container verwenden, sonst zerstören. Keine neue Container-/Asservatenplattform, keine erfundene Herkunft eines Items.

## Alltag und Reisen

- Nur Betten zählen; ein Bett je Person. Vollbelegung sperrt auch Spielermiete. Keine Zimmerbindung.
- Familie wählt am Tresen; ein Mitglied finanziert alle, sonst vorher Bank. Sitzplätze erst bei Beobachtung. Betreuung ungefähr 50/50: ein Elternteil bleibt oder beide gehen und schließen das Kind ein; tagsüber Erwachsener zu Hause bedeutet offen, nachts/alle schlafen geschlossen.
- Bei vollem Wunsch-Inn andere erreichbare offene Inns versuchen. Dann passende Gilde mit belegter Mitgliedschaft oder bestehendem Gast-/Zugangsrecht und freiem Bett. Klasse allein verschafft kein Zugangsrecht. Wenn nichts frei/nutzbar ist, außerhalb der Stadt-/Siedlungsgrenze campen.
- Herkunftshaushalt, Familie, Persönlichkeit und Reisezweck bewahren; Rückweg erst bei Bedarf. Auf unbeobachteten Reisen keine zufällige Beraubung, Verletzung oder Tötung. Geladene tatsächliche Begegnungen bleiben wirksam.

## Begleiter, Navigation und Beute

- Ergänzung des Nutzers: Aktuell mitreisende Begleiter nehmen an der nativen Schnellreise des Spielers teil. Am Ziel bleiben Identität, tatsächliches Inventar, Ausrüstung und Beziehung erhalten. Native Reisezeit und bestehende Vertragsregeln gelten weiterhin; keine zusätzliche regionale Wegsimulation.
- Schnellreisekosten: nativer Alleinreisepreis × (1 + Anzahl tatsächlich mitreisender Begleiter). Ein Begleiter verdoppelt den Preis, zwei verdreifachen ihn. Der Gesamtpreis steht vor Bestätigung im Reisefenster und wird genau einmal abgebucht. Abbruch oder unzureichendes Geld führen zu keiner Reise und keiner Abbuchung. Bestehender Begleiterlohn bleibt ein eigener Vertragsposten.
- Reale native Wege, Fähigkeiten, Sprünge, Klettern und Plattformen verwenden. Keine Teleports zur Kaschierung eines Hindernisses.
- Bei unüberwindbarer Stelle am letzten sicheren erreichbaren Punkt warten. Bei nahe genug befindlichem Spieler normales Dialogfenster, sonst obere Flavour-Textmeldung. Nachricht nennt konkrete Wartezeit und anschließenden Plan.
- Wartefristen richten sich nach bestehendem Vertrauen und Beziehung: rein bezahlter Dienst wartet wenige Spielstunden innen und einige weitere draußen; sehr vertraute gute Freunde warten mehrere Spieltage an beiden Stellen. Keine neue Beziehungswährung. Agent darf einfache begrenzte Staffelung aus tatsächlichen vorhandenen Werten wählen und muss Zahlen/Schwellen im Ergebnis offenlegen. Diese Zahlen sind Implementierungswahl, keine vom Nutzer vorgegebenen Konstanten.
- Nach erster Frist körperlich zum Dungeon-Eingang und draußen weiter warten. Nach zweiter Frist zum tatsächlichen Ort der Anwerbung/Begegnung zurückkehren und dort bestehende Routine fortsetzen, nicht pauschal nach Hause schicken. Ist ein Rückweg ebenfalls blockiert, keine unerreichbare Ankunft behaupten.
- Ohne Absprache kein automatisches Plündern. Bei Planung von Dungeon-Crawl, Überfall, Einbruch oder Mord fragt der Begleiter nach Beuterechten. Zustimmung gilt für das konkrete Unternehmen und Beute selbst besiegter Gegner. Container/fremde Beute benötigen zusätzliche ausdrückliche Freigabe. Keine dauerhafte Globalerlaubnis; Beutezusage ist nicht selbst Zustimmung zur Straftat.
- Bestehender direkter Tageslohn, Gesundheitspausen und tatsächlicher berechtigter Skillfortschritt bleiben erhalten. Keine neue Treuhandverwaltung.

## Sprache, Biografie und Erinnerung

- Dasselbe LLM darf beim ersten relevanten Gespräch wenige dauerhafte Fakten zu Berufseinstieg, prägendem Erlebnis und Wünschen ergänzen. Herkunft/Familie bleiben verbindlich; keine erfundenen besuchbaren Verwandten, Häuser oder Quests.
- Pro Familie drei bis vier konsistente gemeinsame Vergangenheitsfakten, für die beteiligten Familienmitglieder identisch. Keine rückwirkende Weltsimulation.
- Bei endgültig blockiertem Auftrag abhängige Schritte stoppen; offensichtlich unabhängige Schritte nur fortsetzen, soweit der angenommene Auftrag dies erlaubt. Fehlende Wirkung wahrheitsgemäß erklären.
- Regex und Ersatz-Wortmuster dürfen keine Sprachinterpretation übernehmen. Vorhandene semantische Verträge und echte Parser nutzen. Vorhandene Szenenzusammenfassungen und aktuelle Historie nicht als fehlende Systeme neu bauen.

## Verifikation

Quelländerung, Offline-Prüfung und native Spielabnahme getrennt berichten. Build/Player nur durch Hauptagent und nacheinander. Hakon-Saves sind wegwerfbare Testdaten. Keine Produktionsänderung nur zum Erzwingen eines grünen Tests, keine künstliche Zustimmung und kein erfundenes Routing. Angenommene Fünf-Schritt-Ausführung, normale Tagesfolgen, Schichten, Rückreisen und Save/Load brauchen passende tatsächliche Belege. Bestehende Einzelbelege im Bericht vom 27. Oktober bleiben gültig innerhalb ihrer Grenzen.
