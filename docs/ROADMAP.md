# People of the Iliac Bay — Roadmap

<a id="abschluss"></a>
## Verbindlicher Abschluss statt dauerndem „teilweise“

- [x] **Reisende als Zeugen nativer Leichen:** Am28.Oktober nativ rot/grün nach kontrolliertem72-Stunden-Zeitsprung in Gothway. Ein tatsächlich sichtbarer Reisender aus einem anderen Ort löste bei einer echten Angreiferleiche `Invalid aggressor corpse discovery` aus. Die bestehende Besucherauflösung gilt nun auch für Verteidigungs-/Leichenzeugen; Restore validiert Besuche vor diesen Aussagen. Derselbe Reisende und dieselbe Leiche bestehen anschließend samt nativem FullSerializer und validiertem Restore. Kein Kampf oder Zeuge injiziert. Core2156/2156, Runtime0/0, Unity und nativer RunnerExit0. Gildenbett/Camp bleiben separate offene Ankunftskriterien. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Checkout und nötiger Wechsel des Gasthauses:** Am28.Oktober beide nativ Exit0. Ludovic läuft18,60m vom Zimmer zum Ausgang; Bettfreigabe zum ursprünglichen Checkout. Alison erhält im tatsächlich vollen Inn262912 kein Bett, wählt regulär Inn263176 und läuft dort66,82m außen sowie24,84m innen bis zum zugewiesenen Bett319. Keine injizierte Buchung oder NPC-Platzierung. Der erste Umleitungslauf scheiterte am Testbeobachter im Eingang; nur dessen Position wurde korrigiert. Gildenbett und Camp bleiben separate native Abnahmen; Ankunft/Heimat bleibt teilweise. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Rolle & Pflicht — Blase fertig:** Markt-Morgenlauf am28.Oktober nativ Exit0: derselbe Verkäufer im tatsächlichen Bett, normale kostenlose Schlafheilung59→66HP bei unverändert321Gold, Aufwachen/Ausgang und291,27m körperlicher Morgenweg zum Stand. Größter Schritt0,243m; ursprünglicher nativer Körper und Inventarsammlung erhalten, keine mobile Doppelperson. Nachtzustand/Uhr/Verletzung sind kontrolliert. Der bereits belegte Abendweg22 mit294,53m und die gezielte Bodenankunft-Korrektur bleiben getrennte Nachweise; kein neuer Abendweg behauptet. Zusammen mit guard-shift-change15 und guard-sleep15 ist das endliche Rollen-/Dienstkriterium abgeschlossen. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Alltag — Blase fertig:** Gemeinsamer Inn-Ausgang und Heimweg am28.Oktober nativ Exit0: beide tatsächlichen Eltern laufen252,41m bis zum gemeinsamen Heimabschluss, maximal0,50m pro Schritt, durchgehend sichtbar, keine erneute Zahlung. Ursache des bisherigen Stillstands war ein zusätzlicher Einzelwegplaner außerhalb des bereits geprüften Gruppenwegs. Die Paarbewegung verwendet jetzt durchgehend ihre vorhandene körperliche Wegpunktbewegung mit Boden/Kollision und unveränderten Fortschrittsgrenzen. Sitzdauer kontrolliert vorgespult, Ausgang/Heimweg tatsächlich gelaufen. Zusammen mit complaint16 und beiden bereits abgenommenen Betreuungsvarianten ist der endliche Alltagskern abgeschlossen. Core2155/2155, Runtime0/0, Unity und nativer Runner bestanden. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Native Schnellreise mit zwei tatsächlich angeheuerten Begleitern:** Am28.Oktober Exit0: echte26-Minuten-Reise nach Ripwych Commons, sichtbarer Gruppenpreis15 statt5Gold, exakt einmal15Gold abgebucht. Abbruch und Geldmangel ohne Zustandsänderung. Beide ursprünglichen Körper, Gegenstandsobjekte/UIDs/Ausrüstung, Beziehungen und Verträge erhalten; anschließend6,79m bzw.7,00m körperliches Follow mit Boden-/Kollisionsprüfung. Keine neue Anwerbung, Zahlung oder injizierte Zustimmung im Wiederholungslauf. Vorher gemeinsamer nativer Innausgang bestanden; dessen Test liest nach regulärem Körperwechsel die tatsächliche Zielreferenz neu. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Begleiter an Außenecken:** Nativ rot/grün am28.Oktober: Der bevorzugte Abstandspunkt lag außerhalb eines erreichbaren Weges, obwohl der tatsächliche Spieler erreichbar war. Bei diesem Fehlschlag wird dasselbe Wegnetz zum tatsächlichen Ziel geprüft; Sicht, Abstand und Kollision bleiben wirksam. Vollständiger289,42m-Beobachterweg mit ursprünglichem Begleiter/Auftrag bestanden. Anschließend zweite echte Anwerbung für10Gold erfolgreich; die native Mehrbegleiter-Schnellreise ist oben separat abgenommen. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Dungeon-Flucht bei mehreren tatsächlichen Angreifern:** Am28.Oktober nativ rot/grün: Ein tatsächlicher Fluchtschritt führte zum zweiten sichtbaren Angreifer. Fluchtkorridor und tatsächliche Körperplatzierung einschließlich Ausweichen prüfen jetzt alle sichtbaren nativen Angriffe. Gezielter Nachtest:12 Mehrgegnerframes,2,078979m tatsächliche Bewegung, kein gemessener unsicherer Schritt; Körper, Inventar und Vertrag erhalten.20FPS kontrolliert, keine Gegner/NPCs platziert. Sechs-Meter-Grenze erneut bestanden:10,0264s,0m Rücklauf. Keine dauerhafte Zielstabilität oder vollständige lange Containeranreise behauptet. Seitliches Abdriften des Testbeobachters bleibt separat offen. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Semantische Beuterechte im nativen Gespräch:** Am28.Oktober vier echte Freitextschritte mit lokalem Gemma und nativen Ja/Nein-Schaltflächen bestanden: Nein gewährt nichts, Ja nur eigene Killbeute, separate Containerfreigabe ergänzt diesen Bereich, ausdrücklicher Widerruf entfernt beide Rechte. Der zuvor als Smalltalk fehlgeleitete Widerruf wird jetzt semantisch als aktuelle Rechteänderung erkannt. Körper/Inventar/Vertrag unverändert, keine injizierte Zustimmung. Core2155/2155, Runtime0/0, Unity und nativer RunnerExit0. Sprachausgabe gezielt deaktiviert; physische Entnahme separat geprüft. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Längere Stapelbeute ohne falsches Hinderniswarten:** Native Rot-/Grün-Probe am28.Oktober. Vorher beginnt trotz66 erfolgreicher Goldtransfers falsches Warten. Jetzt zählt ausschließlich ein tatsächlich abgeschlossener Transfer als Fortschritt; der NPC bleibt dabei stehen. Nachtest:86 echte Goldstücke in8,51s bei kontrollierten10FPS, exakte Mengenerhaltung, gleicher Körper/Vertrag, kein Hinderniswarten. Gewöhnliche Rechte-/Einzelitemprobe auf demselben Build ebenfalls Exit0. Der alte uninstrumentierte Einzellauf ist nicht eindeutig zuordenbar; der reproduzierte Stapelfehler ist geschlossen. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Separate Containerrechte, gezielter nativer Fall:** Am28.Oktober Exit0: keine Entnahme ohne Zusage oder mit reinen Eigenbeuterechten; separate Containerfreigabe überträgt den tatsächlichen nicht stapelbaren Gegenstand UID33557538. Körper, bestehende Gegenstände und Vertrag erhalten; neues Vorhaben löscht Rechte und lehnt alte Freigaben ab. Kontrollierte Platzierung desselben NPC und Beobachters auf echtem Boden, danach tatsächlicher kurzer Beuteanmarsch. Container/Gegner unverändert. Langer Anmarsch, früherer noch unklassifizierter Transfer-Timeout und semantischer Zustimmungsdialog bleiben offen. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Eigene Dungeon-Killbeute:** Am 28. Oktober nativ Exit0. Stadtwachen-Abhängigkeit der Killbelege und Laufziel auf erhöhter Leichenspriteposition korrigiert. Tatsächlicher Gegner, native Leiche und physischer Anmarsch; ohne Zusage keine Entnahme, mit kontrollierter Zusage exakter Transfer von UID33557756 innerhalb1,1m. Todesstoß und Zustimmung sind ausdrücklich kontrolliert, kein natürlicher Kampf oder semantischer Zustimmungsdialog behauptet. Separate Containerfreigabe im oben dokumentierten gezielten Fall bestanden; dessen offene Grenzen bleiben bestehen. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Automatisches Dungeon-Hinderniswarten und Weiterfolgen:** Am 28. Oktober nativ Exit0. Nach kontrollierter Beobachterplatzierung hinter einem tatsächlich unvollständigen NavMesh-Weg beginnt der unveränderte Begleiter nach 25,75 echten Sekunden selbständig das Warten und zeigt die native HUD-Meldung. Ursprüngliche 3/6-Spielstunden-Wartefolge, Körper, Inventar, Auftrag und Zustimmung bleiben erhalten. Erreichbare Rückkehr des kontrolliert platzierten Beobachters setzt denselben Follow fort. Kein injizierter Wartezustand, kein Zeitsprung, keine NPC-Platzierung oder Gegneränderung. Frühere Hinweise auf eine offene automatische Erkennung sind damit überholt; Rückreise und Mehrbegleiterfälle bleiben eigenständige Kriterien. [Belege](audits/2026-10-28-transition-acceptance.md).

Aktueller Dungeon-Nachweis28.Oktober: echte52-Minuten-Schnellreise für10Gold nach The Hold of Buckington,53,48m Außenanmarsch und nativer Dungeon-Eintritt bestanden. Gefahrenbindung korrigiert und separat nativ geprüft:0m Rücklauf zum weiterhin sichtbaren Angreifer. Ausgang korrigiert: Projektion derselben nativen Tür trotz versenkter Modellunterkante. Finaler Nachtest Exit0 mit gleichem Körper/Items/Auftrag, exklusivem Außenbesitzer und4,48m Außennachfolge. Unbeobachteter Innenrückweg nicht vermessen; keine neue vollständige friedliche Runde behauptet. [Belege](audits/2026-10-28-transition-acceptance.md).

**Fertig bedeutet:** Der vereinbarte Kern ist umgesetzt, seine konkreten gezielten Abnahmekriterien sind bestanden und es gibt keinen bekannten blockierenden Fehler. Eine Prüfung sämtlicher Städte, Ereignisse oder denkbaren Kombinationen ist keine Voraussetzung. Spätere Erweiterungen bleiben eigene Todos. Ein neuer Fehler öffnet den betroffenen Punkt, nicht automatisch jedes verknüpfte System.

Jede Mindmap-Blase nennt ein konkretes Abschlusskriterium. LIFE-Querlinks zeigen Zusammenhänge; sie machen nicht sämtliche offenen Nachbarsysteme zu Abschlussbedingungen. Umfang wird nicht nach bestandenem Test stillschweigend erweitert. „Umsetzung abgeschlossen, Abnahme offen“ ist von tatsächlich fehlender Funktion zu unterscheiden.

**Gezielte Spielprüfung:** Ein offenes Abschlusskriterium pro Lauf. Bereits bestandene Fälle werden nur nach einer relevanten Änderung oder wegen eines konkreten Fehlers wiederholt. Keine vollständige Garnisonsbereitschaft als Voraussetzung anderer Prüfungen, keine minutenlangen Bereitschaftsschleifen. Fehlt die passende reale Ausgangsszene, wird dies kurz als fehlende Voraussetzung gemeldet. Ein später Fehler in einem Sammellauf öffnet nur den betroffenen Fall; für seine Wiederholung wird der bereits vorhandene betreffende Prüfschritt direkt aufgerufen.

**Abgeschlossene Kernsysteme: 17 von 22 — Identität, Rolle/Pflicht, Vergangenheit, Persönlichkeit, Fertigkeiten, Körper/Stimmung, Bedürfnisse/Ziele, Wahrnehmung/Wissen, Gedächtnis, Ausdruck, Zuhause/Haushalt, Alltag/Aufträge, Erlebnis/Bewertung, Beziehungen, Handel, Weltzustand und Speichern.** Spielbelege werden je System abgeschlossen; ein Fehler in einer späteren Teilprüfung widerruft keinen unabhängigen bestandenen Fall.

