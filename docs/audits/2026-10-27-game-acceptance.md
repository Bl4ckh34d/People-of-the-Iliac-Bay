# Spielabnahme mit Hakon Ironhand, 26.–27. Oktober 2026

Die Prüfungen laufen im installierten DFU-Player mit den fünf neuen Hakon-Spielständen. Unity-Builds und Player laufen nacheinander. Kontrollierte Testaufbauten werden ausdrücklich von einem frei beobachteten Tagesablauf unterschieden. Einfachheit bleibt der Maßstab: native Regeln nutzen und nur belegte Lücken schließen.

## Aktuell: Paket24 — lesbare Anheuerungsbestätigung

Paket24 gebaut/installiert; Distribution und Player bytegleich, SHA-256 `1C27EAECE9E0A15A303D9114ACFB2194A736943C77E1B72546F9009B9C72E21E`. `hire-preview24` endet mit Exit0. Die identische Produktionsbox für Allan Stafford zeigt sechs kurze Zeilen mit nativer Umbruchbreite288; tatsächliche Fensteraufnahme bestätigt vollständigen Text und sichtbare Yes-/No-Schaltflächen. Root klickte das native No; kein Auftrag, keine Abbuchung und unveränderter Ledger sind bestätigt. Kein LLM-Aufruf oder erzwungenes Einverständnis in dieser gezielten UI-Prüfung. Die natürliche Testanfrage lautet „for one day“, nicht „game hours“. Reguläre Zustimmung22 und reguläre Ablehnung23 bleiben getrennte Dialogbelege. Quellen: `work/final-acceptance-20261027/hire-preview24-player.log`, `hire-preview24-receipt.json`, `package24-build.log`. Gesamtstand weiterhin15/22; dieser UI-Fix schließt kein zusätzliches Gesamtsystem.

## Historische Abschlüsse und Paket5

Paket5 gebaut und installiert, SHA-256 `71F719AD2C1ECED613A3F088F50327C5945BAD70BD7CC4164B8591DECFD8C132` in Distribution und Player. Build und Integration erfolgreich; `work/user-feedback-20261027/package5-build.log`.

**Speicherkern abgeschlossen:** frische aktuelle Schema-Prüfung5/5 (`schema-closure.log`), ergänzt durch tatsächliche native Serialize/Deserialize/Restore-Pfade im Familienlauf. Diese Abnahme verlangt keinen unendlichen Langzeittest und bestätigt keine unbekannten Feature-Produzenten. Semantische Langzeitverdichtung bleibt eine getrennte Gedächtniserweiterung.

**Familien-Innbesuch bestanden:** `family-native-player.log` enthält reale Sitzplätze, einen tatsächlichen Zahler, Ausgang und Wiederbindung, Nachtverschluss, späteren Tageszutritt und beide Heimkörper. Außenheimweg ausdrücklich projiziert, nicht körperlich durchlaufen. Der ursprüngliche Wrapper endet mit Exit1, weil `TownTest.CurrentAssertions.ps1` zusätzlich die allgemeine Tagesplanprobe fordert, obwohl `TownSmokeHarness` sie für den Familienmodus absichtlich überspringt. Die Bedingung wurde angeglichen; erneute Auswertung der unveränderten nativen Familienbelege besteht. Keine ausgelassene Tagesplanprüfung als erfolgreich gezählt.

**Player-Marktkauf mit Paket5 bestanden:** `vendor-package5-player.log` bestätigt sourceUid33555307, recipientUid33555422, Menge1, Preis4 Gold, Verkäufergeld255 und verbleibenden Bestand7. Der nächste NPC-Kauffall scheitert an fehlendem realen erwachsenen Käufer mit Inventar/Geld; Gesamtlauf daher weiterhin Exit1. Der Nutzer meldet außerdem fehlenden Mauszeiger und weiterlaufende Spielersteuerung beim Handelsfenster. Händler-Agent untersucht den nativen Fenster-Lebenszyklus; gesonderte Input-Abnahme offen. Kein zweites Cursor-System vorgesehen.

Die nachstehenden Abschnitte sind datierte frühere Prüfstände. Ihre offenen Aussagen werden durch die konkreten neueren Belege oben präzisiert, nicht durch einen pauschalen Gesamt-PASS ersetzt.

## Im Player nachgewiesen

Die folgende Tabelle hält die ursprünglichen Einzelbelege fest. Sie ist keine vollständige Wiederholungsprüfung sämtlicher späterer Änderungen.

## Wiederaufnahme nach der VRAM-Pause

### Neuer gemeinsamer Stand mit den Folgekorrekturen

Paket `package4b` ist erfolgreich gebaut und installiert: SHA-256 `8A7DBD99E9ABF7296B8371D9EFCC28959EAFC86468CA5E468A32198ADE6ED709`, Distribution und Player identisch. Es enthält die aktualisierte QuestHooks-DLL sowie die abgeschlossenen alten Agentenarbeiten und die Korrekturen der vier neuen Sol/High-Agenten. Gemeinsame Core-Prüfung2149/2149; Runtime ohne Warnungen/Fehler. Der erste Paketversuch scheiterte an zwei veralteten dreiparametrigen Konstruktoraufrufen im Editor-Prüfcode für NativeGuardHooks. Die Prüfung verwendet nun den tatsächlichen vierparametrigen Vertrag und prüft den Angriffsrückruf bei verwalteten und nicht verwalteten Orten.

Belege dieser Folgerunde: `work/user-feedback-20261027/`. Der neue native Händlertest bestätigt den Fortschritt bis zum echten Kaufen/Sprechen-Menü, scheitert dann aber beim Öffnen des Handelsfensters: `DaggerfallInventoryWindow.SelectTabPage` wird aus `NativeVendorTradeWindow.OnPush` vor dem nativen Aufbau der Schaltflächen aufgerufen. Das ist ein echter UI-Lebenszyklusfehler; noch kein bestandener Kauf. Die gezielte Korrektur besteht sieben Offline-Lebenszyklusfälle und benötigt das nächste Paket. Log: `vendor-native-player.log`.

Die folgenden älteren Belege gelten für ihren jeweils genannten Stand; aus einem gemeinsamen Build wird keine vollständige Spielabnahme abgeleitet.

**Neue native Wach-Teilabnahme auf package4b:** Der kontrollierte Tageslauf erreicht184 Garnisonsidentitäten und92 Diensthabende:32 Stadtposten,16 tatsächlich geprüfte Torposten,30 erreichte Mauerposten und sieben Zweierpatrouillen. Alle30 Mauerwege durften sich tatsächlich fortschreiben; keine sofortige Ankunft zum Erzwingen eines grünen Zählers. Der anschließende Test meldet ein echtes Tages-Mauervergehen als `Trespassing`, aber innerhalb30 Sekunden erscheint kein nativer Festnahmedialog (`shown=False`, Gesundheit400 unverändert). Somit besteht die Tagesverteilung; Festnahme, Verweigerungsangriff, Schlafdarstellung und vollständiger Schichtwechsel sind in diesem Lauf nicht bestanden. Beleg: `work/user-feedback-20261027/guard-shifts-native-player.log`.

Der Nutzer hat weitere DFU-Prüfungen mit „You can resume“ ausdrücklich freigegeben. Die sechs bestehenden Umsetzungspakete werden integriert; Builds und native Läufe bleiben serialisiert. Die aktuelle gemeinsame Core-Prüfung besteht mit 2149/2149, der Runtime-Compile mit null Warnungen und Fehlern. Isolierte Worker-Prüfungen und native Spielbelege bleiben getrennt.

