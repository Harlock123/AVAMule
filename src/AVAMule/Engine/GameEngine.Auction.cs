namespace AVAMule.Engine;

public enum Role { None, Buyer, Seller }

/// <summary>A colonist in an auction: buying or selling, at what price, and how many changed hands.</summary>
public sealed class Trader
{
    public Role Role;
    public double Price;
    public int Traded;
    /// <summary>For computer colonists: the price they head for, and how many they keep or want.</summary>
    public double Target;
    public int Keep;
    public int Want;
}

public sealed partial class GameEngine
{
    /// <summary>The order the goods are auctioned in.</summary>
    public static readonly Good[] AuctionOrder = [Good.Ore, Good.Glowstone, Good.Food, Good.Energy];
    public const double StatusSeconds = 5, DeclareSeconds = 4, TradeSeconds = 12, TradeEvery = 0.22;

    public int AuctionIndex { get; private set; }
    public Good AuctionGood => AuctionOrder[Math.Min(AuctionIndex, 3)];
    public AuctionStage Stage { get; private set; }
    public double StageTime { get; private set; }
    public Trader[] Traders { get; } = [new(), new(), new(), new()];
    /// <summary>The last trade: who sold to whom (-1 for the store), for the screen to flash.</summary>
    public (int Seller, int Buyer, int Price, double Age)? LastTrade { get; private set; }
    private double _tradeClock;

    /// <summary>What a colonist will need of a good next month.</summary>
    public int Need(Colonist c, Good g) => g switch
    {
        Good.Food => Colony.FoodNeed(Month + 1),
        Good.Energy => Colony.Plots(Map).Count(p => p.P.Owner == c.Index && p.P.Mule is { } m && m != Good.Energy) + 1,
        _ => 0,
    };

    public int Surplus(Colonist c, Good g) => c.Goods[(int)g] - Need(c, g);

    private void StartAuction()
    {
        SetPhase(Phase.Auction);
        AuctionIndex = 0;
        StartGood();
    }

    private void StartGood()
    {
        Stage = AuctionStage.Status;
        StageTime = 0;
        LastTrade = null;
        Say($"AUCTION: {AuctionGood.ToString().ToUpperInvariant()}", 2);
    }

    private void AuctionStep(double dt, Controls c)
    {
        StageTime += dt;
        var g = AuctionGood;
        switch (Stage)
        {
            case AuctionStage.Status:
                if (StageTime > (Human is null ? 2.5 : StatusSeconds) || (StageTime > 1 && _fireEdge && Human is not null)) Declare();
                break;
            case AuctionStage.Declare:
                if (Human is { } me)
                {
                    if (_upEdge) { Traders[me.Index].Role = Role.Seller; Play(Sfx.Menu); }
                    if (_downEdge) { Traders[me.Index].Role = Role.Buyer; Play(Sfx.Menu); }
                }
                if (StageTime > (Human is null ? 1.5 : DeclareSeconds)) BeginTrading();
                break;
            case AuctionStage.Trade:
                Trade(dt, c);
                // With only computers trading, the bell rings once the bargaining has stopped.
                bool quiet = Human is null && StageTime > 7 && (LastTrade is null || LastTrade.Value.Age > 1.5);
                if (StageTime > TradeSeconds || quiet) NextGood();
                break;
        }
        if (LastTrade is { } lt) LastTrade = lt with { Age = lt.Age + dt };
    }

    private void Declare()
    {
        Stage = AuctionStage.Declare;
        StageTime = 0;
        var g = AuctionGood;
        foreach (var c in Colonists)
        {
            var t = Traders[c.Index];
            t.Traded = 0;
            if (c.Computer) Computer.Auction(this, c, g, t, _rng);
            else t.Role = Surplus(c, g) > 0 ? Role.Seller : Role.Buyer;
        }
    }

    private void BeginTrading()
    {
        Stage = AuctionStage.Trade;
        StageTime = 0;
        _tradeClock = 0;
        var g = AuctionGood;
        foreach (var t in Traders) t.Price = t.Role == Role.Seller ? Store.Sell(g) : Store.Buy(g);
        Play(Sfx.Gong);
    }

