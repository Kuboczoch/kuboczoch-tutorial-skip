using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Reflection.Emit;
using HarmonyLib;
using KuboczochTutorialSkip;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

public class AnyProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler = (_, _) => null;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    public static object Make(Type t, Func<MethodInfo, object?[]?, object?> handler)
    {
        var proxy = Create(t, typeof(AnyProxy)); ((AnyProxy)proxy).Handler = handler; return proxy;
    }
}
class Program
{
    static int passed;
    static void Check(bool yes, string message) { if (!yes) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); passed++; }
    static void Main()
    {
        var game = Path.GetFullPath(Environment.GetEnvironmentVariable("VINTAGE_STORY")
            ?? throw new InvalidOperationException("Set VINTAGE_STORY to the game directory."));
        CheckLanguages(game);
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            var file = new[] { game, Path.Combine(game, "Lib"), Path.Combine(game, "Mods") }.Select(dir => Path.Combine(dir, name.Name + ".dll")).FirstOrDefault(File.Exists);
            return file == null ? null : ctx.LoadFromAssemblyPath(file);
        };
        var cairo = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "Lib/cairo-sharp.dll"));
        if (OperatingSystem.IsLinux())
            System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(cairo, (name, assembly, path) =>
            name == "libcairo-2" ? System.Runtime.InteropServices.NativeLibrary.Load("libcairo.so.2") : IntPtr.Zero);
        Run();
    }
    static void CheckLanguages(string game)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var folder = Path.Combine(root, "assets/kuboczochtutorialskip/lang");
        var expected = Directory.GetFiles(Path.Combine(game, "assets/game/lang"), "*.json")
            .Select(Path.GetFileName).Where(name => name != "languages.json").ToHashSet();
        var files = Directory.GetFiles(folder, "*.json");
        Check(expected.Count > 0 && expected.SetEquals(files.Select(Path.GetFileName)),
            "translations cover every shipped game locale");
        foreach (var file in files.Order())
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            var entries = json.RootElement.EnumerateObject().ToArray();
            Check(entries.Length == 1 && entries[0].Name == "skip" &&
                entries[0].Value.ValueKind == System.Text.Json.JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(entries[0].Value.GetString()),
                $"{Path.GetFileName(file)} has a translated skip label and no extra keys");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run()
    {
        object? saved = null; int saves = 0; int sounds = 0;
        var guiProperty = typeof(ICoreClientAPI).GetProperty("Gui")!;
        var gui = AnyProxy.Make(guiProperty.PropertyType, (m, args) => { if (m.Name == "PlaySound") sounds++; return null; });
        var api = (ICoreClientAPI)AnyProxy.Make(typeof(ICoreClientAPI), (m, args) =>
        {
            if (m.Name == "get_Gui") return gui;
            if (m.Name == "StoreModConfig") { saved = args![0]; saves++; return null; }
            if (m.Name == "LoadModConfig") return saved;
            return null;
        });
        var tutorial = new FirstStepsTutorial(api);
        var first = SkipController.CurrentStep(tutorial)!;
        var steps = (List<TutorialStepBase>)AccessTools.Field(typeof(TutorialBase), "steps").GetValue(tutorial)!;
        Check(steps.Count == 21, "exact shipped vanilla tutorial initializes 21 steps");
        Check(ReferenceEquals(first, steps[0]), "button targets first incomplete displayed step");
        Check(SkipController.TrySkip(tutorial, tutorial, first), "click uses vanilla Skip(1)");
        Check(first.Complete && steps.Count(s => s.Complete) == 1, "exactly one step completes");
        Check(saves == 1 && sounds == 1, "skip saves immediately and plays native success sound");
        Check(!SkipController.TrySkip(tutorial, tutorial, first) && saves == 1, "duplicate/stale click cannot skip following step");
        var second = SkipController.CurrentStep(tutorial)!;
        Check(ReferenceEquals(second, steps[1]), "next incomplete step becomes target");
        Check(!SkipController.TrySkip(null, tutorial, second), "no active tutorial is safe");
        var another = new FirstStepsTutorial(api);
        Check(!SkipController.TrySkip(another, tutorial, second), "switching tutorials invalidates queued click");
        Check(SkipController.TrySkip(tutorial, tutorial, second) && saves == 2, "fresh click skips only next step");
        var reloaded = new FirstStepsTutorial(api); reloaded.GetTutorialSteps(true); reloaded.Load();
        var reloadedSteps = (List<TutorialStepBase>)AccessTools.Field(typeof(TutorialBase), "steps").GetValue(reloaded)!;
        Check(reloadedSteps.Take(2).All(s => s.Complete) && !reloadedSteps[2].Complete, "vanilla save/load retains skipped steps only");
        for (int i = 2; i < 21; i++)
        {
            var current = SkipController.CurrentStep(tutorial)!;
            Check(SkipController.TrySkip(tutorial, tutorial, current), "skip remaining step " + (i + 1));
        }
        Check(tutorial.Complete && SkipController.CurrentStep(tutorial) == null, "last step completes tutorial; no further skip target");
        Check(saves == 21, "one persistence write per skipped step");
        Vintagestory.API.Config.RuntimeEnv.GUIScale = 1;
        var rich = new GuiElementRichtext(api, Array.Empty<RichTextComponentBase>(), ElementBounds.Fixed(5, 7, 300, 200));
        var button = new SkipButton(api, "Skip step", () => true, rich);
        button.BeforeCalcBounds();
        Console.WriteLine($"Native button layout: {button.Bounds.fixedWidth} x {button.Bounds.fixedHeight}");
        Check(button.Bounds.fixedWidth >= 130 && button.Bounds.fixedHeight >= 32, "button has at least 130 x 32 UI-unit bounds");
        Check(button.Bounds.fixedY == 217 && button.Bounds.fixedX == 5, "button sits below text with 10-unit gap");
        rich.Bounds.fixedHeight = 800; button.BeforeCalcBounds();
        Check(button.Bounds.fixedY == 817, "button follows long/wrapped tutorial text on layout recalculation");
        Check(SkipController.CurrentStep(null) == null, "no tutorial produces no button target");
        var method = AccessTools.Method(typeof(HudTutorial), "loadHud");
        var original = PatchProcessor.GetOriginalInstructions(method).ToList();
        var transformed = HudPatch.Transpile(original).ToList();
        Check(transformed.Count == original.Count + 2, "transpiler modifies only one vanilla Compose call");
        Check(transformed.Count(i => i.operand is MethodInfo m && m.DeclaringType == typeof(HudPatch) && m.Name == "AddButton") == 1, "exactly one button injection");
        bool missingRejected = false; try { HudPatch.Transpile(new List<CodeInstruction>()).ToList(); } catch (InvalidOperationException) { missingRejected = true; }
        Check(missingRejected, "unexpected HUD signature rejected safely");
        var harmony = new Harmony("kuboczoch.skip.tests");
        harmony.Patch(method, transpiler: new HarmonyMethod(typeof(HudPatch), nameof(HudPatch.Transpile)));
        Check(Harmony.GetPatchInfo(method)!.Owners.Contains("kuboczoch.skip.tests"), "Harmony successfully emits patched real HUD method");
        harmony.UnpatchAll("kuboczoch.skip.tests");
        Check(!(Harmony.GetPatchInfo(method)?.Owners.Contains("kuboczoch.skip.tests") ?? false), "removal restores vanilla HUD patch state");
        var refs = typeof(TutorialSkipMod).Assembly.GetReferencedAssemblies();
        Check(refs.All(n => n.Name!.StartsWith("System.", StringComparison.Ordinal) ||
            new[] { "VintagestoryAPI", "VSSurvivalMod", "0Harmony" }.Contains(n.Name)),
            "assembly references contain only runtime and game libraries");
        Console.WriteLine($"All {passed} checks passed.");
    }
}
