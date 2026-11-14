# Build

Requires .NET SDK 10, Python 3 and Vintage Story 1.22.7.

Set `VINTAGE_STORY` to the game directory:

```sh
# Linux / macOS
export VINTAGE_STORY="/path/to/vintagestory"
```

```powershell
# Windows PowerShell
$env:VINTAGE_STORY = "C:\path\to\Vintagestory"
```

Build and package:

```sh
python build.py
```

Windows: use `py build.py` if Python is installed through the launcher.

Output: `dist/KuboczochTutorialSkip-1.0.0.zip`.

Run regression tests:

```sh
dotnet run --project test/Test.csproj -c Release
```

Linux tests require Cairo (`libcairo.so.2`). Tests use game assemblies and substitute game services; they do not replace in-game testing. Do not commit game files or build outputs.

## Publishing

After in-game testing:

1. Use the installable ZIP from a successful `master` build. Do not upload the GitHub artifact wrapper or source archive.
2. Create the Vintage Story Mod DB page as **Kuboczoch Tutorial Skip**, author **Kuboczoch**, client-side, game version **1.22.7**. Use the README's introduction, installation and usage sections as the description; add screenshots when available.
3. Set the source/homepage link to `https://github.com/Kuboczoch/kuboczoch-tutorial-skip` and upload `KuboczochTutorialSkip-1.0.0.zip` as version **1.0.0**.

The ZIP contains `modinfo.json` and `KuboczochTutorialSkip.dll` at its root, plus English and Polish assets. No game libraries are redistributed. The build script parses packaged JSON and checks ZIP integrity.
