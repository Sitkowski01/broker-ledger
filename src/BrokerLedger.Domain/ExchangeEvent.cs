namespace BrokerLedger.Domain;

/// <summary>
/// Miejsce zdarzenia w strumieniu gieldy: przebieg gieldy (start bez wznowienia zaczyna nowy) i numer
/// w obrebie spolki. Po tej parze rozpoznajemy powtorke z Kafki i wiemy, czy zlecenie juz ja rozliczylo.
/// </summary>
public sealed record EventPosition
{
    public long RunId { get; }
    public long Sequence { get; }

    public EventPosition(long runId, long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(runId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        RunId = runId;
        Sequence = sequence;
    }

    /// <summary>To samo zdarzenie albo pozniejsze: najpierw przebieg, w nim numer.</summary>
    public bool IsAtOrAfter(EventPosition other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return RunId > other.RunId || (RunId == other.RunId && Sequence >= other.Sequence);
    }

    public override string ToString() => $"{RunId}:{Sequence}";
}

/// <summary>
/// Zdarzenie z gieldy Parkiet (topic <c>exchange.events</c>), ktore zmienia pieniadze albo akcje klienta.
/// Przyjecie zlecenia do arkusza (RESTED) broker zna juz z odpowiedzi REST, wiec go tu nie ma.
/// </summary>
public abstract record ExchangeEvent
{
    private ExchangeEvent(string symbol, EventPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        Symbol = Asset.Shares(symbol).Code;
        Position = position;
    }

    public string Symbol { get; }
    public EventPosition Position { get; }

    /// <summary>Numery zlecen gieldy, ktorych dotyczy zdarzenie — kazde moze byc zleceniem klienta brokera.</summary>
    public abstract IReadOnlyList<long> ExchangeOrderIds { get; }

    /// <summary>
    /// Transakcja. Cena pochodzi od zlecenia, ktore czekalo w arkuszu (maker); strona aktywnego zlecenia
    /// (taker) mowi, kto kupil — maker zawsze ma przeciwna.
    /// </summary>
    public sealed record Trade : ExchangeEvent
    {
        public Trade(string symbol, EventPosition position, long makerOrderId, long takerOrderId, OrderSide takerSide,
            decimal price, long quantity)
            : base(symbol, position)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(makerOrderId, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(takerOrderId, 1);
            if (makerOrderId == takerOrderId)
            {
                throw new ArgumentException("an order cannot trade with itself", nameof(takerOrderId));
            }
            if (price is <= 0m or > Order.MaxPrice || decimal.Round(price, 2) != price)
            {
                throw new ArgumentOutOfRangeException(nameof(price), price, $"price must be in whole grosze, at most {Order.MaxPrice}");
            }
            ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);
            MakerOrderId = makerOrderId;
            TakerOrderId = takerOrderId;
            TakerSide = takerSide;
            Price = price;
            Quantity = quantity;
        }

        public long MakerOrderId { get; }
        public long TakerOrderId { get; }
        public OrderSide TakerSide { get; }
        public decimal Price { get; }
        public long Quantity { get; }

        public decimal Value => Price * Quantity;

        public override IReadOnlyList<long> ExchangeOrderIds => [MakerOrderId, TakerOrderId];

        /// <summary>Strona, po ktorej w tej transakcji stalo dane zlecenie.</summary>
        public OrderSide SideOf(long exchangeOrderId)
        {
            if (exchangeOrderId == TakerOrderId)
            {
                return TakerSide;
            }
            if (exchangeOrderId == MakerOrderId)
            {
                return TakerSide == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
            }
            throw new ArgumentException($"order {exchangeOrderId} is not part of this trade", nameof(exchangeOrderId));
        }
    }

    /// <summary>
    /// Reszta zlecenia zdjeta z gieldy: na prosbe (REQUESTED) albo zlecenie PKC bez ofert (NO_LIQUIDITY).
    /// <see cref="Quantity"/> to cala niewykonana reszta.
    /// </summary>
    public sealed record Cancelled : ExchangeEvent
    {
        public const int MaxReasonLength = 32;

        public Cancelled(string symbol, EventPosition position, long orderId, long quantity, string reason)
            : base(symbol, position)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(orderId, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            if (reason.Length > MaxReasonLength)
            {
                throw new ArgumentException($"reason must be at most {MaxReasonLength} characters", nameof(reason));
            }
            OrderId = orderId;
            Quantity = quantity;
            Reason = reason;
        }

        public long OrderId { get; }
        public long Quantity { get; }
        public string Reason { get; }

        public override IReadOnlyList<long> ExchangeOrderIds => [OrderId];
    }
}

/// <summary>
/// Zdarzenie z gieldy nie pasuje do zlecenia, do ktorego trafilo (inna strona, wiecej akcji niz zostalo,
/// cena ponad limit). Rozliczenie byloby bledne, wiec stajemy — to wymaga czlowieka, nie ponowienia.
/// </summary>
public sealed class ExchangeEventMismatchException(OrderId order, EventPosition position, string problem)
    : InvalidOperationException($"event {position} does not fit order {order}: {problem}")
{
    public OrderId Order { get; } = order;
}
