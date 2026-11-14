"""Build and package the client mod. Game files are never included."""
import json
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parent


def main():
    subprocess.run([
        "dotnet", "build", str(ROOT / "src/KuboczochTutorialSkip.csproj"),
        "-c", "Release", "--nologo",
    ], cwd=ROOT, check=True)
    info = json.loads((ROOT / "modinfo.json").read_text(encoding="utf-8"))
    output = ROOT / "dist"
    output.mkdir(exist_ok=True)
    archive = output / f"KuboczochTutorialSkip-{info['version']}.zip"
    files = [(ROOT / "modinfo.json", "modinfo.json"),
             (ROOT / "src/bin/Release/net10.0/KuboczochTutorialSkip.dll",
              "KuboczochTutorialSkip.dll")]
    files += [(p, p.relative_to(ROOT).as_posix())
              for p in sorted((ROOT / "assets").rglob("*.json"))]
    for file, name in files:
        if name.endswith(".json"):
            json.loads(file.read_text(encoding="utf-8"))
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as package:
        for file, name in files:
            entry = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o644 << 16
            package.writestr(entry, file.read_bytes())
    with zipfile.ZipFile(archive) as package:
        assert package.testzip() is None
        assert set(package.namelist()) == {name for _, name in files}
    print(archive)


if __name__ == "__main__":
    main()