Paket `build-resume3`: SHA-256 `C1EAD90C793BD1F84460892507352E634FE1BAC5190D938F07873D71197BEB05`, identisch in Distribution und installiertem Player. Belege dieser Runde liegen unter `work/core-completion-20261027/`.

- **Innenraumtreppe:** Der native Familienlauf belegte einen Stillstand an einem Zwischenpunkt: Der Bewegungscontroller akzeptierte bereits 40 cm horizontalen Abstand, der Wegfolger verlangte dagegen 40 cm räumlichen Abstand einschließlich Stufenhöhe. Zwischenpunkte prüfen jetzt denselben horizontalen Radius mit zusätzlich begrenzter Höhendifferenz. Die Endpunktgenauigkeit bleibt erhalten. Der erneute Lauf erreicht die echten Sitzplätze, zahlt aus genau einem Familiengeldbeutel und verlässt das Inn. `family-stair-stall-player.log` und `family-origin-fix-player.log` halten Fehler und Fortschritt fest.
- **Reisendenidentität:** Ein reservierter Herkunftsbewohner wurde als abwesend erkannt, anschließend jedoch zwingend im separaten ResidentTrip-Index gesucht. Das löste eine KeyNotFoundException aus. Die Auflösung unterscheidet jetzt tatsächlich vorhandene Reisendenpläne; der anschließende native Familienlauf kommt über diesen vorherigen Abbruch hinaus.
- **Familientür:** Der weitergehende Lauf stoppt an einer veralteten Testannahme: Nach dem projizierten Heimweg ist es nach 18 Uhr; das reale Haus ist entsprechend der vereinbarten Nachtregel abgeschlossen. Dieser Fehler ist kein Nachweis einer defekten Tagesregel. Körperliche Bewegung im Inn und projizierter Außenheimweg sind unterschiedliche Belege.
- **Stadtmauer:** Im tatsächlich geladenen WALLAA00-Keller von Daggerfall wurden drei reale Hauscontainer, aber keine Aktionstür gefunden; zwölf von zwölf geprüften Ausgangsrouten bestehen. Damit ist dieser Keller kein nachgewiesenes Gefängnis. Der vorhandene verbundene Schlossdungeon wird gesondert geprüft. Quelle: `custody-geometry-player.log`.
- **Markt:** Der erste Lauf scheitert vor dem Kauf an einer zu engen Testvoraussetzung: Der Test fordert gleichzeitig Fischstandplätze und eine Bühne. Der reale Katalog enthält andere bedienbare Verkäufer. Der getrennte native Händlertest findet außerdem den fehlenden Anschluss der vorhandenen Kaufen/Sprechen-Umleitung. Beide Läufe sind fehlgeschlagen, nicht als erfolgreiche Kaufabnahme gezählt. Belege: `market-visit-player.log`, `native-vendor-player.log`.

Vollständige Haft/Beschlagnahme, Gilden-Ausweichbetten, angenommene Fünf-Schritt-Ausführung und sämtliche Begleiter-Rückkehrfälle sind durch diese Runde noch nicht freigegeben.

## Frühere native Einzelbelege

| Ablauf | Ergebnis | Beleg unter `work/immersion-acceptance-20261026/` |
| --- | --- | --- |
| Sicht auf Spielerverfolgung | Eine zuvor stationäre Wache beteiligt sich bei rund 17 m Abstand zum Verfolger. Der erste Halt-Ruf ist für diesen getrennten Test unterdrückt. Der neue Verfolger nutzt echten nativen Kampf; der Bestand bleibt bei 32 Identitäten. | `sight-player.log`, `SIGHT_PURSUIT_NATIVE_PASS` |
| Tatsächlicher Halt-Ruf | Der native Audio-/Halt-Pfad alarmiert 14 erreichbare Wachen innerhalb 200 m vom Rufer, darunter sieben ohne Sichtverbindung. 17 Wachen außerhalb bleiben frei. Hören erzeugt noch keinen sofortigen Kampf. | `halt-player.log`, `HALT_200M_NATIVE_PASS` |
| Gespräch vor dem Abmarsch | Bei der körperlichen Leichen-/Zeugenmeldung standen NPC und Wache 4,64 echte Sekunden still und einander zugewandt. Erst danach folgte der Gang zum Tatort. Dauer wird zwischen zwei und fünf Sekunden gewählt; Spielpause hält den Timer an. Vorhandene Reisezeiten werden verschoben, kein neues Save-Schema und kein LLM-Aufruf. | `report-talk-native-player.log`, `REPORT_CONVERSATION_NATIVE_PASS` |
| Zwei NPC-Mordfälle | Lebender und bereits toter NPC-Täter: native Attacke, tatsächliche Zeugenmeldung, physischer Gang zur Leiche und Beseitigung, Täterbehandlung, Postenrückkehr und native Serialisierung bestehen. Hakon bleibt als unbeteiligter Spieler unbelastet. Der lebende Täterfall besteht erneut mit Gesprächspause. | `corpse-visible-player.log`, zweimal `NPC_MURDER_NATIVE_PASS`; `report-talk-native-player.log` |
| Reisendenwege und Tore | Alle 19 Daggerfall-Inns sind über gewöhnliche Geometrie angebunden. Vier echte körperliche Torquerungen bestehen; geschlossene Tore liefern keine zugelassenen Reisenden-/Wachrouten, Wiederöffnen stellt die Verbindungen her. Keine injizierten Reisenden. Das ist keine vollständige natürliche Hin-/Rückreise. | `travel-screen-player.log`, `TRAVELER_ROUTE_PASS`, viermal `TRAVELER_GATE_WALK_PASS`, `TRAVELER_CLOSED_GATES_PASS` |
| NPC-Regalkauf und anschließende Übergabe | Ein gewöhnlicher geplanter Ladenbesucher läuft zum nativen Regal, zahlt, erhält eine neue eigene Item-UID und bringt Hakon den Gegenstand. Regalbestand und Spielergeld bleiben erhalten. Eigentumswechsel, native Serialisierung und Schutz vor Wiederholung bestehen. | `commerce-portable-player.log`, `SHOP_COMMERCE_FIXTURE_PASS` |
| Nachtruhe ohne Schlafgrafik | Bei ausdrücklich deaktivierter Schlafgrafik steht der Bewohner neben seinem tatsächlich zugewiesenen Bett. Native stündliche Heilung, Krankheitssperre, vollständige Nacht und nachträglich geladene Nacht bestehen; keine Kosten oder Heiltränke. | `no-art-fixed-player.log`, `HOME_SLEEP_NO_ART_PASS` |
| Fünf frische Spielstände | Alle fünf Saves speichern und laden mit insgesamt 100.000 Gold, davon 1.000 Münzen und 99.000 als nativer Kreditbrief. Traglast jeweils 61,25 von 142 kg. | `purse-player.log`, fünfmal `FIXTURE_PURSE_PASS` |
| Ein LLM, fünf freie Auftragsschritte | Das tatsächlich laufende Gemma E4B zerlegt den freien Text in MoveTo, Wait, MoveTo, Wait, MoveTo und bindet beide realen Läden sowie die angegebenen Wartezeiten. Derselbe LLM prüft Zustimmung und entscheidet DECLINE. Es wird korrekt kein Auftrag gestartet. Das ist ein Beleg für Extraktion, Bindung und Ablehnung, kein bestandener vollständiger Ausführungslauf. | `five-step-warm-player.log`, `GOAL_TWO_PASS_DECOMPOSITION`, `GOAL_BOUND_STEP`, `DIALOGUE_WILLINGNESS_RESULT` |

