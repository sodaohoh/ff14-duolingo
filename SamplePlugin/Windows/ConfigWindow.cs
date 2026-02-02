using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace SamplePlugin.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;
    private Plugin Plugin; // 引用 Plugin 以便通知它重載資料

    public ConfigWindow(Plugin plugin) : base(
        "Dual Translation Config###NativeCastBarConfig",
        ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        this.Plugin = plugin;
        this.Configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("Target Language Settings");
        ImGui.Separator();

        ImGui.Spacing();

        // 1. 語言選擇下拉選單
        var currentLang = Configuration.TargetLanguage;
        ImGui.SetNextItemWidth(200);

        if (ImGui.BeginCombo("Language", currentLang.ToString()))
        {
            foreach (var lang in Enum.GetValues<TargetLanguage>())
            {
                if (ImGui.Selectable(lang.ToString(), lang == currentLang))
                {
                    Configuration.TargetLanguage = lang;
                    Configuration.Save();

                    // 當設定改變時，通知 Plugin 重新載入資料
                    Plugin.ReloadDataSources();
                }
            }
            ImGui.EndCombo();
        }

        // 2. 顯示目前的狀態資訊
        ImGui.Spacing();
        if (Configuration.TargetLanguage == TargetLanguage.ChineseTraditional)
        {
            if (Plugin.IsChineseDataLoaded)
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "✓ Traditional Chinese Data Loaded");
                ImGui.TextDisabled($"Source: {Plugin.ChineseDataSource}");
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "✗ Data Missing");
                if (ImGui.Button("Try Download Update"))
                {
                    // 手動觸發更新
                    Plugin.CheckAndDownloadChineseData(true);
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        // 其他設定 (例如高度調整)
        var height = Configuration.CastBarHeight;
        if (ImGui.SliderInt("CastBar Height", ref height, 30, 60))
        {
            Configuration.CastBarHeight = height;
            Configuration.Save();
        }
    }
}