- [x] **Schnellreisebegleiter im Zielgebäude:** Tatsächlicher Eintritt in Ripwych-Inn265, anschließender Innenraum-Diskreload und bezahltes Zusageende bestehen jeweils nativ mit Exit0. Beim Ablauf bleiben derselbe sichtbare Körper (0m Verschiebung), Inventar und Beziehung erhalten; Auftrag und Vertrag enden ohne neue Zahlung. Korrigiert sind das Abräumen beim Zeitnachholen und die Auftragsbeendigung nach bereits verbrauchter Türübergabe. Auch der native Ausgang nach Zusageende besteht: kein verwaister Begleiterkörper oder Reisebesitzer draußen, Daten und terminaler Auftrag erhalten. Unbezahlter Fall und tatsächlicher Rückweg bleiben getrennt offen. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Weiterfolgen nach Schnellreise vor Vertragsablauf:** Ein tatsächlicher Begleiter erreicht Ripwych Commons in26nativen Spielminuten für genau10Gold und folgt anschließend7,20m mit Boden-/Kollisionsprüfung. Körper, native Gegenstandsobjekte/UIDs/Ausrüstung, Beziehung und ursprünglicher Auftrag bleiben erhalten. Mehrbegleiterfall inzwischen separat bestanden. Nach Freigabe der Testpause besteht auch der echte Außen-Diskreload mit Exit0:0m Positionsabweichung statt zuvor2,41m, danach8m tatsächliches Weiterfolgen. Gebäudeprüfungen bleiben separat. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Rückankunft im Anwerbe-Inn nach Diskreload:** SAVE17 besteht nativ mit Exit0. Keine vorzeitige Körperbildung, belegter Eingang hält die Rückreise offen, Freimachen erzeugt genau einen tatsächlichen Körper im richtigen Inn. Inventar/UIDs/Geld und ursprüngliche Vertragsfristen bleiben erhalten; anschließend übernimmt die bestehende Routine mit echter Ausgangsquittung. Korrigiert sind fehlende Rückreiseverantwortung beim Gebäude-Reload und die nur auf Einwohner beschränkte Ankunftsübergabe. Überlandzeit und 531,38-m-Zielweg werden kontrolliert vorgespult; keine beobachtete Gesamtwanderung oder automatische Hinderniserkennung behauptet. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Kontrolliert begonnenes Dungeon-Warten und Rückweg:** Am 28. Oktober Exit0: tatsächliches Gespräch und pausierte Uhr, native Serialisierung, 3/6-Spielstunden-Warten, 9,11 m körperlicher Rückweg nach ursprünglichem Vertragsablauf, echte Portalquerung, Außenkörper/Warten ohne Fristreset und 0,33 m beobachteter Abmarsch. Rückreiseplan verwendet tatsächlichen Anwerbeort 1291010263/263177, 26 gemessene Zielwegpunkte und native Reisezeit. Hindernisstart und Zeitsprünge sind kontrolliert; automatische Hinderniserkennung, echte Zielankunft und Diskreload bleiben offen. [Belege](audits/2026-10-28-transition-acceptance.md).

- [x] **Dungeon-Rundweg und Stop:** Am 28. Oktober nativ mit Exit0 bestanden: kontrollierter 5-m-Außenansatz an echter Tür, tatsächlicher Eintritt, 11,08 m Hinweg, Gespräch, 8,74 m Rückweg, Austritt, 4,85 m Außennachfolge, zweiter Eintritt und Stop. Derselbe Körper, sieben native Gegenstandsobjekte/UIDs und unveränderter Vertrag. Core-Snapshot-Restore belebt den beendeten Auftrag nicht wieder; kein freier semantischer Stop oder Diskreload behauptet. Hindernis-Rückkehr, Beute und intermittierender Imp-Kontakt bleiben separat offen. [Nachweis und Grenzen](audits/2026-10-28-transition-acceptance.md).

