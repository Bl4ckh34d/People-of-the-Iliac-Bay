"""Synchronize the authorized market requirements into the existing NPC mindmap.

Parses the embedded JSON rather than modifying JavaScript or matching prose.
The repository copy is the reviewable artifact; --open-copy can update an
already opened local copy to exactly the same contents.
"""
import argparse
import json
from pathlib import Path

FEATURES = [
    {
        "id": "LIFE-63", "title": "Stationäre und mobile NPCs: gemeinsame Personeninternas",
        "requirement": "Ein gemeinsames Personensystem: stabile Identität, tatsächlicher Haushalt mit gleichem Nachnamen und eigenem realen Bett; Level, Stats, HP/MP, getragene Kleidung, Hausschlüssel, Geld und plausible persönliche Items. Gemeinsame Wahrnehmung, Flucht, bewertete Ereignisse, zufällig begrenzte Erinnerungen und Gesprächsoptionen. Markt ist Prototyp; danach Shops, Gilden, Tempel, Inns und Straße. Keine getrennte vereinfachte Deko-Pipeline.",
        "acceptance": "Noch in Umsetzung: dieselbe Person samt Item-UIDs, Werten, Haushaltsmitgliedschaft und Bett über Arbeit, Weg, Schlaf, Angriff, Flucht und Save/Load nachweisen. Rollen-/Questschutz bewahren. Native Darstellung und tatsächliche Systemanbindung gesondert prüfen."
    },
    {
        "id": "LIFE-64", "title": "Marktpersonal: Haushalt, Standdienst und variable Pendelzeiten",
        "requirement": "Stand oder Bühnenrolle aus realer Platzierung, Tisch, Waren und Szenenbelegen bestimmen. Morgens zwischen06:00 und06:30 körperlich zum eigenen Posten zurückkehren. Feierabend zwischen17:45 und18:15: ohne Kunden früher, bei tatsächlichen Warenbetrachtern/Käufern länger; laufende Transaktion oder Gespräch nicht abbrechen. Danach realen Heimweg und Schlaf im eigenen Bett. Bis Laufkunst vorhanden ist, darf derselbe statische Sprite gleiten. Bei beobachtetem Mord/Angriff gelten gemeinsame Fluchtregeln. Inventar enthält viele belegte rollenpassende Waren; keine autonome Lieferkette.",
        "acceptance": "Offen: realen Markt vermessen/Szenenbilder prüfen; ganze Schicht mit früher/später Rückkehr, Kundschaft, Feierabend, Heimweg, Bett, Zeitsprung und Wiederkehr beobachten. Kontext nennt Identität, Rolle, Tätigkeit, Kunden, Feierabendgrund, Zuhause und tatsächliche Ereignisse. Keine Doppelperson oder positionsabhängige neue Identität."
    },
    {
        "id": "LIFE-65", "title": "Marktbesucher, Warenbetrachtung und Bühnenpublikum",
        "requirement": "Erreichbare Betrachtungsplätze vor realen Warentischen und mehrere freie Zuschauerplätze vor der echten Bühne vermessen. Mobile NPCs verweilen mit Variation, schauen auf Ware oder Aufführung und gehen zum nächsten Stand. Bei Kaufentscheidung gegenüber dem tatsächlichen Verkäufer stehen, mit echtem Gold bezahlen und tatsächliches Item bis zur Heimkehr behalten. Belegte Plätze reservieren, Zuschauer verteilen; keine Körperhaufen, kein pauschaler Spielerblick.",
        "acceptance": "Offen: zwei gleichzeitige Besucher, mehrere Tische, belegte/freie Plätze, sichtbare Zuschauermenge, Kauf mit exakten UIDs/Wallets sowie Abbruch/Gefahr/geschlossener Stand nativ testen. Alle tatsächlichen Aktivitäten und Folgen sind für stationäre und mobile Beteiligte im Kontext und bewerteten Gedächtnis."
    },
    {
        "id": "LIFE-66", "title": "Verkäufer öffnen native DFU-Menüs und Handelsfenster",
        "requirement": "Belegte Marktverkäufer bieten Kaufen/Reden und das native DFU-Shopfenster mit tatsächlichen Waren. Auch der semantische Handelswunsch im Gespräch mit Shopkeepern und tatsächlich verkaufenden Gilden-NPCs öffnet deren native Handelsschnittstelle. Privatpersonen behalten persönlichen Handel. Getragene Kleidung, Schlüssel und geschützte Items bleiben aus dem Warenangebot; echte Bestands-, Preis-, Geld- und Ereignisregeln gelten.",
        "acceptance": "Offen: Markt, nativer Shopkeeper und echte Gildenverkaufsrolle gegenüber gewöhnlichem NPC prüfen. Kaufen/Reden, freie Handelsfrage, Ablehnung, Abbruch, Zahlung, Itemtransfer, Erinnerung und Save/Load ohne doppelten Transfer oder UI-Schleife testen. Trainingsrolle allein ist kein Verkäuferbeleg."
    },
]

