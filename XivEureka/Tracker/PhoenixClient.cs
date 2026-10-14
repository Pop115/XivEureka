using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace XivEureka.Tracker;

/// <summary>
/// Minimal Phoenix Channels client for <c>wss://ffxiv-eureka.com/socket</c>: one connection, joined
/// channels (joined again after a drop), a heartbeat every 30 s, reconnection with a growing delay.
/// The network runs in the background; received messages wait in <see cref="Received"/>, which the
/// plugin drains on the game thread.
/// </summary>
public sealed class PhoenixClient : IDisposable
{
    private static readonly Uri Address = new("wss://ffxiv-eureka.com/socket/websocket?vsn=2.0.0");
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    private readonly IPluginLog log;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly ConcurrentDictionary<string, (string JoinRef, object Payload)> channels = new();
    private int counter;
    private ClientWebSocket? socket;
    private Task? loop;

    public PhoenixClient(IPluginLog log) => this.log = log;

    /// <summary>Received messages, in order, to process on the game thread.</summary>
    public ConcurrentQueue<PhoenixMessage> Received { get; } = new();

    public bool IsConnected => socket?.State == WebSocketState.Open;

    /// <summary>Joins a channel (e.g. "instance:ZduYe9"); it is joined again after every reconnection.</summary>
    public void Join(string topic, object payload)
    {
        var joinRef = NextRef();
        channels[topic] = (joinRef, payload);
        loop ??= Task.Run(Loop);
        _ = SendIfConnected(TrackerFormat.Write(joinRef, joinRef, topic, "phx_join", payload));
    }

    public void Leave(string topic)
    {
        if (!channels.TryRemove(topic, out var channel)) return;
        _ = SendIfConnected(TrackerFormat.Write(channel.JoinRef, NextRef(), topic, "phx_leave", new { }));
    }

    /// <summary>Sends an event on a joined channel (e.g. "set_kill_time").</summary>
    public void Push(string topic, string @event, object payload)
    {
        if (!channels.TryGetValue(topic, out var channel)) return;
        _ = SendIfConnected(TrackerFormat.Write(channel.JoinRef, NextRef(), topic, @event, payload));
    }

    public void Dispose()
    {
        stop.Cancel();
        try { loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        socket?.Dispose();
        stop.Dispose();
    }

    private string NextRef() => Interlocked.Increment(ref counter).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task Loop()
    {
        var delay = TimeSpan.FromSeconds(5);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("Origin", "https://ffxiv-eureka.com");
                ws.Options.SetRequestHeader("User-Agent", "XivEureka (private Dalamud plugin)");
                await ws.ConnectAsync(Address, stop.Token);
                socket = ws;
                delay = TimeSpan.FromSeconds(5);
                foreach (var (topic, channel) in channels.ToArray())
                    await SendIfConnected(TrackerFormat.Write(channel.JoinRef, channel.JoinRef, topic, "phx_join", channel.Payload));

                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var beats = Heartbeat(heartbeat.Token);
                await Receive(ws);
                await heartbeat.CancelAsync();
                await beats;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or InvalidOperationException)
            {
                log.Warning("Tracker: connection lost ({Message}), retrying in {Delay} s", e.Message, delay.TotalSeconds);
            }
            finally
            {
                socket = null;
            }
            try { await Task.Delay(delay, stop.Token); } catch (OperationCanceledException) { return; }
            delay = TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
        }
    }

    private async Task Receive(ClientWebSocket ws)
    {
        var buffer = new byte[64 * 1024];
        var message = new StringBuilder();
        while (ws.State == WebSocketState.Open && !stop.IsCancellationRequested)
        {
            var r = await ws.ReceiveAsync(buffer, stop.Token);
            if (r.MessageType == WebSocketMessageType.Close) return;
            message.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
            if (!r.EndOfMessage) continue;
            if (TrackerFormat.Parse(message.ToString()) is { } m) Received.Enqueue(m);
            message.Clear();
        }
    }

    private async Task Heartbeat(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(HeartbeatInterval, token);
                await SendIfConnected(TrackerFormat.Write(null, NextRef(), "phoenix", "heartbeat", new { }));
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task SendIfConnected(string text)
    {
        var ws = socket;
        if (ws?.State != WebSocketState.Open) return; // sent on (re)connection by the loop
        await sending.WaitAsync();
        try
        {
            await ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, stop.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            log.Warning("Tracker: could not send ({Message})", e.Message);
        }
        finally
        {
            sending.Release();
        }
    }
}