- [x] **Nacht-Mauerfestnahme:** Nacht-Mauerfestnahme21 nativ bestanden: tatsächliche Wache g:1291010263:n:5 erreicht ihren Nachtposten über normalen Anmarsch und bestehende Innenroute. Native Festnahmeaufforderung, tatsächliche Ablehnung und Schaden sind bestätigt. Kein vollständiger Garnisonsaufbau als Voraussetzung; konkrete Bereitschaft höchstens30 Sekunden, danach normaler Weg. Tatsächlicher Posten als Checkpoint gespeichert. Belege: `wall-night21-player.log:1190–1198`, ReceiptExit0.
- [x] **Natürliche Heimkehr:** natural-home22 bestätigt211,3087m tatsächlichen Heimweg zur eigenen nativen Haustür, denselben NPC und unveränderten Reiseplan; Exit0. Regionale Reise ausdrücklich unbeobachtet. Kein erneuter Heimweglauf nötig; Checkout und tatsächlicher Wechsel des Gasthauses sind inzwischen oben separat abgenommen. Gildenbett/Camp bleiben offen.
- [x] **Begleiter-Innausgang:** Native Prüfung vom28.Oktober besteht aus dem echten gespeicherten Allan-Vertrag: bestehender Follow durch die native Inntür, gemessene unbeobachtete Innenroute und tatsächlicher Körper draußen, ohne neue Anwerbung oder Zahlung. Der gültige temporäre Türplan darf nicht als Herkunftsabwesenheit abgebrochen werden. Beobachteraufbau und Außenpositionierung sind ausdrücklich kontrolliert; NPC-Position und Auftrag werden nicht injiziert. [Belege und getrennte Testfehler](audits/2026-10-28-transition-acceptance.md).
- [x] **Fertigkeiten — Blase fertig:** Fertigkeitskern abgeschlossen. Paid-XP15, pausierter Dienst ohne XP, wiederhergestelltes Zauberbuch und Shield14 bleiben abgenommen. Temple21 bestätigt die tatsächlich zweite angebotene Lektion (Skill 5 statt erstem Skill 3) beim richtigen nativen Trainer: einmal 400 Gold vom NPC, 10800 Spielsekunden Unterricht, kein vorzeitiger Skill-/XP-Gewinn, anschließend Skill 9 auf 10 und XP 0 auf 255. Gespeicherter und aktueller nativer Skill stimmen überein; dieselbe Person, Inventarbindung und Traineridentität bleiben erhalten. Spielergeld und alle 35 Spielerfähigkeiten bleiben unverändert. Kontrolliertes Unterrichtsbudget und Zeitsprung zum tatsächlichen Timerende sind markiert. Die zusätzlich gewünschte HUD-Nennung ist in Paket22 gebaut und installiert: tatsächlicher NPC-Name und lokalisierter Skill aus dem abgeschlossenen Auftrag.
- [x] **Handel — Blase fertig:** Handelskern abgeschlossen. vendor8 und shop-route9b belegen native Handelsoberfläche, Eingabesteuerung, Kauf und Abbruch. market-visit17 belegt tatsächlichen Marktbesuch, eigene Zahlung und Besitz bei unverändertem gewerblichem Angebot. repair19/20 bestätigen Reparaturabgabe und echten Diskreload; repair21 schließt die Abholung direkt aus dem gespeicherten Reparaturzustand ab: UID 33555763, Zustand 150/150, Verwahrung beendet, Wallet 456 unverändert seit Zahlung, derselbe Reparateur und verweigerte zweite Abholung. Native Reparaturdauer 86400 Spielsekunden, Preis 2 Gold genau einmal. Erpressung, Bühne und autonome Wirtschaft bleiben getrennte spätere Aufgaben. Beleg der letzten Abholung: `repair21-player.log:1004–1005`, Exit0 in13,96 Sekunden.
- [ ] **Begleiter-Schnellreise (LIFE-68):** Ein tatsächlicher Begleiter ist am28.Oktober nativ abgenommen: sichtbarer Preis20 statt10Gold, Abbruch und Geldmangel ohne Reise/Abbuchung, genau20Gold bezahlt, native2548Spielminuten, identischer Körper und identische Gegenstandsobjekte/UIDs/Ausrüstung/Beziehung am Ziel. Der unterwegs abgelaufene Tagesvertrag wird nicht verlängert. Die separate native Zwei-Begleiterprüfung ist inzwischen bestanden:26Spielminuten,15Gold, Abbruch/Geldmangel, unveränderte Körper/Items/Verträge und körperliches Weiterfolgen. Noch offen bleibt der bereits benannte unbezahlte Ablauf im Zielgebäude; Rückwegprüfungen stehen separat. [Prüfprotokoll](audits/2026-10-28-transition-acceptance.md).
- [x] **Reparaturabgabe und echter Diskreload:** repair19 bestätigt exakte UID 33555763, einmalige Zahlung, verweigerte Frühabholung und Replayschutz. Echter SaveLoadManager-Roundtrip erhält Verwahrung, Zustand, Fälligkeit, Ausrüstungsstatus, Wallet und Laden. Abholangebot und einmalige Abholung sind inzwischen in repair21 bestanden; Handel abgeschlossen.
- [x] **Körperlicher Marktbesuch:** Marktbesuch17 nativ bestanden: tatsächlicher Käufer s:1291010263:h:263682:r:0, 7,81 Meter körperlicher Browse-/Zahl-/Rückweg, korrekte Warenblickrichtung, genau6Gold vom Käufer zum Verkäufer, eigene neue UID33555990 bei unverändertem Angebot. Besitz nach Rückkehr und Typserialisierung erhalten; naher Spieler unterbricht den Besuch nicht. Sieben echte Posts und20 Plätze. Bühne/Zuschauer wurden nicht geprüft und sind keine zusätzliche Kernhürde. Reparaturabwicklung ist inzwischen durch repair19/20/21 vollständig belegt.
- [x] **Körperlicher Beschwerdegang und Gespräch vor Einsatz:** complaint16 nativ bestanden: derselbe tatsächliche Reisende Glonn läuft29,27m in8,74s zur Wache. Beide stehen3,36s still und einander zugewandt, bevor die tatsächliche Drohungsmeldung übergeben wird. Anschließend13,46m normale Bewegung, Meldung bleibt zugestellt. Kontrollierte semantische Drohreaktion und spätere Crime-Bereinigung sind markiert; keine erneute LLM-Klassifikation behauptet. Der gemeinsame Heimweg ist inzwischen ebenfalls abgenommen; Alltag ist abgeschlossen.
- [x] **Wahrnehmung/Wissen — Blase fertig:** Endliches Wahrnehmungs-/Wissenskriterium abgeschlossen: tatsächliche mobile und stationäre Körper respektieren Sicht, Verdeckung, Unsichtbarkeit, Entfernung, Schlaf und Wiederbindung. 22 native Diebstahltransfers bestätigen dieselben Grenzen am echten Ereignisproduzenten. Reale Gespräche erhalten Rolle, Partner und aktuellen Heim-/Tätigkeitskontext. Native Ortsauskunft, Kartenmarker und körperliches Hinführen einschließlich Abstandswarten, Wiederaufnahme und Ankunft sind belegt. Kontrollierte Kenntnis, Beziehung, Schlaf und Beobachterledger bleiben gekennzeichnet. Unbelegte Garten-/Händler-Zukunftsprosa aus biography8 ist kein geprüfter Tagesplan. Keine Vollprüfung aller Städte, Produzenten oder freier Modellformulierungen erforderlich.
- [x] **Tatsächlicher Wachwechsel:** guard-shift-change15 besteht mit tatsächlichen unterschiedlichen Tag-/Nachtkörpern, Nachtfackel, nativ geschlossenen Nachttoren und Rückkehr zum Tagdienst. Keine vollständige Garnison als Voraussetzung; Marktrolle bleibt getrennt offen.
- [x] **Natürlicher bezahlter Begleiter: Zauber-XP:** Paid-XP15 nativ bestanden: natürlich erzeugter Gelem, tatsächliche Zustimmung und10Gold, eigener Heal-Health-Zauber mit21Magicka, genau ein Cast und XP0 auf1. Buch, Profil und andere Skills unverändert; bezahlte Bindung erhalten. Kontrolliert wurde nur der Spielerstandpunkt auf echtem freiem Boden neben dem unveränderten NPC. Der Teststarter erwartete irrtümlich den allgemeinen statt des gezielten XP-Markers; vorhandenes Log nach korrigierter Zuordnung erfolgreich geprüft, ursprünglicher Exit1 erhalten. Kein erneuter Spiellauf. Die Tempellektion ist inzwischen in temple21 bestanden; Fertigkeiten abgeschlossen.
- [x] **Zuhause/Haushalt — Blase fertig:** Haushaltskern abgeschlossen. guard-sleep15 bestätigt echte Nachtwache g:1291010263:n:1 im Quartier770, zugewiesenes natives Bett, korrekte Ausrichtung und in zwei gerenderten Frames genau eine Decke/einen Renderer (Art399/15). Kein doppeltes stehendes Bild. Ruhe ohne Grafik, volle vier Betten mit verweigerter Miete und Vermietung nach Freigabe sind bereits nativ belegt. family-bank14 ergänzt echten vorherigen Bankgang (227,70m Hin/Rück, einmal341Gold, ein Zahler), Gruppenzahlung und tatsächliche Sitze. Der zuvor getrennt offene Inn-Ausgang und gemeinsame Heimweg sind inzwischen unter Alltag nativ abgenommen.
- [x] **Vorheriger Bankgang für die Familie:** family-bank14 bestätigt den vorherigen Bankgang: beide Wallets kontrolliert leer, genau ein regulärer Zahler, einmal341Gold, tatsächlicher Hin-/Rückweg227,70m, größter Schritt0,363m und Heimabstand0,561m. Danach gehen beide gemeinsam los. Hinweg zum Inn253,78/250,62m, echte Käufe und beide Sitze bestehen. Erst der anschließende Zeitsprung im beobachteten Inn legt beide Körper fälschlich in die unbeobachtete Simulation; sichtbarer Ausgang/Heimweg bleiben offen. Gesamt-Exit1 widerruft die vorherigen Teilbelege nicht. Kein erneuter Bank-/Hinweglauf; Wiederholung wird direkt im regulär vorbereiteten Inn begonnen.
- [x] **Mauer tagsüber und direkte Angriffe:** wall-law13 bestätigt native Festnahme vor Schaden, Ablehnung und tatsächliche Gegenwehr; wall-npc14 bestätigt gezielt den fehlenden NPC-/Pfeilfall. Nacht bleibt separat offen. Keine Wiederholung der bestandenen Tagesfälle.
- [x] **Fünf-Schritt-Ablehnung:** five-step13 extrahiert/bindet alle Schritte und erhält reguläres DECLINE ohne Ausführung. Vollständig angenommene Ausführung bleibt offen.
- [x] **Weltzustand — Blase fertig:** Begrenzter Weltkern abgeschlossen. astra-appointments-player.log:1328–1347 bestätigt eine konkrete Zusage für Tag+2, native Zeitfortschreibung bis zur Abfahrt, tatsächlichen Weg über109,39 Spielsekunden, Anwesenheit ohne falsche No-Show-Strafe, Erinnerung und Restore. Der separate native Loiter-Lauf über eine und drei Stunden ist vollständig gemessen: größter Skipframe1596,03ms, nach3h größter Folgeframe273,29ms (release-check/summary.json). Performance12 ergänzt drei aktuelle60s-Stadtsamples ohne Frames ab1s. Damit sind die gemeldeten mehrsekündigen Hänger in diesen konkreten Szenen eingegrenzt. Keine universelle Performancegarantie, weltweite Einzelakteursimulation oder neue kombinatorische Termin-/Lohn-/Zeitsprungmatrix. Historische60/60-Lohnbelege gelten ausdrücklich nicht für heutige direkte Tageslöhne.
- [x] **Körper/Stimmung — Blase fertig:** Körperkern abgeschlossen. shield-only14 besteht: eigene und Player-Touch-Shields, exakte Magicka, echter Schadensverbrauch/Überlauf, erhaltene Stärke und Runden nach Restore, unabhängiger Zeitstempel, unbeobachtetes Altern und Ablauf. Kontrolliertes natives Buch/Level/Mana sind ausdrücklich markiert; kein natürlicher Magier behauptet. Exakte Baseline wiederhergestellt. Alarmunterbrechung, tatsächliche Bewegung, erneute Behandlung und Wiederaufnahme bezahlter Dienste sind bereits durch health-economy-native-final-player.log:1089–1147 belegt. Keine neue Gesundheitsmatrix.
- [x] **Ausdruck — Blase fertig:** Technischer Ausdruckskern abgeschlossen: tatsächliches Gemma/Breeze-Gespräch mit hörbarem Mixer, Wortausrichtung, Mundbewegung und Portraitprojektion in LiveDialoguePresentationProbe.log:967; nativer Textaufbau/Replay/Fehlerfallback in SpeechRevealPresentationProbe.log:966. biography8 bestätigt den vollständigen freien Antwortaufbau, vendor8 neun reale Cursor-/Steuerungsphasen. Die letzten zwei personenbezogenen HUD-Texte erhalten in Paket13 den tatsächlichen Namen vor Gesprächsschluss; Quellprüfung und Runtime-Compile bestanden. Kein neuer Spiel-PASS für diese reine Textkorrektur behauptet. Sprites/Grafiken/Animationen und Spracheingabe bleiben ausdrücklich spätere Arbeit. Doppelte Wachendarstellung bleibt unter dem gezielten Schlaf-/Wachkriterium offen.
- [x] **Bedürfnisse/Ziele — Blase fertig:** Kern abgeschlossen: tatsächliche Behandlung mit Alarmunterbrechung, physischer Rückkehr und Wiederaufnahme (health-economy-native-final-player.log:1089–1147); aktuelle mobile-flee11 und mobile-flee-enemy11 bestätigen Flucht vor Auftrag/Gespräch. TaskFailureFeedbackProbe/Player.log:929–931 bestätigt echten nativen Kauf, kontrolliert verweigerten Übergabestart, behaltene Ware und Geld, übersprungenen abhängigen Folgeschritt und einmalige ehrliche Fehlermeldung samt Wiederherstellung. Kein natürliches Dungeonhindernis behauptet. Freie Fünf-Schritt-Ausführung und Dungeonrückkehr bleiben eigene Kriterien anderer Systeme.
- [x] **Drei aktuelle Stadt-Performance-Stichproben:** Paket12: drei tatsächliche 60-Sekunden-Stadtmessungen bestehen ohne Frame ab einer Sekunde; p95 73,36/85,85/89,08ms, Maximum 371,04ms. Das grenzt die gemeldeten Hänger für diese normale Stadtszene ein; Zeitsprung/Loitern sind damit nicht erneut geprüft. Der dabei natürlich vorbereitete Zustand ist als People - Daggerfall acceptance ready gespeichert, ohne erfundene Akteure/Buchungen.
- [x] **Beide Kinderbetreuungsvarianten:** childcare10b bestätigt tatsächlichen Elternteil mit eigenem Kind im tagsüber offenen Haus; anderer Elternteil ist tatsächlich im geplanten Besuchsgebäude. Zusammen mit family6 für beide Eltern außer Haus/verschlossene Tür abgeschlossen. Keine künstlichen Körper oder Tagespläne.
- [x] **Erlebnis/Bewertung — Blase fertig:** vorhandene native Gesundheits-/Handels-/Diebstahls-/Mordbelege plus grief9 mit echtem Angriff, beobachtetem Tod und richtiger Verantwortung sowie biography8 mit tatsächlichem Innenraumvorfall/korrekter unbekannter Ursache nach Diskload. Keine zusätzliche Ereignismatrix.
- [x] **Beziehungen — Blase fertig:** family6 bestätigt tatsächliche einmalige Familienmitteilung mit eigener Reaktion; grief9 bestätigt echten nativen Angriff/Partnertod, tatsächliche Augenzeugenschaft, wissensgebundene Trauer und richtigen Spieler-Groll. Keine weitere Begrüßungs- oder Weltprüfung erforderlich.
- [x] **Mobile Trefferreaktion/Fluchtpriorität nativ abgenommen:** mobile-flee11 und mobile-flee-enemy11 bestehen jeweils in rund elf Sekunden. Tatsächliche nichttödliche Treffer erzeugen eigene Fluchtreaktion ohne vorzeitiges Mordwissen, unterbrechen den angenommenen Auftrag und lösen den Gesprächshalt; echte Bewegung0,81m bei Playerquelle und0,51m bei tatsächlichem Assassinenkörper als Schadensquelle. Opferreaktion wartet nicht mehr auf die Wachgarnison; beim Nichtplayerfall keine erfundene Spielerverantwortung. Keine Wiederholung nötig.
- [x] **Identität — Blase fertig:** bestehende echte Herkunfts-/Ortswechsel-/Heimkehrbelege, aktueller TravelerHousehold-Nachweis eindeutiger Betten und realer Beziehungen sowie biography8/Home-Disk-Roundtrips erfüllen den Verwaltungskern. Kein neuer Reiselauf als Querlink-Hürde.
- [x] **Nativer Laden-Abbruch:** shop-route9b besteht auf derselben Daggerfall-Fixture:26 Regalitems mit exakten UIDs, persönlicher Bestand getrennt, Abbruch ohne Walletänderung. Keine erneute Markt-Kaufprüfung.
- [x] **Gedächtnis — Blase fertig:** biography8 erhält tatsächliches Ereignis und korrekt zugeordneten Gesprächsverlauf nach echtem Disk-Roundtrip; Folgeantwort verwendet die bekannte Vergangenheit. Bestehende MemorySalience109-Abnahme prüft Ablauf; native Wiederbegrüßung mit echtem Wiederöffnen, exaktem kontrolliertem Text und hörbarer Ausgabe besteht bereits (`release-check/editor.log:4172`). Kein neuer Wiederholungslauf.
- [x] **Vergangenheit — Blase fertig:** biography8 besteht mit tatsächlichen freien Gemma-Gesprächen beider Partner, konsistenten persönlichen und gemeinsamen Familienfakten und erneutem Gespräch nach echtem Disk-Roundtrip.
- [x] **Persönlichkeit — Blase fertig:** drei stabile Charakterzüge und vorhandene Reaktionslogik; biography8 bestätigt alle drei unverändert nach tatsächlichem Speichern/Laden.
- [x] **Stationäre NPCs — Schaden und Loot abgenommen:** stationary8 besteht in40 Sekunden mit29 tatsächlichen Personen in fünf Gebäudetypen: native Treffer, persistenter Tod, exaktes persönliches Inventar, tatsächliches Looten und Wiedereintritt. Das konkrete Save enthält keine questgebundene Person; questLinked=0 bleibt ausdrücklich ungetestet.
- [x] **Inn-Bettenkapazität:** capacity7 verweigert bei vier belegten Betten die native Vermietung ohne Abbuchung. Ein anschließend freies echtes Bett ohne ursprünglichen Restmarker wird für4 Gold nativ vermietet und zum Ruhen erkannt.
- [x] **Markt-Mauszeiger/Spielersteuerung:** vendor8 bestätigt neun native UI-Phasen einschließlich Kaufmenü, Abbruch, erneuter Öffnung, Bestätigung und Rückkehr. Playerkauf4 Gold und NPC-Kauf3 Gold bestehen. Der anschließend separat korrigierte Laden-Abbruchfall besteht in shop-route9b.
- [x] **Speichern des aktuellen Modzustands — Blase fertig:** Schema85; Identität, relevante Erinnerung und Routenzeiten erhalten; alte/ungültige Saves vor Mutation abweisen. Frisch5/5 Schemafälle bestanden. Tatsächliche DFU-Serialisierung/Wiederherstellung im Familienlauf erhält Zahlungen und Zustände ohne Doppelzahlung. Keine Migration oder allgemeine Mehrjahresabnahme erforderlich. Einzelne Produzenten/Feature-Integrationen werden bei ihrem System geprüft.
- [x] **Familien-Innbesuch, gezielte Abnahme:** reale Sitzplätze, ein Gruppen-Zahler, tatsächlicher Ausgang, Wiederbindung, Nachtverschluss und späterer Tageszutritt mit beiden Heimkörpern. Der Außenheimweg dieser Probe ist projiziert; Beide Betreuungsvarianten und der sichtbare gemeinsame Heimweg sind inzwischen separat nativ abgenommen. Der Teststarter verlangte irrtümlich Belege des absichtlich übersprungenen allgemeinen Alltagstests; Zuordnung korrigiert und alle vorhandenen Familienbelege erneut erfolgreich geprüft.
- [x] **Wachen-Tagesbesetzung:**184 Identitäten,92 im Dienst,32 Stadtposten,16 Torposten,30 erreichte Mauerposten und7 Zweierpatrouillen. Nachtwechsel ist ein eigener offener Fall.
- [x] **Player-Marktkauf:** Paket5 erreicht tatsächlichen Itemtransfer und4 Gold Zahlung. Das beweist noch nicht die gemeldete Maus-/Steuerungsübergabe. Der anschließende NPC-Kauffall scheitert an einem fehlenden geeigneten Fixture-Käufer, nicht an diesem Playerkauf.

