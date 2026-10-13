namespace AVAMule.Engine;

/// <summary>The human colonist out and about on their development turn.</summary>
public sealed class Walker
{
    public bool InTown = true;
    public double X, Y;
    public bool HasMule;
    /// <summary>What the mule in tow is outfitted to make (null: not yet).</summary>
    public Good? Mule;
    public (int X, int Y)? Sample;
    public double TimeLeft, TimeTotal;
    /// <summary>The door the walker is standing in, so walking through it acts once.</summary>
    public string? AtDoor;
    public double MuleX, MuleY;
}

/// <summary>One thing a computer colonist does on its turn.</summary>
public sealed record Plan(int X, int Y, Good Good);

public sealed partial class GameEngine
{
    public const double PlotSize = 104;
    public const double MapWidth = Colony.Cols * PlotSize, MapHeight = Colony.Rows * PlotSize;
    public const double GrantSeconds = 0.5;
    public const double TurnSeconds = 45;
    /// <summary>The town's shops along the top of the street, and the assay office and pub below it.</summary>
    public static readonly string[] Shops = ["MULES", "FOOD", "ENERGY", "ORE", "GLOWSTONE"];
    public const double StreetTop = 190, StreetBottom = 330;

    // ───────────────────────────── the land grant ─────────────────────────────

    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public HashSet<int> Granted { get; } = [];
    private double _cursorTime;
    private readonly Dictionary<int, (int X, int Y, double Delay)> _wants = [];

    private void StartGrant()
    {
        SetPhase(Phase.Grant);
        Granted.Clear();
        CursorX = CursorY = 0;
        _cursorTime = 0;
        _wants.Clear();
        foreach (var c in Colonists.Where(c => c.Computer))
            if (Computer.ChoosePlot(this, c) is { } p) _wants[c.Index] = (p.X, p.Y, 0.08 + _rng.NextDouble() * 0.3);
        Say($"MONTH {Month}: LAND GRANT", 2.5);
        Music(true);
    }

    private bool Free(int x, int y) => Map[x, y].Owner < 0 && Map[x, y].Terrain != Terrain.Town;

    private void GrantStep(double dt)
    {
        if (PhaseTime < 1.5) return;
        _cursorTime += dt;
        // The human claims the plot under the cursor.
        if (_fireEdge && Human is { } me && !Granted.Contains(me.Index) && Free(CursorX, CursorY)) Claim(me, CursorX, CursorY);
        foreach (var (who, want) in _wants.ToList())
        {
            if (Granted.Contains(who)) continue;
            if (!Free(want.X, want.Y))
            {
                // Someone beat us to it: choose again among what's left.
                if (Computer.ChoosePlot(this, Colonists[who], CursorY * Colony.Cols + CursorX) is { } p) _wants[who] = (p.X, p.Y, want.Delay);
                else _wants.Remove(who);
                continue;
            }
            if (want.X == CursorX && want.Y == CursorY && _cursorTime >= want.Delay) Claim(Colonists[who], want.X, want.Y);
        }
        if (_cursorTime < GrantSeconds) return;
        _cursorTime = 0;
        do
        {
            CursorX++;
            if (CursorX >= Colony.Cols) { CursorX = 0; CursorY++; }
        } while (CursorY < Colony.Rows && Map[CursorX, CursorY].Terrain == Terrain.Town);
        if (CursorY >= Colony.Rows || Granted.Count == Colonists.Count || !Colony.Plots(Map).Any(p => p.P.Owner < 0 && p.P.Terrain != Terrain.Town)) StartDevelop();
        else Play(Sfx.Tick);
    }

    private void Claim(Colonist c, int x, int y)
    {
        Map[x, y].Owner = c.Index;
        Granted.Add(c.Index);
        Play(Sfx.Claim);
    }

    // ───────────────────────────── development ─────────────────────────────

    /// <summary>The order colonists take their turns this month: the poorest first.</summary>
    public List<int> TurnOrder { get; } = [];
    public int TurnIndex { get; private set; }
    public Colonist? Developer => Phase == Phase.Develop && TurnIndex < TurnOrder.Count ? Colonists[TurnOrder[TurnIndex]] : null;
    public Walker Walker { get; } = new();
    public List<Plan> Plans { get; } = [];
    public int PlansDone { get; private set; }
    private double _planClock;

    private void StartDevelop()
    {
        SetPhase(Phase.Develop);
        UpdateScores();
        TurnOrder.Clear();
        TurnOrder.AddRange(Colonists.OrderBy(c => c.Score).Select(c => c.Index));
        TurnIndex = -1;
        NextTurn();
    }

