# Logikaudit zu Immersion und unnötiger Komplexität

Stand: 26. Oktober 2026. Geprüft wurde der lokale Arbeitsstand auf Basis von `042cec7e50b1f30500b8c4e749e714e7e4888e94`, einschließlich vorhandener uncommitteter Änderungen. Gegenstand ist der Nutzen der Systeme und ihrer Voraussetzungen, nicht nur die technische Fehlerfreiheit.

Der Mod enthält mehrere Mechanismen, die unbeobachtete Detailtreue absichern und dafür sichtbare Abläufe von zusätzlichen Nachweisen abhängig machen. Die stärksten Vereinfachungskandidaten sind die Kopplung von Bettruhe an Grafik, die Vorbereitung fremder Reiseorte, die Vorausplanung von Familienkäufen und die genaue Simulation unbeobachteter Familienbesuche. Die Befunde unten unterscheiden vorhandenes Verhalten von der Bewertung seines Nutzens.

## Maßstab und Prüfgrenze

Eine Regel rechtfertigt sich durch eine erkennbare Wirkung: glaubwürdige Begegnungen, sichtbare Bewegung, nachvollziehbare Gespräche, tatsächlichen Besitz oder Folgen von Spielerhandlungen. Eine intern perfekte Weltbeschreibung ist allein kein Spielwert. Gelegentlich beobachtbare Details müssen auch nicht zwingend vorab für alle NPCs berechnet werden.

Die Prüfung umfasst die zentralen Steuerungs-, Planungs-, Zustands- und Adapterpfade der Mod-Systeme. Sie ist keine lückenlose Zeilenprüfung sämtlicher Dateien. Die Befunde sind am Quellcode belegt; ihr Immersionswert ist eine begründete Bewertung. Es wurden keine neuen Laufzeitmessungen, Modellaufrufe oder Spieltests durchgeführt. Empfehlungen sind weder umgesetzt noch als Nutzerentscheidungen übernommen. Aus Code allein lässt sich nicht bestimmen, welche frühere Anforderung von einer KI erfunden wurde.

## Starke Vereinfachungskandidaten

### 1 Heilung hängt vom Schlafbild ab

**Vorhanden:** `TryDiscoverHealthRestChoice` verwirft Bettruhe, wenn der Schlaf-Atlas, seine Textur oder seine Ansichten fehlen. Die Auswahl verlangt außerdem einen aktiven Körper im geladenen Innenraum. Die Ausführung bestätigt die Schlafpose mit höchstens 0,12 m Abstand zur Matratzenposition.

**Bewertung:** Ein fehlendes Bild ist kein nachvollziehbarer Grund, warum eine verletzte Person nicht ruhen kann. Sichtbar falsch liegende Körper müssen vermieden werden; eine medizinische Entscheidung braucht aber keine erfolgreich geladene Grafik.

**Einfacher:** Heilung an Ruhezeit, Gesundheitszustand, sicheren Ort und Unterbrechung koppeln. Schlafdarstellung separat wählen; außerhalb der Beobachtung reicht der gespeicherte Ruhebeginn. Das ist keine Aufforderung, im Kampf oder durch bloße Zeitüberschreitung zu heilen.

Quelle: `mod/Scripts/StreetPopulation.HealthRest.cs:150`, `:160`, `:174`.

### 2 Für Reisende werden unbekannte Herkunftsorte detailliert vorbereitet

**Vorhanden:** `PlanTravelNeighborhood` betrachtet bis zu 16 Herkunftskandidaten. Für verbundene Orte lädt es eine `UnloadedSettlementSurvey`; `ReserveDemand` liest dort Wohngebäude und Bettenprofile, reserviert echte Bewohner und bereitet weitere Reiserouten vor. `ResidentDiscovery` kann zusätzlich frühere Abfahrtstage eines bisher unbeobachteten Ortes rekonstruieren. Abgeschlossene Besuche oder Transaktionen werden dabei ausdrücklich nicht erfunden.

**Bewertung:** Ein sichtbarer Reisender braucht eine stabile Identität, plausible Herkunft und einen glaubwürdigen örtlichen Weg. Sein vorab vermessenes Herkunftshaus und dessen genauer Abfahrtsweg bieten bei unbekannten Personen wenig zusätzlichen Nutzen. Die vorhandenen Grenzen und Caches machen die Arbeit begrenzt, beseitigen aber diese sachliche Abhängigkeit nicht.