Aktuell gebaut/installiert: **package24 /1C27EAECE9E0**; Distribution und Player bytegleich. Die Begleiter-Anheuerungsbox ist im tatsächlichen Player vollständig lesbar und verwendet „one day“. Native Ablehnung lässt Auftrag, Geld und Ledger unverändert (`hire-preview24`, Exit0). Die Sichtprüfung nutzt dieselbe Produktionsbox ohne Modellentscheidung. Alle bereits bestandenen Einzelkriterien gelten weiterhin; neue Pakete werden gezielt nur für betroffene offene Kriterien geprüft. Ein Build ist kein pauschaler Spiel-PASS. Vollständige Belege und Grenzen: [Spielabnahme](audits/2026-10-27-game-acceptance.md).


Stand: 27. Oktober 2026. Abgleich mit den vorhandenen Testbelegen und den neuesten Aufträgen. Dieser Plan ersetzt die früheren Architektur- und Zwischenstandslisten.

## Tatsächlicher Implementierungsstand am 27. Oktober

**Große Teile des Kerns sind vorhanden. Nicht vollständig spielabgenommen ist nicht gleich nicht implementiert.** Diese Übersicht trennt Quellstand, tatsächliche Spielbelege und verbleibende Arbeit. Grafiken, Sprites und Animationen sind hier kein Implementierungsblocker.

| Bereich | Implementiert / gezielt offline geprüft | Native Spielbelege / echte Restarbeit |
| --- | --- | --- |
| Dialog, Biografie, Familie, Auftragsketten | Ein Gemma plus Breeze; kompakte dauerhafte Biografie beim ersten relevanten Gespräch, drei bis vier gemeinsame Familienfakten, Schutz realer Herkunft, abhängige Schritte stoppen und ausdrücklich unabhängige Schritte fortsetzen. 53 Biografie-Assertions und fünf Fortsetzungsfälle bestehen. | Freie Fünf-Schritt-Zerlegung und Bindung sind nativ belegt; der damalige NPC lehnte korrekt ab. Biografiegespräche mit beiden Partnern und echtem Disk-Roundtrip bestehen in biography8. Angenommene vollständige Fünf-Schritt-Ausführung bleibt offen. Kein zweites Modell erforderlich. |
| Markt, Geld und Besitz | Gewerbliche NPC-Käufe erhalten Angebot, zahlen echtes Geld und erzeugen eigene UIDs; Reparaturbesitz ist geschützt. Gewöhnliche markierte Einkäufe werden bei tatsächlicher Heimkehr verbraucht, Geschenke/Ausrüstung/Auftragsitems bleiben. Marktsteuerung und native Kaufen/Reden-Anbindung sind integriert. | vendor8 bestätigt neun tatsächliche Inputphasen, Playerkauf4Gold und gewerblichen NPC-Kauf3Gold; shop-route9b bestätigt getrennten persönlichen Bestand und unveränderte26Regal-UIDs beim Abbruch. market-visit17 besteht mit tatsächlichem Hin-/Rückweg,6Gold, neuer eigener UID und stockDelta0. Nicht wiederholen. Standdienst und234,52m Pendelweg sind in market-commute17 belegt; vollständige Heim-/Bett-/Morgenfolge bleibt offen. Bühne nicht ausgeübt, Quellenabwesenheit nicht bewiesen; getrennte Option. |
| Alltag, Kinder, Türen und Reisen | Kinderbetreuung, Gruppenfinanzierung, physische Inn-Bewegung, Reisendenidentität und Herkunftshaushalt vorhanden. Alternative Inns/Außencamp für gewöhnliche TravelerPerson-Besuche implementiert und offline geprüft. | Familien-Sitzgang, Gruppenzahlung und Inn-Ausgang nativ belegt; späterer Außenheimweg in dieser Probe projiziert. Nachtverschluss und späterer Tageszutritt mit beiden Heimkörpern bestehen; der gemeinsame Außenheimweg ist am28.Oktober ebenfalls mit252,41m pro Elternteil nativ abgenommen. ResidentTrip-Ausweichunterkünfte sind inzwischen ebenfalls implementiert: bestehender ServiceExit hält den ursprünglichen Reiseplan, alternative Unterkunft/Camp kehrt rechtzeitig zum bisherigen Anschlusspunkt zurück; vier Szenarien und75 verknüpfte Fälle bestehen. Native Wiederholung offen. NPC-Gildenberechtigung und freier berechtigter Bettzugang sind inzwischen implementiert und gezielt geprüft; tatsächliche Gilden-/Campankunft bleibt nativ offen. |
| Wachen, Verfolgung und Tatort | Sichtbeitritt, tatsächlicher Halt-Ruf auf200m, variable erreichbare Suchziele sowie Meldung/Befragung mit2–5Sekunden, Fokus und nahem Text integriert. | Sicht/Halt und zwei NPC-Mordfälle bestehen nativ. complaint16 besteht mit29,27m Meldeweg,3,36s tatsächlicher Gesprächspause und13,46m weiterem Weg nach Unterbrechung. guard-shift-change15 bestätigt echte Ablösung, Nachtfackel, geschlossene Nachttore und Rückkehr zum Tagdienst. Der frühere statische4,68s-Befragungsbeleg bleibt erhalten; eine darüber hinausgehende Fokusabnahme wird hier nicht behauptet. Haft und die ausdrücklich offenen Teilfälle bleiben getrennt. |
| NPC-Haft und Beschlagnahme | Anforderungen festgelegt; reale Quellen untersucht. | Haftzustand, Strafzeit-/Abführungsadapter und Beschlagnahme sind inzwischen implementiert und gezielt geprüft. Native Gesamtaufnahme bleibt offen: vier echte Schlosskäfige sind über vorhandene Wege nicht erreichbar; kein gewöhnlicher Raum wird als Zelle erfunden. |
| Dungeonbegleiter und Beute | Fähigkeiten/Navigation, beziehungsabhängiges Warten, Dialog/HUD-Hinweis, gemessener unbeobachteter Rückweg und native zeitbasierte Rückkehr zum tatsächlichen Anwerbeort integriert. Wartebänder innen/außen: 3/6, 12/24 oder 72/72 Spielstunden. Automatischer Hindernisstart, getrennte Rückkehrprüfung, eigener Killbeutetransfer und separate Containerfreigabe sind in ihren gezielten nativen Fällen belegt. Semantische Planungsfrage, natives Nein/Ja, zusätzliche Containerfreigabe und Widerruf bestehen separat mit tatsächlichem Modell und ohne injizierte Rechte. | Todesstoß/Zusagen der physischen Beuteproben beziehungsweise örtlicher Container-Teststart sind kontrolliert. Langer Container-Anmarsch bleibt offen; Sprachausgabe und Entnahme unmittelbar nach der geprüften Dialogfolge wurden darin nicht getestet. Der reproduzierte Stapel-Timeout ist nativ rot/grün behoben; der alte Einzellauf ohne Itemdiagnose bleibt historisch nicht eindeutig zuordenbar. Echte unpassierbare Wege bleiben blockiert; keine Produktions-Teleports oder erfundenen Ankünfte. |
| Gesundheit, Dienste, Speicherung | Vorhandene native Heilung, Dienste, Reparaturverwahrung und aktuelle Speicherung gezielt geprüft; Tempeltraining bindet den tatsächlichen Auftragnehmer. Keine Altsave-Migration. | Shield14 bleibt bestanden. repair19 bestätigt Abgabe/Pending-Diskreload; repair21:1004–1005 ergänzt dieselbe UID33555763,150/150, keine zweite Zahlung und abgewiesene Wiederholung, ReceiptExit0. temple21:1093 bestätigt tatsächliches Training:400Gold, Skill9→10, Praxis0→255,10800Spielsekunden, Spieler unverändert und dieselbe tatsächliche Person. Der spätere Runnerstop ist im Assertion-Recheck getrennt; Training/Reparatur nicht wiederholen. Weitere Pflege-/Kampfketten behalten ausschließlich ihre eigenen offenen Kriterien. |
| Alle stationären NPCs | Implementiert: native/modseitige stationäre Personen einschließlich tatsächlich questgebundener Figuren verwenden vorhandene Schadens-, Identitäts-, Inventar- und Todespfade. 27 gezielte Prüfungen, Runtime und QuestHooks ohne Compilerfehler. | stationary8 besteht mit29 tatsächlichen Personen, fünf Gebäudetypen, nativen Treffern, persistentem Tod, exakter Beute und Wiedereintritt. Keine questgebundene Person im Save (questLinked=0); dieser Sonderpfad ist nativ noch nicht belegt. Keine zweite Inventarverwaltung. |

Letzte gemeinsame vollständige Core-Prüfung: **2149/2149**; spätere gezielte Prüfungen ersetzen diese Angabe nicht durch eine neue Vollsuite. Paket21 / `147E21264B18` ist gemeinsam gebaut und installiert; der jeweils aktuelle Folgepaketstand steht oben. Ein Paketbuild ist kein pauschaler Spiel-PASS. Markt-UI, Laden-Abbruch, tatsächlicher Marktbesuch, ganze Reparaturfolge und Tempeltraining sind abgeschlossen; die ganze Markt-Pendelschicht bleibt getrennt offen. Einzelbelege unter `work/final-acceptance-20261027/`: `vendor8:998–1014/1068`, `shop-route9b:997`, `market-visit17:1191–1197`, `guard-shift-change15:1113`, `complaint16:1129–1142`, `repair19:997/1045`, `repair21:1004–1007`, `temple21:1093–1094`. Zugehörige Receipts und Grenzen: [Spielabnahme](audits/2026-10-27-game-acceptance.md). Die [Mindmap](NPC-MINDMAP.html) führt dieselben endlichen Abschlusskriterien; ihre22Themenblasen sind keine Fertigstellungsquote.

Tempel21 hat einen tatsächlichen nativen Abschluss-PASS, aber keinen nachträglich erfundenen Exit0: Der Runner erkannte den exklusiven Tempelmodus beziehungsweise dessen neuen Abschlussmarker nicht und wurde nach dem PASS gestoppt. Das ursprüngliche Exit1-Receipt bleibt erhalten. `temple21-assertion-recheck.json` prüft dieselben tatsächlichen Logassertions mit ErgebnisPASS und SourceLogSHA256 `0D74793322FB53A48B5A220077A042C73F68E9D7ED98FEADF65C388C5241FD4B`. Keine erneute Trainingsausführung nötig.

**Darstellung:** Jede flache Sprite-Fläche bleibt zum Spieler gedreht. Mobile NPCs verwenden dafür bereits vorhandene Richtungsbilder entsprechend ihrer logischen Blickrichtung. Stationäre Richtungsbilder kommen später; Gesprächsfokus funktioniert unabhängig davon. Keine zusätzliche Grafikarbeit in diesem Auftrag.

## Neue Spielrückmeldungen und Folgearbeiten

Die Folge-Agenten bearbeiten die konkret verbliebenen Fehler; der gemeldete Markt-Maus-/Steuerungsfehler ist seit vendor8 nativ abgeschlossen. Alle auf ausdrückliche Nutzerwahl mit GPT-6.1 Sol / High. Teilnachweise bleiben erhalten und ersetzen keine ungeprüften Nachbarabläufe. Der Hauptagent führt Builds und Spieltests nacheinander aus.

