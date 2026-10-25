# Zielentwurf: NPC-Leben außerhalb der Spielerumgebung

> Historischer Entwurf, seit24. Oktober2026 durch die vereinfachte [Roadmap](ROADMAP.md) begrenzt. Gestrichene Liefer-, Produktions-, Ereignisbudget- und Weltgeschichtssysteme hier sind keine offenen Implementierungsaufträge.

Stand: 23. Oktober 2026. Dokumentation und offener Backlog, keine implementierte Weltsimulation. Verbindliche Anforderungen stehen in `ROADMAP.md`, LIFE-45–57; frühere LIFE-01–44 bleiben bestehen. Gedächtnisverdichtung bleibt separat zurückgestellt.

## Empfehlung

Eine gemeinsame persistente Welt mit drei Rechendetailstufen. Sichtweite entscheidet, wie genau gerechnet und dargestellt wird; sie erzeugt keine zweite Wahrheit.

| Stufe | Welche Akteure? | Was wird gerechnet? |
|---|---|---|
| Sichtbar | NPCs im aktiven Umfeld | Native Bewegung, Kampf, Wahrnehmung, Interaktionen und konkrete Transaktionen. |
| Bekannt, aber entfernt | Reisende, Gruppen, reservierte Familien, offene Lieferungen und Termine | Fällige Abreisen, Ankünfte, Arbeitsergebnisse und begrenzte Risikoprüfungen. Keine Körper, Animationen oder permanenten Modellaufrufe. |
| Noch unbesucht | Übrige Orte und ihre noch nicht festgelegten Bewohner | Stabile Ortsgrundlage, Kapazitäten, saisonale Aggregate und wenige relevante Änderungen. Individuen erst bei Bedarf konkretisieren. |

Eine Vollsimulation jedes Weltbewohners wäre unnötig teuer. Reiner Besuchszufall wäre billig, würde aber Geschichten und Besuchsreihenfolgen widersprüchlich machen. Der vorgeschlagene Mittelweg berechnet entfernte Ereignisse sparsam und bewahrt jede bereits verbindliche Tatsache.

## Alle Ortstypen müssen durch den vollständigen Ablauf

`SettlementClassification.SupportsPopulation` akzeptiert aktuell nur native Typen0–2; `FromLocationType` liefert City/Town/Hamlet. Die Aktivierung erzeugt Haushalte über `store.GetOrCreateFamilies(...)`. Der historische0.3.31-Checkpoint nennt isolierte Höfe/Tavernen und Wildnisreisende ausdrücklich als offen. Daher reicht es nicht, zusätzliche Enum-Werte freizuschalten.

Ortsart und Fähigkeiten trennen: Wohnen, Betten, Feldarbeit, Viehhaltung, Handel, Tempeldienst, Reisehalt, Dungeonzugang und sichere Rückzugsorte sind jeweils eigene verfügbare Funktionen. Alle nutzen gemeinsame Identitäten, Besitz, Skills, Ereignisse und Saves. Die Ortsadapter ermitteln tatsächlich vorhandene Möglichkeiten.

| Ort | Benötigte Integration | Darf nicht pauschal vorausgesetzt werden |
|---|---|---|
| City/Town/Hamlet | Bestehende Bevölkerung, neue Arbeit und Reisen; Regressionen prüfen | Jede Stadt hat Tiere, Felder oder jede Dienstleistung. |
| Isoliertes Inn/Taverne | Personal, Betten, Gäste, Reisestopp, Vorräte, Nachtbetrieb | Stadtwachen, öffentliche Stadttore oder große Haushaltsbevölkerung. |
| Homestead/Bauernhof | Bewohner, Felder, Tiere, saisonale Aufgaben, Lagerung | Vorhandene Farming-Mechanik oder animierte Tiere ohne explizite Adapter. |
| Tempel | Personal, Dienstpflichten, Heilen, Arbeit/Ruhe, Besucher | Jeder Anwesende ist Magier oder permanent dort wohnhaft. |
| Dungeon | Native Akteure, Expeditionen, Gefahr, Ressourcen und Questschutz | Automatisch generierte Bürgerfamilien oder frei plünderbare Questobjekte. |
| Wildnisort | Stabile Weltposition, Reisebegegnung, geeignete Lager-/Arbeitsorte | Gebäude, Eigentumsrechte oder sichere Unterkunft. |

Pro Ortstyp nachweisen: Klassifikation, Survey, Identität/Haushalt, Routinen, Reisen, Gefahr/Interaktion, Besitz, Erinnerungen, Entladen/Wiederladen und alte Saves. Nicht anwendbare Funktionen ausdrücklich markieren; unbekannte native/modded Kategorien nicht still als Stadt behandeln.

## Ländlicher Alltag

