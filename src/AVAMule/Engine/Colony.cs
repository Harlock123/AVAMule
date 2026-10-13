namespace AVAMule.Engine;

/// <summary>The stick (eight ways), the action button, and a second button.</summary>
public record struct Controls(int Dx, int Dy, bool Fire = false, bool Roll = false);

public enum Terrain { Plains, River, Hills1, Hills2, Hills3, Town }

/// <summary>What the colony makes and trades.</summary>
public enum Good { Food, Energy, Ore, Glowstone }

public enum Race { Human, Burrowkin, Skitter, Floatling }

/// <summary>One square of the colony map: what kind of ground, who owns it, and the mule working it.</summary>
public sealed class Plot
{
    public Terrain Terrain;
    /// <summary>Glowstone under the ground (0–4); unknown until someone takes it to the assay office.</summary>
    public int Glow;
    public int Owner = -1;
    public Good? Mule;
    /// <summary>What it made last month, for the production display.</summary>
    public int Output = -1;
    public bool Hills => Terrain is Terrain.Hills1 or Terrain.Hills2 or Terrain.Hills3;
}

public sealed class Colonist
{
    public string Name = "";
    public Race Race;
    public uint Colour;
    public bool Computer;
    public int Index;
    public int Money;
    public int[] Goods = new int[4];
    // This month's books, per good, for the auction's status screen.
    public int[] Previous = new int[4];
    public int[] Used = new int[4];
    public int[] Spoiled = new int[4];
    public int[] Produced = new int[4];
    /// <summary>Glowstone levels this colonist has had assayed, by plot.</summary>
    public HashSet<(int, int)> Assayed = [];
    public int Score;

    public static int StartMoney(Race r) => r switch { Race.Human => 600, Race.Floatling => 1600, _ => 1000 };

    /// <summary>How fast the colonist walks on each kind of ground (pixels a second).</summary>
    public double Speed(Terrain t)
    {
        double ground = t switch { Terrain.River => 110, Terrain.Hills1 or Terrain.Hills2 or Terrain.Hills3 => 75, _ => 150 };
        return ground * Race switch
        {
            Race.Skitter => 1.25,
            Race.Floatling => 0.85,
            Race.Burrowkin => t is Terrain.Hills1 or Terrain.Hills2 or Terrain.Hills3 ? 1.5 : 0.9,
            _ => 1.0,
        };
    }
}

/// <summary>The colony store: it buys and sells everything, and builds mules out of ore.</summary>
public sealed class Store
{
    public int[] Stock = [8, 8, 8, 0];
    public int Mules = 14;
    /// <summary>What the store pays for a unit; it sells for <see cref="Spread"/> more.</summary>
    public int[] Price = [30, 25, 50, 100];
    public const int Spread = 35;
    public int Buy(Good g) => Price[(int)g];
    public int Sell(Good g) => Price[(int)g] + Spread;
    public int MulePrice => Math.Clamp((int)(Price[(int)Good.Ore] * 1.4), 100, 300);
    public static int Outfit(Good g) => g switch { Good.Food => 25, Good.Energy => 50, Good.Ore => 75, _ => 100 };
}

/// <summary>The map and the rules of making things. All the numbers are this game's own.</summary>
public static class Colony
{
    public const int Cols = 9, Rows = 5;
    public const int TownX = 4, TownY = 2;
    public const int PlotValue = 500;

    public static Plot[,] Generate(Random rng)
    {
        var map = new Plot[Cols, Rows];
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                map[x, y] = new Plot { Terrain = x == TownX ? (y == TownY ? Terrain.Town : Terrain.River) : Terrain.Plains };
        // Hills, in ones twos and threes, never in the river.
        int hills = 0;
        while (hills < 10)
        {
            int x = rng.Next(Cols), y = rng.Next(Rows);
            if (map[x, y].Terrain != Terrain.Plains) continue;
            map[x, y].Terrain = (Terrain)((int)Terrain.Hills1 + (rng.Next(10) switch { < 5 => 0, < 8 => 1, _ => 2 }));
            hills++;
        }
        // Two glowstone seams, richest at their centre.
        for (int s = 0; s < 2; s++)
        {
            int cx = rng.Next(Cols), cy = rng.Next(Rows);
            for (int x = 0; x < Cols; x++)
                for (int y = 0; y < Rows; y++)
                {
                    int d = Math.Max(Math.Abs(x - cx), Math.Abs(y - cy));
                    int level = Math.Max(0, 4 - d * 2 + (d == 1 ? 1 : 0));
                    if (map[x, y].Terrain is not (Terrain.River or Terrain.Town)) map[x, y].Glow = Math.Max(map[x, y].Glow, level);
                }
        }
        return map;
    }

    /// <summary>The month's food each colonist needs.</summary>
    public static int FoodNeed(int month) => month <= 4 ? 3 : month <= 8 ? 4 : 5;

    /// <summary>A plot's natural yield for a kind of mule, before bonuses.</summary>
    public static int Yield(Plot p, Good g) => g switch
    {
        Good.Food => p.Terrain switch { Terrain.River => 4, Terrain.Plains => 2, _ => 1 },
        Good.Energy => p.Terrain switch { Terrain.River => 2, Terrain.Plains => 3, _ => 1 },
        Good.Ore => p.Terrain switch { Terrain.Plains => 1, Terrain.Hills1 => 2, Terrain.Hills2 => 3, Terrain.Hills3 => 4, _ => 0 },
        _ => p.Terrain is Terrain.River or Terrain.Town ? 0 : p.Glow,
    };

    /// <summary>
    /// What a plot will make: its yield, one more next to another of its owner's plots making the
    /// same thing, one more for every three such plots its owner has (up to two), and a little luck.
    /// </summary>
    public static int Produce(Plot[,] map, int x, int y, Random? luck)
    {
        var p = map[x, y];
        if (p.Mule is not { } g || p.Owner < 0) return 0;
        int amount = Yield(p, g);
        if (amount == 0) return 0;
        bool neighbour = false;
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < Cols && ny < Rows && map[nx, ny].Owner == p.Owner && map[nx, ny].Mule == g) neighbour = true;
        }
        if (neighbour) amount++;
        int same = 0;
        foreach (var q in map) if (q.Owner == p.Owner && q.Mule == g) same++;
        amount += Math.Min(2, same / 3);
        if (luck is not null) amount += luck.Next(100) switch { < 15 => -1, >= 85 => 1, _ => 0 };
        return Math.Clamp(amount, 0, 8);
    }

    public static IEnumerable<(int X, int Y, Plot P)> Plots(Plot[,] map)
    {
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                yield return (x, y, map[x, y]);
    }
}
