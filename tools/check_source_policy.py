"""Offline syntax/import gate for the project's no-regular-expressions boundary."""
import ast
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def check(root=ROOT):
    errors = []
    counts = {".py": 0, ".cs": 0}
    for directory in ("src", "mod", "tests", "tools"):
        for path in (root / directory).rglob("*"):
            if any(part in {"bin", "obj", "work", "__pycache__"} for part in path.parts) or path.suffix not in counts:
                continue
            counts[path.suffix] += 1
            source = path.read_text(encoding="utf-8-sig")
            if path.suffix == ".cs":
                if "System.Text.RegularExpressions" in source:
                    errors.append(f"{path}: forbidden regular-expression namespace")
                continue
            try:
                tree = ast.parse(source, filename=str(path))
            except SyntaxError as error:
                errors.append(str(error))
                continue
            for node in ast.walk(tree):
                names = [alias.name for alias in node.names] if isinstance(node, ast.Import) else (
                    [node.module or ""] if isinstance(node, ast.ImportFrom) else [])
                if any(name.split(".")[0] in {"re", "regex"} for name in names):
                    errors.append(f"{path}:{node.lineno}: forbidden regular-expression import")
    return counts, errors

if __name__ == "__main__":
    counts, errors = check()
    if errors:
        raise SystemExit("\n".join(errors))
    print(f"PASS source policy: {counts['.cs']} C# sources; {counts['.py']} Python sources parse.")