## Behobene Ursachen

1. Die neuen Saves enthielten 100.000 physische Goldmünzen. Allein diese wogen 250 kg; mit Ausrüstung trug Hakon 308,5 kg bei 142 kg Tragkraft. Die Übergabe wurde daher zu Recht verweigert. Die native Umwandlung des überschüssigen Bargelds in einen Kreditbrief erhält den Gesamtwert. Auch der Erzeuger neuer Testcharaktere verwendet diese einfache Aufteilung.
2. Gewöhnliche Bewohner ohne Schlafgrafik wurden an einem allgemeinen Hausanker platziert. Der Fallback verwendet jetzt den vorhandenen Stehplatz neben dem zugewiesenen Bett. Es gibt keine neue Zimmer-, Reservierungs- oder Schlafzustandslogik.
3. Innenraum-Zwischenpunkte verlangten noch 15 cm Genauigkeit und schalteten auf präzises Nachlaufen. Für Zwischenpunkte gelten nun 40 cm; präzise Ankunft bleibt auf den Endpunkt beschränkt. Der erneute native Kauf-/Übergabelauf besteht. Das beweist nicht sämtliche möglichen Innenraumwege.
4. Das noch aktive native Tutorial öffnete in Daggerfall ein Fenster und pausierte die Simulation. Der Bildschirm zeigte Seite 12. Nach Aufhebung der Pause bestand der Reisendenlauf. In den fünf Hakon-Testständen und der abgeleiteten Familien-Fixture wurde ausschließlich `_TUTOR__.questComplete` gesetzt; die ursprünglichen Questdateien liegen als Sicherung bei den Belegen. Andere Quests und Moddaten bleiben erhalten.

Die Testvorbereitung wurde an frische Saves angepasst: vorhandene Straßenroutinen können über den bestehenden Vorbereitungsmodus gespeichert werden; der Fünf-Schritt-Test wartet auf eine tatsächliche gewöhnliche Ausfahrt. Native Navigationstests schließen vorhandene Dialoge und setzen die Simulation fort. Der Handelstest gibt bei verschwundenen Akteuren den ursprünglichen Aufgabenfehler aus statt einer NullReferenceException.

## Noch keine vollständige Freigabe

Die komplette Gesundheitskette scheiterte nach bestandener Nachtruhe an einem fehlenden Straßenbewohner im frischen Ausgangssave. Der erste Reisendenlauf wurde durch das Tutorial blockiert; die Tor-/Routenprüfung besteht inzwischen. Der alte Mordstellenaufbau verlangte nahe Navigationspunkte und anschließend einen Augenzeugen eines zunächst unsichtbaren Angreifers. Solche Vorbereitungsfehler werden nicht als bestandene Spielfunktionen gezählt.

Familienausflug, physische Tatortermittlung, Reisendenankunft und der angenommene vollständige Fünf-Schritt-Auftrag müssen separat bewertet werden. Ebenso noch offen: beide Kinderbetreuungsvarianten im freien Alltag, Begleiterlohn im vollständigen Anwerbeablauf und Wachablösung nach erst beim Eintritt zugewiesenem Bett. Historische Prüfungen oder reine Quelltests ersetzen diese Abnahme nicht.

## Wiederholbarkeit

`tools/TownTest.ps1` verwendet `-FixtureCharacter 'Hakon Ironhand'` und den jeweils genannten Save. Für gezielte native Fälle gelten `-OutdoorNavigationProbe`, `POTIB_NAVMESH_TRIAL=1` und `POTIB_ROADMAP_PROBE=guard-sight-pursuit`, `guard-halt-hearing` oder `home-sleep`. Der letzte Fall verwendet zusätzlich `POTIB_HOME_SLEEP_NO_ART=1`. Handel nutzt `-ShopCommerceProbe` mit `People - Shop entrance`; freie Fünf-Schritt-Extraktion nutzt `-GoalChainLength 5 -GoalTwoPass` mit `People - Gothway Garden` und dem bereits laufenden lokalen Gemma-Service.

Die Core-Suite besteht einschließlich zweier neuer Meldepausenfälle 2149/2149. Beide neuen Fälle wurden zunächst rot geprüft; zusätzlich wurde ein fehlerhaft fortgeschriebenes Ermittlungsende nach vorherigem Save-Restore rot nachgewiesen und korrigiert. Pause, Wegfortsetzung, doppelte Callback-Abwehr und Roundtrip bestehen. Unity-Paketprüfung und Installation bestehen. Finale Bundle-Identität und ergänzende native Ergebnisse werden nach den laufenden Prüfungen hier festgehalten.


## Paket8: abgeschlossene Biografie, Persönlichkeit und Handels-UI

`stationary8` besteht (Exit0, 39,4 Sekunden):29 tatsächliche native/modseitige stationäre Personen über fünf Gebäudetypen, native Treffer und persistente Todeszustände, genaue Inventare, tatsächliche Lootübernahme und Wiedereintritt. `questLinked=0`: Kein tatsächlicher questgebundener NPC in diesem Save, deshalb kein entsprechender nativer Nachweis. Dieser bestandene Fall wird nicht wegen anderer offener Wach-/Haftkriterien wiederholt.

`wall-law8` wurde vor der eigentlichen Abnahme beendet und ist kein PASS. Nutzer verbietet weitere minutenlange Garnisons-Bereitschaftstests. Die fokussierten Quellprüfer verwenden künftig nur die passende vorhandene Szene/einzelne Wache mit höchstens30 Sekunden Bereitschaft; kein vollständiger Garnisonsaufbau als Testvoraussetzung. Der spätere `custody8`-Quellenlauf beendet sich in44 Sekunden:16 Schlossblöcke,173 native Türen und45 Container geladen. Der fehlende Navigationsblock `S0000160.RDB` verhindert Pfadabfragen; protokolliertes entrancePath=false ist hier kein bewiesener unpassierbarer Weg. Keine Zelle oder Haftabnahme bestätigt.

- `work/final-acceptance-20261027/biography8-receipt.json`: Exit0, SHA256 `BC1B170A09872028D2958439F33AC84CCAE879102E2EC1693BD570AB63F547A3`. Tatsächliche freie Gemma-Gespräche, reale Partner, unveränderte persönliche und gemeinsame Familienfakten sowie drei stabile Charakterzüge nach echtem Disk-Roundtrip. Vergangenheit und Persönlichkeit erfüllen ihre endlichen Abschlusskriterien. Zusammen mit Speicherung sind3/22 Kernsysteme abgeschlossen.
- Derselbe Disk-Roundtrip bestätigt die tatsächlich im Innenraum erzeugte Erinnerung mit gleicher ID, Zeit, Herkunft, Wertung und Abrufbarkeit. Das ist ein konkreter Gedächtnisbeleg, kein pauschaler Abschluss sämtlicher Ereignisproduzenten.
- `capacity7-player.log`: tatsächliche volle vier Betten verhindern Vermietung und Abbuchung; Freigabe eines ursprünglich markerlosen echten Betts ermöglicht native Vermietung für4 Gold und Ruhe.
- `vendor8-player.log`: reguläres Ende des nativen Startvideos beseitigt die vom früheren Testablauf zurückgelassene Cursor-Sperre. Neun tatsächliche UI-Phasen bestehen mit Mauszeiger, pausiertem Input und korrekter Rückkehr. Playerkauf4 Gold und NPC-Kauf3 Gold bestehen. Gesamtlauf bleibt wegen späterem `Cancelling vanilla commerce changed personal stock or wallets` fehlgeschlagen; Agent untersucht diesen getrennten Fall.
- `temple8-player.log`: reale Bewohner vorhanden, aber kein zulässiges Eingangsangebot; Ursache im Probe-Aufbau gefunden: Gesprächskatalog wurde vor Herstellung tatsächlicher Spielernähe abgefragt. Korrektur für Paket9 bereit; kein Trainings-PASS behauptet.