**Einfacher:** Unbekannte Reisende beim relevanten Zusammentreffen mit Herkunft und Reisezeit erzeugen. Exakte Fernkontinuität für bekannte Einwohner, Begleiter und Spieleraufträge erhalten. Herkunftsbezug und Wiedererkennung bleiben bestehen; unbekannte Häuser werden bei Bedarf konkretisiert.

Quelle: `mod/Scripts/AutomaticResidentTravel.cs:104`, `:128`, `:149`, `:160`; `mod/Scripts/UnloadedPopulation.cs:14`; `src/PeopleOfTheIliacBay.Core/ResidentDiscovery.cs:6`.

### 3 Ein Familienausflug braucht vorab beide Menüs und einen Finanzierungsplan

**Vorhanden:** `PrepareSharedInnBudget` verlangt einen Inn-Katalog, berechnet elf Menüpreise für den zukünftigen Besuch, wählt beide Speisen/Getränke und einen Zahler. Ohne Vorbereitung überspringt der Familienproduzent diesen Ausflugskandidaten. Die Geldbeschaffung prüft anschließend Banköffnung, Hinweg, Heimweg und Rückkehr vor der gemeinsamen Abfahrt.

**Bewertung:** Gemeinsam ausgehen und tatsächlich zahlen sind sichtbar. Bereits zu Hause zu wissen, welches konkrete Menü jeder später kauft, ist dafür unnötig. Die konkrete Vorauswahl koppelt Freizeit an Menü-, Kalender-, Inventar- und Banklogik.

**Einfacher:** Einen kleinen allgemeinen Ausgabenrahmen verwenden; Menü und tatsächlichen Zahler am Tresen bestimmen. Bankbesuche können als sichtbare Tätigkeit bleiben, ohne jeden normalen Ausflug von einer exakten Menüfinanzierung abhängig zu machen. Tatsächliche Zahlungen bleiben im selben Geldbeutel verbucht.

Quelle: `mod/Scripts/StreetPopulation.PlannedInnCash.cs:12`, `:52`; `mod/Scripts/StreetPopulation.HouseholdProducer.cs:85`; `mod/Scripts/StreetPopulation.CashVisits.cs:63`.

### 4 Unbeobachtete Familienbesuche simulieren zwei genaue Körper und Sitzplätze

**Vorhanden:** `HouseholdJointSimulation` speichert für beide Partner Wegsegment, Distanz und Position sowie mehrere Phasen und Übergaben. Der Inn-Katalog berechnet Wege vom Tresen zu Sitzen und zurück zum Eingang und bildet bis zu 32 passende Sitzpaare. Auch projizierte Besuche speichern genaue Sitz-IDs, Katalog, Tagesplan, Tarif und einzelne Kaufbelege.

**Bewertung:** Gemeinsam laufen und an einem Tisch sitzen haben hohen sichtbaren Wert. Dieselbe Genauigkeit in einem vom Spieler nicht geladenen Inn liefert wenig zusätzlichen Nutzen und erzeugt eigene Rekonstruktions- und Reservierungszustände.

**Einfacher:** Unbeobachtet Ort, gemeinsame Tätigkeit, Ankunft, Ende und tatsächlich relevante Käufe speichern. Bei Beobachtung ein verfügbares Tischpaar wählen und von dort konsistent fortsetzen. Bereits beobachtete Körper dürfen dabei nicht vor dem Spieler springen; doppelte Zahlung bleibt ausgeschlossen.

Quelle: `src/PeopleOfTheIliacBay.Core/HouseholdJointSimulation.cs:20`, `:89`; `mod/Scripts/ShopVisitGeometry.InnServiceCatalog.cs:40`, `:76`; `src/PeopleOfTheIliacBay.Core/HouseholdInnService.cs:8`.

### 5 Tagesleben beginnt mit einem Netz möglicher Wege und Mindestbeschäftigung

**Vorhanden:** Der Aufbau vermisst Wege zwischen den ausgewählten Gebäudekandidaten eines Bewohners, bevor dessen Besuchsfolge feststeht. Danach versucht der Planer unter anderem mindestens drei Stopps und zunächst drei Stunden Abwesenheit; bei Misserfolg lockert er diese Anforderungen. Gemeinsamer Cache, umgekehrte Routen und Arbeitsscheiben begrenzen bereits den Aufwand.

