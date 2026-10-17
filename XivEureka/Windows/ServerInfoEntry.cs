using System;
using System.Linq;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Plugin.Services;
using XivEureka.Zones;

namespace XivEureka.Windows;

/// <summary>
/// A short entry in the server info bar ("Eurêka") to open the plugin: left click opens the map in
/// Eureka and the Logos actions elsewhere, right click always the Logos actions. The tooltip gives
/// the Eorzea time and the current and next weather. Kept short on purpose: the details live in the
/// window.
/// </summary>
public sealed class ServerInfoEntry : IDisposable
{
    private const string Title = "Xiv Eurêka";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly Configuration configuration;
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly WeatherNames weatherNames;
    private readonly IDtrBarEntry entry;
    private DateTime nextUpdate = DateTime.MinValue;

    public ServerInfoEntry(Configuration configuration, IDtrBar dtrBar, IClientState clientState, IFramework framework,
        WeatherNames weatherNames, MainWindow window)
    {
        this.configuration = configuration;
        this.clientState = clientState;
        this.framework = framework;
        this.weatherNames = weatherNames;

        entry = dtrBar.Get(Title);
        entry.Text = "Eurêka";
        entry.OnClick = e =>
        {
            var inEureka = EurekaWeather.Zone(clientState.TerritoryType) != null;
            window.Open(e.ClickType == MouseClickType.Left && inEureka ? MainWindow.Tab.Map : MainWindow.Tab.Logos);
        };
        framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        entry.Remove();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (DateTime.UtcNow < nextUpdate) return;
        nextUpdate = DateTime.UtcNow + Interval;

        entry.Shown = configuration.ServerInfoEntry;
        if (!entry.Shown) return;
        var tooltip = Tooltip();
        // Only rewrite when it changes: every assignment rebuilds the bar.
        if (entry.Tooltip?.TextValue != tooltip) entry.Tooltip = tooltip;
    }

    private string Tooltip()
    {
        var clicks = Loc.T("Clic : carte (à Eurêka) ou actions Logos · clic droit : actions Logos",
            "Click: map (in Eureka) or Logos actions · right click: Logos actions");
        if (EurekaWeather.Zone(clientState.TerritoryType) is not { } zone) return clicks;
        var now = DateTimeOffset.UtcNow;
        var (h, m) = EurekaWeather.EorzeaTime(now);
        var periods = EurekaWeather.Periods(zone, now).Take(2).ToList();
        return $"{zone.Name} · ET {h:00}:{m:00}\n"
               + $"{weatherNames[periods[0].Weather]} → {weatherNames[periods[1].Weather]} {Loc.T("dans", "in")} {Formatting.Duration(periods[1].Start - now)}\n"
               + clicks;
    }
}