## Gedächtniskern: Abschluss anhand vorhandener Belege

4/22 Kernsysteme abgeschlossen. `biography8-player.log:1046` enthält den tatsächlichen Gesprächsverlauf mit getrennten Sprechern; nach Diskload stehen in1268 wieder korrekte Herkunft und frühere Beiträge,1269 ist die tatsächliche erinnernde Antwort. Innenraumereignis-ID/Herkunft/Zeit/Wertung/Abruf bleiben im selben Lauf erhalten. MemorySalience109 prüft Fristauslauf. `work/playtest-2026-10-26/editor/release-check/editor.log:4172` bestätigt `RETURN_GREETING_VOICE_PASS` mit reopenedTalk/exactText/nonzeroMixer/completed=true; Text dort ausdrücklich controlledReply=true. Der Abschluss beansprucht keine bestimmte freie Begrüßungsformulierung. Kein zusätzlicher oder wiederholter Spieltest erforderlich.


## Identitätskern und enger Laden-Abbruch abgeschlossen

5/22 Kernsysteme abgeschlossen. Identität: `biography8-player.log:923,1319`, `work/home-container-native-pass.log:1125-1128`, `work/VisitorOrigins-native-pass.log:733,768,805,890` und aktueller `work/immersion-simplification-20261026/integrated-traveler-household.log` bestätigen Herkunft, reale Familie, eindeutige Betten, dieselbe Person bei Ortswechsel und Wiederherstellung. Kein zusätzlicher Reise-Test als neue Abschlussbedingung.

`shop-route9b` besteht mit Paket9/E446E39D854F, Exit0. Tatsächlicher Keeper npc:1291010263:327947:52,26 Regalitems, exakte Shelf-UIDs und getrennter persönlicher Bestand; synchroner Abbruch erhält beide Wallets. Der vorherige Garden-Versuch shop-route9 traf ein tatsächliches Questangebot statt des Talkfensters; keine Produktionskorrektur dafür.


## Beziehungen: tatsächliche Trauerabnahme

`grief9` besteht mit Paket9, Exit0 in9,8 Sekunden. Tatsächliches Elternpaar in Home196864; nativer Playerangriff und echter Partnertod; direkte Augenzeugenschaft, wissensgebundene Trauer, korrekte Spielerverantwortung und Groll, Erinnerung genau einmal, native Serialisierung. Der Marker erklärt ausdrücklich `wholeWorldRestore=false`; kein vollständiger Welt-Restore behauptet. Zusammen mit der vorhandenen tatsächlichen Familienmitteilung aus family6 erfüllt dies den Beziehungskern. Nun6/22 abgeschlossen.


## Erlebnis/Bewertung: restliche tatsächliche Empfänger belegt

7/22 Kernsysteme abgeschlossen. Vorhandene Gesundheits-/Handels-/Diebstahls-/Mordbelege gelten laut endlichem Abschlusskriterium. `grief9-player.log:938,941-943` belegt tatsächlichen Angriff/Tod samt richtigem Empfänger und Täterwissen; `biography8-player.log:1202,1258` belegt tatsächliches Innenraumereignis mit unbekannter Ursache und unverändertem Disk-Abruf. Keine umfassende Ereigniskombinationsprüfung als neue Hürde. Fluchtausführung bleibt unter Gefahr/Bedürfnisse offen.


## Paket10b: Kinderbetreuung bestanden, Flucht offen

Paket10b ist gebaut/installiert, Distribution/Player SHA256 identisch `A6642AB34F175881FFD4457E467A32918021822B0DAC778D747EC654755D1A7F`. Nach dem vorangegangenen Snapshot wurde der zuletzt geänderte DefenseCombatAdapter erneut korrekt übernommen; Paket10 wurde nicht für die Fluchtabnahme verwendet.

`childcare10b` besteht in36,7 Sekunden: tatsächlicher anderer Elternteil in Gebäude131343, Heimelternteil und eigenes Kind in Home196864, normaler Tagesplan, tagsüber unabgeschlossene native Tür und echter Zutritt. Keine Plan-/Körperinjektion; kein vollständiger ganzer Tag behauptet. Beide Betreuungsvarianten damit zusammen mit family6 abgenommen; Bank/physischer Außenheimweg bleiben offen.

`mobile-flee10b` reproduziert weiterhin den Fehler: phase=Accepted, reaction=none, conversing=true, moved=0. Nicht als behoben markiert; konkrete native Resultate beim zuständigen Agenten. `custody-capture10b` scheitert am falschen vollständigen Szenenexport gegen den vorhandenen Modulmanifestvertrag; gezielter RDB-Blockexport für denselben geladenen Ort ist für den nächsten Stand korrigiert, noch nicht ausgeführt.


## Paket11: mobile Flucht und gezielter Castle-Capture

Paket11 SHA256 `F1BE89C14162C3605E1A3F0A1C3E458C635161AB3E33FA90BA7FBF5EB176A4E1`. `mobile-flee11` Exit0 in10,7 Sekunden: echter nativer Player-Schaden89→88, lebendes Opfer reagiert mit Flucht, angenommener Wait unterbrochen, kontrollierter Gesprächshalt gelöst, tatsächliche Bewegung0,8103972m, kein falsches Mordwissen, übrige Gegner unverändert. Exakter Ausgangszustand danach wiederhergestellt. Ursache des vorigen Fehlers war die ungültige Bindung der Opfersicherheit an das fertige Wachroster; diese Abhängigkeit ist entfernt.

`custody-capture11` Exit0 in16,3 Sekunden: genau16 RDB-Module des tatsächlich geladenen Schlosses erfasst, vorhandener per-block Exportpfad; keine Garnisonswartezeit oder Weltgeometriesuche. Die Daten müssen im folgenden Build gebacken werden. Keine Zelle/Haftabnahme behauptet.


`mobile-flee-enemy11` besteht ebenfalls: tatsächlicher vorhandener Assassinenkörper als kontrollierte1HP-Schadensquelle, lebendes Opfer flieht0,5109354m, Auftrag unterbrochen und Gesprächshalt gelöst; keine Player-Zuordnung oder vorzeitige Mordkenntnis. Exakter Baseline-Restore, Exit0 in10,54 Sekunden. Dies prüft native Schadensreaktion/Priorität; kein erfundener vollständiger Nahkampfablauf. Fluchtfehler für beide Quellen abgeschlossen.


## Paket12: Stadtmessung und vorbereiteter echter Prüfstand