    private void NextTurn()
    {
        TurnIndex++;
        if (TurnIndex >= TurnOrder.Count)
        {
            StartProduce();
            return;
        }
        var c = Colonists[TurnOrder[TurnIndex]];
        PhaseTime = 0;
        // This month's food is eaten now: a hungry colonist has less time.
        Array.Copy(c.Goods, c.Previous, 4);
        Array.Clear(c.Used);
        Array.Clear(c.Spoiled);
        Array.Clear(c.Produced);
        int need = Colony.FoodNeed(Month);
        int eaten = Math.Min(need, c.Goods[(int)Good.Food]);
        c.Goods[(int)Good.Food] -= eaten;
        c.Used[(int)Good.Food] = eaten;
        double time = Math.Max(10, TurnSeconds * (0.3 + 0.7 * eaten / need));
        if (Month > 1 && _rng.Next(100) < 30) PersonalEvent(c);
        if (c.Computer)
        {
            Plans.Clear();
            Plans.AddRange(Computer.Develop(this, c));
            PlansDone = 0;
            _planClock = 0;
            Say($"{c.Name.ToUpperInvariant()} IS DEVELOPING", 2);
        }
        else
        {
            Walker.InTown = true;
            Walker.X = 468;
            Walker.Y = (StreetTop + StreetBottom) / 2;
            Walker.MuleX = Walker.X - 40;
            Walker.MuleY = Walker.Y;
            Walker.HasMule = false;
            Walker.Mule = null;
            Walker.Sample = null;
            Walker.AtDoor = null;
            Walker.TimeTotal = Walker.TimeLeft = time;
            Say(eaten < need ? "YOU'RE SHORT OF FOOD: LESS TIME THIS TURN" : "YOUR TURN: BUY A MULE, OUTFIT IT, PUT IT TO WORK", 3);
        }
    }

    /// <summary>Good luck for those behind, bad luck for those ahead.</summary>
    private void PersonalEvent(Colonist c)
    {
        bool leader = c.Score >= Colonists.Max(o => o.Score);
        bool good = !leader && (_rng.Next(100) < 70 || c.Score <= Colonists.Min(o => o.Score));
        int amount = 25 * (1 + Month / 3) * (1 + _rng.Next(4));
        string who = c.Computer ? c.Name.ToUpperInvariant() : "YOU";
        if (good)
        {
            string[] luck = ["A PARCEL ARRIVES FROM HOME", "AN OLD DEBT IS REPAID", "A MULE WINS A PLOUGHING CONTEST", "SPARE PARTS SELL WELL OFF-WORLD"];
            c.Money += amount;
            Say($"{luck[_rng.Next(luck.Length)]}: {who} +${amount}", 3.5);
        }
        else
        {
            string[] bad = ["A MULE KICKS THROUGH THE SHED WALL", "A DUST STORM WRECKS THE AWNINGS", "THE PLUMBING BURSTS", "A FINE FOR PARKING A MULE IN TOWN"];
            amount = Math.Min(amount, c.Money);
            c.Money -= amount;
            Say($"{bad[_rng.Next(bad.Length)]}: {who} -${amount}", 3.5);
        }
        Play(Sfx.Event);
    }

    private void DevelopStep(double dt, Controls c)
    {
        var who = Developer;
        if (who is null) return;
        if (who.Computer)
        {
            _planClock += dt;
            if (_planClock < 1.0) return;
            _planClock = 0;
            if (PlansDone < Plans.Count)
            {
                var p = Plans[PlansDone++];
                int cost = Store.MulePrice + Store.Outfit(p.Good);
                if (Store.Mules > 0 && who.Money >= cost && Map[p.X, p.Y].Owner == who.Index)
                {
                    who.Money -= cost;
                    Store.Mules--;
                    Map[p.X, p.Y].Mule = p.Good;
                    Play(Sfx.Install);
                }
                return;
            }
            NextTurn();
            return;
        }
        Walk(who, dt, c);
    }

    // ───────────────────────────── walking about ─────────────────────────────

    public static (double X, double Y, double W, double H) Door(string name)
    {
        int i = Array.IndexOf(Shops, name);
        if (i >= 0) return (i * MapWidth / Shops.Length + MapWidth / Shops.Length / 2 - 30, StreetTop - 30, 60, 34);
        return name == "ASSAY" ? (180, StreetBottom - 4, 60, 34) : (696, StreetBottom - 4, 60, 34);
    }

