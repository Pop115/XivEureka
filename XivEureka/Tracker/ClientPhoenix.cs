using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace XivEureka.Tracker;

/// <summary>
/// Client Phoenix Channels minimal pour <c>wss://ffxiv-eureka.com/socket</c> : une connexion, des
/// canaux rejoints (et rejoints de nouveau après une coupure), un signal de présence toutes les
/// 30 s, une reconnexion avec délai croissant. Le réseau tourne en tâche de fond ; les messages
/// reçus attendent dans <see cref="Recus"/>, que le plugin vide sur le fil du jeu.
/// </summary>
public sealed class ClientPhoenix : IDisposable
{
    private static readonly Uri Adresse = new("wss://ffxiv-eureka.com/socket/websocket?vsn=2.0.0");
    private static readonly TimeSpan Presence = TimeSpan.FromSeconds(30);

    private readonly IPluginLog log;
    private readonly CancellationTokenSource arret = new();
    private readonly SemaphoreSlim envoi = new(1, 1);
    private readonly ConcurrentDictionary<string, (string JoinRef, object Charge)> canaux = new();
    private int compteur;
    private ClientWebSocket? socket;
    private Task? boucle;

    public ClientPhoenix(IPluginLog log) => this.log = log;

    /// <summary>Messages reçus, dans l'ordre, à traiter sur le fil du jeu.</summary>
    public ConcurrentQueue<MessagePhoenix> Recus { get; } = new();

    public bool Connecte => socket?.State == WebSocketState.Open;

    /// <summary>Rejoint un canal (ex. « instance:ZduYe9 ») ; il sera rejoint de nouveau après chaque reconnexion.</summary>
    public void Rejoindre(string sujet, object charge)
    {
        var joinRef = Ref();
        canaux[sujet] = (joinRef, charge);
        boucle ??= Task.Run(Boucle);
        _ = EnvoyerSiConnecte(FormatTracker.Ecrire(joinRef, joinRef, sujet, "phx_join", charge));
    }

    public void Quitter(string sujet)
    {
        if (!canaux.TryRemove(sujet, out var canal)) return;
        _ = EnvoyerSiConnecte(FormatTracker.Ecrire(canal.JoinRef, Ref(), sujet, "phx_leave", new { }));
    }

    /// <summary>Envoie un évènement sur un canal rejoint (ex. « set_kill_time »).</summary>
    public void Envoyer(string sujet, string evenement, object charge)
    {
        if (!canaux.TryGetValue(sujet, out var canal)) return;
        _ = EnvoyerSiConnecte(FormatTracker.Ecrire(canal.JoinRef, Ref(), sujet, evenement, charge));
    }

    public void Dispose()
    {
        arret.Cancel();
        try { boucle?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        socket?.Dispose();
        arret.Dispose();
    }

    private string Ref() => Interlocked.Increment(ref compteur).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task Boucle()
    {
        var attente = TimeSpan.FromSeconds(5);
        while (!arret.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("Origin", "https://ffxiv-eureka.com");
                ws.Options.SetRequestHeader("User-Agent", "XivEureka (plugin Dalamud privé)");
                await ws.ConnectAsync(Adresse, arret.Token);
                socket = ws;
                attente = TimeSpan.FromSeconds(5);
                foreach (var (sujet, canal) in canaux.ToArray())
                    await EnvoyerSiConnecte(FormatTracker.Ecrire(canal.JoinRef, canal.JoinRef, sujet, "phx_join", canal.Charge));

                using var presence = CancellationTokenSource.CreateLinkedTokenSource(arret.Token);
                var signaux = SignalerPresence(presence.Token);
                await Recevoir(ws);
                await presence.CancelAsync();
                await signaux;
            }
            catch (OperationCanceledException) when (arret.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or InvalidOperationException)
            {
                log.Warning("Tracker : connexion perdue ({Message}), nouvel essai dans {Attente} s", e.Message, attente.TotalSeconds);
            }
            finally
            {
                socket = null;
            }
            try { await Task.Delay(attente, arret.Token); } catch (OperationCanceledException) { return; }
            attente = TimeSpan.FromSeconds(Math.Min(60, attente.TotalSeconds * 2));
        }
    }

    private async Task Recevoir(ClientWebSocket ws)
    {
        var tampon = new byte[64 * 1024];
        var message = new StringBuilder();
        while (ws.State == WebSocketState.Open && !arret.IsCancellationRequested)
        {
            var r = await ws.ReceiveAsync(tampon, arret.Token);
            if (r.MessageType == WebSocketMessageType.Close) return;
            message.Append(Encoding.UTF8.GetString(tampon, 0, r.Count));
            if (!r.EndOfMessage) continue;
            if (FormatTracker.Lire(message.ToString()) is { } m) Recus.Enqueue(m);
            message.Clear();
        }
    }

    private async Task SignalerPresence(CancellationToken jeton)
    {
        try
        {
            while (!jeton.IsCancellationRequested)
            {
                await Task.Delay(Presence, jeton);
                await EnvoyerSiConnecte(FormatTracker.Ecrire(null, Ref(), "phoenix", "heartbeat", new { }));
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task EnvoyerSiConnecte(string texte)
    {
        var ws = socket;
        if (ws?.State != WebSocketState.Open) return; // envoyé au (re)branchement par la boucle
        await envoi.WaitAsync();
        try
        {
            await ws.SendAsync(Encoding.UTF8.GetBytes(texte), WebSocketMessageType.Text, true, arret.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            log.Warning("Tracker : envoi impossible ({Message})", e.Message);
        }
        finally
        {
            envoi.Release();
        }
    }
}