Distribution und installiertes Paket: SHA256 `76193E2A7BEFC4402B69DF32D34033AB002DD51632D6E087A215D6CFA60B0AEC`.
`performance12` besteht mit Exit0. Drei 60-Sekunden-Stichproben messen p95 73,36/85,85/89,08ms, Maxima 371,04/162,02/159,90ms. Keine Frames ab einer oder vier Sekunden; native Scopes aktiv. Kein pauschaler Nachweis für Loitern/Zeitsprünge.

Der vorhandene SaveCheckpoint speichert anschließend den tatsächlich beobachteten Zustand als `People - Daggerfall acceptance ready` (SAVE9). Keine Akteure/Buchungen injiziert, keine zusätzliche Vorbereitungswartezeit. Der Snapshot enthält tatsächlich zugelassene Gäste und bereits erreichte Mauerposten; folgende Prüfungen verwenden diese vorhandene Szene.

`wall-law12` scheitert konkret an `Real upper-wall player/legitimate guard classification failed.` Kein erneuter Garnisons-Bereitschaftsfehler; Ursache beim Wachagenten.

`custody12` ist eine bestandene Quellenprüfung, keine Haftabnahme: tatsächlich geladener Käfig41313 in W0000016.RDB, echte Tür28112642, beidseitiger Boden und tatsächliche Türbarriere, doorCrossingPath=true. Beide Eingangspfade zur Zelle bleiben false. Die echten 16 Schlossmodule wurden zuvor gebacken; die fehlende Verbindung bleibt offen.


## Bedürfnisse/Ziele: endlicher Kern abgeschlossen

Kern abgeschlossen: tatsächliche Behandlung mit Alarmunterbrechung, physischer Rückkehr und Wiederaufnahme (health-economy-native-final-player.log:1089–1147); aktuelle mobile-flee11 und mobile-flee-enemy11 bestätigen Flucht vor Auftrag/Gespräch. TaskFailureFeedbackProbe/Player.log:929–931 bestätigt echten nativen Kauf, kontrolliert verweigerten Übergabestart, behaltene Ware und Geld, übersprungenen abhängigen Folgeschritt und einmalige ehrliche Fehlermeldung samt Wiederherstellung. Kein natürliches Dungeonhindernis behauptet. Freie Fünf-Schritt-Ausführung und Dungeonrückkehr bleiben eigene Kriterien anderer Systeme.

8/22 abgeschlossen. Vorhandenes TaskFailureFeedbackProbe/result.json bestätigt Exit0; kein Wiederholungslauf erforderlich.


## Paket13 und Ausdruckskern

Distribution und Player identisch: `331A57B8C23A937F48FC3758A6E961702F8ED48A33AB2DAA0F5897C2F35A4C1C`. Unity-Integration bestanden.

Technischer Ausdruckskern abgeschlossen: tatsächliches Gemma/Breeze-Gespräch mit hörbarem Mixer, Wortausrichtung, Mundbewegung und Portraitprojektion in LiveDialoguePresentationProbe.log:967; nativer Textaufbau/Replay/Fehlerfallback in SpeechRevealPresentationProbe.log:966. biography8 bestätigt den vollständigen freien Antwortaufbau, vendor8 neun reale Cursor-/Steuerungsphasen. Die letzten zwei personenbezogenen HUD-Texte erhalten in Paket13 den tatsächlichen Namen vor Gesprächsschluss; Quellprüfung und Runtime-Compile bestanden. Kein neuer Spiel-PASS für diese reine Textkorrektur behauptet. Sprites/Grafiken/Animationen und Spracheingabe bleiben ausdrücklich spätere Arbeit. Doppelte Wachendarstellung bleibt unter dem gezielten Schlaf-/Wachkriterium offen.

9/22 abgeschlossen. Shield-only13 scheitert separat nach tatsächlichem Cast am Speichern der Ressourcen; Körper bleibt offen und der konkrete Fehler liegt beim Gesundheitsagenten.


## Paket14: Körperkern abgeschlossen

Distribution und Player identisch: `4F7E372C438F6F4EA19B37F5376337FD0601855F47DC707A13085E57E7A64782`. Unity-Integration und Runtime-Compile0/0 bestanden.

Körperkern abgeschlossen. shield-only14 besteht: eigene und Player-Touch-Shields, exakte Magicka, echter Schadensverbrauch/Überlauf, erhaltene Stärke und Runden nach Restore, unabhängiger Zeitstempel, unbeobachtetes Altern und Ablauf. Kontrolliertes natives Buch/Level/Mana sind ausdrücklich markiert; kein natürlicher Magier behauptet. Exakte Baseline wiederhergestellt. Alarmunterbrechung, tatsächliche Bewegung, erneute Behandlung und Wiederaufnahme bezahlter Dienste sind bereits durch health-economy-native-final-player.log:1089–1147 belegt. Keine neue Gesundheitsmatrix.

10/22 abgeschlossen. Native Marker in shield-only14-player.log:929 und981, Receipt Exit0.


## Weltzustand: vorhandene tatsächliche Belege schließen den Kern

Begrenzter Weltkern abgeschlossen. astra-appointments-player.log:1328–1347 bestätigt eine konkrete Zusage für Tag+2, native Zeitfortschreibung bis zur Abfahrt, tatsächlichen Weg über109,39 Spielsekunden, Anwesenheit ohne falsche No-Show-Strafe, Erinnerung und Restore. Der separate native Loiter-Lauf über eine und drei Stunden ist vollständig gemessen: größter Skipframe1596,03ms, nach3h größter Folgeframe273,29ms (release-check/summary.json). Performance12 ergänzt drei aktuelle60s-Stadtsamples ohne Frames ab1s. Damit sind die gemeldeten mehrsekündigen Hänger in diesen konkreten Szenen eingegrenzt. Keine universelle Performancegarantie, weltweite Einzelakteursimulation oder neue kombinatorische Termin-/Lohn-/Zeitsprungmatrix. Historische60/60-Lohnbelege gelten ausdrücklich nicht für heutige direkte Tageslöhne.

11/22 abgeschlossen. Keine erneute bereits bestandene Termin-/Loiterprüfung.

market-commute14 wurde ohne PASS durch Root beendet: alte180s-Bereitschaftsschleife statt vereinbarter kurzer Grenze, kein tatsächlich ausgewählter Pendler. Der Agent korrigiert Auswahl/Diagnose; kein weiterer kalter Wartelauf.


## Weitere getrennte Paket13/14-Ergebnisse

wall-law13: Tagesangebot vor Schaden, Ablehnung und17HP-Treffer; direkter nativer Wachentreffer285→275 und Gegenwehr10HP. Nur späterer inaktiver Foe-Fixturefall scheitert. Dieser fehlende Fall besteht separat in wall-npc14 (Exit0,31s): aktiver nativer Barbarian, tatsächlicher Wehrgang, echter Pfeilverbrauch und exaktes Ziel.

five-step13: gültige fünf Schritte und Bindung; gültige Fortsetzungsentscheidung; normales DECLINE. Keine Ausführung behauptet. cancel13 ebenfalls reguläre Ablehnung vor Cancel-Prüfung.

paid-xp14: tatsächliches natives Ja zum Lohn, danach Follow mangels sicheren Anlaufwegs abgewiesen und beide Wallets zurückgerollt; keine XP-Abnahme. dungeon-known14: echter10Gold-Hire/Vertrag, aber Streaming-Owner-NRE und fehlender gemessener Außenportalboden. temple14c: keine vorhandenen feindlichen Gegner beim ausgewählten echten Reisenden; angenommener Enter wird unmittelbar durch Herkunft-away-Sperre beendet. Exakte Befunde an zuständige Agenten; keine Wiederholung ohne relevante Korrektur.