    private void Walk(Colonist me, double dt, Controls c)
    {
        var w = Walker;
        w.TimeLeft -= dt;
        if (w.TimeLeft <= 0)
        {
            if (w.HasMule) Say("TIME'S UP - YOUR MULE WANDERS OFF", 2.5);
            NextTurn();
            return;
        }
        double k = c.Dx != 0 && c.Dy != 0 ? 0.7071 : 1;
        if (w.InTown)
        {
            double speed = 260;
            w.X += c.Dx * speed * k * dt;
            w.Y = Math.Clamp(w.Y + c.Dy * speed * k * dt, StreetTop - 20, StreetBottom + 20);
            // Out of the town's west or east gate, onto the map beside the town.
            if (w.X < 0 || w.X > MapWidth)
            {
                bool west = w.X < 0;
                w.InTown = false;
                w.X = west ? Colony.TownX * PlotSize - 14 : (Colony.TownX + 1) * PlotSize + 14;
                w.Y = Colony.TownY * PlotSize + PlotSize / 2;
                w.AtDoor = null;
            }
            else Doors(me);
        }
        else
        {
            var under = Map[Math.Clamp((int)(w.X / PlotSize), 0, Colony.Cols - 1), Math.Clamp((int)(w.Y / PlotSize), 0, Colony.Rows - 1)];
            double speed = me.Speed(under.Terrain);
            w.X = Math.Clamp(w.X + c.Dx * speed * k * dt, 6, MapWidth - 6);
            w.Y = Math.Clamp(w.Y + c.Dy * speed * k * dt, 6, MapHeight - 6);
            int px = (int)(w.X / PlotSize), py = (int)(w.Y / PlotSize);
            if (px == Colony.TownX && py == Colony.TownY)
            {
                // Back into town through the nearest gate.
                w.InTown = true;
                bool west = w.X < (Colony.TownX + 0.5) * PlotSize;
                w.X = west ? 12 : MapWidth - 12;
                w.Y = (StreetTop + StreetBottom) / 2;
            }
            else if (_fireEdge) Act(me, px, py);
        }
        // The mule trots along behind.
        double mx = w.X - w.MuleX, my = w.Y - w.MuleY, d = Math.Sqrt(mx * mx + my * my);
        if (d > 36) { w.MuleX += mx * (d - 36) / d; w.MuleY += my * (d - 36) / d; }
    }

    /// <summary>Walking through a shop's door does that shop's business, once per visit.</summary>
    private void Doors(Colonist me)
    {
        var w = Walker;
        string? at = null;
        foreach (var name in Shops.Concat(["ASSAY", "PUB"]))
        {
            var (dx, dy, dw, dh) = Door(name);
            if (w.X >= dx && w.X <= dx + dw && w.Y >= dy && w.Y <= dy + dh) at = name;
        }
        if (at == w.AtDoor) return;
        w.AtDoor = at;
        if (at is null) return;
        switch (at)
        {
            case "MULES":
                if (w.HasMule)
                {
                    w.HasMule = false;
                    w.Mule = null;
                    Store.Mules++;
                    me.Money += Store.MulePrice;
                    Say("YOU RETURN THE MULE", 1.5);
                    Play(Sfx.Coin);
                }
                else if (Store.Mules <= 0) Say("THE CORRAL IS EMPTY - NO MULES TO SELL", 2);
                else if (me.Money < Store.MulePrice) Say($"A MULE COSTS ${Store.MulePrice}", 2);
                else
                {
                    me.Money -= Store.MulePrice;
                    Store.Mules--;
                    w.HasMule = true;
                    w.Mule = null;
                    w.MuleX = w.X;
                    w.MuleY = w.Y + 30;
                    Say($"YOU BUY A MULE FOR ${Store.MulePrice}. NOW OUTFIT IT.", 2);
                    Play(Sfx.Bray);
                }
                break;
            case "PUB":
            {
                int win = 50 * ((Month + 3) / 4) + _rng.Next((int)(w.TimeLeft * 5) + 1);
                me.Money += win;
                if (w.HasMule) Store.Mules++;
                Say($"THE PUB: YOU WIN ${win}", 2.5);
                Play(Sfx.Pub);
                NextTurn();
                break;
            }
            case "ASSAY":
                if (w.Sample is { } s)
                {
                    me.Assayed.Add(s);
                    int level = Map[s.X, s.Y].Glow;
                    Say($"ASSAY: {(level == 0 ? "NO" : level switch { 1 => "A TRACE OF", 2 => "SOME", 3 => "RICH", _ => "A HUGE SEAM OF" })} GLOWSTONE THERE", 3);
                    w.Sample = null;
                    Play(Sfx.Assay);
                }
                else Say("BRING A SAMPLE: PRESS FIRE ON ANY PLOT", 2);
                break;
            default:
            {
                var good = (Good)(Array.IndexOf(Shops, at) - 1);
                int cost = Store.Outfit(good);
                if (!w.HasMule) Say("BUY A MULE FIRST", 1.5);
                else if (w.Mule == good) Say($"IT'S ALREADY OUTFITTED FOR {good.ToString().ToUpperInvariant()}", 1.5);
                else if (me.Money < cost) Say($"OUTFITTING FOR {good.ToString().ToUpperInvariant()} COSTS ${cost}", 2);
                else
                {
                    me.Money -= cost;
                    w.Mule = good;
                    Say($"OUTFITTED FOR {good.ToString().ToUpperInvariant()} (${cost})", 2);
                    Play(Sfx.Outfit);
                }
                break;
            }
        }
    }

