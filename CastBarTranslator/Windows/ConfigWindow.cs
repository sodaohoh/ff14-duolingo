using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace CastBarTranslator.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin _plugin;

    public ConfigWindow(Plugin plugin) : base(
        "Cast Bar Translator Settings###CastBarTranslatorConfig",
        ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        _plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("Cast bar display");
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("Native game cast text remains unchanged.");
        ImGui.Text("Traditional Chinese action name displays beneath it.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();


        // Data status
        if (_plugin.IsDataLoaded)
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "Data: Loaded");
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Data: Missing");
        }

        if (ImGui.Button("Reload Data"))
        {
            _plugin.ReloadData(true);
        }

    }
}
