"""Build a local review document from actual production artifacts."""
import html
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent


def main():
    inventory = json.loads((HERE / "source-inventory.json").read_text())
    examples = [
        ("Portrait 236 · vollständiger Kandidat", "portraits/npc236-complete-candidate/mouths54-contact.png", "portraits/npc236-complete-candidate/manifest.json", "54 eigene Augenstates plus54 bereits exakt abgeleitete220-Mundstates.108 Teile und2.916 Kombinationen außerhalb der Masken geprüft. C/E und G/FV bleiben begrenzt; keine Stimme oder zeitliche Freigabe."),
        ("Portrait 236 · 54 Augenstates", "portraits/npc236-eyes-v1/selected54-contact.png", "portraits/npc236-eyes-v1/selected-manifest.json", "Reframing der3×3-Rohblätter erkannt und verworfen. Einzelbilder liefern Lid und Blickrichtungen; HALF bewahrt Iriszeile22. Eigene Brauen pro Stimmung, geteilter Lid-/Blickbereich desselben Gesichts. Kontrast und Ausdrucksvariation bleiben Grenzen."),
        ("Portrait 047 · vollständiger Kandidat", "portraits/npc047-complete-candidate/mouths54-contact.png", "portraits/npc047-complete-candidate/manifest.json", "54 eigene Augenstates und 54 exakt abgeleitete Mundstates. HALF behält Iriszeile27, Goldrand bleibt fest. Seitliche Augen heller; Phonemwirkung und individuelle Stimme offen."),
        ("Portrait 047 · 54 Augenstates", "portraits/npc047-eyes-v1/selected54-contact.png", "portraits/npc047-complete-candidate/manifest.json", "Alle Zustände statisch gesehen. HALF teilweise nahe CLOSED; keine zeitliche Freigabe."),
        ("Portrait 046 · 54 Augenstates", "portraits/npc046-eyes-v1/selected54-contact.png", "portraits/npc046-complete-candidate/manifest.json", "Ein sichtbares Auge animiert; verdeckte Gegenseite bleibt original. 108 Teile insgesamt vorhanden. Schmale HALF-Öffnung und zurückhaltende Ausdrücke bleiben Grenzen."),
        ("Portrait 236 · exakt übernommene Münder", "portraits/exact-feature-reuse-wave3/common-236/mouths-54-contact.png", "portraits/exact-feature-reuse-wave3/common-236/manifest.json", "Fünf weitere Quellen erhalten je54 Mundstates von220. Tatsächliche Änderungsmaske samt1px-Rand exakt kompatibel. Keine neu generierten Posen; eigene Augen werden separat erzeugt."),
        ("Nahkampf · 392 · korrigierte Haube", "combat/melee-392-directions-0-3-review.png", "combat/candidate-melee-392-eight-directions/manifest.json", "Blaue native Haube in allen acht Ansichten wiederhergestellt.32 statisch geprüfte Phasen; Haar-, Stoff- und Größenvariation bleiben."),
        ("Rennen · 395 · Perspektiven 0–3", "mobile-locomotion/families/395/candidate-v1/review-0.png", "mobile-locomotion/families/395/candidate-v1/manifest-reviewed.json", "32 Phasen mit20 erzeugten Ansichten und12 nativen Spiegelableitungen. Weißer Umhang mitroten Streifen und offene Seiten quellengeprüft; Detaildrift bleibt."),
        ("Portrait 012 · eigene Augen, exakte Mundübernahme", "portraits/npc012-complete-candidate/mouths54-contact.png", "portraits/npc012-complete-candidate/manifest.json", "108 Teile vorhanden: 54 eigene Augenstates und 54 bereits exakt abgeleitete Mundstates. Kapuze und Gesicht bleiben außerhalb der Masken unverändert. Zeitliche Wirkung und individuelle Stimme offen."),
        ("Portrait 012 · 54 Augenstates", "portraits/npc012-eyes-v1/selected54-contact.png", "portraits/npc012-eyes-v1/selected-manifest.json", "HALF bewahrt die passende OPEN-Iris auf Zeile 27; Unterlid ab Zeile 28 bleibt fest. Seitliche Augen zeigen hellere Sklera und andere Glanzdetails. Statisch geprüft, keine zeitliche Freigabe."),
        ("Portrait 220 · vollständiger Kandidat", "portraits/npc220-complete-candidate/mouths54-contact.png", "portraits/npc220-complete-candidate/manifest.json", "54 eigene Münder ergänzen die vorhandenen exakt abgeleiteten Augen. Eigene E-Rundkontur erhalten. C/E bleiben subtil; G/FV-Kontakt begrenzt."),
        ("Portrait 425 · vollständiger Kandidat", "portraits/npc425-complete-candidate/mouths54-contact.png", "portraits/npc425-complete-candidate/manifest.json", "54 eigene Münder ergänzen die vorhandenen Augen. Guarded-Framing korrigiert. E teils hoch, G/FV und Ausdrucksunterschiede begrenzt; individuelle Stimme fehlt."),
        ("Rennen · Narr · acht Perspektiven", "stationary-locomotion/family-182-5/run/selected-views-review-sheet.png", "stationary-locomotion/family-182-5/manifest.json", "32 Rennphasen ergänzen 32 Gehphasen für drei identische Originalquellen. Kontaktseite statisch geprüft; Gesicht, Propgröße und Palette variieren."),
        ("Gehen · Narr · acht Perspektiven", "stationary-locomotion/family-182-5/walk/selected-views-review-sheet.png", "stationary-locomotion/family-182-5/manifest.json", "32 Phasen für drei identische Originalquellen. Rot/goldene Asymmetrie und Marotte in rechter Hand bleiben erhalten; Gesicht, Requisitengröße und Palette variieren."),
        ("Portrait 197 · vollständiger Kandidat", "portraits/npc197-complete-candidate/mouths54-contact.png", "portraits/npc197-complete-candidate/manifest.json", "54 eigene Augen- und 54 Mundteile. E gezielt gerundet; G/FV-Kontakt sowie ähnliche Sad/Fear-Ausdrücke bleiben Grenzen."),
        ("Portrait 196 · 54 Mundvarianten", "portraits/npc196-complete-candidate/mouths54-contact.png", "portraits/npc196-complete-candidate/manifest.json", "108 Augen-/Mundteile vorhanden. G liest sich teilweise wie zusammengebissene Zähne; F/V-Lippenkontakt bleibt begrenzt. Keine pauschale Phonemfreigabe."),
        ("Portrait 452 · exakt übernommene Augen", "portraits/exact-feature-reuse/common-452/eyes-54-contact.png", "portraits/exact-feature-reuse/common-452/manifest.json", "108 Teile aus passenden vorhandenen Merkmalen abgeleitet. Eigene Haare und Hintergrund bleiben pixelgenau erhalten. Keine neu generierten Posen; Qualitätsgrenzen des Donors bleiben."),
        ("Rennen · 386 · acht Perspektiven", "mobile-locomotion/families/386/candidate-v1/animated-preview.gif", "mobile-locomotion/families/386/candidate-v1/manifest-reviewed.json", "20 erzeugte Posen plus zwölf native Spiegelableitungen. Gesicht, Schulterdetails und Helligkeit weichen ab; Kandidat."),
        ("Rennen · stationär 182/0 · acht Perspektiven", "stationary-locomotion/run-182-0/run-eight-view-review-sheet-2x.png", "stationary-locomotion/run-182-0/run-review-manifest.json", "32 ausgewählte Phasen. Rückseite links auf gemeinsame Kamera korrigiert; Körperhöhe, Taschen und Farbe variieren weiterhin."),
        ("Klettern · 381 · acht Perspektiven", "traversal/381-climb-eight-views-v1/cycle-eight-views.gif", "traversal/381-climb-eight-views-v1/manifest.json", "32 Reach/Pull/Tuck/Push-Phasen. Front-rechts-Kamera und frontale Push-Handphase korrigiert. Verdeckte Gliedmaßen und genaue Wandkontakte bleiben Prüflücken."),
        ("Rennen · 381 · acht Perspektiven", "mobile-locomotion/families/381/candidate-v1/animated-preview.gif", "mobile-locomotion/families/381/candidate-v1/manifest-reviewed.json", "20 erzeugte Posen plus zwölf ausdrücklich gekennzeichnete Spiegelableitungen nach nativem Renderer. Stoff-/Körpervariation bleibt. Kein pauschaler Qualitäts-PASS."),
        ("Schwimmen · 381 · acht Perspektiven", "traversal/381-swim-eight-views-v2/cycle-eight-views.gif", "traversal/381-swim-eight-views-v2/manifest.json", "32 Frames; Front und Rückseite neu mit niedrigerer Kameraprojektion. Unterschiede zwischen Viertelansichten bleiben Kandidatengrenzen."),
        ("Portrait 195 · 54 Mundvarianten", "portraits/npc195-complete-candidate/mouths54-contact.png", "portraits/npc195-complete-candidate/manifest.json", "54 Augen- und 54 Mundframes bilden den vollständigen Kunstkandidaten. C/E sind teilweise schwach unterscheidbar; keine pauschale Phonem- oder zeitliche Freigabe."),
        ("Stehen · 182/9 · acht Perspektiven", "idle-sleep/native-182-9-idle-native-review.png", "idle-sleep/native-idle-candidate-coverage.json", "Blonde Figur mit grauem Kleid und goldenem Schal. Originalfront erhalten; frühere falsche Kapuzenfassung ausdrücklich verworfen. Identische Quelle 357/2 verwendet denselben Kandidaten."),
        ("Stehen · 182/54 · acht Perspektiven", "idle-sleep/native-182-54-idle-native-review.png", "idle-sleep/native-idle-candidate-coverage.json", "Kapuzenfigur; Originalfront erhalten. Quellidentisch mit 182/55. Neue Seitenansichten glatter als das Original."),
        ("Nahkampf · 382 · Perspektiven 0–3", "combat/melee-382-directions-0-3-review.png", "combat/candidate-melee-382-eight-directions/manifest.json", "Weitere 32 sourcegebundene Phasen in acht Ansichten. Rückseite links begrenzt prüfbar durch verdeckten Griff."),
        ("Rennen · 385 · acht Perspektiven", "mobile-locomotion/eight-view-candidate-v1/animated-preview.gif", "mobile-locomotion/eight-view-candidate-v1/manifest-reviewed.json", "32 verschiedene Kontakt-/Flugframes. Beide Kontaktseiten geprüft; Farb- und Detailabweichungen bleiben. Weltmaßstab separat dokumentiert."),
        ("Nahkampf · 381 · Perspektiven 0–3", "combat/melee-381-directions-0-3-review.png", "combat/candidate-melee-381-eight-directions/manifest.json", "Neue Identität mit weißer Tunika. Rückseite links: Waffenarm teilweise verdeckt; keine vollständige Anatomiefreigabe."),
        ("Nahkampf · 381 · Perspektiven 4–7", "combat/melee-381-directions-4-7-review.png", "combat/candidate-melee-381-eight-directions/manifest.json", "32 Frames dieses Kandidaten. Frühere Beutelentfernung korrigiert: echte native Gehrückseiten zeigen den grünen Beutel; erzeugte Idle-Bilder waren dafür keine Originalquelle."),
        ("Schwimmen · 381 · früherer Profilpilot", "traversal/381-swim-right-cycle-v2/contact-review.png", "traversal/381-swim-right-cycle-v2/manifest.json", "Vier Phasen mit korrigierter Körperlänge. Der aktuelle Achtansichten-Kandidat ist oben verlinkt."),
        ("Portrait 195 · 54 Augenvarianten", "portraits/npc195-eyes-v1/selected54-contact.png", "portraits/npc195-eyes-v1/selected54-manifest.json", "Sechs Stimmungen × drei Blickrichtungen × drei Lidstände. Guarded-HALF wirkt asymmetrisch; zeitlicher Übergang offen. Alle 54 Mundvarianten liegen ebenfalls vor."),
        ("Nahkampf · 385 · Perspektiven 0–3", "combat/melee-385-directions-0-3-review.png", "combat/candidate-melee-385-eight-directions/manifest.json", "Vier Phasen in jeder Ansicht. Rückseite links: Waffenarm teilweise verdeckt; Strike/Recovery bleiben in der Sichtprüfung offen."),
        ("Nahkampf · 385 · Perspektiven 4–7", "combat/melee-385-directions-4-7-review.png", "combat/candidate-melee-385-eight-directions/manifest.json", "Weitere vier Ansichten desselben 32-Frame-Kandidaten. Keine Nutzerabnahme oder Runtime-Integration."),
        ("Springen · 381 · acht Perspektiven", "traversal/381-jump-eight-views-v1/cycle-eight-views.gif", "traversal/381-jump-eight-views-v1/manifest.json", "32 Frames vorhanden. Größen, Farbgebung und Beutelposition unterscheiden sich teilweise zwischen Ansichten. Kandidat, nicht integriert."),
        ("Gehen · 182/0 · linkes Profil", "stationary-locomotion/cycle-review-4fps.gif", "stationary-locomotion/cycle-review.json", "Vier einzeln erzeugte Phasen. Native Körperhöhe 76–77 px; kräftigerer Hosenfarbton bleibt sichtbar."),
        ("Rennen · 385 · linkes Profil", "mobile-locomotion/left-cycle-v2/preview.gif", "mobile-locomotion/left-cycle-v2/manifest-reviewed.json", "Vier Kontakt-/Flugphasen mit Armwechsel. Farb- und Faltenabweichungen bleiben; vollständiger Achtansichten-Kandidat oben."),
        ("Nahkampf · 385 · früherer Profilpilot", "combat/melee-right-cycle-preview.gif", "combat/candidate-melee-right-cycle/manifest.json", "Ready, Windup, Strike und Recovery mit gleicher Waffenhand. Der vollständige Achtansichten-Kandidat ist oben verlinkt."),
        ("Nahkampf · 385 · frontal", "combat/melee-front-cycle-2x-review.png", "combat/candidate-melee-front-cycle/manifest.json", "Vier frontal erzeugte Phasen. Dies ist ein Standbildvergleich, kein Nachweis spielinterner Animation."),
        ("Stehen · DET 10015/0 · acht Perspektiven", "idle-sleep/idle-det-10015-0-native-review.png", "idle-sleep/sleep-candidate-coverage.json", "Originalfront erhalten. Neue Seitenansichten sind kräftiger und weicher gezeichnet; Augen-/Brillendetails noch nicht identisch."),
        ("Schlafbilder · DET-Gelehrte", "idle-sleep/sleep-det-10015-cutout-review.png", "idle-sleep/sleep-candidate-coverage.json", "Fünf neue Kandidaten mit geschlossenen Augen, grüner Decke und weißem Kissen. Weitere DET-Batches werden separat geprüft."),
        ("Portrait 195 · Emotionsmaster", "portraits/npc195-masters-v1/masters-review.png", "portraits/coverage.json", "Neutral bleibt Original. Fünf erzeugte Ausdrücke; die daraus entstandenen 108 Augen-/Mundteile sind oben verlinkt."),
        ("Portrait 195 · neutrales Zwischenlid", "portraits/npc195-eyes-v1/neutral-half-v2-review.png", "portraits/coverage.json", "Einzelbild-Pilot mit sichtbarer Zwischenöffnung. Kein allgemeiner Portrait-PASS."),
    ]
    cards = []
    for title, image, manifest, description in examples:
        if not (HERE / image).is_file():
            continue
        manifest_link = f'<a href="{html.escape(manifest)}">Dateien und Prüfdaten</a>' if (HERE / manifest).is_file() else ""
        cards.append(f'<article><h2>{html.escape(title)}</h2><p>{html.escape(description)}</p><a href="{html.escape(image)}"><img loading="lazy" src="{html.escape(image)}" alt="{html.escape(title)}"></a>{manifest_link}</article>')
    document = '''<!doctype html><html lang="de"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>DFU NPC-Kunst – Arbeitsstand</title><style>
    :root{color-scheme:dark}body{font:16px/1.55 system-ui,sans-serif;max-width:1400px;margin:auto;padding:28px;background:#191e23;color:#e3e8eb}h1{font-size:30px}h2{font-size:21px}p{max-width:1000px}a{color:#91c9ee}article{margin:26px 0;padding:20px;background:#242b32;border:1px solid #3e4852;border-radius:8px}img{display:block;max-width:100%;height:auto;image-rendering:pixelated;margin:18px 0}.notice{padding:16px;border-left:4px solid #d9ab52;background:#302d27}nav{display:flex;gap:20px;flex-wrap:wrap}
    </style><h1>DFU NPC-Kunst · Arbeitsstand vom 28. Oktober 2026</h1>
    <p class="notice">Das Gesamtziel ist offen. Hier liegen erzeugte und unterschiedlich weit geprüfte Kandidaten. Keine pauschale visuelle Freigabe, Nutzerabnahme oder spielinterne Integration. GIFs dienen der manuellen Sichtprüfung.</p>
    <nav><a href="README.md">Auftrag und Arbeitsaufteilung</a><a href="source-inventory.json">Quelleninventar</a><a href="SOURCE-REVIEW.md">Quellenprüfung</a><a href="coverage-ledger.json">Abdeckung pro Quelle und Aktion</a></nav>
    '''
    counts = inventory["counts"]
    document += f'<p>{counts["knownProviderQualifiedIdentities"]} providerqualifizierte Quelldatensätze, darunter {counts["excludedEditorMarkers"]} ausgeschlossene Editor-Markierungen. {counts["enabledMods"]} Mods aktiviert. Die Quellenzahl ist keine Anzahl fertig animierter NPCs.</p>'
    document += "".join(cards)
    document += '<details><summary>Alle exakt abgeleiteten Portrait-Teile: eigene Originale bleiben erhalten</summary><p>Dies sind überwiegend nur Mundsets oder nur Augensets. Technische Pixelprüfung bestanden; die zweite Serie wartet auf vollständige visuelle Prüfung. Vorlagenqualität und fehlende andere Gesichtshälfte bleiben offen. Kein neuer AI-Posenbestand.</p>'
    for folder_name in ("exact-feature-reuse", "exact-feature-reuse-wave2", "exact-feature-reuse-wave3"):
        folder = HERE / "portraits" / folder_name
        summary = json.loads((folder / "summary.json").read_text())
        for item in summary["candidates"]:
            manifest_path = Path(item["manifest"])
            manifest = json.loads(manifest_path.read_text())
            document += f'<p>{html.escape(item["targetId"])} · {item["parts"]} Teile · '
            for feature in manifest["featureReuse"]["frames"]:
                contact = (manifest_path.parent / f"{feature}-54-contact.png").relative_to(HERE).as_posix()
                document += f'<a href="{contact}">{html.escape(feature)}: alle 54 Zustände</a> '
            evidence = manifest_path.relative_to(HERE).as_posix()
            document += f'<a href="{evidence}">Herkunft/Prüfdaten</a></p>'
    document += '</details>'
    sleep_pages = sorted((HERE / "idle-sleep").glob("sleep-candidate-review-*.png"), key=lambda p: int(p.stem.rsplit("-", 1)[1]))
    if sleep_pages:
        document += '<details><summary>Alle neuen DET-Schlafkandidaten prüfen</summary>'
        for page in sleep_pages:
            relative = page.relative_to(HERE).as_posix()
            document += f'<a href="{relative}"><img loading="lazy" src="{relative}" alt="DET Schlafkandidaten {page.stem.rsplit("-", 1)[1]}"></a>'
        document += '</details>'
    document += '<p>Ein Bogen-Nock-Frame wurde von der Bildgenerierung mit <code>moderation_blocked</code> zurückgewiesen. Das vorhandene Draw-/Release-Paar schließt diese Lücke nicht. Portrait 152 bleibt wegen frontal fast offener HALF-Augen ungeklärt.</p></html>'
    (HERE / "review.html").write_text(document, encoding="utf-8")
    print(json.dumps({"review": str(HERE / "review.html"), "includedActualPreviews": len(cards)}))


if __name__ == "__main__":
    main()
