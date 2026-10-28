# Integration der vollständigen NPC-Sets — 28. Oktober 2026

Das Paket ist in `Player/DaggerfallUnity_Data/StreamingAssets/Mods/peopleoftheiliacbay.dfmod` installiert. Der genaue SHA256, die Auswahl und die Prüfnachweise stehen in `runtime-installation.json`, `runtime-integration.json` und `runtime-evidence/`.

## Enthalten

| Aktion | Vollständige Sets | Frames |
|---|---:|---:|
| Rennen | 25 | 800 |
| Gehen, stationäre Figuren | 2 | 64 |
| Idle, stationäre Figuren | 38 | 304 |
| Nahkampf mit einhändigem Schwert | 18 | 576 |
| Springen | 3 | 96 |
| Klettern | 3 | 96 |
| Schwimmen | 2 | 64 |
| **Körperanimationen insgesamt** | **91** | **2.000** |

Exakte Quellaliase ergeben 114 Zuordnungen von Identität und Aktion. Native Gehbewegungen und bestehende mobile Idle-Sets bleiben verfügbar. Zusätzlich sind 68 geprüfte DET-Schlafbilder registriert; der gesamte Schlafkatalog enthält damit 394 providerqualifizierte Zuordnungen.

Zwölf neue vollständige Portraits sind hinzugekommen: 012, 013, 014, 046, 047, 195, 196, 197, 220, 236, 425 und 452. Das sind 1.296 neue registrierte Bildteile und insgesamt 62 animierte Portraits. Stimmen und bestehende Zuordnungen wurden nicht geändert.

## Einbindung

`CompletedNpcArt` lädt nur vollständige, prüfsummengebundene Atlanten. Identität und tatsächlich aktiver Texturprovider müssen übereinstimmen. `CompletedNpcPose` folgt den vorhandenen Bewegungs-, Kampf-, Sprung-, Kletter- und örtlichen Dungeon-Wasserzuständen. Die Einbindung erzeugt keine neuen Bewegungsfähigkeiten und verändert keinen Schaden oder Inventarbestand.

Körperhöhe kommt aus dem nativen Billboard einschließlich dessen Skalierung. Transparenter Atlasrand, Fußpunkt und Bildauflösung bleiben davon getrennt. Phasen behalten denselben Maßstab. Die Darstellung unterdrückt das originale Zeichnen mit `forceRenderingOff`; native Schlaf-, Sichtbarkeits- und Unsichtbarkeitslogik behält `enabled`. Bestehende Stand- und Händlerdarstellungen werden geordnet freigegeben und wiederhergestellt.

Ohne vollständiges Set bleibt die bisherige Darstellung erhalten. Ausgeschlossen sind insbesondere Run455/456, Bogen-Teilsets, Swim383, reparaturbedürftiges Walk183/3, unvollständige Locomotion183/2, Idle183/13, nicht kalibrierte Änderungen der Ausgangshaltung182/37 und182/38, sieben DET-Schlafbilder mit Identitätsvorbehalt sowie unzusammengesetzte Portrait-Teile. DET-Mobilquellen ohne belegte Laufzeitzuordnung werden nicht geraten.

## Prüfung

- 2.155/2.155 Kernprüfungen; 15 Auswahl-/Phasenprüfungen; sechs Prüfungen der tatsächlichen Renderer-Freigabemethode.
- Bestehende Schlafregressionen: 14/14 GuardSleepVisual und 7/7 MarketSleep.
- Runtime-Kompilierung ohne Warnungen oder Fehler. Unity lädt sämtliche 91 Atlanten aus dem fertigen Bundle und prüft Hashes, Dimensionen und Runtime-Deserialisierung.
- Nativer Spieltest: acht Laufrichtungen, vier Phasen, tatsächliche Bewegung und Rückkehr bei Gespräch, Stand, Schlaf und Tod.
- Native Gesprächsfenster: alle zwölf neuen Portraits plus 20 bestehende; 16.524 verglichene Ausgabekombinationen, Sprecherwechsel, Originalerhalt, Fallback und Idle-Blinzeln bestanden. Screenshots013 und236 angesehen.

Die übrigen Körperaktionen sind technisch und als Paket geprüft; eine vollständige visuelle Spielabnahme jeder Identität und Aktion wird nicht behauptet. Die unveränderten Produktionsmanifeste behalten ihre ursprünglichen Kandidaten- und Qualitätsangaben; diese Installationsnachweise ergänzen sie.

## Wiederholen

```powershell
python tools/BuildCompletedNpcSprites.py --check
dotnet run --project tests/CompletedNpcSprites.Regression
dotnet build tools/RuntimeCompile/RuntimeCompile.csproj --nologo -v:q
./tools/Build.ps1 -InteractiveOnly
./tools/TownTest.ps1 -RunningPilotProbe -FixtureCharacter 'Hakon Ironhand'
./tools/TownTest.ps1 -SplitPortraitRuntimeProbe -FixtureCharacter 'Hakon Ironhand' -FixtureSaveName 'People - Gothway Garden'
```

Unity und Spiel werden ausschließlich nacheinander gestartet. Die Tests verwenden vorhandene Test-Spielstände und speichern die Testfiguren nicht. Alle Generierungsagenten bleiben unterbrochen.

## Verbliebene temporäre Dateien

Die automatische Freigabeprüfung hat die abschließende Löschung mit „blocked by policy“ abgelehnt. Keine Löschung wurde ausgeführt. Betroffen sind:

- `C:/Users/Daniel/AppData/LocalLow/Daggerfall Workshop/Daggerfall Unity/PeopleOfTheIliacBay-running-pilot-<ID>-angle-*.png` mit genau diesen drei eigenen Lauf-IDs: `42abafcd5cfd4d76bb12588928ac2422`, `85cad045c44041d4bd13db5c397a5202`, `cf86a45fbd174a32aa485a5482e47880`.
- Im selben Ordner `portrait-gallery-native-13.png` und `portrait-gallery-native-236.png`; ausgewählte Belegkopien liegen bereits unter `runtime-evidence/`.
- `C:/Users/Daniel/Desktop/Code_Experiments/DFU/LivingCities/tests/CompletedNpcSprites.Regression/bin` und `obj`.

Die Sperre wurde nicht umgangen. Aktuelles Mod-Paket, Quellen und einzigartige Kunst bleiben erhalten.
