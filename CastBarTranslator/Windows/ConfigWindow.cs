using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace CastBarTranslator.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin _plugin;
    private readonly Configuration _configuration;

    public ConfigWindow(Plugin plugin) : base(
        "Cast Bar Translator Settings###CastBarTranslatorConfig",
        ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        _plugin = plugin;
        _configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("Cast bar display");
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("Native game cast text remains unchanged.");
        ImGui.Text("Selected translation displays beneath it.");
        ImGui.Spacing();
        ImGui.Text("Second Language:");
        var secondLanguage = _configuration.BottomLanguage;
        ImGui.SetNextItemWidth(200);
        if (ImGui.BeginCombo("##SecondLanguage", secondLanguage.ToString()))
        {
            foreach (var language in Enum.GetValues<GameLanguage>())
            {
                if (ImGui.Selectable(language.ToString(), language == secondLanguage))
                {
                    _configuration.BottomLanguage = language;
                    _configuration.Save();
                    _plugin.ReloadDataSources();
                }
            }
            ImGui.EndCombo();
        }
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