## Bank14 bestanden; spätere Inn-Ausgangsgrenze getrennt

family-bank14 bestätigt den vorherigen Bankgang: beide Wallets kontrolliert leer, genau ein regulärer Zahler, einmal341Gold, tatsächlicher Hin-/Rückweg227,70m, größter Schritt0,363m und Heimabstand0,561m. Danach gehen beide gemeinsam los. Hinweg zum Inn253,78/250,62m, echte Käufe und beide Sitze bestehen. Erst der anschließende Zeitsprung im beobachteten Inn legt beide Körper fälschlich in die unbeobachtete Simulation; sichtbarer Ausgang/Heimweg bleiben offen. Gesamt-Exit1 widerruft die vorherigen Teilbelege nicht. Kein erneuter Bank-/Hinweglauf; Wiederholung wird direkt im regulär vorbereiteten Inn begonnen.

Belege family-bank14-player.log:1312–1314 Bank/Home/Abfahrt,1338 tatsächlicher gemeinsamer Hinweg,1356–1363 Käufe/Sitze,1380 fehlender körperlicher Ausgang. Kontrollierte Wallets/Beobachter und ruhige Fixture sind ausdrücklich markiert; Inn-Bankinnenraum war unbeobachtet.


## Paket15: Zuhause/Haushalt abgeschlossen

Distribution und Player identisch: `FBBE3162509742D05CE53C51037069E63F30EC1A96D8C6376457E85BDDD8526D`. Gemeinsamer Runtime-Compile0/0 und Unity-Integration bestanden. guard-sleep15 Exit0.

Haushaltskern abgeschlossen. guard-sleep15 bestätigt echte Nachtwache g:1291010263:n:1 im Quartier770, zugewiesenes natives Bett, korrekte Ausrichtung und in zwei gerenderten Frames genau eine Decke/einen Renderer (Art399/15). Kein doppeltes stehendes Bild. Ruhe ohne Grafik, volle vier Betten mit verweigerter Miete und Vermietung nach Freigabe sind bereits nativ belegt. family-bank14 ergänzt echten vorherigen Bankgang (227,70m Hin/Rück, einmal341Gold, ein Zahler), Gruppenzahlung und tatsächliche Sitze. Der getrennte Fehler am Inn-Ausgang nach beobachtetem Zeitsprung bleibt unter Alltag offen; er widerruft diese Haushaltsbelege nicht.

12/22 abgeschlossen. Fehlende NavMesh-Innenvariante im Quartier wird nicht als Navigation-PASS ausgegeben; der gemessene Schlafkörper-/Rendererfall ist davon unabhängig.


## Paket15: echter bezahlter Zauberfortschritt

Paid-XP15 nativ bestanden: natürlich erzeugter Gelem, tatsächliche Zustimmung und10Gold, eigener Heal-Health-Zauber mit21Magicka, genau ein Cast und XP0 auf1. Buch, Profil und andere Skills unverändert; bezahlte Bindung erhalten. Kontrolliert wurde nur der Spielerstandpunkt auf echtem freiem Boden neben dem unveränderten NPC. Der Teststarter erwartete irrtümlich den allgemeinen statt des gezielten XP-Markers; vorhandenes Log nach korrigierter Zuordnung erfolgreich geprüft, ursprünglicher Exit1 erhalten. Kein erneuter Spiellauf.

Belege: `paid-xp15-player.log:1009–1014`, ursprüngliches `paid-xp15-receipt.json` und getrenntes `paid-xp15-assertion-recheck.json`. Log-SHA256 `1DEE41F1A08D1895FA256259EF7C79E3D0BCA71154C429D22E674BADCAC676AD`. Kein allgemeiner Follow-Navigationsnachweis.

Temple15: tatsächlicher Reisender überschreitet die genaue Außentür; nach Inneneintritt fehlt die Variante TEMPAAH0.RMB:13 und die Bereitschaft kommt nicht voran. Root beendet den eigenen Player nach mehr als30s ohne Fortschritt, statt die alte90s-Schleife abzuwarten. `temple15-readiness-stop.json` trennt diesen Abbruch von einem nativen Testfehler. Keine Ausbildungsabnahme.


## Wahrnehmung/Wissen abgeschlossen; Wachwechsel15 bestanden

Endliches Wahrnehmungs-/Wissenskriterium abgeschlossen: tatsächliche mobile und stationäre Körper respektieren Sicht, Verdeckung, Unsichtbarkeit, Entfernung, Schlaf und Wiederbindung. 22 native Diebstahltransfers bestätigen dieselben Grenzen am echten Ereignisproduzenten. Reale Gespräche erhalten Rolle, Partner und aktuellen Heim-/Tätigkeitskontext. Native Ortsauskunft, Kartenmarker und körperliches Hinführen einschließlich Abstandswarten, Wiederaufnahme und Ankunft sind belegt. Kontrollierte Kenntnis, Beziehung, Schlaf und Beobachterledger bleiben gekennzeichnet. Unbelegte Garten-/Händler-Zukunftsprosa aus biography8 ist kein geprüfter Tagesplan. Keine Vollprüfung aller Städte, Produzenten oder freier Modellformulierungen erforderlich.

Vorhandene Logs direkt erneut gelesen, kein Spiellauf wiederholt: `work/actions-installed-native-final-player.log:952,957,1094–1100`, `work/playtest-2026-10-26/editor/scene-observers-green/editor.log:3307,3317`, `work/native-theft-memory-01-player.log:924–948`, `biography8-player.log:1157–1174`. Das ausdrücklich begrenzte Abschlusskriterium verlangt keine vollständige Producer-/Stadtmatrix.

guard-shift-change15 besteht mit tatsächlichen unterschiedlichen Tag-/Nachtkörpern, Nachtfackel, nativ geschlossenen Nachttoren und Rückkehr zum Tagdienst. Keine vollständige Garnison als Voraussetzung; Marktrolle bleibt getrennt offen. Beleg `guard-shift-change15-player.log` und Exit0-Receipt.13/22 Kernsysteme abgeschlossen.


## Paket15: gezielte Restfehler an Agenten zurückgegeben

`complaint15`: realer Traveler Glonn startet31,26m Beschwerdeweg, bleibt am normalen Besuchspunkt hängen; nach120s nur0,30m. Gemeinsamer Owner fehlte. `market-commute15`: tatsächlicher Händler/Home vorhanden, bereits zu Hause ohne beobachteten Arbeitsstart,0m; ein Zeitsprung-Effekt ist die Quellhypothese, kein gemessener Zeitüberlauf. `market-visit15`: reale sieben Marktposts samt Browse-/Zahlplätzen vorhanden; Mittagsplan286 zu Hause, kein geeigneter Käufer. Root beendet alte75s-Readiness nach mehr als30s; kein Kauf-PASS. `repair15`: tatsächliches HomeExit/Crossing/Landing; danach kein normales Angebot für die konkrete Reparaturtür. `dungeon-known15`: tatsächlicher Dungeon-Eintritt jetzt erreicht, Follow steht wegen fälschlich verlangter Außenweltbereitschaft im Dungeon. Setup-Anreise und ursprüngliche Consent-Vorgabe bleiben ausdrücklich kontrolliert. `paid-five-step15`: keine gebundene Gesprächssitzung vor Submit, daher keine Modellzustimmung/Yesbox; Baseline exakt wiederhergestellt. Alle Original-Logs/Receipts erhalten. Fehlfälle werden erst nach konkreter Korrektur wiederholt; bestandene unabhängige Kriterien bleiben abgenommen.