    /// <summary>The fire button out on the map: install the mule, or take a sample.</summary>
    private void Act(Colonist me, int px, int py)
    {
        var w = Walker;
        var plot = Map[px, py];
        if (!w.HasMule)
        {
            w.Sample = (px, py);
            Say("YOU TAKE A SAMPLE. CARRY IT TO THE ASSAY OFFICE.", 2.5);
            Play(Sfx.Sample);
            return;
        }
        if (plot.Owner != me.Index)
        {
            w.HasMule = false;
            w.Mule = null;
            Say("NOT YOUR LAND! THE MULE BOLTS AND IS GONE", 2.5);
            Play(Sfx.Runaway);
            return;
        }
        if (w.Mule is not { } good)
        {
            Say("AN EMPTY MULE IS NO USE - OUTFIT IT IN TOWN", 2);
            return;
        }
        var old = plot.Mule;
        plot.Mule = good;
        Play(Sfx.Install);
        if (old is { } o)
        {
            w.Mule = o;
            Say($"SWAPPED: YOU NOW LEAD THE {o.ToString().ToUpperInvariant()} MULE", 2);
        }
        else
        {
            w.HasMule = false;
            w.Mule = null;
            Say($"THE MULE GETS TO WORK MAKING {good.ToString().ToUpperInvariant()}", 2);
        }
    }

    /// <summary>The human ends their turn early (the key for it, or a test).</summary>
    public void EndTurn()
    {
        if (Developer is { Computer: false }) NextTurn();
    }

    // ───────────────────────────── production ─────────────────────────────

    public string? EventText { get; private set; }

