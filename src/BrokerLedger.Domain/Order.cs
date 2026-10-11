namespace BrokerLedger.Domain;

/// <summary>
/// Zlecenie klienta i jego droga na gielde (saga): blokada srodkow → wyslanie → przyjecie albo odmowa.
/// Kazdy krok ma klucz idempotencji, wiec po awarii mozna go bezpiecznie powtorzyc.
/// </summary>
public sealed class Order
{
    /// <summary>Najwyzsza cena na Parkiecie: 10 mln zl.</summary>
    public const decimal MaxPrice = 10_000_000m;

    /// <summary>Najwieksze zlecenie na Parkiecie: miliard akcji.</summary>
    public const long MaxQuantity = 1_000_000_000;

    public const int MaxClientKeyLength = 100;

    private Order(OrderId id, ClientId client, string clientKey, OrderRequest request, decimal blockedAmount)
    {
        Id = id;
        Client = client;
        ClientKey = clientKey;
        Request = request;
        BlockedAmount = blockedAmount;
    }

    public OrderId Id { get; }
    public ClientId Client { get; }

    /// <summary>Idempotency-Key, z ktorym klient zlozyl zlecenie — powtorka zadania nie tworzy drugiego.</summary>
    public string ClientKey { get; }

    public OrderRequest Request { get; }
    public string Symbol => Request.Symbol;
    public OrderSide Side => Request.Side;
    public OrderType Type => Request.Type;
    public decimal? LimitPrice => Request.LimitPrice;
    public long Quantity => Request.Quantity;

    /// <summary>Ile zablokowac: przy kupnie cena x ilosc + prowizja (zl), przy sprzedazy — akcje.</summary>
    public decimal BlockedAmount { get; }

    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public long? ExchangeOrderId { get; private set; }
    public string? RejectionReason { get; private set; }
    public bool FundsBlocked { get; private set; }
    public bool FundsReleased { get; private set; }

    /// <summary>
    /// Ile razy zlecenie poszlo (albo moglo pojsc) na gielde. Zapisywane PRZED wyslaniem: tylko przy pierwszej
    /// probie wiadomo, ze nic wczesniej nie czeka w kolejce gieldy.
    /// </summary>
    public int SubmitAttempts { get; private set; }

    /// <summary>
    /// Wersja zapisu (blokada optymistyczna): zlecenia moze jednoczesnie dotykac zadanie klienta i reconciler.
    /// Podbija ja repozytorium po udanym zapisie.
    /// </summary>
    public int Version { get; private set; }

    public Asset BlockedAsset => Side == OrderSide.Buy ? Asset.Cash(Currency.Pln) : Asset.Shares(Symbol);

    public string BlockKey => $"order:{Id}:block";
    public string ReleaseKey => $"order:{Id}:release";

    /// <summary>Czy reconciler ma tu cos dokonczyc: zablokowac, wyslac, wyjasnic albo zwolnic blokade.</summary>
    public bool NeedsAttention => Status is OrderStatus.Pending or OrderStatus.Unknown
        || (Status == OrderStatus.Rejected && FundsBlocked && !FundsReleased);