**Bewertung:** Abwechslung und ausreichend Straßenleben sind wichtig. Die vollständige Verbindung der Kandidaten und eine Mindestzahl von Erledigungen sind dafür zusätzliche Mittel, keine zwingenden Spielregeln. Viele vorbereitete Verbindungen werden vom gewählten Tag nicht benötigt.

**Einfacher:** Ein nächstes plausibles Ziel auswählen, dessen tatsächlichen Weg bestimmen und später weitere Aktivitäten ergänzen. Arbeitszeiten und vereinbarte Treffen bleiben feste Termine. Straßendichte direkt beurteilen, statt sie indirekt durch ein möglichst vollständiges Tagesprogramm abzusichern.

Quelle: `mod/Scripts/StreetPopulation.cs:599`, `:610`, `:670`; `mod/Scripts/StreetPopulation.DailyItinerary.cs:95`.

### 6 Nach Unterbrechungen muss der verbleibende Tagesplan wieder beweisbar passen

**Vorhanden:** `RoutineRecoveryPlanner` baut einen eigenen Graphen aus gespeicherten Wegen. Ein Ausgangsort ohne solchen Weg wird abgelehnt. Eine Heimkehr nach der vorgesehenen Schlafzeit scheitert mit `ProtectedSleep`. Termine und Familienpläne werden zusätzlich eingepasst.

**Bewertung:** Sichtbare Rückkehr nach Gespräch, Flucht oder Auftrag ist wichtig. Den ganzen restlichen Alltag einschließlich genauer Schlafgrenze wiederherzustellen, ist meist weniger wichtig als überhaupt sinnvoll weiterzugehen. Eine verspätete Heimkehr ist plausibler als das Scheitern einer Wiederaufnahme.

**Einfacher:** Vom tatsächlichen Ort zum nächsten relevanten Termin oder nach Hause gehen; optionale Erledigungen verwerfen. Die Schlafzeit als Präferenz behandeln. Verbindliche Spielertermine, erreichbare Wege und sichtbare Kontinuität erhalten.

Quelle: `src/PeopleOfTheIliacBay.Core/RoutineRecoveryPlanner.cs:15`, `:118`, `:232`.

### 7 Tageslohn läuft weiterhin durch eine Treuhandbuchhaltung

**Vorhanden:** Begleiter werden für einen begonnenen 24-Stunden-Block bezahlt; frühe Entlassung erstattet diesen Block nicht. Trotzdem führen die Verträge getrennte Werte für Finanzierung, Treuhand, Verdienst, Auszahlung und Erstattung sowie Transferbelege und Abrechnungsübergänge.

**Bewertung:** Diese Buchhaltung ist umfangreicher als die sichtbare Regel. Der aktuelle Code rechnet nicht mehr minutengenau ab; dieser frühere Vorwurf wäre falsch. Der Vereinfachungskandidat ist die verbliebene mehrstufige Geldverwaltung.

**Einfacher:** Bei erfolgreichem Dienstbeginn einmal vom Spieler an den NPC zahlen, Ende und Status speichern. Bei gescheitertem Start dieselbe Transaktion zurückrollen. Doppelte Zahlung und verlorenes Spielergold müssen weiterhin verhindert werden; dafür braucht es nicht zwingend einen dauerhaften dritten Geldbesitzer.

Quelle: `src/PeopleOfTheIliacBay.Core/NpcServiceContract.cs:368`, `:400`, `:424`; `src/PeopleOfTheIliacBay.Core/NpcServiceSettlement.cs:16`; `mod/Scripts/PeopleOfTheIliacBayMod.DialoguePaidCompanions.cs:25`.

### 8 Gildenheiltränke haben einen zusätzlichen künstlichen Warenbestand

**Vorhanden:** `NpcGuildPotionSupply` erzeugt genau acht Einheiten pro Anbieter und Tag. Reservierte UIDs aus einer vorherigen Generation können erhalten bleiben. Angebote prüfen Tagesgeneration, Inventarrevision und Reservierungen laufender Patientenaufgaben.

**Bewertung:** Der sichtbare Nutzen ist, dass ein verletzter NPC einen echten Trank kauft, bezahlt und benutzt. Eine zusätzliche künstliche Knappheit von acht Stück und Reservierungen über Tagesgenerationen erzeugen keine erkennbare Spieltiefe, solange diese Versorgung nicht selbst ein beabsichtigtes Spielsystem ist.

