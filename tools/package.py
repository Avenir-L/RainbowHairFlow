from pathlib import Path
import io
import re
import sys
import tarfile

root = Path(__file__).resolve().parents[1]
slug = sys.argv[1]
if not re.fullmatch(r"[\w.-]+/[\w.-]+", slug):
    raise SystemExit("Expected owner/repository")
output = root / "RainbowHairFlow-0.3.0.unitypackage"
with tarfile.open(output, "w:gz") as archive:
    for meta in sorted((root / "Assets").rglob("*.meta")):
        asset = meta.with_suffix("")
        guid = re.search(rb"(?m)^guid: ([0-9a-f]{32})", meta.read_bytes())[1].decode()
        parts = {"asset.meta": meta.read_bytes(), "pathname": asset.relative_to(root).as_posix().encode()}
        if asset.is_file():
            data = asset.read_bytes()
            if asset.name == "FlowWindow.cs":
                data = data.replace(b'"Avenir-L/RainbowHairFlow"', ('"' + slug + '"').encode())
            parts["asset"] = data
        for name, data in parts.items():
            entry = tarfile.TarInfo(f"{guid}/{name}")
            entry.size = len(data)
            archive.addfile(entry, io.BytesIO(data))
print(output)
