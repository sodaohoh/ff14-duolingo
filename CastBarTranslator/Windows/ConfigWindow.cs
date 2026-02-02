using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace CastBarTranslator.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration _configuration;
    private readonly Plugin _plugin;

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
        ImGui.Text("Language Settings");
        ImGui.Separator();
        ImGui.Spacing();

        // Top language selection (learning target)
        ImGui.Text("Top (Learning Target):");
        var topLang = _configuration.TopLanguage;
        ImGui.SetNextItemWidth(200);
        if (ImGui.BeginCombo("##TopLanguage", topLang.ToString()))
        {
            foreach (var lang in Enum.GetValues<GameLanguage>())
            {
                if (ImGui.Selectable(lang.ToString(), lang == topLang))
                {
                    _configuration.TopLanguage = lang;
                    _configuration.Save();
                    _plugin.ReloadDataSources();
                }
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();

        // Bottom language selection (native/reference)
        ImGui.Text("Bottom (Native/Reference):");
        var bottomLang = _configuration.BottomLanguage;
        ImGui.SetNextItemWidth(200);
        if (ImGui.BeginCombo("##BottomLanguage", bottomLang.ToString()))
        {
            foreach (var lang in Enum.GetValues<GameLanguage>())
            {
                if (ImGui.Selectable(lang.ToString(), lang == bottomLang))
                {
                    _configuration.BottomLanguage = lang;
                    _configuration.Save();
                    _plugin.ReloadDataSources();
                }
            }
            ImGui.EndCombo();
        }

        // Warning if same language selected
        if (_configuration.TopLanguage == _configuration.BottomLanguage)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1, 0.8f, 0, 1), "Warning: Same language selected for both.");
        }

        // Warning for Chinese font compatibility
        if (_configuration.TopLanguage == GameLanguage.ChineseTraditional ||
            _configuration.BottomLanguage == GameLanguage.ChineseTraditional)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1),
                "Note: Chinese may not display correctly on cast bars");
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1),
                "(game fonts don't support CJK characters)");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Chinese data status
        if (_configuration.TopLanguage == GameLanguage.ChineseTraditional ||
            _configuration.BottomLanguage == GameLanguage.ChineseTraditional)
        {
            if (_plugin.IsChineseDataLoaded)
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "Chinese Data: Loaded");
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Chinese Data: Missing");
            }

            if (ImGui.Button("Reload Data"))
            {
                _plugin.CheckAndDownloadChineseData(true);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        // Height adjustment
        var height = _configuration.CastBarHeight;
        if (ImGui.SliderInt("Cast Bar Height", ref height, 30, 60))
        {
            _configuration.CastBarHeight = height;
            _configuration.Save();
        }
        ImGui.TextDisabled("Adjusts height for two-line display.");

        // Preview
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("Preview:");
        ImGui.BeginChild("Preview", new Vector2(200, 50), true);
        ImGui.Text($"{_configuration.TopLanguage}");
        ImGui.Text($"{_configuration.BottomLanguage}");
        ImGui.EndChild();
    }
}
