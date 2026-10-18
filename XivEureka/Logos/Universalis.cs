using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace XivEureka.Logos;

/// <summary>
/// Market board listings of the logograms, from universalis.app (crowd-sourced by the players'
/// plugins: a few minutes to a few hours old). One request per data center, merged: the player can
/// travel to any of them. Read in the background; <see cref="Markets"/> stays the last result.
/// </summary>
public sealed class Universalis
{
    private static readonly HttpClient Http = CreateHttp();
    private const int ListingsPerItem = 50;

    private Task? fetching;

    /// <summary>Last listings read, by logogram; empty before the first read.</summary>
    public IReadOnlyDictionary<uint, ShardMarket> Markets { get; private set; } = new Dictionary<uint, ShardMarket>();

    /// <summary>When <see cref="Markets"/> was read, and for which data centers.</summary>
    public DateTime? ReadAt { get; private set; }
    public IReadOnlyList<string> ReadFor { get; private set; } = [];

    /// <summary>Universalis's own date for the oldest of the data centers' last uploads.</summary>
    public DateTime? OldestUpload { get; private set; }

    public string? Error { get; private set; }
    public bool Busy => fetching is { IsCompleted: false };

    /// <summary>Starts a read unless one is running.</summary>
    public void Refresh(IReadOnlyList<uint> items, IReadOnlyList<string> dataCenters)
    {
        if (Busy || dataCenters.Count == 0) return;
        fetching = Task.Run(async () =>
        {
            try
            {
                var merged = new Dictionary<uint, List<Listing>>();
                DateTime? oldest = null;
                foreach (var dc in dataCenters)
                {
                    var url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(dc)}/{string.Join(',', items)}?listings={ListingsPerItem}&entries=0&fields=items.itemID,items.lastUploadTime,items.listings.pricePerUnit,items.listings.quantity,items.listings.worldName";
                    var json = await Http.GetStringAsync(url).ConfigureAwait(false);
                    var (listings, upload) = Parse(json, dc);
                    foreach (var (item, list) in listings)
                    {
                        if (!merged.TryGetValue(item, out var all)) merged[item] = all = [];
                        all.AddRange(list);
                    }
                    if (upload is { } u && (oldest == null || u < oldest)) oldest = u;
                }
                Markets = merged.ToDictionary(e => e.Key,
                    e => new ShardMarket(e.Key, [.. e.Value.OrderBy(l => l.PricePerUnit)]));
                ReadAt = DateTime.Now;
                ReadFor = dataCenters;
                OldestUpload = oldest;
                Error = null;
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
        });
    }

    /// <summary>
    /// Listings by item from a multi-item answer (<c>items: {id: {listings: [...]}}</c>) or a
    /// single-item one, and the oldest of the items' last upload. Kept apart to test on a capture.
    /// </summary>
    public static (Dictionary<uint, List<Listing>> Listings, DateTime? OldestUpload) Parse(string json, string dataCenter)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var result = new Dictionary<uint, List<Listing>>();
        DateTime? oldest = null;

        void Item(JsonElement item)
        {
            if (!item.TryGetProperty("itemID", out var id)) return;
            var listings = new List<Listing>();
            if (item.TryGetProperty("listings", out var array))
                foreach (var l in array.EnumerateArray())
                    listings.Add(new Listing(l.GetProperty("pricePerUnit").GetInt32(), l.GetProperty("quantity").GetInt32(),
                        l.TryGetProperty("worldName", out var world) ? world.GetString() ?? dataCenter : dataCenter, dataCenter));
            result[id.GetUInt32()] = listings;
            if (item.TryGetProperty("lastUploadTime", out var time) && time.TryGetInt64(out var ms) && ms > 0)
            {
                var when = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
                if (oldest == null || when < oldest) oldest = when;
            }
        }

        if (root.TryGetProperty("items", out var items))
            foreach (var item in items.EnumerateObject()) Item(item.Value);
        else
            Item(root);
        return (result, oldest);
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("XivEureka (+https://github.com/Pop115/XivEureka)");
        return http;
    }
}
