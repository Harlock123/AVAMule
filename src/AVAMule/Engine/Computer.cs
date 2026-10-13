namespace AVAMule.Engine;

/// <summary>
/// How the computer colonists think: which land to claim, what to make on it, and how to haggle.
/// They know where the glowstone is (they have their own assay results), feed and power themselves
/// first, and then chase whatever pays best.
/// </summary>
public static class Computer
{
    private static double Worth(GameEngine g, Good good) => good switch
    {
        Good.Food => g.Store.Price[0] * 1.2,
        Good.Energy => g.Store.Price[1] * 1.2,
        Good.Ore => g.Store.Price[2],
        _ => g.Store.Price[3] * 0.9,
    };

    /// <summary>The best use of a plot for a colonist, and what it's worth a month.</summary>
    public static (Good Good, double Value) BestUse(GameEngine g, Colonist c, int x, int y)
    {
        var p = g.Map[x, y];
        int food = Making(g, c, Good.Food), energy = Making(g, c, Good.Energy), mules = Mules(g, c);
        Good best = Good.Food;
        double bestValue = -1;
        foreach (var good in Enum.GetValues<Good>())
        {
            double value = Colony.Yield(p, good) * Worth(g, good);
            // Feed and power yourself first.
            if (good == Good.Food && food < Colony.FoodNeed(g.Month + 1)) value *= 2.2;
            if (good == Good.Energy && energy < mules + 2) value *= 2.6;
            if (value > bestValue) { bestValue = value; best = good; }
        }
        return (best, bestValue);
    }

    private static int Making(GameEngine g, Colonist c, Good good) =>
        Colony.Plots(g.Map).Where(p => p.P.Owner == c.Index && p.P.Mule == good).Sum(p => Colony.Yield(p.P, good));

    private static int Mules(GameEngine g, Colonist c) => Colony.Plots(g.Map).Count(p => p.P.Owner == c.Index && p.P.Mule is not null);

    /// <summary>The free plot a colonist wants most (only those the grant cursor hasn't passed).</summary>
    public static (int X, int Y)? ChoosePlot(GameEngine g, Colonist c, int after = -1)
    {
        (int X, int Y)? best = null;
        double bestValue = 0;
        foreach (var (x, y, p) in Colony.Plots(g.Map))
        {
            if (p.Owner >= 0 || p.Terrain == Terrain.Town || y * Colony.Cols + x <= after) continue;
            double value = BestUse(g, c, x, y).Value;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < Colony.Cols && ny < Colony.Rows && g.Map[nx, ny].Owner == c.Index) value *= 1.15;
            }
            value *= 0.9 + (x * 7 + y * 13 + c.Index * 5) % 10 / 50.0;
            if (value > bestValue) { bestValue = value; best = (x, y); }
        }
        return best;
    }

    /// <summary>What a computer colonist does on its development turn: put mules on its idle land.</summary>
    public static List<Plan> Develop(GameEngine g, Colonist c)
    {
        var plans = new List<Plan>();
        int money = c.Money - 40, mules = g.Store.Mules;
        foreach (var (x, y, p) in Colony.Plots(g.Map).Where(p => p.P.Owner == c.Index && p.P.Mule is null).OrderByDescending(p => BestUse(g, c, p.X, p.Y).Value).ToList())
        {
            // Plan as if the mules already planned were at work, so needs are counted once.
            var (good, value) = BestUse(g, c, x, y);
            p.Mule = good;
            int cost = g.Store.MulePrice + Store.Outfit(good);
            if (value <= 0 || money < cost || mules <= 0 || plans.Count >= 3) { p.Mule = null; break; }
            plans.Add(new Plan(x, y, good));
            money -= cost;
            mules--;
        }
        foreach (var plan in plans) g.Map[plan.X, plan.Y].Mule = null;
        return plans;
    }

    /// <summary>A computer colonist's part in an auction: sell its surplus, buy what it's short of.</summary>
    public static void Auction(GameEngine g, Colonist c, Good good, Trader t, Random rng)
    {
        double lo = g.Store.Buy(good), hi = g.Store.Sell(good);
        int surplus = g.Surplus(c, good);
        if (surplus > 0)
        {
            t.Role = Role.Seller;
            t.Keep = g.Need(c, good);
            t.Target = lo + (hi - lo) * (0.2 + rng.NextDouble() * 0.4);
        }
        else if (surplus < 0)
        {
            t.Role = Role.Buyer;
            t.Want = -surplus;
            bool desperate = good is Good.Food or Good.Energy && c.Goods[(int)good] == 0;
            t.Target = lo + (hi - lo) * (desperate ? 0.9 : 0.35 + rng.NextDouble() * 0.35);
        }
        else
        {
            t.Role = Role.None;
            t.Keep = c.Goods[(int)good];
        }
    }
}
