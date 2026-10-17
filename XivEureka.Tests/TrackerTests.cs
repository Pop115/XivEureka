using System.Text.Json;
using XivEureka.Tracker;
using Xunit;

namespace XivEureka.Tests;

/// <summary>Real messages from the ffxiv-eureka.com server, captured on 2026-10-14 on a tracker created for the test.</summary>
public class TrackerFormatTests
{
    private const string Initial =
        """[null,null,"instance:ZduYe9","initial_payload",{"data":{"attributes":{"data-center-id":null,"instance-id":null,"notorious-monsters":"{}","password":"vampires-system-fuchsia","prepped-notorious-monsters":"{}"},"id":"ZduYe9","relationships":{"zone":{"data":{"id":3,"type":"zone"}}},"type":"instance"}}]""";

    private const string Kill =
        """[null,null,"instance:ZduYe9","payload",{"data":{"attributes":{"notorious-monsters":"{\"38\":1792015799985}","updated-at":"2026-10-14T22:09:58.334690Z","prepped-notorious-monsters":"{\"38\":false}"},"id":"ZduYe9","type":"instance"}}]""";

    private const string Reset =
        """[null,null,"instance:ZduYe9","payload",{"data":{"attributes":{"notorious-monsters":"{}","updated-at":"2026-10-14T22:09:58.438205Z"},"id":"ZduYe9","type":"instance"}}]""";

    [Fact]
    public void Reads_the_tracker_then_its_updates()
    {
        var initial = TrackerFormat.Parse(Initial)!;
        Assert.Equal("initial_payload", initial.Event);
        var t = TrackerFormat.ReadInstance(initial.Payload.GetProperty("data"));
        Assert.Equal("ZduYe9", t.Id);
        Assert.Equal(3, t.Zone);
        Assert.Equal("vampires-system-fuchsia", t.Password);
        Assert.Empty(t.Kills);

        t = TrackerFormat.Merge(t, TrackerFormat.Parse(Kill)!.Payload.GetProperty("data"));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792015799985), t.Kills[38]);
        Assert.Empty(t.Prepped);               // "false" is not prepped
        Assert.Equal(3, t.Zone);               // missing from the update: kept
        Assert.Equal("vampires-system-fuchsia", t.Password);

        t = TrackerFormat.Merge(t, TrackerFormat.Parse(Reset)!.Payload.GetProperty("data"));
        Assert.Empty(t.Kills);
    }

    [Fact]
    public void Writes_messages_like_the_site()
    {
        Assert.Equal("""["1","1","instance:ZduYe9","phx_join",{}]""", TrackerFormat.Write("1", "1", "instance:ZduYe9", "phx_join", new { }));
        Assert.Equal("""[null,"5","phoenix","heartbeat",{}]""", TrackerFormat.Write(null, "5", "phoenix", "heartbeat", new { }));
        using var body = JsonDocument.Parse(TrackerFormat.CreationBody(3));
        Assert.Equal("3", body.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("zone-id").GetString());
    }

    [Fact]
    public void Unreadable_message_is_ignored() => Assert.Null(TrackerFormat.Parse("not json"));

    [Fact]
    public void Site_zones_and_data_centers()
    {
        Assert.Equal(3, TrackerFormat.SiteZone(795));
        Assert.Null(TrackerFormat.SiteZone(132));
        Assert.Equal(11, TrackerFormat.DataCenters["Light"]);
        Assert.Equal(6, TrackerFormat.DataCenters["chaos"]);
    }

    [Fact]
    public void Only_a_refused_join_or_a_removal_drops_the_tracker()
    {
        // Join "3" refused: the tracker is gone.
        Assert.True(TrackerSession.LosesTracker(TrackerFormat.Parse(
            """["3","3","instance:ZduYe9","phx_reply",{"response":{"reason":"not found"},"status":"error"}]""")!));
        Assert.True(TrackerSession.LosesTracker(TrackerFormat.Parse(
            """["3",null,"instance:ZduYe9","instance_removed",{}]""")!));
        // A push (ref "7") refused, e.g. the data center or a kill report: the tracker stays.
        Assert.False(TrackerSession.LosesTracker(TrackerFormat.Parse(
            """["3","7","instance:ZduYe9","phx_reply",{"response":{},"status":"error"}]""")!));
        // The join accepted.
        Assert.False(TrackerSession.LosesTracker(TrackerFormat.Parse(
            """["3","3","instance:ZduYe9","phx_reply",{"response":{},"status":"ok"}]""")!));
    }
}