LINKS = {
    "identity": ["LIFE-63", "LIFE-64"], "skills": ["LIFE-63"],
    "state": ["LIFE-63"], "knowledge": ["LIFE-63", "LIFE-64", "LIFE-65"],
    "memory": ["LIFE-63", "LIFE-64", "LIFE-65"], "home": ["LIFE-64"],
    "daily": ["LIFE-64", "LIFE-65"], "economy": ["LIFE-64", "LIFE-65", "LIFE-66"],
    "places": ["LIFE-64", "LIFE-65", "LIFE-66"], "presentation": ["LIFE-66"],
    "events": ["LIFE-64", "LIFE-65"], "relations": ["LIFE-63", "LIFE-64"],
    "role": ["LIFE-63", "LIFE-64", "LIFE-66"],
}


def update(source: str) -> str:
    start = source.index("const data=") + len("const data=")
    data, consumed = json.JSONDecoder().raw_decode(source[start:])
    existing = {feature["id"]: feature for feature in data["features"]}
    for feature in FEATURES:
        item = dict(feature, links="docs/ROADMAP.md#market-prototype; docs/WORKLIST-2026-10-26.md: Markt-Abnahme")
        if item["id"] in existing:
            existing[item["id"]].update(item)
        else:
            data["features"].append(item)
    for node in data["nodes"]:
        for feature_id in LINKS.get(node["id"], []):
            if feature_id not in node["features"]:
                node["features"].append(feature_id)
    ids = [feature["id"] for feature in data["features"]]
    if len(ids) != len(set(ids)):
        raise ValueError("Duplicate feature identity")
    for node in data["nodes"]:
        if any(feature_id not in ids for feature_id in node["features"]):
            raise ValueError("Unknown node feature reference")
    previous_count = len(ids) - sum(feature["id"] not in existing for feature in FEATURES)
    result = source[:start] + json.dumps(data, ensure_ascii=False, separators=(",", ":")) + source[start + consumed:]
    result = result.replace(f"<summary>{previous_count} Funktionsbereiche", f"<summary>{len(ids)} Funktionsbereiche")
    notice = '<p id="market-implementation-status">Neu beauftragt: LIFE-63–66. Gemeinsame Interna für stationäre/mobile Personen, vollständiges Marktpersonal mit Haushalt, Kundenzeiten und Waren; Markt-/Bühnenbesuche; native Verkäufer-Menüs. Umsetzung und native Abnahme sind offen. Anforderungen und Korrekturziele bleiben über die Feature-IDs nachvollziehbar.</p>'
    if 'id="market-implementation-status"' not in result:
        result = result.replace('<p class="footer">', notice + '\n<p class="footer">', 1)
    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--open-copy", type=Path)
    args = parser.parse_args()
    result = update(args.source.read_text(encoding="utf-8"))
    if update(result) != result:
        raise ValueError("Mindmap update must be idempotent")
    args.output.write_text(result, encoding="utf-8")
    if args.open_copy:
        args.open_copy.write_text(result, encoding="utf-8")
    print("Mindmap updated; LIFE-63–66 present; feature references valid; idempotence verified.")


if __name__ == "__main__":
    main()