**Einfacher:** Den nativen Anbieterbestand verwenden, soweit verfügbar. Andernfalls einen einfachen Kauf über den nativen Dienst abwickeln und den tatsächlich erworbenen Gegenstand erzeugen. Echte UID, Preis, Geld und anschließender Verbrauch bleiben erhalten; das zusätzliche Lager entfällt.

Quelle: `src/PeopleOfTheIliacBay.Core/NpcGuildPotionSupply.cs:10`, `:37`, `:81`, `:124`; `mod/Scripts/NativeNpcGuildPotionSupply.cs:138`.

### 9 Zimmer sind eine Kapazitätseinheit statt lediglich Betten

**Vorhanden:** `TryBookLodging` verlangt genug Betten in einem einzelnen Zimmer und reserviert dieses Zimmer gegen weitere überlappende Buchungen. Auch ein einzelner NPC kann damit ein Mehrbettzimmer exklusiv belegen. In den geprüften produktiven Aufrufern werden gewöhnliche Reisende mit Gruppengröße eins gebucht; eine aktuell aktive Familien-Gruppenbuchung wurde hier nicht nachgewiesen.

**Bewertung:** Das bestätigt die unnötige Zimmerabstraktion, aber nicht pauschal die behauptete aktuelle Ausführung einer Familienzimmerregel. Entscheidend sind verfügbare Betten, tatsächliche Belegung und native Spielerrechte.

**Einfacher:** NPC-Betten einzeln belegen und eine Familien-Zimmerbindung streichen, falls ein weiterer Pfad sie verwendet. Native Mietmarker und bereits bezahlte Spielerrechte separat respektieren. Keine allgemeine Raumsegmentierung als Voraussetzung für Gästeaufnahme.

Quelle: `src/PeopleOfTheIliacBay.Core/Lodging.cs:117`, `:140`, `:205`; `src/PeopleOfTheIliacBay.Core/TravelerState.cs:113`; `src/PeopleOfTheIliacBay.Core/ResidentInnStay.cs:139`.

## Weitere Kandidaten mit erkennbarem Spielwert und engerem Kürzungsziel

### 10 Elternkalender sichern durchgehende Kinderbetreuung ab

**Vorhanden:** Der Haushaltsplan verschiebt individuelle Ausflüge anhand der belegten Intervalle beider Eltern und prüft die vollständige Betreuungsverfügbarkeit. Die Suche kann pro Kandidat bis zu 64 Versuche durchlaufen.

**Bewertung:** Familienbeziehungen und gelegentliche Betreuung sind glaubwürdig. Eine lückenlos nachgewiesene Betreuung über den ganzen Tag ist für die gewöhnliche Spielbeobachtung meist übergenau und kann freie Aktivitäten reduzieren. Das ist eine Abwägung, kein Nachweis eines Fehlers.

**Einfacher:** Einen groben Betreuungsplan oder eine einfache Zuhause-Präferenz nutzen. Verwandtschaft, tatsächlich anwesende Kinder und Reaktionen auf Gefahr erhalten. Den sichtbaren Familienausflug nicht entfernen.

Quelle: `src/PeopleOfTheIliacBay.Core/HouseholdDayPlanner.cs:61`, `:137`, `:142`.

### 11 Wachruhe setzt genaue freie Betten und Innenwege voraus

**Vorhanden:** Der Dienstwechsel sucht Quartiere, vermisst Innenräume und Bettrouten und wählt ein freies konkretes Bett. Für gewöhnliche Ruhe muss die geplante Rückkehr vor dem Dienstende der Pause passen. Der Spuk-Schutzpfad besitzt dagegen bereits einen Rückfall auf freie Innenpositionen.

**Bewertung:** Schichtwechsel, Rückzug in Quartiere und eine endliche Wachzahl sind sichtbar wertvoll. Die vollständige Bettzuweisung außerhalb der Beobachtung ist ein engerer Kürzungskandidat. Diese Bettanforderung steht außerdem ausdrücklich im aktuellen Auftrag zur Wachorganisation; sie wird hier neu bewertet, nicht als nachweislich erfundene KI-Regel bezeichnet.

