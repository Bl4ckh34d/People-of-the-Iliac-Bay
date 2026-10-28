# Regex durch semantische Entscheidungen und echte Parser ersetzen

Stand: 27. Oktober 2026. Der historische Audit vom 25. Oktober bleibt unten erhalten; sein damaliger Befund ist keine Beschreibung des aktuellen Runtime-Pfads.

## Aktueller Quellstand und Prüfgrenze

Die reine Quellsuche mit `rg -n -F -e Regex -e RegularExpressions -e WantsMap -e NormalizeRoutingSelection -e ResolveFactualGroup src mod/Scripts -g '*.cs'` findet am 27. Oktober keine Treffer. Die früheren aktiven Routing-/Kartenüberschreibungen sind bereits entfernt. Routing und Zielbindung verwenden Gemmas strukturierte Entscheidungen; technische Schlüssel, Zahlen und native Zustände werden exakt geprüft. Dieser Umfang ist keine projektweite Regexfreiheitsbehauptung für Werkzeuge und Tests.

Der Dialogclient liest Completion-Inhalt und Kontextfehler jetzt mit dem vorhandenen FullSerializer-JSON-Parser. Nur das echte Root-Feld `content` ist Inhalt. Kontextfehler stammen aus dem exakten internen Fehlerwert oder dem strukturierten Servertyp `exceed_context_size_error`; Wörter in einer Antwort sind keine Metadaten. `DialogueCompletionText.Extract` bleibt noch in drei Offline-Evaluatoren referenziert; der aktive Dialogclient verwendet diesen historischen Fragmentleser nicht mehr.

Neue semantische Routen `biography` und `enterprise` verwenden dasselbe Gemma. Biografie ergänzt beim ersten relevanten persönlichen Gespräch drei kurze dauerhafte persönliche Sätze und, sofern eine echte Familie existiert, drei bis vier exakt geteilte Familienfakten. Native Herkunft und Familien-IDs bleiben verankert; Narrative erzeugen keine Personen, Häuser, Quests oder Wegziele. Vorhandene Gesprächshistorie, Szenenerinnerung und Zusammenfassungen bleiben erhalten. Unternehmensbezogene Beuterechte werden an die aktuelle Follow-Aktion und exakte Unternehmens-ID gebunden. Eigene Gegnerbeute und Containerfreigabe sind getrennt; keine Beutezusage erteilt Straftatzustimmung.

Bei mehrschrittigen Aufträgen markiert eine kleine semantische Entscheidung ausschließlich die bereits vorgeschlagenen, ausdrücklich unabhängig fortsetzbaren Schritte. Diese Metadaten gehen vor der normalen freiwilligen Zustimmung in deren unveränderliche Signatur ein. Standard ist Abbruch; erworbene Gegenstände und an eine vorherige Ankunft gebundenes Warten bleiben abhängig. Fehlgeschlagene und blockierte Schritte erhalten keine Erfolgsbelege. Teilfortsetzung darf weder Zähler noch Dialogquittung als Gesamterfolg darstellen.

Prüfgrenze nach ausdrücklicher Wiederaufnahme: Die aktuellen isolierten Prüfungen bestehen mit 53 Biografie-/Parser-Assertions, fünf Fortsetzungsfällen, 57 vorhandenen Auftrags-/Gesprächsfällen, 56/56 Goal-Tests, 17/17 Zustimmungsadapter-Tests und dem FullSerializer-Dialogmetadaten-Suite. Ein reproduzierter Fehler beim späteren nativen Bett-/Familienaufbau ist behoben: Die bereits gespeicherten persönlichen Sätze bleiben erhalten, während nur tatsächlich bestätigte Familienfakten nachträglich zugeordnet werden. Während des vorherigen Teststopps erfolgte keine Ausführung; dessen alter 24-Assertion-Zwischenstand ist kein aktueller Gesamtbeleg. Die neuesten Runtime-Kontext-/Uhrzeitänderungen benötigen noch Hauptagent-Kompilierung. Native und freie Gemma-Abnahme bleiben ausstehend. Details: `work/core-completion-20261027/dialogue_memory/RESULT.md`.

## Entscheidung und Zuständigkeit

Die Nutzeranweisung gilt im gesamten eigenen DFU-Projekt: kein Regex, auch nicht in Werkzeugen und Tests. Natürliche Sprache darf nicht ersatzweise mit `Contains`, Wortlisten oder handgeschriebenen Satzmustern klassifiziert werden.

Gemma interpretiert die aktuelle Nachricht zusammen mit zugeordnetem Gesprächsverlauf, tatsächlichem Aufenthalts-/Aktionszustand, Persönlichkeit, Stimmung, Beziehung und offenen Angeboten. Das Modell bestimmt Bedeutung, Bezug und freiwillige Zustimmung. DFU liefert tatsächliche Kandidaten und prüft Identität, Bestand, Wissen, Zugänglichkeit, Prioritäten und Ausführbarkeit. Eine Spielregel darf eine verstandene Aktion verhindern, aber den Spielertext nicht neu interpretieren.