- [x] **Tagesbesetzung in Daggerfall nativ geprüft:** Im kontrollierten Lauf mit Paket `8A7DBD99E9AB` erreicht die Garnison184 Identitäten, davon92 im Tagesdienst:32 Stadtposten,16 Torposten,30 tatsächlich erreichte Mauerposten und sieben Zweierpatrouillen. Das ist kein bestandener Nachtwechsel oder Festnahmetest. Beleg: `work/user-feedback-20261027/guard-shifts-native-player.log`.
- [ ] **Mauerwachen und Festnahme — wall_guard_fixes:** wall-law13 besteht tagsüber mit tatsächlichem Wehrgang, nativem Festnahmeangebot, No und Schaden17 sowie echtem Wachtreffer285→275, Assault und Weigerungsschaden10. wall-npc14 bestätigt tatsächliche Mauerklassifikation, verbrauchten nativen Pfeil und exaktes NPC-Ziel. Diese Teilfälle nicht wiederholen; noch nicht belegte Nacht-/Haftteile bleiben offen. Passanten unterhalb der Mauer bleiben getrennt.
- [x] **Benannte, wahrheitsgemäße Meldungen — complaints_fixes (complaint16):** Tatsächlicher Meldeweg29,27m zu verfügbaren Wachen, vorgeschaltete3,36s Gesprächspause mit Stillstand/Blickkontakt und weiterer13,46m-Weg nach Unterbrechung bestehen; delivered bleibt erhalten und die Heimroutine übernimmt die Meldung nicht. Der kontrollierte konkrete Drohungsbeleg bleibt gekennzeichnet; freie LLM-Auswahl wird nicht als zusätzlich geprüft behauptet. Eine reine Verweigerung darf weiterhin keine ungeschehene Wachenmeldung behaupten.
- [x] **Erfahrungsaustausch in der Familie — family_experience_fixes (family6 und grief9):** Nach tatsächlicher Gelegenheit zum Gespräch teilen Familienmitglieder bedeutsame negative Spielererfahrungen. Der Partner kennt die Mitteilung und reagiert gemäß eigener Persönlichkeit und Beziehung. Hörensagen bleibt Hörensagen, keine automatische Augenzeugenschaft oder zusätzliche Strafregistrierung; kein wiederholtes Aufsummieren desselben Vorfalls.
- [x] **Nur ein Schlafbild derselben Wache — sleep_visual_fixes (guard-sleep15):** Bei aktiver Schlafgrafik darf der stehende Sprite nicht zusätzlich sichtbar sein. Aufwachen, Tod und Unterbrechung stellen den passenden Zustand wieder her. Fehlende Grafik verhindert weder Ruhe noch Heilung; kein zweiter Körper oder Inventarbesitzer.
- [ ] **Todo: Einschüchtern und Geld erpressen — LIFE-67, noch nicht implementiert:** Der Spieler fordert einen konkreten Betrag über den vorhandenen semantischen Dialog. Einfacher Ansatz für die spätere Erfolgsberechnung: glaubhafte Drohwirkung aus vorhandenen Spielerwerten und Situation, Mut/Persönlichkeit und aktuelle Furcht des NPC sowie Höhe der Forderung relativ zu seinem tatsächlichen Geld. Ergebnisse: tatsächliche Zahlung bei ausreichenden Mitteln, Gegenwehr/Verweigerung oder physische Flucht zur Wache. Keine erfundenen Münzen, kein unbegrenztes Neuwürfeln durch Wiederholung und kein neues Modell. Gewichte erst bei Umsetzung festlegen und prüfen.

## Sechs bisherige Umsetzungspakete

Nach abgeschlossener Detailklärung bearbeiten sechs GPT-6.1-Sol-Agenten mit High
den vorhandenen Kernumfang: Markt/Personen/Handel, Wachen/Haft,
Dungeon/Begleiter, Alltag/Familie/Reisen, Dialog/Biografie/Erinnerung sowie
Gesundheit/Dienste/Performance. Der Hauptagent integriert und führt Builds sowie
Spielprüfungen nacheinander aus. Arbeitsbeginn ist keine Fertigmeldung.

Die [bestätigten Detailregeln](superpowers/specs/2026-10-27-core-completion.md)
und [Zuständigkeiten](superpowers/plans/2026-10-27-six-agent-core-completion.md)
sind maßgeblich. Gegenüber älteren Abschnitten gilt insbesondere:

- NPC-Käufe erhalten auch gewerblichen Marktbestand; Playerkäufe und privater
  Handel bleiben tatsächliche Bestandstransfers. Gewöhnliche gekaufte
  Verbrauchswaren gelten bei Heimkehr als verbraucht; Ausrüstung, Geschenke und
  Auftragsgegenstände bleiben erhalten. Kein Hunger- oder Haushaltslagersystem.
- Publikum schaut nur bei real vorhandenem Darsteller einer Aufführung zu.
- Meldung/Befragung erhält zwei bis fünf Sekunden gegenseitigen Blickkontakt
  und in Spielernähe sachlichen Text, ohne zusätzliche Hintergrund-LLM-Aufrufe.
- Festnahme verwendet echte Zellen. Belegtes Diebesgut geht an den tatsächlichen
  lebenden Eigentümer zurück; übrige Waffen in einen vorhandenen
  Stadtmauercontainer. Ohne Container werden nicht zurückgebbare Gegenstände
  zerstört. Bei totem Eigentümer Container oder Zerstörung; kein Wachinventar
  als Ersatzlager und keine erfundenen Container.
- Volles Inn: andere erreichbare offene Inns, dann tatsächlich berechtigtes
  freies Gildenbett, dann Camp außerhalb der Siedlung. Keine unbeobachteten
  zufälligen Reiseverletzungen oder Todesfälle.
- Blockierte Begleiter warten abhängig von Vertrauen und Beziehung: bezahlter
  Dienst Stunden, enge vertraute Freunde Tage; erst innen, dann körperlich am
  Eingang draußen. Danach Rückkehr zum tatsächlichen Anwerbeort und bestehender
  Routine. Nähe bestimmt Dialogfenster oder obere Textmeldung. Kein Teleport.
- Beutezusage gilt für das konkrete Unternehmen und eigene besiegte Gegner;
  Container benötigen separate Zustimmung. Der Begleiter fragt bei der Planung.
- Kurze dauerhafte LLM-Biografie und drei bis vier konsistente gemeinsame
  Familienfakten; bestehende Identitäten und reale Orte bleiben verbindlich.

Spätere Expeditionen, Nachfolger, Gefangenenbefreiung, Spracheingabe und Release
bleiben zurückgestellt. Alte offene Checkboxen sind gegen aktuelle Quelle und
neuere Belege zu prüfen, nicht als Auftrag zum Neubau vorhandener Systeme.

<a id="immersion-simplification"></a>
## Aktuell integriert: Vereinfachung ohne Verlust relevanter NPC-Funktionen

Die vereinfachten Kernpfade sind überwiegend integriert. **Die Spielabnahme läuft auf frischen Hakon-Saves;
mehrere Kernabläufe bestehen nativ. Eine vollständige Freigabe besteht noch nicht.** Kontext, Erinnerungen und
technische Historienaufbewahrung bleiben auf ausdrücklichen Nutzerwunsch erhalten.
Sie werden erst bei nachgewiesenen Performanceproblemen neu bewertet.

| Bereich | Verbindliches vereinfachtes Verhalten | Status und nächste Prüfung |
| --- | --- | --- |
| LLM und Stimme | Genau ein LLM (Gemma) plus TTS (Breeze). Goal Extraction, Two Pass, Parameterklärung, Zustimmung und native Ausführungsprüfung bleiben erhalten. D3 und Decision Shadow entfallen. Mehrere Aufrufe desselben LLM sind zulässig. | Integriert: Zustimmung16/16, Ziele/Dispatch56/56 und Launcher15 Fälle. Die frühere reale Gemma-Studie bestand6/6 Fünf-Schritt-Pläne; neue durchgehende Spielabnahme offen. |
| Schlaf und Inn | Ohne Schlafsprite steht der NPC neben seinem tatsächlich erreichten Bett und erholt sich dennoch. Sprites kommen später. Nur freie Betten bestimmen Inn-Kapazität: ein Bett pro Person; bei Vollbelegung keine Spielermiete. Keine Zimmervermessung oder Familienzimmerbindung. | Nachtruhe ohne Art nativ bestanden. capacity7 verweigert bei vier belegten echten Betten ohne Abbuchung; ein anschließend tatsächlich freies Bett wird für4Gold vermietet und als Ruheplatz erkannt. Diese Kapazitäts-/Spielermietfälle sind abgeschlossen. |
| Herkunft und Reisen | Ein realer reservierter Herkunftshaushalt mit einem Bett pro Person, gleichem Nachnamen, Familienmitgliedern und Beziehungen. Identität, Persönlichkeit und Reisezweck bleiben erhalten. Keine ganze Herkunftsstadt vermessen; Rückweg erst bei Bedarf berechnen, vorhandene geprüfte Wege wiederverwenden. | Herkunfts-/Identitätsprüfung in6 Gruppen bestanden; natürliche Reise, Wiederkehr und Familienbegegnung nativ prüfen. |
| Familienausflug | Menüwahl am Tresen. Ein Gruppenmitglied kann die gesamte Gruppe bezahlen; sonst vorher Bank. Unbeobachtete Besuche grob fortschreiben, benachbarte Plätze erst bei Beobachtung oder Inn-Eintritt zuweisen. | family6 und family-bank14 belegen Sitze, Gruppenzahlung und tatsächlichen Bankgang mit genau einem Zahler. Sichtbarer gemeinsamer Außenheimweg nach dem beobachteten Inn bleibt offen; Bank-/Hinwegteil nicht wiederholen. |
| Kinder und Türen | Stabile Tageswahl ungefähr50/50: ein Elternteil bleibt beim Kind oder beide gehen aus und schließen das Kind ein. Erwachsener zu Hause bedeutet tagsüber offen; Nacht oder alle schlafen bedeutet abgeschlossen. | Beide tatsächlichen Varianten bestehen in family6 und childcare10b, einschließlich realer Heimkörper und nativer Türzustände. Keine zusätzliche vollständige Tages- oder Städtematrix als Voraussetzung. |
| Routinen | Nächstes sinnvolles Ziel inkrementell bestimmen. Keine Mindestzahl von Besorgungen oder Mindestabwesenheit. Nach Unterbrechung gewöhnliche optionale Erledigungen überspringen; feste Zusagen und Familienaktivität bewahren. Späte körperliche Heimkehr und Schlaf auch über Mitternacht zulassen. | 7 neue und120 verknüpfte Fälle bestanden; freie Tagesabläufe und sichtbare Rückkehr offen. |
| Geld und Käufe | Begleiterlohn direkt pro begonnenem24-Stunden-Block; Rückabwicklung nur bei fehlgeschlagenem Start. NPC-Shopkäufe vermindern nativen Regalbestand nicht, bewegen aber echtes Geld und erzeugen echte eigene Items. Kein künstlicher Gilden-Tagesbestand. Gewerbliche NPC-Marktkäufe erhalten das Angebot ebenfalls; privater Handel überträgt tatsächlichen Besitz. | NPC-Regalkauf/Übergabe sowie gewerblicher Marktkauf bestehen, zuletzt market-visit17 mit6Gold/neuer eigener UID/stockDelta0. Der echte Begleiter-Erstlohn10Gold/24h ist in dungeon-known14:1011 belegt; paid-xp15:1012 bestätigt tatsächliche Zahlung und einen natürlichen eigenen Heilzauber mit nativer Praxis. Späterer Fehler eines Sammellaufs widerruft diese Teilbelege nicht. |
| Wachen | Sicht auf eine laufende Spielerverfolgung genügt zum Mitmachen. Ein tatsächlicher „Halt!“-Ruf alarmiert ohne Sichtverbindung bis200m von der rufenden Wache. Tatortsuche nutzt verschiedene erreichbare Ziele und physische Befragung sichtbarer Personen. Bei Zeugenmeldung stehen NPC und Wache2–5echte Sekunden einander zugewandt still, dann zieht die Wache los. Wachbetten erst bei geladenen Quartieren bestimmen; keine verspätete Ablösung durch Bettzuweisung. | Sichtbeitritt/200-m-Halt und zwei NPC-Mordfälle bleiben bestanden. complaint16 bestätigt Gesprächspause und körperlich fortgesetzte Meldung; guard-shift-change15 bestätigt echte Tages-/Nachtablösung. Die ausdrücklich offenen Haft- und übrigen Teilfälle bleiben separat. |
| Speichern | Nur aktuelles Schema85; alte Entwicklungssaves vor Zustandsänderung ablehnen. Keine Migration und keine Rettung alter Teststände. Aktuelle Identitäten, Erinnerungen und Zusagen bleiben speicherbar. | Schema5/5 und gesamte Core-Suite2149/2149 bestanden; RuntimeCompile0 Warnungen/Fehler. Frische Moddaten für neue Spielabnahme. |

<a id="game-acceptance"></a>
### Spielabnahme vom 27. Oktober