Direkter Tempelcapture15 besteht: Innenraum197645 und exakte Modellvariante TEMPAAH0.RMB:13#7525256e6f4d2ab591309d634d2d8630ec389b4c2c5210ce526452da04ead000 erfasst. Nächster Build muss diese Geometrie backen; Capture ist kein Trainings-PASS.


## Paket16: Beschwerdegang einschließlich Gespräch und Fortsetzung bestanden

Paket-SHA256 `0445AB5F35A0E8AB272C4C9B930377434A2B6AE65E2782E7DEDA354095D9DDF5`, Distribution und Player identisch. Unity-Integration erfolgreich.

complaint16 nativ bestanden: derselbe tatsächliche Reisende Glonn läuft29,27m in8,74s zur Wache. Beide stehen3,36s still und einander zugewandt, bevor die tatsächliche Drohungsmeldung übergeben wird. Anschließend13,46m normale Bewegung, Meldung bleibt zugestellt. Kontrollierte semantische Drohreaktion und spätere Crime-Bereinigung sind markiert; keine erneute LLM-Klassifikation behauptet. Belege `complaint16-player.log`, `complaint16-receipt.json` Exit0. Der vorgelagerte Traveler-Besuchsowner darf die Meldung nicht überschreiben; existierender gemeinsamer Beschwerdehandler genügt.

Temple16 erreicht erneut die genaue Tür und lädt jetzt korrekt die native Innengeometrie. Fehlender Körper liegt laut Quellprüfung am vom Testspieler blockierten Landepunkt; nächster Aufbau räumt nur den Beobachter auf einen belegten freien Bodenpunkt. Keine Ausbildungsabnahme aus diesem Lauf.


## Paket16: unabhängige Teilbelege erhalten, tatsächliche Restursachen

Reparatur16: NATIVE_REPAIR_JOURNEY_PASS für tatsächliche Person s:1291010263:h:394249:r:0,16,39567m, nativeExit/nativeEnter/exakteTürreceipts, keinNPC-Move durchProbe und keineDutylockerung. Erst spätere Dropoff-Voraussetzung scheitert; normaleRoutine startet ungeplanten Ausgang.

Dungeon16: ersteWaitFollower-/über5m-/Identitätsassertions bestanden, anschließend echterTalk; späterer Innenrückweg meldet destination-off-mesh, gemessenerBoden38,4 gegen Ziel39,0. Auch15 erreichte den späterenTalkmarker; die vorherige alleinige Ursachenzuschreibung anReadiness war zu stark.17 nutzt nativeBodenmessung und ergänzt Phasenmarker. Gesamt-Roundtrip/Stop bleiben offen.

Familienreturn16: Testaufbau mit normalem unobserved Inn-Besuch, tatsächliche Eltern und Käufe; beide stallen vorSit am selbenZwischenpunkt. Wiederholte Stallbelege sichern ursprünglichen9Punkte-Weg und spätere Detours; Beobachterüber11mweg. Root bricht redundantesWarten ab (`family-inn-return16-stall-stop.json`).17 protokolliert exakteNativeNav-/Segmentdaten, keine spekulativeFallbackänderung.

Marktschicht16: second64620, closes64680, Home/sleep. Root/Agent verifizieren int-Überlauf in2 Tagesmultiplikationen.17 verwendet86400d; realeEpochausdrücke0/2rot→2/2grün. Marktbesuch16 hat keine publizierte zukünftigeOuting im kaltenSAVE3; echtesSAVE9 hat286Routinen/250Itineraries.17 verwendet dieses vorhandeneSave ohnePlaninjektion.

Paid-five-step16: Modell tatsächlich erreicht, gültigeEin-Schritt-Anwerbung, danach fehlendeZielbindung; keineYesbox und keineFolgeaufgabe.17 erläutert nur bezahltesFollow/Lohn≠Itemmenge im vorhandenenPrompt.

Paket17 RuntimeCompile0/0, Unity-Integration bestanden, Distribution/Player identisch `10B1D03336D107942437AF99EA807C91CE9E3C37E2147ED53003453CD308F7E1`. Dies ersetzt keine nativeAbnahme derÄnderungen.


## Marktbesuch17 abgeschlossen; Pendelwegfrist korrigiert

Marktbesuch17 nativ bestanden: tatsächlicher Käufer s:1291010263:h:263682:r:0, 7,81 Meter körperlicher Browse-/Zahl-/Rückweg, korrekte Warenblickrichtung, genau6Gold vom Käufer zum Verkäufer, eigene neue UID33555990 bei unverändertem Angebot. Besitz nach Rückkehr und Typserialisierung erhalten; naher Spieler unterbricht den Besuch nicht. Sieben echte Posts und20 Plätze. Bühne/Zuschauer wurden nicht geprüft und sind keine zusätzliche Kernhürde.

Belege: `market-visit17-player.log:1191–1197`, Exit0 in `market-visit17-receipt.json`. Quelldaten33555273 bleiben im Angebot; Käufer erhält33555990. Verkäufer erhält genau6Gold. Der natürliche Käufer wird aus vorhandener SAVE9-Routine gewählt; Beobachter/Uhr und anfängliche Testpositionen sind ausdrücklich kontrollierter Aufbau. Keine Person oder Finanzierung erzeugt.

Market-commute17 bestätigt den zuvor fehlerhaften Arbeitszustand vor Feierabend. Danach234,52m gemessener Weg, maximal0,1553m pro Schritt. Die Frist240s enthielt bereits verbleibenden Dienst;293m bei1,3m/s benötigen allein225,39s. Probe18 trennt Restdienst und verwendet für jeden realen Weg gemessene Länge/Tempo plus30s. Kein sechsminütiges Warten auf allgemeine Bereitschaft; keine neue Bewegungsbehauptung.

Paket18: RuntimeCompile0/0 und Unity-Integration bestanden. Distribution/Player identisch `1FD2C6BB391F7B06A8A8DB237FE00DB01E0B6C3A62658CE791BC54019FB3BC35`. Gemeinsamer normaler Enter→Wait-Dienstaufbau, tatsächliche Innencheckpoints und Dispatch des angenommenen Folgeschritts vor RoutineReturn integriert. Native Dienstabnahmen weiterhin offen.


## Paket19: konkrete Dienstfortschritte, Restfehler getrennt

Distribution und Player sind identisch: `267057D1C71496E217C6FA96D86C97D2CBA3F66E7E6BAC1E8A14331419260806`. Unity-Build und Integration erfolgreich.

Temple19 beginnt direkt aus SAVE10. Elf native Lektionen, zweite Auswahl (Skill5 statt3), tatsächlicher Trainerfußpunkt mit31 Bodenkandidaten und erreichbarem Sichtpunkt sind bestätigt. Der tatsächliche Weg scheitert danach mit `The walking route remained blocked`. Kein Unterrichtsabschluss behauptet.

