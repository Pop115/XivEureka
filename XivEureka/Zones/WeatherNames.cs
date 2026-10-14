using System.Collections.Generic;
using System.Linq;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace XivEureka.Zones;

/// <summary>
/// A weather's name in the client language, from its English name (the one in ffxiv-eureka.com's
/// tables): find the Weather sheet row in English, then read its name in the game language. Without
/// a match, the English name stays.
/// </summary>
public sealed class WeatherNames(IDataManager data)
{
    private readonly Dictionary<string, string> cache = [];

    public string this[string english]
    {
        get
        {
            if (cache.TryGetValue(english, out var name)) return name;
            var englishSheet = data.GetExcelSheet<Weather>(ClientLanguage.English);
            var row = englishSheet.FirstOrDefault(w => w.Name.ExtractText() == english);
            var local = row.RowId == 0 ? null : data.GetExcelSheet<Weather>().GetRowOrDefault(row.RowId)?.Name.ExtractText();
            return cache[english] = string.IsNullOrEmpty(local) ? english : local;
        }
    }
}
