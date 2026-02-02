using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace CastBarTranslator;

/// <summary>
/// Available languages for cast bar translation.
/// </summary>
public enum GameLanguage
{
    English,
    Japanese,
    German,
    French,
    ChineseTraditional
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    /// <summary>
    /// Default height for the cast bar text node when displaying two lines.
    /// </summary>
    public const int DefaultCastBarHeight = 44;

    public int Version { get; set; } = 1;

    /// <summary>
    /// The language displayed on top (translation you want to learn).
    /// </summary>
    public GameLanguage TopLanguage { get; set; } = GameLanguage.Japanese;

    /// <summary>
    /// The language displayed on bottom (your native/reference language).
    /// </summary>
    public GameLanguage BottomLanguage { get; set; } = GameLanguage.English;

    /// <summary>
    /// Adjustable height for the cast bar text display.
    /// </summary>
    public int CastBarHeight { get; set; } = DefaultCastBarHeight;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