Die VRAM-Testpause ist durch „You can resume“ ausdrücklich aufgehoben.
Die gemeinsame Paketfolge reicht inzwischen mindestens bis zum installierten
`package19` / `267057D1C714`; der jeweils aktuelle Installationsstand steht oben.
Die letzte vollständige gemeinsame Core-Prüfung bleibt2149/2149;
spätere Runtimecompiles und Spielbelege sind gezielt ausgewiesen. Die folgenden ursprünglichen nativen
Einzelbelege stammen aus der vorherigen Runde mit Paket `9227BBCCD619`;
sie sind keine vollständige Wiederholungsabnahme aller neuen Änderungen.
Nachgewiesen: Wachbeitritt auf Sicht, echter Halt-Ruf auf 200 m einschließlich
verdeckter Wachen, vollständiger NPC-Regalkauf mit Übergabe sowie Nachtruhe ohne
Schlafgrafik. Zwei native NPC-Mordfälle bestehen Zeugenmeldung, Leichenbeseitigung,
Täterbehandlung und Rückkehr der Wache. Die neue Gesprächspause vor dem Abmarsch
besteht nativ mit 4,64 Sekunden, Stillstand und gegenseitiger Blickrichtung.
Alle 19 Daggerfall-Inns bestehen erneut die Toranbindung; vier körperliche
Querungen und geschlossene Tore sind geprüft. Hakon trägt seinen erhaltenen Gesamtwert
jetzt als 1.000 Münzen plus 99.000 im nativen Kreditbrief; Überladung blockiert
die Übergabe nicht mehr. Der fehlende gewöhnliche Bettplatz ohne Sprite ist behoben.

Gemma zerlegt und bindet den freien Fünf-Schritt-Auftrag korrekt; der NPC lehnte
im damaligen Lauf nachvollziehbar ab. Vollständige angenommene Ausführung bleibt
offen. Beide Betreuungsvarianten sind durch family6/childcare10b und der tatsächliche
Wachwechsel durch guard-shift-change15 inzwischen abgenommen. Sichtbarer gemeinsamer
Familienheimweg, natürliche Reise und ausdrücklich offene Tatort-/Haftfälle bleiben
bei ihren eigenen konkreten Abschlusskriterien.
[Einzelergebnisse, Fehlerursachen und offene Abnahmen](audits/2026-10-27-game-acceptance.md).

Die bestehende [Mindmap](NPC-MINDMAP.html) bildet diese Regeln unter ihren stabilen
LIFE-IDs ab. [Umsetzung, Belege und Grenzen](audits/2026-10-26-immersion-simplification-results.md).

### Daneben: aktuelle Betten- und Ankunftsarbeit

Aktuelle Vereinfachung: Inn-Kapazität besteht ausschließlich aus den tatsächlich
vorhandenen Betten. Jedes Bett hat eine stabile ID und genau eine Belegung oder
Reservierung; Familien reservieren ein Bett je Person. Native Mietmarker und
dauerhafte Bewohner werden direkt konkreten Betten zugeordnet. Bodenbereiche,
Türzustände oder Familienzugehörigkeit fassen keine Betten zusammen. Die Core-Suite
bestand damals einschließlich der Catch-up-Regel mit2147/2147 Schema-Checks, später2149/2149; RuntimeCompile mit0
Warnungen/Fehlern. Native Bettinventare bestehen5 Gothway- und19 Daggerfall-City-
Inns; Inn263174 besteht Aufnahme, Türdurchgang und Innenmaterialisierung. Nach
Laden, Loitern oder Zeitsprung wird nach der geplanten Ankunft kein Außenkörper
mehr am Türpunkt erzeugt, auch wenn die Ankunft zuvor sichtbar begonnen hatte.
Mit Bett erscheint der Reisende beim Laden des Inns direkt innen. Nur eine ohne
Catch-up durchgehend sichtbare Ankunft beendet ihren körperlichen Türweg. Die aktualisierte native Assertion benötigt noch einen frischen
Paketlauf. [Diagnose und Grenzen](audits/2026-10-26-inn-pending-door-crowd.md).

Weitere aktive Restbereiche: natürliche Inn-/Ausweichankunft, ausdrückliche
Haft-/Mauerteilfälle, stationäre Marktpersonen über die ganze Nacht, Lysandus-Zielwahl,
Dungeon-Navigation und die neu beauftragte Begleiter-Schnellreise gemäß LIFE-68.
Tempeltraining21, Reparatur21, Shield14, endlicher Gedächtniskern und Wachwechsel15
sind bereits belegt und werden daraus nicht erneut
zu Pflichtprüfungen. Die jeweiligen eigenen Belege bleiben maßgeblich; Builds und
native Tests laufen weiter nacheinander.

Historischer Übergabestand vor der aktuellen Spielabnahme: Commit `042cec7`, Bundle `D3CA68C3D919`; Core-Basis2162/2162
und Unity-Paketprüfung bestehen. Stuhlidentität und Tresenzahlung sind korrigiert;
zwei Inn-Layouts bestehen Bezahlung, Sitzen und Kauferinnerungen. Kontrollierter
Familienlauf15 besteht auch den Ausgang und Heimweg; Ausgang im ursprünglichen
Inn131841 bleibt offen. Reisezeitbudget, Reparaturverwahrung und benannte
NPC-Übergabeerinnerungen sind installiert. Der native Banktest besteht mit erhaltenem
Budget tatsächlichen Eintritt und anschließendes Warten bei Spieler draußen;
kontrollierte beobachtete/unbeobachtete Wach-Türdurchgänge bestehen.22 native
Diebstahls-Erinnerungsfälle bestehen. Dungeon-Follow-Quelle ist enthalten;
die damaligen nativen Läufe scheiterten vor Eintritt an fehlenden Reisenden-Routen.
Dieser damalige Blocker ist durch spätere Torprüfungen für19 Inns überholt;
das belegt noch keine vollständige Dungeon-Follow-Abnahme.
Wissensgebundene Familientrauer, Begleiter-Fertigkeitsfortschritt und Tageslohn
von10g pro tatsächlichem NPC-Level waren damals integriert und CPU-geprüft.
Inzwischen sind wissensgebundene Trauer durch grief9, tatsächlicher Begleiterlohn
durch dungeon-known14 und natürliche eigene Zauberpraxis durch paid-xp15:1012 belegt;
das nimmt keine ungetestete Dungeon-/Rückkehrkette vorweg. Die damalige Testpause ist beendet: Routinen,
Reisendenwege und Wachorganisation sind erneut beauftragt. Spätere Diagnosepakete
sind kein neuer vollständig abgenommener Übergabestand. Die oben dokumentierte Spielabnahme vom 27. Oktober enthält dagegen einen neuen Build und dessen Installation.

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

Aktuelle Modelle: genau ein LLM (Gemma) und ein TTS-Modell (Breeze). Zielzerlegung, Two Pass, Parameterklärung, Zustimmung und Antworten verwenden dasselbe LLM. D3 und Decision Shadow sind entfernt; dies entfernt keine mehrstufigen Aufträge. Keine zusätzliche Modellfamilie, Bildinterpretation aus NPC-Augen oder Backend-/Gerätekombinationsmatrix als Core-Voraussetzung. Spracheingabe bleibt zurückgestellt.

Kein Regex im eigenen DFU-Projekt, einschließlich Werkzeugen und Tests. Gemma interpretiert Sprache; DFU prüft reale Ziele und führt Aktionen aus. Keine Ersatz-Wortmuster mit `Contains`. Der bestehende Mischbetrieb aus Modellentscheidungen und Regex-Altwegen wird anhand [des geprüften Ersatzkonzepts](NO-REGEX-DIALOGUE.md) bereinigt. Noch offen; zuerst aktive Routing-/Ortsauskunft-Overrides, danach Aktionen und übrige Altwege.

## Spielbarer Stand — bisherige Nachweise, am27. Oktober ergänzt

Der aktuelle Entwicklungsbuild ist im lokalen Player installiert. Start und kurze Tests: [PLAYTEST.md](PLAYTEST.md). Exakter Paketstand und Nachweise: [TESTING.md](TESTING.md). „Geprüft“ bezeichnet jeweils den genannten Ablauf, keine vollständige Abnahme aller Orte und Formulierungen.

| Bereich | Geprüfter Stand | Noch offen |
| --- | --- | --- |
| Alltag und Familie | Mehrere Tagesziele, reale Partner-/Kinderkörper, Inn-Sitze, Gruppenzahlung, Bankgang und beide Kinderbetreuungsvarianten bestehen (family6, childcare10b, family-bank14). | Gemeinsamer Außenheimweg am28.Oktober nativ abgeschlossen:252,41m pro Elternteil, keine neue Zahlung. Alltagskern fertig; bestandene Bank-/Hinweg-/Betreuungsfälle nicht wiederholen. Weitere Städte und Gebäude bleiben eigene Beobachtungen. |
| Navigation und Sitzen | Vorgerechnete zwölf Gothway-RMB-Varianten, normale Laufwege, Terrasse/Treppe/Sitzplatz und Aufstehen; 40cm Wegpunkttoleranz und Paar-Kollisionsausnahme. | Weltweite Blockvarianten, weitere Innenräume und Dungeons; Springen, Klettern und bewegliche Plattformen. |
| Gespräch und Ortsauskunft | Tatsächlicher Aufenthalts-/Wegkontext; Persönlichkeit und Beziehung beeinflussen Reaktionen; bekannte native Orte nennen, markieren und hinführen. | Breite freie Formulierungen, zusätzliche Questadapter und zuverlässige lange Aufträge. |
| NPC-Aktionen | Follow/Wait/Stop, Hinführen, echte Itemübergaben und Regalkauf mit Übergabe sind belegt. Enter→gebundenerWait und reale Innencheckpoints bestehen in den Tempel-/Reparaturfolgen18. | Vollständige angenommene Fünf-Schritt-Kette, weitere beauftragte Dienste und die eigenen konkreten Begleiterkriterien bleiben offen. |
| Gesundheit | Native Tränke/Selbstzauber, Schlafheilung und Gegengift bleiben belegt. Shield14, natürliche eigene Heilzauberpraxis15 und tatsächliches Tempeltraining21 mit400Gold/Skill9→10/Praxis0→255 bestehen. | Ausdrücklich offene weitere Pflege-/Kampf-/Dungeonfälle; keine erneute Shield- oder Trainings-Grundprüfung. |
| Wachen | Sichtbeitritt/Halt200m, Tagesbesetzung184/92, Patrouillen, tatsächlicher Tages-/Nachtwechsel15 und körperlich fortgesetzte Meldung16 sind belegt. wall-law13 und wall-npc14 bestätigen die benannten Tages-Mauerreaktionen. | Tatsächlich offene Nacht-/Haft-/Zellenfälle und übrige ausdrücklich benannte Täterfälle. Bestandene Besetzungs-/Schicht-/Meldebelege gelten weiter. |
| Erinnerung und Beziehung | Endlicher Kern abgeschlossen: biography8 belegt tatsächliche Innenraumerinnerung und späteren Abruf nach Diskload; grief9 ergänzt direkte Todeswahrnehmung, korrekte Schuld und genau eine Erinnerung. Gesprächshistorie/Wiederbegrüßung und unwichtiger Ablauf sind gesondert belegt. | Zusätzliche Erlebnisse, langfristige semantische Verdichtung und allgemeine freie Dialogqualität bleiben eigene Erweiterungen; kein neuer Pflichtlauf für den fertigen Kern. |
| Persönlicher Handel | Mobile Vorschau/Abbruch/Kauf/Verkauf/Leihe und statischer persönlicher Handel bleiben belegt. Markt-UI8, Laden-Abbruch9b, tatsächlicher Marktbesuch17 und die ganze Reparaturfolge21 bestehen. | Der endliche Besitz-/Handelskern ist abgeschlossen. Weitere Rollen, zusätzliche Tauschvarianten und freie Formulierungen bleiben eigene Detailprüfungen; LIFE-68 ist ein neu beauftragter Reiseablauf. |
| Besitz und Begleiter | Native persönliche Gegenstände, Geld und Heimschlüssel sind integriert und teilweise direkt nativ belegt. Tatsächliche10Gold-Begleiterzahlung, natürliche eigene Zauberpraxis sowie Reparatur/Training21 bestehen. | Bezahlte Dungeon-/Rückkehr-/Beutekette und neu vereinbarte Mitnahme/Schnellreisekosten behalten ihre jeweils eigenen offenen Kriterien. |

Handel setzt Bereitschaft voraus; Mercantile beeinflusst Preise und Gegenangebote, ist keine Zugangssperre. Das NPC-Geld bleibt internes Kaufbudget. Schlechte eingereichte Angebote verschlechtern die Stimmung, vorteilhafte Abschlüsse verbessern sie. Leihen verlangt bekanntes Vertrauen ab75; die Rückgabe eines belegten Gegenstands verlangt kein erneutes Vertrauen. Schlüssel, ausgerüstete und geschützte Gegenstände bleiben unverkäuflich.

