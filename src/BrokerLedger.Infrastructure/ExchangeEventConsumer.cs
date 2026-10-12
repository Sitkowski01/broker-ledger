using BrokerLedger.Domain;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BrokerLedger.Infrastructure;

internal sealed record ExchangeEventsSettings(string BootstrapServers, string Topic, string GroupId, TimeSpan RetryDelay);

/// <summary>
/// Czyta <c>exchange.events</c> i odklada zdarzenia do skrzynki. Offset zapisujemy dopiero po zapisie w bazie,
/// wiec awaria w srodku konczy sie powtorka (inbox ja odrzuci), a nigdy zgubionym zdarzeniem.
/// Nic tu nie rozlicza — to robi osobny proces ze skrzynki, ktory moze poczekac na zlecenie w drodze.
/// </summary>
internal sealed partial class ExchangeEventConsumer(
    ExchangeEventsSettings settings, IExchangeInbox inbox, PostgresDeadLetters deadLetters, ILogger<ExchangeEventConsumer> logger)
    : BackgroundService
{
    public enum Intake
    {
        Stored,
        Duplicate,

        /// <summary>Zdarzenie bez wplywu na srodki (RESTED) albo typ nieznany tej wersji.</summary>
        Ignored,

        /// <summary>
        /// Wiadomosc nie do przyjecia — trafia do <c>exchange_dead_letters</c> i do logu. Zatrzymanie kolejki przez
        /// nia byloby gorsze, a cichy przeskok zgubilby moze transakcje klienta.
        /// </summary>
        Malformed,
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Consume() blokuje watek — oddajemy start hosta, zanim zaczniemy czekac na wiadomosci.
        await Task.Yield();
        var config = new ConsumerConfig
        {
            BootstrapServers = settings.BootstrapServers,
            GroupId = settings.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            // Offset idzie do Kafki automatycznie, ale tylko ten, ktory sami oznaczymy po zapisie w skrzynce.
            EnableAutoCommit = true,
            EnableAutoOffsetStore = false,
        };
        using var consumer = new ConsumerBuilder<string?, string?>(config)
            .SetErrorHandler((_, error) => LogKafkaError(logger, error.Reason))
            .Build();
        consumer.Subscribe(settings.Topic);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string?, string?> result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException e)
                {
                    // Np. topic jeszcze nie istnieje, bo gielda nie wystartowala. Wyjatek zatrzymalby cala aplikacje.
                    LogKafkaError(logger, e.Error.Reason);
                    await Task.Delay(settings.RetryDelay, stoppingToken);
                    continue;
                }
                await StoreWithRetryAsync(result.Message.Value, stoppingToken);
                try
                {
                    consumer.StoreOffset(result);
                }
                catch (KafkaException e)
                {
                    // Baza lezala tak dlugo, ze Kafka uznala konsumenta za martwego i oddala partycje komus innemu.
                    // Nic nie ginie: nowy wlasciciel przeczyta wiadomosc jeszcze raz, a skrzynka odrzuci powtorke.
                    LogKafkaError(logger, e.Error.Reason);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            // Oddaje partycje grupie od razu, zamiast czekac, az broker uzna konsumenta za martwego.
            consumer.Close();
        }
    }

    /// <summary>Baza na chwile niedostepna nie moze przeskoczyc zdarzenia — probujemy, az sie uda.</summary>
    public async Task<Intake> StoreWithRetryAsync(string? message, CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                return await StoreAsync(message, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // Stryker disable once Statement : samo logowanie, bez wplywu na dzialanie
                LogStoreFailure(logger, e);
                // Stryker disable once Statement : przerwa zmienia tylko tempo ponowien, nie ich wynik
                await Task.Delay(settings.RetryDelay, stoppingToken);
            }
        }
    }

    public async Task<Intake> StoreAsync(string? message, CancellationToken cancellationToken = default)
    {
        ExchangeEvent? exchangeEvent;
        try
        {
            exchangeEvent = ParkietEvents.Parse(message);
        }
        catch (FormatException e)
        {
            // Stryker disable once Statement : samo logowanie, wiadomosc i tak trafia do dead letters
            LogMalformed(logger, e, message);
            await deadLetters.AddAsync(message, e.Message, cancellationToken);
            return Intake.Malformed;
        }
        if (exchangeEvent is null)
        {
            return Intake.Ignored;
        }
        return await inbox.AddAsync(exchangeEvent, cancellationToken) ? Intake.Stored : Intake.Duplicate;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kafka: {Reason}")]
    private static partial void LogKafkaError(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not store an exchange event, retrying")]
    private static partial void LogStoreFailure(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Skipping a malformed exchange event: {Message}")]
    private static partial void LogMalformed(ILogger logger, Exception exception, string? message);
}