Arbeit ergibt sich aus Beruf, Jahreszeit, Klima, vorhandenem Arbeitsplatz und Bedarf. Feldvorbereitung, Aussaat, Pflege und Ernte passen vor allem in Frühling bis Herbst. Winter kann Vorratspflege, Werkzeugreparatur, Holzarbeit oder andere Innenarbeit bedeuten. Ein winterlicher Holzplan benötigt Holzbedarf, geeigneten Ort, Werkzeug und Zeit. Keine universelle Holzfällerrolle für jeden Bauern.

Tierpflege ist ganzjährig: Besitzer, Betreuer, Bestand, Futter/Wasser und gegebenenfalls Erzeugnisse gehören zusammen. Ein abreisender Tierhalter muss Vertretung oder die Folgen fehlender Versorgung berücksichtigen. Routinen dürfen wirtschaftliche Ergebnisse erst buchen, wenn diese Tätigkeiten tatsächlich unterstützt sind. Animation und Dialog allein produzieren nichts.

## Gruppen und Dungeonreisen

Geeignete NPCs können aus eigenen Zielen Gruppen bilden. Mitglieder müssen verfügbar und einverstanden sein. Gruppen speichern Ziel, Mitglieder, Führung, Vorräte, Kosten, Beuteabrede und Rückkehrplan. Wache im gebundenen Dienst oder questgebundene Figur wird nicht still zum Abenteurer.

Eine entfernte Expedition braucht keine simulierte Schlagfolge. Reisezeit, Fähigkeiten, Ausrüstung, Versorgung und begrenzte Risikostufen bestimmen Resultate wie Rückzug, Verletzung, kleiner Fund oder erfolgreicher Abschluss. Die Ergebnisverteilung muss kalibriert werden; konkrete Erfolgs- oder Todesquoten sind noch nicht festgelegt.

Zufällig erzeugte Expeditionsbeute erhält eine persistente Herkunft und ein endliches Budget. Eine konkrete native Truhe kann nur geplündert werden, wenn deren Entnahme später korrekt sichtbar ist. Sonst bleibt die Geschichte bei belegten begrenzten Funden; sie behauptet keine vollständig geleerte Dungeoninstanz. Questobjekte und native Respawnregeln brauchen eigene Integration. Beim Treffen mit dem Spieler wechselt dieselbe Expedition in die physische Simulation; kein vorab feststehender Erfolg darf dessen Eingriff übergehen.

## Erst speichern, dann erzählen

Eine überprüfbare Familiengeschichte setzt verbindliche Identitäten voraus. Vor einer wahrheitsgemäßen Aussage über ein besuchbares Zuhause werden reale Heimat, geeignetes Gebäude, Kapazität, Haushalt und relevante Familienmitglieder reserviert. Eine Generierung ohne sichtbare Szene darf dafür native Layoutdaten lesen, soweit diese zuverlässig verfügbar sind. Ist das nicht möglich, bleibt die Aussage weniger konkret; es wird keine Adresse erfunden.

Beim ersten Besuch gelten native/questgebundene Belegung und bestehende Reservierungen zuerst. Nur freie Kapazität bekommt neue Bewohner. Lebensgeschichte wird mit bestehenden Personen, Beziehungen und Ereignissen vereinbar ergänzt. Reisender und späterer Bewohner müssen dieselbe Person sein. `TravelerState` besitzt derzeit eine eigene Reisendenidentität und verhindert residente Duplikate; der Übergang benötigt eine geprüfte Identitätsbrücke und Migration, nicht zwei Datensätze derselben Figur.

Ein Lügner kann eine falsche Geschichte erzählen. Diese wird als seine Aussage gespeichert und verändert nicht Heimat oder Familie. Ist ein Familienmitglied inzwischen gestorben, müssen Zeitpunkt und Ereignis die frühere Aussage erklären, statt sie nachträglich als Lüge zu behandeln.

## Zufallsereignisse sind Zustandsänderungen

Ein Einbruch braucht Gelegenheit, geeigneten Täter und zulässige Beute. Der Täter verliert Zeit und trägt Risiko; Besitz wird genau einmal übertragen. Das Opfer kennt den Verlust nach plausibler Entdeckung und erkennt ohne Beleg nicht automatisch den Täter. Zeugen wissen nur, was sie wahrnehmen konnten. Nachbarn erfahren davon erst durch eine Übertragung, nicht sofort weltweit.

Spuren und spätere Gespräche stammen aus demselben Ereignis. Eine native Tür wird nur als beschädigt beschrieben, wenn der Adapter diesen Zustand tatsächlich abbildet. Sonst reichen fehlende Gegenstände oder die Erinnerung an den Verlust. Ähnliche Regeln gelten für Hilfe, Lieferungen, Jagd-/Expeditionserfolge und Streit. Spieler- und Questbesitz sind keine beliebigen Zufallsziele.