Repair19 beginnt direkt aus SAVE11, ohne erneuten Hausweg. `NATIVE_REPAIR_DROPOFF_PASS` und `NATIVE_REPAIR_DISK_PASS` bestätigen Abgabe und echten Diskreload. Der Lauf scheitert erst nach Fälligkeit am passenden Abholangebot desselben Reparateurs; keine Abholung behauptet. Der redundante isolierte Snapshot-Klon aus18 wurde entfernt; der echte Disknachweis blieb erhalten und besteht jetzt.

Dungeon18 erreicht `DUNGEON_FOLLOW_OUTWARD_PASS` mit10,74899 Metern. Nach echtem Gespräch meldet die Rückwegprobe kombinierte Boden-/Kollisionsverletzung bei(27.4,38.5,23.0). Weitere Diagnose muss zwischen Beobachterkollision und NPC-Bewegungsfehler unterscheiden. Der Hinweg bleibt bestanden.

Belege: `temple19-player.log`, `repair19-player.log`, `dungeon-known18-player.log` und jeweilige Receipts unter `work/final-acceptance-20261027`. Gesamtzahl unverändert13/22.


## Handel abgeschlossen: repair21

Handelskern abgeschlossen. vendor8 und shop-route9b belegen native Handelsoberfläche, Eingabesteuerung, Kauf und Abbruch. market-visit17 belegt tatsächlichen Marktbesuch, eigene Zahlung und Besitz bei unverändertem gewerblichem Angebot. repair19/20 bestätigen Reparaturabgabe und echten Diskreload; repair21 schließt die Abholung direkt aus dem gespeicherten Reparaturzustand ab: UID 33555763, Zustand 150/150, Verwahrung beendet, Wallet 456 unverändert seit Zahlung, derselbe Reparateur und verweigerte zweite Abholung. Native Reparaturdauer 86400 Spielsekunden, Preis 2 Gold genau einmal. Erpressung, Bühne und autonome Wirtschaft bleiben getrennte spätere Aufgaben.

Paket21 `147E21264B18F5230FA6B38E323FB3D458ACF3A3B2421A9045B5A7E83BC27A31`, Distribution/Player identisch. Native Startbasis ist der echte Pending-Save12 „People - Repair 38d500cb“; Abgabe und Heimweg werden nicht wiederholt. `repair21-player.log:1004–1005` bestätigt Completed und sämtliche Ergebniswerte. Wrapper-Receipt Exit0, Laufdauer13,96 Sekunden. Nach dem nativen Tageswechsel ist die aktuelle Inventarbindung neu erstellt; die Probe prüft deshalb dieselbe Person über ihre aktuelle Bindung. Der frühere Fehler war der Zugriff auf die alte zerstörte Körperbindung. Der Gesprächsabstand9,80m wurde zuvor als separate Ursache des fehlenden Abholangebots nachgewiesen; Annäherung des Beobachters stellt das normale Angebot her, ohne den NPC oder Dienstregeln zu verändern.

Roadmap/Mindmap jetzt14/22. Neue Nutzeranforderung LIFE-68 (Schnellreise mit Begleitern und Alleinreisepreis mal Gruppengröße) bleibt ausdrücklich offen im Gruppensystem.


## Fertigkeiten abgeschlossen: temple21

Fertigkeitskern abgeschlossen. Paid-XP15, pausierter Dienst ohne XP, wiederhergestelltes Zauberbuch und Shield14 bleiben abgenommen. Temple21 bestätigt die tatsächlich zweite angebotene Lektion (Skill 5 statt erstem Skill 3) beim richtigen nativen Trainer: einmal 400 Gold vom NPC, 10800 Spielsekunden Unterricht, kein vorzeitiger Skill-/XP-Gewinn, anschließend Skill 9 auf 10 und XP 0 auf 255. Gespeicherter und aktueller nativer Skill stimmen überein; dieselbe Person, Inventarbindung und Traineridentität bleiben erhalten. Spielergeld und alle 35 Spielerfähigkeiten bleiben unverändert. Kontrolliertes Unterrichtsbudget und Zeitsprung zum tatsächlichen Timerende sind markiert. Die zusätzlich gewünschte HUD-Nennung der gelernten Fähigkeit ist in Quelle22 ergänzt; deren Build steht noch aus.

Paket21 `147E21264B18F5230FA6B38E323FB3D458ACF3A3B2421A9045B5A7E83BC27A31`. Die tatsächlichen Marker TEMPLE_PAID_CHECKPOINT, TEMPLE_COMPLETION_VALUES und TEMPLE_TRAINING_NATIVE_PASS stehen in `temple21-player.log`. Der Runner besaß für den exklusiven TempleTrainingProbe-Modus keine Completion-Zuordnung und wartete trotz Erfolg weiter. Root beendet diesen unnötigen Lauf, bewahrt das ursprüngliche Exit1 und korrigiert die Zuordnung. Die bestehenden nativen Assertions wurden auf die tatsächlich getrennten Start-/Endbelege aktualisiert und auf demselben Log erfolgreich erneut ausgeführt; `temple21-assertion-recheck.json` enthält Log-SHA und PASS. Kein Spieltest wiederholt.

Produktionskorrektur: Bereits bezahltes laufendes Training behält beim zeitbedingten Innenraum-Rejoin Executing-Phase und ursprünglichen Timer. Gezielte Regression rot/grün. Die abschließende Probe liest nach natürlicher Körperneuerstellung die aktuelle Bindung derselben Person. Tatsächlicher Abschlussbeleg: CompletedOnce=True, Wallet0/0, SavedSkill10/LiveSkill10, XP255, Spieler unverändert. Mindmap/Roadmap15/22.


## Nachtposten21 und Paket22

Nacht-Mauerfestnahme21 nativ bestanden: tatsächliche Wache g:1291010263:n:5 erreicht ihren Nachtposten über normalen Anmarsch und bestehende Innenroute. Native Festnahmeaufforderung, tatsächliche Ablehnung und Schaden sind bestätigt. Kein vollständiger Garnisonsaufbau als Voraussetzung; konkrete Bereitschaft höchstens30 Sekunden, danach normaler Weg. Tatsächlicher Posten als Checkpoint gespeichert.

Der bisherige Gesamtblock-Survey verzögerte bereits die erste Zuweisung. Dieselbe vorhandene Oberflächenprüfung erfolgt nun je tatsächlich benötigtem Türblock; fertige Site wird regulär zugewiesen. Native Bewegung und30-Sekunden-Bereitschaft bleiben unverändert. `wall-night21-player.log:1190–1198` bestätigt tatsächlichen Posten, gespeicherten Checkpoint und Festnahme/Ablehnung/Schaden. WrapperExit0, Gesamt136,67 Sekunden einschließlich tatsächlichem Anmarsch. Innenweg bleibt ausdrücklich normale unbeobachtete Projektion, Außenankunft tatsächlich beobachtet.

Paket22 Distribution/Player identisch `F941999CD8B6314B4F6817113C2AA1EBEE3F7059BAD3D0356A319ABCD9A02DCB`. Unity-Integration erfolgreich. Enthält native Inn-Korridorkorrektur (Abnahme separat), die kleine Trainings-HUD-Präzisierung und eindeutige Skill-Marker. Der HUD-Text verwendet echten NPC-Namen und den lokalisierten Skill aus job.Target.ItemId. Schnellreise ist noch nicht aktiv angeschlossen; eine vorbereitete Kostenpolicy allein ist kein Implementierungs- oder Spiel-PASS.