**Einfacher:** Dienstfreie Wachen an einem erreichbaren Quartier aus dem Außendienst nehmen. Bett und Innenpose erst bei tatsächlicher Innenbeobachtung zuweisen. Dienstzeit und wiederkehrende Identität bleiben stabil.

Quelle: `mod/Scripts/GuardDayRest.cs:40`, `:103`, `:109`; `docs/ROADMAP.md`, Abschnitt zur Wachorganisation.

### 12 Abgeschlossene Arbeitsabläufe bleiben als technische Historie erhalten

**Vorhanden:** Die Aufbewahrung schützt gewöhnlich 30 Spieltage sowie jüngste abgeschlossene Aufgaben und Aktionen pro Person. Beim Entfernen werden Referenzen untersucht und ein Kandidat durch `CopyValidated` für den Weltzustand geprüft. Erinnerungen, noch offene Effekte und andere Verweise können technische Datensätze festhalten.

**Bewertung:** Erinnerbare Ergebnisse haben Spielwert. Die vollständige technische Schrittgeschichte einer erledigten Aufgabe muss nicht ebenso lange Teil der aktiven Simulation bleiben. Der Nutzen liegt hier teilweise in Fehlerdiagnose und Wiederholschutz; das ist von NPC-Erinnerung zu trennen. Eine konkrete Laufzeitbelastung wurde in diesem Audit nicht gemessen.

**Einfacher:** Nach Abschluss relevante Wirkung und einen kleinen Abschlussbeleg behalten; technische Schritte nach Auflösung ihrer tatsächlichen Abhängigkeiten freigeben. Diagnosedetails optional protokollieren. Offene Zahlungen, einmalige Effekte und noch referenzierte Belege nicht blind löschen.

Quelle: `src/PeopleOfTheIliacBay.Core/NpcOperationalHistoryRetention.cs:18`, `:34`, `:177`; `mod/Scripts/PeopleOfTheIliacBayMod.OperationalHistory.cs:12`.

## Reine Wartungskomplexität ohne zusätzlichen Immersionswert

### 13 Alte Entwicklungsspielstände werden weiterhin umfangreich unterstützt

**Vorhanden:** `WorldState` steht auf Schema 83, akzeptiert ältere Versionen und enthält unter anderem frühere Familienformen, alte Inventar-/Beleggrenzen sowie die Migration von 12-facher auf 2-fache Spielzeit. Weitere Routinen enthalten Versionspfade.

**Bewertung:** Für die ausdrücklich entbehrlichen Entwicklungsspielstände besteht kein entsprechender Nutzen. Das ist ein klarer Kürzungskandidat, allerdings nicht automatisch eine häufig ausgeführte Laufzeitlast.

**Einfacher:** Ein bewusst festgelegtes aktuelles Entwicklungsformat unterstützen und inkompatible Teststände klar ablehnen. Normales Speichern/Laden des aktuellen Formats bleibt notwendig. Keine Spielstände im Rahmen dieses Audits löschen.

Quelle: `src/PeopleOfTheIliacBay.Core/WorldState.cs:10`, `:267`, `:519`, `:600`, `:823`; `AGENTS.md`.

### 14 Mehrere optionale Dialogexperimente bleiben im Produktionsquellbaum

**Korrektur nach Nutzerabgleich:** Goal Extraction und Two Pass sind keine entbehrlichen zusätzlichen Modelle. Die reale Modellstudie vom 22.–23. Oktober zeigte mit Gemma E4B allein sechs von sechs korrekte Fünf-Schritt-Pläne und insgesamt 17/18 korrekte Drei- bis Fünf-Schritt-Pläne, ohne fälschlich ausführbare Kontrollfälle. Die spätere Zielkorrektur erreichte 18/18 auf bereits bekannten Fällen und blieb damals evaluatorseitig. Diese Ergebnisse belegen Modellinterpretation, keine vollständige native Ausführung derselben Fälle. Zielzerlegung, Parameterklärung, Zustimmung und native Prüfung bleiben im normalen Ein-LLM-Pfad erhalten. Entfernt wird die gesonderte D3-/Decision-Shadow-Infrastruktur. Mehrere Aufrufe desselben LLM sind mit der gewünschten Architektur „ein LLM plus TTS“ vereinbar. Quelle: `docs/2026-10-22-npc-model-study-results.md`.

