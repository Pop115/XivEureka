using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace XivEureka.Windows;

/// <summary>The plugin window: Logos actions, what to hit, map and progress tabs.</summary>
public sealed class MainWindow : Window
{
    public enum Tab
    {
        Logos,
        Hunt,
        Map,
        Progress,
    }

    private readonly LogosTab logos;
    private readonly HuntTab hunt;
    private readonly MapTab map;
    private readonly ProgressTab progress;
    private Tab? toSelect;

    public MainWindow(LogosTab logos, HuntTab hunt, MapTab map, ProgressTab progress)
        : base("Xiv Eurêka###XivEurekaLogos")
    {
        this.logos = logos;
        this.hunt = hunt;
        this.map = map;
        this.progress = progress;
        Size = new Vector2(1000, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600 + LogosTab.SheetWidth, 420),
            MaximumSize = new Vector2(4000, 3000),
        };
    }

    /// <summary>Opens the window on the given tab.</summary>
    public void Open(Tab tab)
    {
        IsOpen = true;
        toSelect = tab;
    }

    public override void Draw()
    {
        using var bar = ImRaii.TabBar("##tabs");
        if (!bar) return;
        DrawTab(Loc.T("Actions Logos", "Logos actions") + "###logos", Tab.Logos, logos.Draw);
        DrawTab(Loc.T("Quoi taper", "What to hit") + "###hunt", Tab.Hunt, hunt.Draw);
        DrawTab(Loc.T("Carte", "Map") + "###map", Tab.Map, map.Draw);
        DrawTab(Loc.T("Progression", "Progress") + "###progress", Tab.Progress, progress.Draw);
        toSelect = null;
    }

    private void DrawTab(string title, Tab tab, Action draw)
    {
        var flags = toSelect == tab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        using var item = ImRaii.TabItem(title, flags);
        if (item) draw();
    }
}