Das vereinfachte Handelsfenster zeigt NPC-Items links und Spieler-Items rechts, nur OFFER/CLEAR/EXIT sowie helle Felder für NPC-Preis und eigenes Angebot. Beide Warenrichtungen werden zu einem Goldsaldo verrechnet. Native DFU-Preise mit Mercantile/Personality bleiben die Basis; NPC-Persönlichkeit und Disposition sowie bekanntes Vertrauen und aktuelle Stimmung beeinflussen die Konditionen. Vertrauen/Stimmung wirken gleitend, erst Goldbeträge werden gerundet. Streetwise/Etiquette beeinflussen keine Handelspreise. Die Preisgrenze bleibt pro unverändertem Warenkorb fest; Stimmung und Vertrauen beeinflussen weitere Gegenangebote. Neue Hintergrundsysteme sind dafür nicht erforderlich.

<a id="delivery-plan"></a>
## Nächste Umsetzungsschritte

**Aktuelle Priorität vom26. Oktober:** Zuerst den integrierten Schema85-Stand mit frischen Moddaten serialisiert bauen und die oben benannten sichtbaren Änderungen prüfen. Danach die folgenden offenen Abläufe fortsetzen; bereits integrierte Vereinfachungen nicht erneut als neue Systeme planen. Die Einzelcheckliste bleibt Quelle für offene Detailprüfungen.

1. **Morgenroutinen:** Zielwahl, tatsächliche Wegdauer, Abfahrt und native Öffnungszeiten gemeinsam prüfen. Frühe Ankunft trotz auf Öffnung begrenztem Termin ist bereits im Umverteilungscode korrigiert und über500 Seeds geprüft; frische Pläne und tatsächliche Ankünfte bleiben zu messen. Die gespeicherte Stichprobe umfasst1.074 Besuche von286 Bewohnern: ein Kleiderladen erhält30 Besuche, davon12 in der Stunde ab10 Uhr; acht Gemischtwarenläden zusammen216. Das beweist keine allgemeine Kleiderladen-Präferenz. Auch Tempel172 und Abend-Inn-Spitze untersuchen. Allein untätige Wartende sollen innerhalb einer Spielminute nach Öffnung zum Eintritt aufbrechen. Längere Streuung bis45 Minuten nur bei tatsächlicher Zwischenaktivität. Bei geschlossenem Laden ein bereits geplantes offenes Ziel vorziehen oder sinnvoll unterhalten/umhergehen; Anlass, Partner, Tätigkeit und Rückkehr in Kontext/Erinnerung aufnehmen. Vollständigen Türdurchgang prüfen, nicht nur Bewegungsfreigabe. [Verteilung und Grenzen](audits/2026-10-26-routine-destination-distribution.md).
2. **Reisende und Tore:** Die frühere Diagnose mit129 leeren Routen ist überholt:19 getestete Inns, acht Wachquerungen, vier körperliche Torquerungen sowie Schließung und Wiederöffnung bestehen auf den dokumentierten Prüfständen. [Torbelege](audits/2026-10-26-traveler-native-gates.md). Noch prüfen: natürliche Reiseaufnahme, vollständiger Hin-/Rückweg, neue Herkunftshaushalte und volle Übernachtung ohne Wachregression. Keine künstliche Reisendenidentität oder erfundene Passage als Erfolgsnachweis; Torprüfung allein ist keine Dungeon-Follow-Abnahme.
3. **Wachbestand und zwei Schichten — vorhandene Belege erhalten:** Daggerfall erreicht nativ184Identitäten/92Tagesdienst,32Stadt-/16Tor-/30Mauerposten und sieben Zweierpatrouillen. guard-shift-change15:1113 bestätigt tatsächliche unterschiedliche Ersatzkörper, Nachtfackel, geschlossene native Nachttore und Rückkehr zum Tagdienst; guard-sleep15 bestätigt die einzelne Schlafdarstellung. Aufgeschobene Bettzuweisung ist integriert. Keine erneute vollständige Garnisonsbereitschaft als Voraussetzung anderer Tests. Noch nicht belegte Mauer-/Haft-/Alarmteilfälle bleiben bei ihren eigenen Kriterien; weitere Ortstypen sind keine Wiederholung des bestandenen Schichtwechsels.

### Danach: bestehende Abnahme fortsetzen

1. **Diesen Build im normalen Spiel prüfen:** Bewegung, Tagesleben, Gespräche, Ortsauskunft und persönlicher Handel. Gemeldete Fehler haben Vorrang.
2. **Abgeschlossenen Handel erhalten:** Markt-UI/Käufe8, Laden-Abbruch9b, Marktbesuch17 und Reparaturabholung21 nicht wiederholen. Weitere Rollen, Tauschvarianten und freie Formulierungen sind gesonderte Detailprüfungen; Schnellreisekosten gehören zum neuen LIFE-68.
3. **Vorhandene Aktionsadapter fertig prüfen:** konkrete bezahlte Ortswechsel, neue Begleiter-Schnellreise und ausdrücklich offene Übergaben. Enter→Wait18, Shield14 sowie vollständiges Training/Reparatur21 behalten ihre bestandenen Belege.
4. **Gesundheit und Wachreaktionen abrunden:** freie Täterreaktionen und Verfolgung durch beobachtete Türen; Festnahme erst nach Nachweis nutzbarer Gefängnisgeometrie.
5. **Navigation erweitern:** zusätzliche Innen-/Dungeonblöcke, Springen, Klettern und die Thronplattform in Privateer's Hold.
6. **Spätere Erinnerungserweiterungen:** weitere tatsächliche Erlebnisse und Zusagen im Gespräch; keine zusätzliche Hintergrundsimulation und keine Wiederöffnung des bereits fertigen endlichen Gedächtnis-/Beziehungskerns.

Integration, gemeinsame Dateien und serialisierte Unity-/Player-Tests bleiben
koordiniert. Die beauftragten Audit-Vereinfachungen sind im Quellstand integriert;
vorherige Pakete behalten ihre offenen Spielprüfungen. Modell-/Agentenfreigaben
aus einzelnen Aufträgen sind keine zusätzliche Funktionsanforderung.
Keine weiteren Debug-Kamera- oder Nebensysteme.

<a id="market-prototype"></a>
### Neu beauftragt: vollständige stationäre Personen und lebendiger Markt — LIFE-63–66

Dieser Auftrag vom26. Oktober ist aktive Umsetzung. Er ersetzt die frühere
Zurückstellung der Marktrollen. Der Markt ist der erste vollständige Anwendungsfall;
danach gilt dasselbe Personenmodell auch für stationäre Menschen in Shops, Gilden,
Tempeln, Inns und auf Straßen. Stationär beschreibt den Arbeitsplatz und die
Darstellung, nicht geringere Komplexität. Einzelne kompilierte Adapter sind keine
Abnahme des vollständigen Ablaufs.

- **LIFE-63 — Gemeinsame Personeninternas.** Dieselbe stabile Person verwendet
  Haushalts-, Werte-, Inventar-, Wahrnehmungs-, Gefahren-, Ereignis-, Erinnerungs-
  und Gesprächssysteme wie mobile NPCs. Echte Haushaltsmitgliedschaft, derselbe
  Haushaltsnachname und ein eigenes belegbares Bett in einem tatsächlich bekannten
  Haus sind erforderlich. Kleidung entspricht dem getragenen Outfit; Hausschlüssel,
  Geld, erwartbare persönliche Items, Level, Stats und HP/MP sind vorhanden.
  Zugehörigkeit erzeugt keine unbelegten Ehe-/Elternbeziehungen. Keine native und
  mobile Doppelperson oder positionsabhängig neu gewürfelte Identität.
- **LIFE-64 — Marktpersonal und Pendeln.** Rollen und Warentypen werden aus realem
  Marktstand, Tisch, Props und überprüften Szenenbildern bestimmt. Händler haben
  zahlreiche passende tatsächliche Waren im Inventar, etwa erwartbare Produkte
  eines belegten Fleisch-/Viehstands. Tagsüber bleiben sie am eigenen Posten.
  Feierabend variiert zwischen17:45 und18:15: ohne Kundschaft früher, bei echten
  Warenbetrachtern oder Käufern länger. Bloße Passanten zählen nicht als Kunden;
  laufende Gespräche und Transaktionen dürfen nicht abgebrochen werden. Danach
  folgt ein tatsächlicher Heimweg und Schlaf im zugewiesenen Bett. Morgens kehren
  sie mit Variation zwischen06:00 und06:30 zum Posten zurück. Bis Laufanimationen
  vorhanden sind, darf derselbe statische Sprite gleiten. Bei Angriff oder
  beobachtetem Mord gelten dieselben Flucht- und Wahrnehmungsregeln wie bei mobilen
  Personen. Identität, Rolle, aktuelle Tätigkeit, Kunden, Feierabendgrund, Zuhause,
  Bett und tatsächliche Ereignisse stehen im gemeinsamen Gesprächskontext und
  zeitlich begrenzten bewerteten Gedächtnis.
- **LIFE-65 — Besucher, Waren und Bühne.** Tatsächlich erreichbare Betrachtungsplätze
  vor Warentischen und mehrere Zuschauerplätze vor der Bühne werden aus nativer
  Geometrie bestimmt. Besucher wechseln mit variiertem Aufenthalt zum nächsten
  Tisch und schauen auf Waren oder Aufführung. Belegte Plätze werden berücksichtigt;
  bei voller Belegung entsteht eine verteilte Zuschauermenge. Kein Körperhaufen
  und kein pauschales Drehen zum Spieler. Bei einer Kaufentscheidung stellt sich
  der Käufer gegenüber dem tatsächlichen Verkäufer auf, zahlt mit eigenem Gold
  und behält das tatsächlich übertragene Item bis zur Heimkehr. Standbesuch,
  Bühnenaufenthalt, Kauf, Abbruch und Gefahr werden passende Ereignisse.
- **LIFE-66 — Native Verkäufer-Menüs.** Marktverkäufer erhalten Kaufen/Reden und
  ein natives DFU-Shopfenster mit ihren tatsächlichen Waren. Ein semantischer
  Handelswunsch im Gespräch mit belegten Shopkeepern oder tatsächlich verkaufenden
  Gilden-NPCs öffnet deren native Handelsoberfläche statt des persönlichen
  NPC-Handels. Reine Trainings-/Dienstrollen sind kein Verkäuferbeleg.
  Privatpersonen behalten den persönlichen Handel. Getragene Kleidung, Schlüssel
  und geschützte Items sind keine verkäuflichen Waren. Native Stock-/Zahlungswege
  bleiben maßgeblich; Markttransfers verwenden echte UIDs, Bestände und Geldbeutel.

**Abnahme bleibt offen:** reale Markt-/Bühnengeometrie und Rollen belegen; mehrere
gleichzeitige Besucher, Warenblick und Zuschauerbelegung prüfen; vollständige
Arbeits-/Heim-/Schlaf-/Rückkehrfolge zweier Personen mit verschiedenen Zeiten;
Kundschaft und laufenden Handel an der Schließgrenze; tatsächlichen Angriff und
Mordbeobachtung mit Flucht; Inventar-/Gold-/Schlüssel-/Bettrechte und Namen über
Zeitsprung, Szenenwechsel und Save/Load erhalten. Markt, Shopkeeper und tatsächliche
Gildenverkaufsrolle müssen native Menüs/Handel zeigen, eine Privatperson weiterhin
persönlichen Handel. Quelle, Kompilierung, gezielte Adaptertests und native
Spielabnahme werden getrennt dokumentiert. Die [Mindmap](NPC-MINDMAP.html) enthält
dieselben stabilen IDs und offenen Korrekturziele.

<a id="detailed-design"></a>
## Verbindlicher Funktionsumfang

### Fortschritt seit dem vorherigen Mindmap-Stand

Die folgenden Belege stammen aus abgeschlossenen kontrollierten Läufen beziehungsweise Quellprüfungen. Sie ersetzen keine allgemeine Spielabnahme und keine Prüfung des gerade laufenden Entwicklungsstands.

