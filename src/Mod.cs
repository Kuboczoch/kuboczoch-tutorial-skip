using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace KuboczochTutorialSkip;

public sealed class TutorialSkipMod : ModSystem
{
    private const string PatchId = "kuboczoch.tutorialskip";
    private Harmony? harmony;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        harmony = new Harmony(PatchId);
        harmony.Patch(AccessTools.Method(typeof(HudTutorial), nameof(HudTutorial.loadHud)),
            transpiler: new HarmonyMethod(typeof(HudPatch), nameof(HudPatch.Transpile)));
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(PatchId);
        harmony = null;
    }
}

public static class SkipController
{
    public static TutorialStepBase? CurrentStep(ITutorial? tutorial) =>
        tutorial?.GetTutorialSteps(true).FirstOrDefault(s => !s.Complete);

    // Bind to the displayed instance AND displayed step. A queued/stale click cannot skip the next step.
    public static bool TrySkip(ITutorial? active, ITutorial expected, TutorialStepBase displayed)
    {
        if (!ReferenceEquals(active, expected) || !ReferenceEquals(CurrentStep(active), displayed)) return false;
        expected.Skip(1);
        expected.Save();
        return true;
    }
}

public static class HudPatch
{
    private static readonly FieldInfo Api = AccessTools.Field(typeof(GuiDialog), "capi");
    private static readonly FieldInfo Current = AccessTools.Field(typeof(ModSystemTutorial), "currentTutorialInst");
    private static readonly MethodInfo Reload = AccessTools.Method(typeof(ModSystemTutorial), "reloadTutorialPage");
    private static readonly MethodInfo Stop = AccessTools.Method(typeof(ModSystemTutorial), "StopActiveTutorial");

    public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var list = instructions.Select(i => new CodeInstruction(i)).ToList();
        var sites = list.Select((instruction, index) => (instruction, index)).Where(p =>
            p.instruction.operand is MethodInfo m && m.DeclaringType == typeof(GuiComposer) && m.Name == "Compose").ToArray();
        if (sites.Length != 1 || sites[0].instruction.operand is not MethodInfo compose ||
            compose.GetParameters().Length != 1 || compose.GetParameters()[0].ParameterType != typeof(bool))
            throw new InvalidOperationException("Unsupported tutorial HUD composition signature; refusing to install skip patch.");
        // Stack at Compose(bool): composer, bool. Preserve both and compose through the helper.
        var site = sites[0].index;
        var injected = new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(HudPatch), nameof(AddButton)))
        };
        injected[0].labels.AddRange(list[site].labels);
        list.RemoveAt(site);
        list.InsertRange(site, injected);
        return list;
    }

    public static GuiComposer AddButton(GuiComposer composer, bool focusFirstElement, HudTutorial hud, string pagecode)
    {
        var api = (ICoreClientAPI)Api.GetValue(hud)!;
        var system = api.ModLoader.GetModSystem<ModSystemTutorial>();
        var tutorial = (ITutorial?)Current.GetValue(system);
        var displayed = SkipController.CurrentStep(tutorial);
        if (tutorial == null || displayed == null || tutorial.PageCode != pagecode) return composer.Compose(focusFirstElement);
        var richtext = composer.GetRichtext("richtext");
        bool queued = false;
        bool Click()
        {
            if (queued) return true;
            queued = true;
            api.Event.EnqueueMainThreadTask(() =>
            {
                if (!hud.IsOpened()) return;
                if (!SkipController.TrySkip((ITutorial?)Current.GetValue(system), tutorial, displayed)) return;
                Reload.Invoke(system, null);
                if (tutorial.Complete) Stop.Invoke(system, null);
                else hud.loadHud(tutorial.PageCode);
            }, "kuboczoch-skip-tutorial-step");
            return true;
        }
        // Match vanilla small-button font; reserve a comfortable 130 x 32 UI-scaled hit target.
        var button = new SkipButton(api, Lang.Get("kuboczochtutorialskip:skip"), Click, richtext);
        composer.BeginChildElements(richtext.Bounds.ParentBounds)
            .AddInteractiveElement(button, "kuboczoch-skip-step")
            .EndChildElements();
        return composer.Compose(focusFirstElement);
    }
}

public sealed class SkipButton : GuiElementTextButton
{
    private readonly GuiElementRichtext text;
    public SkipButton(ICoreClientAPI api, string label, ActionConsumable click, GuiElementRichtext text)
        : base(api, label, CairoFont.SmallButtonText(EnumButtonStyle.Normal),
            CairoFont.SmallButtonText(EnumButtonStyle.Normal), click, ElementBounds.Fixed(0, 0, 130, 32), EnumButtonStyle.Normal)
    {
        this.text = text;
    }

    public override void BeforeCalcBounds()
    {
        // Richtext is registered first; use its calculated height, including wrapping and GUI-scale changes.
        Bounds.fixedX = text.Bounds.fixedX;
        Bounds.fixedY = text.Bounds.fixedY + text.Bounds.fixedHeight + 10;
        base.BeforeCalcBounds();
    }
}
