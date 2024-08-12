using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Numerics;

namespace SamplePlugin.Windows;

public class MainWindow : Window, IDisposable
{
    private string JapaneseName { get; set; } = string.Empty;
    private string EnglishName { get; set; } = string.Empty;
    private static ImFontPtr customFont;


    public MainWindow(Plugin plugin) : base(
        "Dual Casting",
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoResize)
    {
        
        var io = ImGui.GetIO();
        customFont = io.Fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\arialbd.ttf", 24);
        ImGui.GetIO().Fonts.Build(); // Rebuild the font atlas



    }

    public void SetSpellNames(string japanese, string english)
    {
        JapaneseName = japanese;
        EnglishName = english;
    }

    public override void Draw()
    {

        //ImGui.PushFont(customFont);
        ImGui.SetWindowFontScale(1.25f);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.957f, 0.903f, 0.854f, 1.0f)); // #fff9e9 color


        float windowWidth = ImGui.GetWindowWidth();
        float textWidth = ImGui.CalcTextSize(EnglishName).X;

        // Ensure the text is positioned within visible bounds
        ImGui.SetCursorPosX(Math.Max(0, windowWidth - textWidth - ImGui.GetStyle().ItemSpacing.X));


        ImGui.Text($"{EnglishName}");

        ImGui.PopStyleColor(); // Reset to default color
        ImGui.SetWindowFontScale(1f);
        //ImGui.PopFont();
        //ImGui.TreePop();
    }

    public void Dispose()
    {
        //ImGui.PopFont();
        // No additional disposal logic required unless managing other resources
    }
}