## Schnellreise und nachgeholte Zeit

1. Abreise festhalten: laufende sichtbare Handlungen abschließen oder sauber übergeben; keine neue Kopie eines reisenden Akteurs.
2. Tatsächlich verstrichene DFU-Spielzeit ermitteln, einschließlich Unterbrechung oder Zwischenbegegnung.
3. Fällige abhängige Ereignisse chronologisch bearbeiten: Spielergruppe, bekannte Reisende, reservierte Familien, Fristen und Zielort. Kosten und Resultate unter stabilen Ereignis-IDs einmal buchen.
4. Ankunftszustand gemeinsam festlegen. Erst danach sichtbare Figuren, Inventare und Gesprächskontext freigeben.
5. Unabhängige ferne Orte bleiben kompakt und werden später budgetiert aktualisiert. Eine dort benötigte Tatsache muss allerdings vor ihrer Veröffentlichung auf denselben Zeitpunkt abgeglichen werden.

Zufallsströme sind an stabile Akteure/Orte, Regeln und kanonische Zeitintervalle gebunden. Ein30-Tage-Sprung und30 einzelne Tage sollen bei gleichen Eingriffen dieselben verbindlichen Resultate liefern. Besuch oder Reload ist kein zusätzlicher Würfelwurf. Bei vollständiger physischer Beobachtung können neue Eingriffe Resultate ändern; das ist eine echte Ursache, kein Rechenfehler.

Noch nicht berechnete Geschichte darf nur in freien, unbeobachteten Bereichen ergänzt werden. Frühere Beobachtungen, schon übermittelte Nachrichten und veröffentlichte Familienfakten sind feste Grenzen. Markt-/Reiseereignisse zwischen zwei Regionen brauchen gemeinsame Buchung, bevor eine Seite davon erzählt.

## Rechen- und Speichergrenzen

Fällige Ereignisse statt Welt-Ticks; analytische Routinefortschreibung statt stündlicher Schleifen; getrennte Budgets für sichtbare Akteure, kritische Ankunftsarbeit und Hintergrundarbeit. Kein LLM für jede Kuh, jeden Handel oder jeden entfernten Kampf. Das LLM formuliert aus akzeptierten Resultaten oder hilft bei begrenzten übergeordneten Entscheidungen.

Persistieren: bekannte Personen, Reservierungen, Reise-/Gruppenzustände, relevante Besitzänderungen, offene Verpflichtungen und wichtige Ereignisse. Wiederholte Routine als kompakte Summen behandeln, ohne individuelle Erinnerungen zu erfinden. Die ausdrücklich gestoppte historische Gedächtnisverdichtung wird dadurch nicht neu gestartet; bestehende Archive bleiben erhalten.

Noch offen sind konkrete Zeit-/Speicherbudgets und Balancingquoten. Vor Freigabe CPU-Frametimes, Ladezeit, Warteschlangenwachstum, aktive Körper, Savegröße und Ergebnisse über Monate/Jahre messen. Ein langsamer Rechner darf weniger Darstellung erhalten, aber keine widersprüchlichen Fakten oder still ausgelassenen Verpflichtungen.

## Umsetzung in prüfbaren Etappen

Am23. Oktober vom Nutzer akzeptierte Vereinfachung: gleiche Grundbedürfnisse und Aktionsregeln für alle Berufe, mit rollen-/persönlichkeitsabhängigen Prioritäten. Arbeit wird in Tätigkeitsblöcken gerechnet, entfernte Expeditionen als Gruppenablauf. Routineproduktion/-verbrauch darf aggregiert werden; persönlicher Besitz, wichtige Käufe und Diebstähle bleiben exakt. Beziehungen entstehen nur für tatsächliche Kontakte, keine vollständige Beziehungsmatrix aller Einwohner. Dauerhafte Charakteränderungen werden nur bei seltenen prägenden Entwicklungen geprüft. Zunächst wenige begrenzte Wechselwirkungen; keine umfassende Weltwirtschaft und keine weltweiten Ernte-/Preis-/Krankheits-/Migrationskaskaden.

Erhalten bleiben acht Attribute,35 Skills und drei Charakterzüge sowie stabile Identität, Familie, Zuhause, Vergangenheit, Wissensgrenzen, bedeutsame Erinnerungen und offene Zusagen. Weniger Rechendetail darf keine überprüfbare Tatsache verändern. Dies ist eine Vorgabe für die Umsetzung, keine bereits ausgeführte Änderung der Laufzeit.