    private void NextGood()
    {
        AuctionIndex++;
        if (AuctionIndex >= AuctionOrder.Length) StartSummary();
        else StartGood();
    }

    /// <summary>
    /// Sellers come down from the store's selling price and buyers go up from its buying price. Where
    /// a buyer's bid meets a seller's asking price, goods change hands at the asking price; a seller
    /// down at the store's price sells to the store, and a buyer up at its price buys from it.
    /// </summary>
    private void Trade(double dt, Controls c)
    {
        var g = AuctionGood;
        double lo = Store.Buy(g), hi = Store.Sell(g), speed = (hi - lo) / 3.5;
        foreach (var col in Colonists)
        {
            var t = Traders[col.Index];
            if (t.Role == Role.None) continue;
            if (!col.Computer) t.Price -= c.Dy * speed * dt;
            else
            {
                // With nobody to sell to, a seller goes straight to the store; late in the trading
                // everyone settles for the store's price.
                bool noBuyers = Colonists.All(o => Traders[o.Index].Role != Role.Buyer);
                bool late = StageTime > TradeSeconds * 0.45;
                double target = t.Role == Role.Seller ? (late || noBuyers ? lo : t.Target) : late ? Math.Min(hi, t.Target + (hi - lo) * 0.3) : t.Target;
                double step = speed * 0.8 * dt;
                t.Price += Math.Clamp(target - t.Price, -step, step);
            }
            t.Price = Math.Clamp(t.Price, lo, hi);
        }
        _tradeClock += dt;
        if (_tradeClock < TradeEvery) return;
        _tradeClock = 0;

        bool CanSell(Colonist s) => Traders[s.Index].Role == Role.Seller && s.Goods[(int)g] > (s.Computer ? Traders[s.Index].Keep : 0);
        bool CanBuy(Colonist b, int price) => Traders[b.Index].Role == Role.Buyer && b.Money >= price && (!b.Computer || Traders[b.Index].Traded < Traders[b.Index].Want);
        var seller = Colonists.Where(CanSell).OrderBy(s => Traders[s.Index].Price).FirstOrDefault();
        var buyer = Colonists.Where(b => CanBuy(b, (int)Traders[b.Index].Price)).OrderByDescending(b => Traders[b.Index].Price).FirstOrDefault();
        if (seller is not null && buyer is not null && Traders[buyer.Index].Price >= Traders[seller.Index].Price && buyer.Money >= (int)Traders[seller.Index].Price)
        {
            int price = (int)Traders[seller.Index].Price;
            Move(seller, buyer, g, price);
            return;
        }
        // The store takes what sellers bring down to its price, and sells to buyers who come up to its.
        var dump = Colonists.FirstOrDefault(s => CanSell(s) && Traders[s.Index].Price <= lo + 0.5);
        if (dump is not null)
        {
            Move(dump, null, g, (int)lo);
            return;
        }
        var shopper = Colonists.FirstOrDefault(b => CanBuy(b, (int)hi) && Traders[b.Index].Price >= hi - 0.5);
        if (shopper is not null && Store.Stock[(int)g] > 0) Move(null, shopper, g, (int)hi);
    }

    /// <summary>How many units have changed hands this game (for the tests).</summary>
    public int Trades { get; private set; }

    private void Move(Colonist? seller, Colonist? buyer, Good g, int price)
    {
        Trades++;
        if (seller is not null)
        {
            seller.Goods[(int)g]--;
            seller.Money += price;
            Traders[seller.Index].Traded++;
        }
        else Store.Stock[(int)g]--;
        if (buyer is not null)
        {
            buyer.Goods[(int)g]++;
            buyer.Money -= price;
            Traders[buyer.Index].Traded++;
        }
        else Store.Stock[(int)g]++;
        LastTrade = (seller?.Index ?? -1, buyer?.Index ?? -1, price, 0);
        Play(Sfx.Trade);
    }
}