Vorhandene strukturierte Ausgaben und Adapter bleiben die Grundlage. Kein zusätzlicher Agent, kein neues Modell, keine neue Planungsplattform und kein erzwungener kompletter JSON-Umbau. Eine Ausgabegrammatik beschränkt ausschließlich das Maschinenformat; sie darf keine Formulierungen des Spielers vorschreiben.

## Verifizierte Befunde und passende Ersatzwege

| Bereich | Aktueller Befund | Ersatz |
| --- | --- | --- |
| Routing und Karten | `ConversationKnowledge.NormalizeRoutingSelection` korrigiert den Modell-Intent anhand von `WantsMap`. Der aktive Adapter ruft diese Methode auf. `ConversationBrowsing.ResolveFactualGroup` und weitere Kartenabkürzungen treffen zusätzliche sprachliche Entscheidungen vor dem vorhandenen Topic-Binding. | Gemmas strukturierte Route beibehalten. Das vorhandene Topic-Binding um reale vorher genannte Ziele und Vorschläge ergänzen. Modell wählt Kategorie, vorhandene Ziel-ID oder Rückfrage. Keine nachträgliche Regex-Korrektur. |
| Ortsreferenzen | `ResolveGroundedMapReference` verlangt bestimmte Formulierungen, obwohl bereits ein natives vorheriges Ziel existiert. Kategorien und Synonyme stehen in Regex-Tabellen. | Vorherige reale Ziel-ID und zuletzt genannte Kandidaten als Gesprächskontext anbieten. „Den dort“, „irgendeinen“ oder ein unbekannter Name werden vom Modell aufgelöst. DFU bestätigt nur ein tatsächlich bekanntes Ziel; erfundene Ortsbeschreibung erzeugt keinen Marker. |
| Aktionen, Gegenstände, Angebote | `NpcActionChat` enthält sowohl `ParseSemantic` als auch alte Verb-/Mengenmuster. Native Inventar-, Kampf- und Serviceadapter unterscheiden semantische und alte Textpfade. | Vorhandene Kandidatenliste und `NpcActionIntent.TryBind` als einzigen Weg nutzen. Modell wählt Aktions-ID, Menge und gegebenenfalls Dauer. Eigentum, UID, verfügbare Menge, Zustimmung und aktuellen Auftrag nativ prüfen. Offenes Angebot samt ID bereitstellen; Zustimmung nicht über eine Ja-Wortliste erkennen. |
| Termine und Zuhause | `SemanticAppointmentChat` und `SemanticHomeChat` existieren bereits. `DialogueAppointments` hält gleichzeitig einen alten Regex-Pfad vor; der geprüfte Gesprächsaufruf setzt bereits `semantic=true`. | Semantischen Weg exklusiv verwenden; tote Altzweige samt zugehörigen Tests entfernen. Modell interpretiert Termin und Bezug. Native Kalenderrechnung, gültige Uhrzeiten, Ort und aktuelle Buchungen validieren. Fehlende Angaben führen zur Rückfrage. |
| Beleidigung, Bedrohung, Entschuldigung, Abschied | Alte Regex-Helfer existieren neben der quellengebundenen `NpcSpeechActClassification` und semantischer Scope-Prüfung. Nicht jeder alte Helfer ist deshalb noch im aktiven Gesprächspfad. | Die vorhandene semantische Klassifikation zur einzigen sprachlichen Quelle machen. Modell berücksichtigt Sprecher, Adressat, Zitat, Scherz, Verneinung und Gesprächskontext. Bestehende Persönlichkeits-/Beziehungsregeln bestimmen Folgen. Quellenbindung und Deduplizierung behalten; kein zweites semantisches Veto per Wortmuster. |
| Erinnerung | `ConversationFeatureExtractor` nutzt Regex für Token und Pronomen; `ConversationRetrieval` für Ausschnitte. Zusätzlich existieren manuelle Begriffsgruppen. | Technische Wort-/Textsegmentierung durch Unicode-Zeichenscanner ersetzen. Bedeutung und Referenzen bleiben beim Modell. Tatsächliche Ereignisse über vorhandene Personen-, Orts-, Vorgangs- und Erinnerungs-IDs abrufen; letzte relevante Gesprächsbezüge mitgeben. Lexikalisches Ranking darf Kandidaten sortieren, aber keine Absichten oder Ereigniswahrheit feststellen. Keine neue Vektordatenbank als Voraussetzung. |
| Serverantworten und Maschinenformate | `LocalDialogueClient` liest `n_ctx`, Tokenlisten und `truncated` mit Regex aus JSON. `ParseTopicSelection` validiert eine feste strukturierte Ausgabe ebenfalls mit Regex. | Vorhandenen FullSerializer-JSON-Parser verwenden; Felder und Typen prüfen. Feste strukturierte Zeilen mit Feldtrennung, exakten Enum-Werten und Zahlenparsern lesen. Dateinamen/IDs mit expliziten Formatprüfungen. Dafür kein Modell aufrufen. |
| Werkzeuge und Tests | Frühere Bestandsaufnahme nennt weitere Python-/PowerShell-/C#-Regex-Verwendungen, einschließlich Probes. | JSON als JSON lesen, Logmarker exakt vergleichen, Datentypen und Fehlerwerte direkt prüfen. Semantische Tests anhand der strukturierten Entscheidung und tatsächlichen Wirkung bewerten; keine vorgeschriebenen Wörter in frei formulierter Antwort. |