| Bereich | Erreicht und belegt | Noch offen |
| --- | --- | --- |
| Handel und Preise | Statischer persönlicher Handel repariert: Abbruch, Teilstapelkauf und Rückverkauf mit exakten UIDs/Wallets nativ bestanden. Gültiger Ausdauertrank: Basis25g, im Test Kauf33g/Verkauf18g; Nullwert-Pergament in beiden Richtungen bestanden. Soziale Preisumkehr im Code durch begrenzten Rückkaufpreis korrigiert. | Gemischter Tausch, weitere Rollen, freie Formulierungen, neuer Preisumkehrfix nativ; offene UI-, Kleidung-, Portrait- und Wiederbegrüßungsprüfungen bleiben bestehen. |
| Erinnerung und Wahrnehmung | Bewertung−5 bis+5, Beziehungseinfluss, gewichteter Zufall bei Erinnerungslänge und gemeinsame mobile/stationäre Erinnerungspfade integriert. Kontrollierte Sicht-, Schlaf-, Unsichtbarkeits- und Wiederbindungsprüfungen korrigieren falsches Zeugenwissen.22 native Diebstahlsfälle bestanden. | Nicht jeder Ereignisproduzent ist vollständig im Spiel geprüft. Semantische Langzeitzusammenfassungen bleiben offen; zufällige Aufbewahrung ersetzt sie nicht. |
| Begegnungen und Warten | Markt-NPC-Begegnung mit Partnerblick, beidseitigem Kontext/Erinnerung und anschließendem Weiterlauf bestanden. Dreiergruppe vor geschlossenem Laden erreicht Plätze, hält Abstand und steht ohne Laufanimation. | Natürliche Häufigkeit, alle Unterbrechungen, vollständiger Inn-Umweg und Ladeneintritt. |
| Bank und Familien-Inn | Echter Angestelltenweg,13,81s Aufenthalt, einmalige Auszahlung und Heimweg geprüft. Ein vollständiger kontrollierter Familienablauf besteht morgendliche Bank, Paarweg, beide Käufe, Sitzen, Erinnerungen, Ausgang und Heimkehr. | Ausgang im ursprünglichen Inn131841, weitere Layouts und ununterbrochener freier Tagesablauf. |
| Wachen und Meldungen | Individuell beobachtete Türverfolgung und unbeobachtete Flucht ohne Innenraumwissen geprüft. Anonymer Mordbericht durch reales Gespräch, Zustimmung und körperliche Übermittlung bestanden. Dringliche Fluchtgeschwindigkeit und Stillstand bei verzögerter Meldungsannahme korrigiert. | Freie Täterentscheidungen, weitere Innenräume, reale Zellen/Festnahme und neue Wachschichten. |
| Aufträge und Begleiter | Bank/Service-Enter→Wait, Reparaturabgabe/Disk/Abholung21 und Tempeltraining21 sind nativ belegt. Tatsächlicher Begleiterlohn14, natürliche eigene Zauberpraxis15, Shield14 und wissensgebundene Trauer9 behalten ihre jeweiligen Belege. | Vollständige angenommene Auftrags-/Dungeon-Follow-/Rückkehr-/Beutekette und neue Begleiter-Schnellreise bleiben offen; bestandene Lohn-/Training-/Reparatur-/Trauerteile nicht wiederholen. Die frühere leere Torroute ist für19 Inns behoben. |
| Performance | Kontrollierter Vergleich: maximale Inventarinitialisierung1.208,32 auf29,37ms; Frame-p95 nach3h Loitern1.138,88 auf49,74ms. Separater Wohnungs-Startup3.097,35 auf8,92ms mit gleicher Ausgabe für408 Häuser. | Weitere Zeit-Skip-Spitzen um1,6s und gemeldete4–6s-Playerhänger offen. Kein Nachweis einer allgemeinen Behebung und kein begründeter GPU-Umbau. |

Belege: [Handel](audits/2026-10-26-static-personal-trade.md), [Begegnungen](audits/2026-10-26-stationary-street-encounters.md), [Bank](audits/2026-10-26-bank-cash-visit.md), [Familie](audits/2026-10-26-family-cash-visit.md), [Wachverfolgung](audits/2026-10-26-guard-witnessed-door-pursuit.md), [Mordmeldung](audits/2026-10-26-murder-report-dialogue.md), [Performance](audits/2026-10-26-loiter-performance.md), [Einzelcheckliste](WORKLIST-2026-10-26.md), [Paket-/Teststände](TESTING.md).

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
| Inn | Nur Bettenkapazität, keine Zimmerlogik. Menü am Tresen; ein Zahler für die Gruppe, nötigenfalls vorher Bank. Benachbarte Plätze erst bei Beobachtung. Kein Wirtswarenbestand als Voraussetzung. | LIFE-17,21,26,36 |
| Einkommen | Wenn benötigt, einfacher rollenabhängiger Tageslohn bei tatsächlicher Beschäftigung. Keine Abrechnung jedes Arbeitsschritts, Arbeitgeberkonten oder Hintergrund-Lieferketten. | LIFE-26 |
| Erinnerung | Vorhandener aktueller Kontext, Ereignisse, Begleiterwissen und technische Historienaufbewahrung bleiben erhalten. Erst bei gemessenen Performanceproblemen optimieren; keine zusätzliche Archiv-/Altsave-Plattform. | LIFE-39–41 |
| Weltzustand | Geladene Akteure bewegen; bekannte Reisende, Familienausflüge und konkrete Zusagen bei Zeitablauf fortschreiben. Unbesuchte Orte aus Seed und bereits bekannten Fakten erzeugen. | LIFE-43,49,51,52 |
| Darstellung | Gemma-Text, Breeze-Stimme und Portrait aus demselben NPC-Zustand. Audio, wortweise Anzeige und Mundanimation zusammen starten und synchron bleiben. | LIFE-42 |

### Feste Rollenregeln

- Adel und belegte hohe Ränge werden gegenüber dem Spieler nicht unterwürfig oder verängstigt.
- Palastwachen und Soldaten kämpfen bis zum Tod. Stadtwachen kämpfen zu98% weiter; die übrigen2% dürfen bei schwerer Verletzung in erreichbare Stadtmauerquartiere fliehen.
- Unabhängige Rogues, Assassinen und ähnliche Kämpfer können bei schwerer Verletzung fliehen. Belegter Palast-/Adelsdienst hat Vorrang vor dem unabhängigen Standard.
- Diese Regeln ändern weder native Feindschaft noch Questschutz und verhindern keine Heilung.

## Später, nach funktionierenden Kernabläufen

- **Weitere stationäre Rollen nach dem Marktprototyp:** Die vollständige
  Personenanbindung und Marktrollen sind unter LIFE-63–66 bereits aktiv beauftragt.
  Danach dieselbe Anbindung auf Bedienungen innen/außen, weitere Läden, Gilden,
  Tempel, Straßenpersonen und Inns ausweiten. Mehrdeutige Rollen benötigen belegte
  Platzierung/Props oder gezielte Ortszuordnung; ein wiederverwendeter Sprite allein
  beweist keinen Beruf. Bestehende Quest- und Individualrollen erhalten.

- **Spracheingabe-Modelltest (STT):** Auf Nutzerwunsch vom25. Oktober2026 **Underdog Bark 3.0 Plus** als Kandidaten testen (korrigiert von Pro). Erwarteter Nutzen laut Nutzer: kleiner und ähnlich gut; noch nicht verifiziert. Zuerst genaue Modellidentität und tatsächliche STT-Eignung bestätigen, danach Speicherbedarf, CPU-/GPU-Latenz, Erkennungsqualität und Verhalten bei Stille/Hintergrundgeräuschen messen. Modellentscheidung und Integration bleiben offen; dieser Eintrag startet keine STT-Implementierung.
- **Begleiter:** Rekrutierung, Gruppenverwaltung, Mitreisen und Kampfhilfe. Einfache tageweise Bezahlung mit klaren Kündigungsregeln; keine minutengenaue Treuhand-/Erstattungsarchitektur. Bei Stadtaufenthalten wenige begründete Erledigungen und ein klarer Treffpunkt. | LIFE-19,24,25,47 |
- **Begleiter-Fortschritt:** Tatsächlich erfolgreiche berechtigte Skillnutzung ist integriert und regressionsgeprüft; native Abnahme bleibt offen. Gewöhnliche Einwohner behalten ihr erzeugtes Profil; kein laufendes Berufstraining der gesamten Bevölkerung und keine allgemeine Persönlichkeitsentwicklung. | LIFE-09 |
- **NPC-Gruppen und Expeditionen:** Sichtbare Gruppen, Reise und ein kompaktes Ergebnis bei Abwesenheit. Keine unbeobachtete Ausführung einzelner Dungeonkämpfe oder Containerleerungen. | LIFE-47,48 |
- **Weitere Orte und Berufe:** Weitere native Ortstypen schrittweise anbinden. Saisonale Tätigkeiten als Routine-/Darstellungswechsel; keine landwirtschaftliche Ressourcenproduktion. | LIFE-34,45,57 |
- **Weitere native Aktionen:** Enter/Exit im Inn und körperliche Routine-Rückkehr aus einem fremden öffentlichen Gebäude sind geprüft. Weitere Tür-/Questgeometrien und gemeinsame Familienbesuche weiter anbinden. Kampfunterstützung, Zauber, Diebstahl, Suche, Reparatur und Training jeweils als vollständiger kleiner Ablauf. | LIFE-20,23,30,32,38 |
- **Lokale Folgen:** Wissensgebundene Familientrauer ist integriert und regressionsgeprüft; native Abnahme bleibt offen. Geschlossener Laden, Nachfolger und weitere örtliche Folgen bleiben spätere Erweiterungen. Kein allgemeines Erb-/Schuldrecht und keine flächendeckende Ereigniserzeugung. | LIFE-31,37,54 |
- **Veröffentlichung:** Installation, verständliche Einstellungen und normales Übertragen von Spielständen erst für den Release bearbeiten; funktionierende bestehende Speicherwege nutzen. |

## Aus dem aktiven Plan gestrichen

- Autonomer Händler-zu-Händler-Handel, Preisrecherche, Lieferketten und allgemeines Schuldenmanagement. Frühere LIFE-27/28.
- Quantitative Tierhaltung, Futter-/Ertragsketten und Ersatzbetreuung als Wirtschaftssystem. Frühere LIFE-46; sichtbare Tätigkeiten bleiben unter LIFE-45.
- Eine zusätzliche weltweite Ereigniswarteschlange, rückwirkende Lebensgeschichte unbekannter Orte und regionale Produktions-/Wachstumsbudgets. Frühere LIFE-50/53/55.
- Eine weltweite Mehrjahresabnahme als Voraussetzung für normale lokale Funktionen. Frühere LIFE-56; gezielte Szenarien bleiben unter LIFE-44.
- Weiterer Ausbau von NPC-Augenbildern, Export-/Archivplattformen, Entwicklungs-Save-Migrationen, dauernder Alltags-Skillentwicklung und permanentem Trait-Umbau.
- Zimmervermessung/Familienzimmerbindung, vorgezogene unbeobachtete Sitzplanung, exakte Vorabmenüs, Mindestbesorgungsquoten, Begleiter-Escrow und künstliche Gilden-Tagesbestände sind entfernt. Ihre konkreten Ersatzregeln stehen im aktuellen Integrationsabschnitt.
- D3 und Decision Shadow sind entfernt. Goal Extraction, Two Pass und Zustimmung bleiben als Verarbeitungsschritte desselben LLM erhalten.

Die früheren IDs bleiben zur Nachverfolgung in historischen Dokumenten. Sie begründen keine Implementierungspflicht. Zusätzliche Mechaniken benötigen einen konkreten sichtbaren Nutzen.

## Prüfungen und Dokumentation

- Kompilieren und betroffene vorhandene Tests ausführen. Für sichtbare Änderungen einen passenden normalen Spielablauf prüfen.
- Stabile Identität, keine doppelten tatsächlichen Items/Zahlungen und funktionierende Rückkehr nach Unterbrechung bleiben nötig. Keine kombinatorische Absicherung hypothetischer Sonderfälle.
- Keine neue GPU-Arbeit außerhalb von Gemma/Breeze ohne konkrete Notwendigkeit und Koordination.
- Die Mindmap zeigt denselben reduzierten Umfang. Details zum aktuellen Nachweis stehen in [TESTING.md](TESTING.md).
- [Historische Roadmap vor der Vereinfachung](archive/ROADMAP-2026-10-24-before-simplification.md): Nachschlagequelle, kein zweiter aktiver Arbeitsplan.


### Gezielter Reststand nach den Belegen aus Paket21

Der aktuelle Abschlusszähler steht ausschließlich oben. Marktbesuch17, Beschwerde16, Reparatur21 und tatsächliches Tempeltraining21 sind inzwischen abgeschlossen; ein fehlgeschlagener späterer Runnerzweig widerruft keinen belegten Teilfall. Offen bleiben die ausdrücklich benannten aktuellen Dungeon-/Haft-/Ankunfts- und Auftragskriterien sowie die separat benannten Restfälle zu LIFE-68. Exakte Protokolle und Grenzen stehen im [Abnahmebericht](audits/2026-10-27-game-acceptance.md). Keine Wiederholung bestandener Handels-UI-, Bank-, Flucht-, Schlaf-, Reparatur- oder Trainingsprüfungen.