    /// <summary>Nowe zlecenie. Kupno PKC nie przechodzi: bez limitu ceny nie wiadomo, ile zablokowac.</summary>
    public static Order Place(OrderId id, ClientId client, string clientKey, OrderRequest request, Commission commission)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(commission);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);
        if (clientKey.Length > MaxClientKeyLength)
        {
            throw new ArgumentException($"client key must be at most {MaxClientKeyLength} characters", nameof(clientKey));
        }
        var asset = Asset.Shares(request.Symbol);
        if (request.Quantity is < 1 or > MaxQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Quantity, $"quantity must be 1-{MaxQuantity}");
        }
        switch (request.Type)
        {
            case OrderType.Limit when request.LimitPrice is not { } price || price <= 0m || price > MaxPrice
                                      || decimal.Round(price, 2) != price:
                throw new ArgumentException($"limit order needs a price in whole grosze, at most {MaxPrice}", nameof(request));
            case OrderType.Market when request.LimitPrice is not null:
                throw new ArgumentException("market order must not have a price", nameof(request));
            case OrderType.Market when request.Side == OrderSide.Buy:
                throw new ArgumentException("market buy is not supported: without a price limit the block is unknown", nameof(request));
        }

        var blocked = request.Side == OrderSide.Sell
            ? request.Quantity
            : BuyBlock(request.LimitPrice!.Value, request.Quantity, commission);
        // Stryker disable once Equality : wartosc + prowizja nie trafi dokladnie w bilion przy dozwolonych cenach i ilosciach
        if (blocked > Posting.MaxAmount)
        {
            throw new ArgumentException($"order value exceeds {Posting.MaxAmount}", nameof(request));
        }
        return new Order(id, client, clientKey, request with { Symbol = asset.Code }, blocked);
    }

    /// <summary>Odtworzenie z bazy — bez regul skladania, bo stan juz kiedys je przeszedl.</summary>
    public static Order Restore(OrderId id, ClientId client, string clientKey, OrderRequest request,
        decimal blockedAmount, OrderStatus status, long? exchangeOrderId, string? rejectionReason,
        bool fundsBlocked, bool fundsReleased, int submitAttempts, int version) =>
        new(id, client, clientKey, request, blockedAmount)
        {
            Status = status,
            ExchangeOrderId = exchangeOrderId,
            RejectionReason = rejectionReason,
            FundsBlocked = fundsBlocked,
            FundsReleased = fundsReleased,
            SubmitAttempts = submitAttempts,
            Version = version,
        };

    public JournalEntry BlockEntry() => JournalEntries.Block(Client, BlockedAsset, BlockedAmount);

    public void MarkFundsBlocked()
    {
        Require(Status == OrderStatus.Pending, "block funds");
        FundsBlocked = true;
    }

    /// <summary>Gielda przyjela zlecenie. Powtorka z tym samym numerem (np. po wyjasnieniu) niczego nie zmienia.</summary>
    public void Accept(long exchangeOrderId)
    {
        if (Status == OrderStatus.Accepted && ExchangeOrderId == exchangeOrderId)
        {
            return;
        }
        Require(Status is OrderStatus.Pending or OrderStatus.Unknown && FundsBlocked, "accept");
        ArgumentOutOfRangeException.ThrowIfLessThan(exchangeOrderId, 1);
        Status = OrderStatus.Accepted;
        ExchangeOrderId = exchangeOrderId;
    }

    /// <summary>Zlecenie zaraz pojdzie na gielde — kolejna proba.</summary>
    public void BeginSubmission()
    {
        Require(Status is OrderStatus.Pending or OrderStatus.Unknown && FundsBlocked, "submit");
        SubmitAttempts++;
    }

    public void MarkUnknown()
    {
        Require(Status is OrderStatus.Pending or OrderStatus.Unknown, "mark unknown");
        Status = OrderStatus.Unknown;
    }

    /// <summary>Zlecenie na pewno nie weszlo na gielde. Jesli srodki byly zablokowane, czeka je zwolnienie.</summary>
    public void Reject(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Require(Status is OrderStatus.Pending or OrderStatus.Unknown, "reject");
        Status = OrderStatus.Rejected;
        RejectionReason = reason;
    }

    public JournalEntry ReleaseEntry()
    {
        Require(Status == OrderStatus.Rejected && FundsBlocked && !FundsReleased, "release funds");
        return JournalEntries.Release(Client, BlockedAsset, BlockedAmount);
    }

    public void MarkFundsReleased()
    {
        Require(Status == OrderStatus.Rejected && FundsBlocked, "mark funds released");
        FundsReleased = true;
    }

    public void RequestCancel()
    {
        Require(Status == OrderStatus.Accepted, "cancel");
        Status = OrderStatus.CancelRequested;
    }

    /// <summary>Wolane przez repozytorium po udanym zapisie.</summary>
    public void MarkSaved() => Version++;

    private static decimal BuyBlock(decimal price, long quantity, Commission commission)
    {
        var value = Money.Pln(price * quantity);
        return (value + commission.For(value)).Amount;
    }

    private void Require(bool allowed, string action)
    {
        if (!allowed)
        {
            throw new InvalidOrderTransitionException(Id, Status, action);
        }
    }
}