    private void StartProduce()
    {
        SetPhase(Phase.Produce);
        EventText = null;
        int energyBonus = 0, foodBonus = 0;
        bool quake = false, pirates = false;
        int blight = -1;
        if (Month > 1 && _rng.Next(100) < 60)
        {
            switch (_rng.Next(8))
            {
                case 0: energyBonus = 3; EventText = "SOLAR FLARES! EVERY ENERGY MULE MAKES 3 MORE."; break;
                case 1:
                    blight = Colonists.OrderByDescending(c => c.Score).First().Index;
                    EventText = $"ROCKMITES! THEY EAT {Colonists[blight].Name.ToUpperInvariant()}'S CROPS.";
                    break;
                case 2: quake = true; EventText = "A QUAKE SHAKES THE HILLS: ORE OUTPUT IS HALVED."; break;
                case 3:
                {
                    var spots = Colony.Plots(Map).Where(p => p.P.Terrain != Terrain.Town && p.P.Terrain != Terrain.River).ToList();
                    var hit = spots[_rng.Next(spots.Count)];
                    hit.P.Glow = 4;
                    bool lost = hit.P.Mule is not null;
                    hit.P.Mule = null;
                    EventText = $"A METEOR FALLS AND LEAVES A SEAM OF GLOWSTONE{(lost ? " - AND FLATTENS A MULE" : "")}!";
                    break;
                }
                case 4: pirates = true; EventText = "SKY PIRATES RAID THE COLONY AND TAKE ALL THE GLOWSTONE."; break;
                case 5: Array.Clear(Store.Stock); EventText = "FIRE IN THE STORE! ITS STOCK IS LOST."; break;
                case 6: foodBonus = 1; energyBonus = -1; EventText = "THE RAINS COME: FARMS DO WELL, SOLAR DOES POORLY."; break;
                default:
                {
                    var working = Colony.Plots(Map).Where(p => p.P.Mule is not null).ToList();
                    if (working.Count > 0)
                    {
                        var hit = working[_rng.Next(working.Count)];
                        hit.P.Mule = null;
                        EventText = "A RADIATION STORM: ONE MULE GOES HAYWIRE AND WANDERS OFF.";
                    }
                    break;
                }
            }
            if (EventText is not null) Play(Sfx.Event);
        }

        foreach (var (_, _, p) in Colony.Plots(Map)) p.Output = -1;
        foreach (var c in Colonists)
        {
            // Each mule not making energy needs a unit of it to run.
            var mules = Colony.Plots(Map).Where(p => p.P.Owner == c.Index && p.P.Mule is not null).ToList();
            var thirsty = mules.Where(p => p.P.Mule != Good.Energy).ToList();
            int energy = c.Goods[(int)Good.Energy];
            int used = Math.Min(energy, thirsty.Count);
            c.Goods[(int)Good.Energy] -= used;
            c.Used[(int)Good.Energy] += used;
            var idle = thirsty.OrderBy(_ => _rng.Next()).Take(thirsty.Count - used).Select(p => (p.X, p.Y)).ToHashSet();
            foreach (var (x, y, p) in mules)
            {
                int amount = idle.Contains((x, y)) ? 0 : Colony.Produce(Map, x, y, _rng);
                var g = p.Mule!.Value;
                if (amount > 0)
                {
                    if (g == Good.Energy) amount = Math.Max(0, amount + energyBonus);
                    if (g == Good.Food) amount = blight == c.Index ? 0 : amount + foodBonus;
                    if (g == Good.Ore && quake) amount /= 2;
                }
                p.Output = amount;
                c.Goods[(int)g] += amount;
                c.Produced[(int)g] += amount;
            }
            if (pirates) c.Goods[(int)Good.Glowstone] = 0;
            // Spoilage: food and energy go off, and there's only so much room for ore and glowstone.
            int foodNeed = Colony.FoodNeed(Month + 1), energyNeed = thirsty.Count + 1;
            Spoil(c, Good.Food, Math.Max(0, c.Goods[0] - foodNeed * 2) / 2);
            Spoil(c, Good.Energy, Math.Max(0, c.Goods[1] - energyNeed) / 4);
            Spoil(c, Good.Ore, Math.Max(0, c.Goods[2] - 50));
            Spoil(c, Good.Glowstone, Math.Max(0, c.Goods[3] - 50));
        }
        if (pirates) Store.Stock[(int)Good.Glowstone] = 0;
        Play(Sfx.Produce);
        Prices();
    }

    private static void Spoil(Colonist c, Good g, int amount)
    {
        c.Goods[(int)g] -= amount;
        c.Spoiled[(int)g] += amount;
    }

    /// <summary>The store's prices follow supply and demand; glowstone goes by the off-world market.</summary>
    private void Prices()
    {
        int people = Colonists.Count;
        double Ratio(double demand, double supply) => Math.Clamp(demand * 1.4 / (supply + 1), 0.6, 2.4);
        int foodSupply = Colonists.Sum(c => c.Goods[0]) + Store.Stock[0];
        Store.Price[0] = (int)Math.Round(30 * Ratio(people * Colony.FoodNeed(Month + 1), foodSupply));
        int energyDemand = Colonists.Sum(c => Colony.Plots(Map).Count(p => p.P.Owner == c.Index && p.P.Mule is { } g && g != Good.Energy) + 1);
        Store.Price[1] = (int)Math.Round(25 * Ratio(energyDemand, Colonists.Sum(c => c.Goods[1]) + Store.Stock[1]));
        Store.Price[2] = (int)Math.Round(50 * Ratio(10 + Math.Max(0, 14 - Store.Mules), Colonists.Sum(c => c.Goods[2]) + Store.Stock[2]));
        Store.Price[3] = 50 + _rng.Next(101);
    }

    private void ProduceStep()
    {
        if (PhaseTime > (EventText is null ? 4 : 6)) StartAuction();
    }

    // ───────────────────────────── the month's end ─────────────────────────────

    private void StartSummary()
    {
        SetPhase(Phase.Summary);
        // The store builds new mules out of the ore it has.
        while (Store.Mules < 14 && Store.Stock[(int)Good.Ore] >= 2)
        {
            Store.Stock[(int)Good.Ore] -= 2;
            Store.Mules++;
        }
        UpdateScores();
        Play(Sfx.Gong);
    }

    private void SummaryStep()
    {
        if (PhaseTime < 1.5) return;
        if (_fireEdge || PhaseTime > (State == GameState.Attract ? 4 : 12))
        {
            if (Month >= Months)
            {
                Music(false);
                Play(Sfx.GameOver);
                if (State == GameState.Attract) { ShowHighScores(-1); return; }
                State = GameState.GameOver;
                StateTime = 0;
                return;
            }
            Month++;
            StartGrant();
        }
    }
}