## Ein durchgehender Gesprächsweg

1. Aktuellen Gesprächskontext erfassen. Tatsächlichen Aufenthaltsort getrennt von Zuhause und geplanter Tätigkeit angeben. Offene Angebote und reale zuletzt genannte Ziele mitgeben.
2. Vorhandene Gemma-Klassifikation entscheidet den Sprechakt. Gewöhnliches Gespräch bleibt ohne Aktionsaufruf.
3. Nur falls erforderlich den vorhandenen Fachadapter verwenden: Ziel auswählen, Auftrag binden, Termin interpretieren oder Handelsbereitschaft entscheiden. Verfügbare Kandidaten werden aus Spielzustand ermittelt, nicht durch Wörter im Spielertext vorgefiltert.
4. Bestehende native Prüfungen bestätigen die konkrete Aktion. Ungültige IDs oder inzwischen verschwundene Gegenstände werden nicht ausgeführt. Keine zusätzliche semantische Interpretation in diesem Schritt.
5. Das tatsächliche Ergebnis wird Grundlage der NPC-Antwort: zugesagt, abgelehnt, unklar, begonnen, abgeschlossen oder gescheitert. Ein zugesagter Weg ist noch keine Ankunft. Keine pauschale „I haven't agreed …“-Antwort für beliebige Unklarheiten.

Bei unklarer Bedeutung fragt der NPC nach. Bei ungültiger Modellausgabe oder nicht verfügbarem Modell erfolgt keine neue Aktion und keine Strafreaktion. Das ist kein Ersatzklassifikator. Bestehende native UI-Aktionen funktionieren weiterhin über ihre expliziten IDs.

## Reihenfolge und Abnahme

1. **Ortsauskunft und Routing:** aktive Regex-Overrides entfernen und vorhandenes semantisches Ziel-Binding vervollständigen. Prüfen: zufälliger Rüstungsladen, unbekannter Eigenname, zuvor genannter Laden ohne Namen, Markieren gegenüber Hinführen, „nicht markieren“ und zitierte Bitte. Nur bekannte native Ziele dürfen wirken.
2. **Aktionen und Angebote:** semantische Kandidaten exklusiv; alte sprachabhängige Filter und Legacy-Parser entfernen. Prüfen: freie Zustimmung zu offenem Angebot, Mengen/Teilübergaben, Besitzwechsel, Ablehnung, Auftragsabbruch und Gewaltandrohung statt Auftrag.
3. **Soziale Reaktion, Termine, Zuhause:** vorhandene semantische Wege vereinheitlichen. Prüfen: freundschaftlicher Scherz, echte Drohung, zitierte Drohung, Entschuldigung, „morgen hier“ und mehrdeutige Terminangabe.
4. **Technische Parser, Erinnerung, Werkzeuge und Probes:** Regex vollständig aus eigenem Quellcode entfernen. Kein Umbenennen oder Nachbauen semantischer Muster. Bestehende relevante Prüfungen auf strukturierte Ergebnisse umstellen.

Pro Schritt gezielte Core-/Adaptertests und tatsächlich betroffene freie Gemma-Gespräche. Im Spiel mindestens Ortsmarkierung/Hinführen, Itemaktion und Ablehnung prüfen. Varianten müssen dieselbe Bedeutung trotz anderer Wörter sowie andere Bedeutung trotz ähnlicher Wörter prüfen. Laufzeit und Anzahl Modellaufrufe messen; keine neue Prüfung für jedes Wort und kein pauschales Wiederholen bis die gewünschte Entscheidung erscheint.

## Statusgrenze

Dieses Dokument belegt passende vorhandene Bausteine und aktive Problemstellen durch Quellcodeinspektion. Es ersetzt keinen Modell- oder Spieltest. Die frühere breite Regex-Bestandsaufnahme steht in Obsidian; ihre Anzahl ist kein neuer vollständiger Audit dieses Dokuments. Erst nach Entfernen aller eigenen Verwendungen und erneuter Prüfung darf der Mod als regexfrei bezeichnet werden.
