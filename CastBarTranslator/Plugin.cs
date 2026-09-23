using System;
using System.IO;
using CastBarTranslator.Features;
using CastBarTranslator.Translation;
using CastBarTranslator.Windows;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CastBarTranslator;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;

    private readonly WindowSystem _windowSystem = new("CastBarTranslator");
    private readonly ConfigWindow _configWindow;
    private readonly TranslationService _translationService;
    private readonly CastBarFeature _castBarFeature;

    public Configuration Configuration { get; init; }

    public bool IsDataLoaded => _translationService.IsLoaded;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _translationService = new TranslationService(PluginInterface.AssemblyLocation.Directory?.FullName);

        _configWindow = new ConfigWindow(this);
        _windowSystem.AddWindow(_configWindow);

        ReloadDataSources();

        _castBarFeature = new CastBarFeature(
            TargetManager,
            AddonLifecycle,
            GameGui,
            Configuration,
            _translationService,
            Log);
        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += () => _configWindow.IsOpen = true;

        Log.Information("Cast Bar Translator loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _windowSystem.RemoveAllWindows();
        _castBarFeature.Dispose();
    }
    public void ReloadDataSources()
    {
        try
        {
            var counts = _translationService.Reload(
                GameLanguage.ChineseTraditional,
                GameLanguage.ChineseTraditional);

            Log.Information(
                $"Loaded Traditional Chinese ({counts.TopCount} entries)");
        }
        catch (FileNotFoundException ex)
        {
            Log.Warning($"Data file not found: {ex.FileName}");
            NotificationManager.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
            {
                Content = $"Missing file: {Path.GetFileName(ex.FileName)}",
                Title = "Cast Bar Translator",
                Type = Dalamud.Interface.ImGuiNotification.NotificationType.Warning,
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load language data");
            NotificationManager.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
            {
                Content = $"Failed to load language: {ex.Message}",
                Title = "Cast Bar Translator",
                Type = Dalamud.Interface.ImGuiNotification.NotificationType.Error,
            });
        }
    }

    public void ReloadData(bool showNotification)
    {
        ReloadDataSources();
        if (!showNotification || !IsDataLoaded)
            return;

        NotificationManager.AddNotification(new Dalamud.Interface.ImGuiNotification.Notification
        {
            Content = "Data reloaded successfully.",
            Title = "Cast Bar Translator",
            Type = Dalamud.Interface.ImGuiNotification.NotificationType.Success,
        });
    }
}
