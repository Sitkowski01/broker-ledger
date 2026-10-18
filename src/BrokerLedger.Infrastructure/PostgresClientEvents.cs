using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using BrokerLedger.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BrokerLedger.Infrastructure;

internal sealed record ClientEventsSettings(string ConnectionString, TimeSpan RetryDelay)
{
    public const string Channel = "client_events";

    /// <summary>Ile niewyslanych sygnalow trzymamy na subskrybenta; przy wolnym kliencie gina najstarsze.</summary>
    public const int Buffer = 64;
}

/// <summary>
/// Jedno polaczenie z <c>LISTEN client_events</c> na cala aplikacje; sygnaly z wyzwalaczy (migracja ClientEvents)
/// rozdaje subskrybentom danego klienta. Polaczenie, ktore sie zerwie, wraca po chwili, a wszyscy subskrybenci
/// dostaja <see cref="ClientEventKind.Resync"/> — w przerwie mogly przepasc sygnaly, wiec niech przeczytaja stan od nowa.
/// </summary>
internal sealed partial class PostgresClientEvents(ClientEventsSettings settings, ILogger<PostgresClientEvents> logger)
    : BackgroundService, IClientEvents
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ClientId, List<Channel<ClientEvent>>> _subscribers = [];

    /// <summary>Ile razy nasluch sie polaczyl — testy czekaja na pierwsze polaczenie.</summary>
    public int Connections { get; private set; }

    /// <summary>Ilu klientow ma otwarta subskrypcje — testy sprawdzaja, ze zamknieta nie zostaje w pamieci.</summary>
    internal int SubscribedClients
    {
        get
        {
            lock (_gate)
            {
                return _subscribers.Count;
            }
        }
    }

    public async IAsyncEnumerable<ClientEvent> SubscribeAsync(
        ClientId client, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Stryker disable once Statement : bez tego slownik nizej rzuca ten sam ArgumentNullException
        ArgumentNullException.ThrowIfNull(client);
        var channel = Channel.CreateBounded<ClientEvent>(new BoundedChannelOptions(ClientEventsSettings.Buffer)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            // Stryker disable once Boolean : tylko podpowiedz dla optymalizacji kanalu, czyta zawsze jeden strumien
            SingleReader = true,
        });
        lock (_gate)
        {
            _subscribers.TryGetValue(client, out var list);
            _subscribers[client] = [.. list ?? [], channel];
        }
        try
        {
            await foreach (var e in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return e;
            }
        }
        finally
        {
            lock (_gate)
            {
                var rest = _subscribers[client].Where(c => c != channel).ToList();
                if (rest.Count == 0)
                {
                    _subscribers.Remove(client);
                }
                else
                {
                    _subscribers[client] = rest;
                }
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Stryker disable once Statement : bez return petla i tak konczy sie na warunku while
                return;
            }
            catch (Exception e)
            {
                // Baza na chwile niedostepna nie moze zatrzymac aplikacji — wyjatek z BackgroundService by to zrobil.
                // Stryker disable once Statement : samo logowanie, bez wplywu na dzialanie
                LogListenFailure(logger, e);
                await Task.Delay(settings.RetryDelay, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync(stoppingToken);
        connection.Notification += (_, notification) => Dispatch(notification.Payload);
        await using (var listen = new NpgsqlCommand($"LISTEN {ClientEventsSettings.Channel}", connection))
        {
            await listen.ExecuteNonQueryAsync(stoppingToken);
        }
        // Pierwsze polaczenie niczego nie zgubilo; kazde nastepne mowi "przeczytaj od nowa".
        if (Connections++ > 0)
        {
            Broadcast(ClientEventKind.Resync);
        }
        while (true)
        {
            await connection.WaitAsync(stoppingToken);
        }
    }

    /// <summary>Sygnal z bazy do subskrybentow klienta. Zly sygnal logujemy i pomijamy — to tylko podpowiedz.</summary>
    internal void Dispatch(string payload)
    {
        if (Parse(payload) is not { } clientEvent)
        {
            LogMalformed(logger, payload);
            return;
        }
        List<Channel<ClientEvent>>? targets;
        lock (_gate)
        {
            _subscribers.TryGetValue(clientEvent.Client, out targets);
        }
        foreach (var channel in targets ?? [])
        {
            channel.Writer.TryWrite(clientEvent);
        }
    }

    private void Broadcast(ClientEventKind kind)
    {
        List<(ClientId Client, Channel<ClientEvent> Channel)> all;
        lock (_gate)
        {
            all = [.. _subscribers.SelectMany(s => s.Value.Select(c => (s.Key, c)))];
        }
        foreach (var (client, channel) in all)
        {
            channel.Writer.TryWrite(new ClientEvent(client, kind));
        }
    }

    internal static ClientEvent? Parse(string payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            if (!root.TryGetProperty("client", out var client) || !client.TryGetGuid(out var clientId) || clientId == Guid.Empty)
            {
                return null;
            }
            var owner = new ClientId(clientId);
            return root.GetProperty("kind").GetString() switch
            {
                "ledger" => new ClientEvent(owner, ClientEventKind.Ledger),
                "order" when root.GetProperty("order").TryGetGuid(out var order) && order != Guid.Empty
                             && Enum.TryParse<OrderStatus>(root.GetProperty("status").GetString(), out var status) =>
                    new ClientEvent(owner, ClientEventKind.Order, new OrderId(order), status),
                _ => null,
            };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Listening for client events failed; retrying")]
    private static partial void LogListenFailure(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring malformed client event {Payload}")]
    private static partial void LogMalformed(ILogger logger, string payload);
}
