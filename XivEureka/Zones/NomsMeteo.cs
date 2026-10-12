using System.Collections.Generic;
using System.Linq;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace XivEureka.Zones;

/// <summary>
/// Nom d'une météo dans la langue du client, à partir de son nom anglais (celui des tables de
/// ffxiv-eureka.com) : on retrouve la ligne de la feuille Weather en anglais, puis on lit son nom
/// dans la langue du jeu. Faute de correspondance, le nom anglais reste.
/// </summary>
public sealed class NomsMeteo(IDataManager donnees)
{
    private readonly Dictionary<string, string> cache = [];

    public string this[string anglais]
    {
        get
        {
            if (cache.TryGetValue(anglais, out var nom)) return nom;
            var anglaises = donnees.GetExcelSheet<Weather>(ClientLanguage.English);
            var ligne = anglaises.FirstOrDefault(w => w.Name.ExtractText() == anglais);
            var local = ligne.RowId == 0 ? null : donnees.GetExcelSheet<Weather>().GetRowOrDefault(ligne.RowId)?.Name.ExtractText();
            return cache[anglais] = string.IsNullOrEmpty(local) ? anglais : local;
        }
    }
}