**Vorhanden:** Goal Extraction, Two Pass und Decision Shadow haben eigene Protokolle, Zustände und Diagnosepfade. Die geprüften Varianten benötigen ausdrücklich gesetzte Startargumente. Ihre Existenz beweist keine zusätzlichen Modellaufrufe im normalen Gemma-Betrieb.

**Bewertung:** Das ist vor allem Wartungs- und Verständlichkeitsaufwand. Es wäre unbegründet, daraus einen aktuellen Immersions- oder Performancefehler abzuleiten.

**Einfacher:** Nicht mehr benötigte Versuche aus dem produktiven Aufbau nehmen oder als getrennte Werkzeuge erhalten. Einen verbindlichen normalen Dialogweg pflegen. Sprachverständnis nicht durch Wortlisten ersetzen und native Ausführungsprüfung beibehalten.

Quelle: `mod/Scripts/PeopleOfTheIliacBayMod.GoalActions.cs:18`; `mod/Scripts/PeopleOfTheIliacBayMod.GoalTwoPass.cs:25`; `mod/Scripts/LocalDecisionShadowConfiguration.cs:18`.

## Was die Prüfung nicht als unnötig einstuft

| Bereich | Zu erhaltender Nutzen | Prüfgrundlage |
| --- | --- | --- |
| Navigation, Türen, Stühle und Tische | Sichtbare Körper dürfen nicht durch Wände laufen, in Möbeln sitzen oder denselben Platz belegen. Bedarfsabhängige Vermessung bleibt sinnvoll. | `StreetPopulation.cs`, `ShopVisitGeometry.InnServiceCatalog.cs`, `RoadTravelActors.DungeonFollow.cs` |
| Identität und Gegenstände | Dieselbe Person und tatsächlich übertragene Gegenstände müssen nach Gespräch, Tod und Laden erhalten bleiben. | `WorldState.cs`, `PersonalBarterContract.cs`, `NativeMarketStock.cs` |
| Zeugen, Verbrechen und Wissen | Ein nicht beobachteter Täter darf nicht automatisch bekannt sein. Erinnerung an Spielerhandlungen trägt direkt zur Immersion bei. | `CrimeScenes.cs`, `NpcExperienceCommit.cs`, `NpcSceneMemory.Summaries.cs` |
| Wachen, Kampf und Spuk | Endliche Besetzung, sichtbare Reaktion, native Treffer und Questende haben unmittelbare Spielwirkung. | `GuardRefuges.cs`, `GuardDayRest.cs`, `NativeHauntingTargets.cs` |
| Gesundheit und Begleiter | Tatsächlicher Schaden, native Heilwirkung, brauchbares Folgen und klare Bezahlung bleiben relevant. | `StreetPopulation.HealthCare.cs`, `StreetPopulation.HealthRest.cs`, `NpcServiceContract.cs` |
| Familie und soziale Begegnungen | Gemeinsam unterwegs sein, miteinander sitzen und plausibel auf Unterbrechungen reagieren sind sichtbar. | `HouseholdDayPlanner.cs`, `StreetPopulation.HouseholdJointInn.cs`, `StreetPopulation.ClosedShops.cs` |
| Markt und persönlicher Handel | Sichtbare Ware, echte Käufe, Feierabend und Schutz getragener/Questgegenstände haben direkten Nutzen. | `NativeMarketStock.cs`, `NativeMarketCommuter.cs`, `PersonalBarterContract.cs` |
| Dialog, Erinnerung, Stimme und Portrait | Passende Antworten, Wiedererkennung und korrekte Zuordnung von Stimme/Bild sind die Spielerfahrung selbst. | `PeopleOfTheIliacBayMod.Experiences.cs`, `SharedConversationArchive.cs`, `PortraitSpeechOwnership.cs` |
| Quests und asynchrone Ergebnisse | Questpersonen nicht beschädigen; verspätete Modellantworten nicht auf eine andere Person oder einen anderen Spielstand anwenden. | `QuestOfferBindings.cs`, `NpcOperationAdmission.cs`, `NpcExperienceCommit.cs` |

Die Querschnittsvereinfachung lautet: Unbeobachtete Vorgänge nur so genau fortführen, wie spätere Begegnungen und tatsächliche Folgen es verlangen. Zusätzliche Details bei Bedarf bestimmen. Bereits beobachtete Fakten, Besitz und zugesagte Spieleraufträge behalten ihre Verbindlichkeit.