1. Zuerst einen vollständigen Ablauf mit bestehenden Grundlagen zuverlässig integrieren: arbeiten, verletzt werden, Hilfe suchen, tatsächlich bezahlen, sich erholen und später quellengetreu erinnern. Reload, fehlende Mittel und Ablehnung gehören zur Abnahme.
2. Ortsfähigkeiten und Abdeckungsmatrix, zunächst ein isoliertes Inn und ein Homestead vollständig integrieren; keine bloße Enum-Erweiterung. Gemeinsame Mechanismen des ersten Ablaufs wiederverwenden.
3. Heimatreservierung und gemeinsame Reisenden-/Bewohneridentität, einschließlich Erstbesuch und Save-Migration.
4. Kalender, Ereignisquellen, Rechenbudgets und Schnellreise-Abgleich; Besitz und Wissen gemeinsam testen.
5. Saisonarbeit und Tierpflege mit realen Fähigkeiten; dann begrenzte NPC-Gruppen und Expeditionen.
6. Weitere Tempel-, Dungeon- und Wildnisadapter sowie Jahres-/Performanceabnahme. Die Reihenfolge verkleinert die einzelnen Schritte, nicht die zugesagte Gesamtabdeckung.

Abnahmeszenarien und Grenzen sind als LIFE-45–57 in der zentralen ROADMAP ausgeschrieben. Dieser Entwurf belegt keine laufende Weltsimulation oder zugesicherte Performance.

## Population baseline: ordinary lives, rare adventurers (23 October user decision)

This is a cross-system ToDo requirement, not implemented behavior. It constrains LIFE-02/08/09/16/20/21/35/38/47/48/49/52/53 and companion recruitment F01–F06.

- [ ] The overwhelming majority of inhabitants in cities, towns, villages, farms and other civilian locations are ordinary people. “98%” expresses the intended feel, not a fixed population quota. Their work, household, sleep and leisure routines vary only modestly. They avoid adventuring and cannot be talked into leaving with the player or launching their own expeditions merely through persuasion, gifts or high disposition. Ordinary civilian travel for work, family or lodging remains possible and is not adventuring.
- [ ] Persist adventure inclination separately from combat class, current profession, service obligation, relationship and actual capability. Ordinary skills and attributes reflect ordinary lives; occupational expertise and rare exceptional backgrounds remain possible. Mage, warrior or rogue class alone does not imply recruitment eligibility or a desire to adventure. Existing authored identities and saved histories retain authority.
- [ ] City guards can consider quitting their job to join the player only after a genuinely close friendship. Friendship is a necessary condition, not guaranteed consent. Joining requires an explicit, persisted departure from service and resolution of relevant obligations; until then duty and the existing city-watch combat policy still apply. This exception does not authorize recruiting palace guards or bypassing story/quest restrictions.
- [ ] Recruitable adventurers are a small, persistent minority with varied classes. Preferred encounters depend on class and actual activities: mages favor the Mages Guild, Inns and alchemy shops; rogues favor Inns and weapon/armor shops; fighters favor Inns and weapon/armor shops. All classes occasionally visit pawn shops and general stores to sell loot or buy useful items. These are weighted tendencies, not exclusive locations or fixed spawn rules. Lodging, concrete needs, available services, stock, purchasing budget and actual routes determine presence; a visit alone does not imply a successful transaction. Most Inn patrons are still ordinary travelers or locals drinking alone or meeting peers. Presence in an Inn, shop or guild never creates adventurer status or automatic consent.
- [ ] Most adventurers travel and lodge in Inns; only a minority maintain a home with family. Do not create a permanent household for every adventurer. Preserve optional home/family ties, current lodging and journey as separate facts. A traveler can truthfully have no permanent home; do not infer that they have no relatives. Their answers about home, family or lodging must match saved facts and actual booking status. For the minority with a claimed visitable family home, the existing identity/capacity reservation rules still apply.
- [ ] Use modest routine variations and sparse, evidence-based skill practice for ordinary civilians. Activate expedition planning, adventure progression and complex travel/companion schedules only for eligible actors with relevant needs or commitments. Shared health, memory, economy and relationship rules still apply to everyone; civilians retain real needs, interruptions and consequences. Avoid per-civilian adventure planning or routine LLM polling.

Acceptance: population samples across locations/seeds show a clearly dominant ordinary population without fixing 98%; Inn patrons are not predominantly adventurers. Repeated persuasion cannot recruit ordinary civilians. Non-close city guards refuse; close friendship alone does not silently terminate service. Adventurer status, service transitions and optional home/family/lodging survive save/load and revisits without rerolling. Most adventurers can truthfully describe a traveling life and Inn lodging, while the smaller home-owning group references their real household. Measure planning work against active eligible actors, while health and everyday life remain functional for ordinary residents. No exact rates or performance gains are claimed before calibration.

