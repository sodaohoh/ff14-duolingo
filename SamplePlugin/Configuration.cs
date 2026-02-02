using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace SamplePlugin;

public enum TargetLanguage
{
    English,
    Japanese,
    ChineseTraditional
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public TargetLanguage TargetLanguage { get; set; } = TargetLanguage.English;

    public int CastBarHeight { get; set; } = 44; // 可調整的高度

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
