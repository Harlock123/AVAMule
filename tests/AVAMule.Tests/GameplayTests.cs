using AVAMule.Engine;

namespace AVAMule.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avamule-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Game(int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        g.Tick(default);
        return g;
    }

    /// <summary>Runs until a condition holds (or gives up), pressing nothing.</summary>
    private static void Until(GameEngine g, Func<GameEngine, bool> done, int seconds = 600)
    {
        for (int i = 0; i < seconds * 60 && !done(g); i++) g.Tick(default);
    }

    private static void Press(GameEngine g, Controls c, int ticks = 1)
    {
        for (int i = 0; i < ticks; i++) g.Tick(c);
        g.Tick(default);
    }

    /// <summary>Puts the walker in a shop's door (one step there and back acts once).</summary>
    private static void Visit(GameEngine g, string door)
    {
        var (x, y, w, h) = GameEngine.Door(door);
        g.Walker.InTown = true;
        g.Walker.X = x + w / 2;
        g.Walker.Y = y + h / 2;
        g.Tick(default);
        g.Walker.Y = (GameEngine.StreetTop + GameEngine.StreetBottom) / 2;
        g.Tick(default);
    }

    [Fact]
    public void The_map_has_its_town_its_river_its_hills_and_glowstone()
    {
        var map = Colony.Generate(new Random(3));
        Assert.Equal(Terrain.Town, map[Colony.TownX, Colony.TownY].Terrain);
        for (int y = 0; y < Colony.Rows; y++)
            if (y != Colony.TownY) Assert.Equal(Terrain.River, map[Colony.TownX, y].Terrain);
        Assert.Equal(10, Colony.Plots(map).Count(p => p.P.Hills));
        Assert.Contains(Colony.Plots(map), p => p.P.Glow == 4);
        Assert.All(Colony.Plots(map).Where(p => p.P.Terrain is Terrain.River or Terrain.Town), p => Assert.Equal(0, p.P.Glow));
    }

    [Fact]
    public void What_a_plot_makes_depends_on_its_ground_its_neighbours_and_how_many_its_owner_has()
    {
        var map = Colony.Generate(new Random(1));
        foreach (var (_, _, p) in Colony.Plots(map)) { p.Owner = -1; p.Mule = null; if (p.Terrain is not (Terrain.Town or Terrain.River)) p.Terrain = Terrain.Plains; }
        map[0, 0].Owner = 0; map[0, 0].Mule = Good.Energy;
        Assert.Equal(3, Colony.Produce(map, 0, 0, null));
        map[1, 0].Owner = 0; map[1, 0].Mule = Good.Energy;
        Assert.Equal(4, Colony.Produce(map, 0, 0, null));
        map[3, 0].Owner = 0; map[3, 0].Mule = Good.Energy;
        Assert.Equal(5, Colony.Produce(map, 0, 0, null));
        Assert.Equal(4, Colony.Produce(map, 3, 0, null));
        map[2, 2].Terrain = Terrain.Hills3; map[2, 2].Owner = 1; map[2, 2].Mule = Good.Ore;
        Assert.Equal(4, Colony.Produce(map, 2, 2, null));
        map[2, 2].Mule = Good.Food;
        Assert.Equal(1, Colony.Produce(map, 2, 2, null));
        Assert.Equal(0, Colony.Yield(map[Colony.TownX, 0], Good.Ore));
    }

    [Fact]
    public void In_the_land_grant_each_colonist_claims_one_plot_and_the_town_is_never_taken()
    {
        var g = Game();
        Assert.Equal(Phase.Grant, g.Phase);
        Until(g, x => x.PhaseTime > 1.6);
        Press(g, new Controls(0, 0, true));
        Assert.Equal(0, g.Map[0, 0].Owner);
        Press(g, new Controls(0, 0, true));
        Until(g, x => x.Phase != Phase.Grant);
        Assert.Equal(Phase.Develop, g.Phase);
        Assert.All(g.Colonists, c => Assert.Equal(1, Colony.Plots(g.Map).Count(p => p.P.Owner == c.Index)));
        Assert.Equal(-1, g.Map[Colony.TownX, Colony.TownY].Owner);
    }

    /// <summary>Skips to the human's development turn, owning plot (0,0).</summary>
    private static GameEngine AtMyTurn(int seed = 7)
    {
        var g = Game(seed);
        Until(g, x => x.PhaseTime > 1.6);
        Press(g, new Controls(0, 0, true));
        Until(g, x => x.Developer is { Computer: false });
        return g;
    }

    [Fact]
    public void Buy_a_mule_outfit_it_and_put_it_to_work()
    {
        var g = AtMyTurn();
        var me = g.Human!;
        int money = me.Money, price = g.Store.MulePrice, mules = g.Store.Mules;
        Assert.True(g.Walker.InTown);
        Visit(g, "MULES");
        Assert.True(g.Walker.HasMule);
        Assert.Equal(money - price, me.Money);
        Assert.Equal(mules - 1, g.Store.Mules);
        Visit(g, "ENERGY");
        Assert.Equal(Good.Energy, g.Walker.Mule);
        Assert.Equal(money - price - Store.Outfit(Good.Energy), me.Money);
        // Out of the west gate and over to plot (0,0).
        g.Walker.X = 20;
        Press(g, new Controls(-1, 0), 10);
        Assert.False(g.Walker.InTown);
        g.Walker.X = 50;
        g.Walker.Y = 50;
        Press(g, new Controls(0, 0, true));
        Assert.Equal(Good.Energy, g.Map[0, 0].Mule);
        Assert.False(g.Walker.HasMule);
    }

    [Fact]
    public void A_mule_led_onto_someone_elses_land_bolts()
    {
        var g = AtMyTurn();
        Visit(g, "MULES");
        Visit(g, "FOOD");
        g.Walker.InTown = false;
        var other = Colony.Plots(g.Map).First(p => p.P.Owner is 1 or 2 or 3 || (p.P.Owner < 0 && p.P.Terrain != Terrain.Town));
        g.Walker.X = other.X * GameEngine.PlotSize + 50;
        g.Walker.Y = other.Y * GameEngine.PlotSize + 50;
        Press(g, new Controls(0, 0, true));
        Assert.False(g.Walker.HasMule);
        Assert.Null(g.Map[other.X, other.Y].Mule);
    }

    [Fact]
    public void A_sample_taken_to_the_assay_office_reveals_the_glowstone()
    {
        var g = AtMyTurn();
        g.Walker.InTown = false;
        g.Walker.X = 3 * GameEngine.PlotSize + 50;
        g.Walker.Y = 1 * GameEngine.PlotSize + 50;
        Press(g, new Controls(0, 0, true));
        Assert.Equal((3, 1), g.Walker.Sample);
        Visit(g, "ASSAY");
        Assert.Contains((3, 1), g.Human!.Assayed);
        Assert.Null(g.Walker.Sample);
    }

    [Fact]
    public void The_pub_pays_out_and_ends_the_turn()
    {
        var g = AtMyTurn();
        int money = g.Human!.Money;
        Visit(g, "PUB");
        Assert.True(g.Human.Money > money);
        Assert.NotEqual(g.Human, g.Developer);
    }

    [Fact]
    public void A_colonist_short_of_food_gets_less_time()
    {
        var g = Game();
        g.Human!.Goods[(int)Good.Food] = 0;
        Until(g, x => x.PhaseTime > 1.6);
        Press(g, new Controls(0, 0, true));
        Until(g, x => x.Developer is { Computer: false });
        Assert.True(g.Walker.TimeTotal < GameEngine.TurnSeconds * 0.5);
    }

    [Fact]
    public void Running_out_of_time_loses_the_mule_in_tow()
    {
        var g = AtMyTurn();
        Visit(g, "MULES");
        int mules = g.Store.Mules;
        Until(g, x => x.Developer is not { Computer: false }, 60);
        Assert.Equal(mules, g.Store.Mules);
        Assert.DoesNotContain(Colony.Plots(g.Map), p => p.P.Owner == g.Human!.Index && p.P.Mule is not null);
    }

    [Fact]
    public void Mules_without_energy_stand_idle()
    {
        var g = AtMyTurn();
        var me = g.Human!;
        g.Map[0, 0].Mule = Good.Food;
        me.Goods[(int)Good.Energy] = 0;
        Visit(g, "PUB");
        Until(g, x => x.Phase == Phase.Produce);
        Assert.Equal(0, g.Map[0, 0].Output);
        Assert.Equal(0, me.Produced[(int)Good.Food]);
    }

    [Fact]
    public void In_an_auction_buyers_and_sellers_meet_and_the_store_takes_the_rest()
    {
        var g = AtMyTurn();
        Visit(g, "PUB");
        Until(g, x => x.Phase == Phase.Auction);
        var me = g.Human!;
        me.Goods[(int)Good.Ore] = 6;
        int money = me.Money;
        int storeOre = g.Store.Stock[(int)Good.Ore];
        Until(g, x => x.Stage == AuctionStage.Declare);
        Press(g, new Controls(0, -1));          // up: sell
        Assert.Equal(Role.Seller, g.Traders[me.Index].Role);
        Until(g, x => x.Stage == AuctionStage.Trade);
        // Hold down to bring the price down to the store's.
        for (int i = 0; i < 60 * 8; i++) g.Tick(new Controls(0, 1));
        Assert.True(me.Goods[(int)Good.Ore] < 6);
        Assert.True(me.Money > money);
        Assert.True(g.Store.Stock[(int)Good.Ore] > storeOre || g.Colonists.Any(c => c != me && c.Goods[(int)Good.Ore] > 0));
    }

    [Fact]
    public void A_buyer_who_goes_up_to_the_stores_price_buys_from_the_store()
    {
        var g = AtMyTurn();
        Visit(g, "PUB");
        Until(g, x => x.Phase == Phase.Auction && x.AuctionGood == Good.Food);
        var me = g.Human!;
        me.Goods[(int)Good.Food] = 0;
        me.Money = 2000;
        g.Store.Stock[(int)Good.Food] = 10;
        Until(g, x => x.Stage == AuctionStage.Declare);
        Press(g, new Controls(0, 1));           // down: buy
        Until(g, x => x.Stage == AuctionStage.Trade);
        for (int i = 0; i < 60 * 8; i++) g.Tick(new Controls(0, -1));
        Assert.True(me.Goods[(int)Good.Food] > 0);
        Assert.True(me.Money < 2000);
    }

    [Fact]
    public void The_store_builds_mules_from_its_ore()
    {
        var g = AtMyTurn();
        Visit(g, "PUB");
        g.Store.Mules = 5;
        g.Store.Stock[(int)Good.Ore] = 20;
        Until(g, x => x.Phase == Phase.Summary);
        Assert.True(g.Store.Mules > 5);
    }

    [Fact]
    public void A_whole_colony_of_computers_plays_through_to_the_end()
    {
        var g = new GameEngine(4, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartAllComputer(months: 6);
        Until(g, x => x.State != GameState.Playing, 60 * 30);
        Assert.Equal(GameState.GameOver, g.State);
        Assert.Equal(6, g.Month);
        Assert.True(g.Trades > 20);
        Assert.All(g.Colonists, c => Assert.True(Colony.Plots(g.Map).Count(p => p.P.Owner == c.Index) >= 4));
        Assert.All(g.Colonists, c => Assert.True(c.Score > 1000));
        Assert.False(string.IsNullOrEmpty(g.Rating));
    }

    [Fact]
    public void The_title_chooses_a_people_and_a_length()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        Press(g, new Controls(1, 0));
        Assert.Equal(Race.Burrowkin, g.ChosenRace);
        Press(g, new Controls(0, 1));
        Assert.Equal(6, g.ChosenMonths);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(Race.Burrowkin, g.Human!.Race);
        Assert.Equal(1000, g.Human.Money);
        Assert.Equal(6, g.Months);
    }

    [Fact]
    public void The_title_turns_into_a_demonstration_then_the_table_then_the_title()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle(60 * 21);
        Assert.Equal(GameState.Attract, g.State);
        Assert.Null(g.Human);
        g.Idle(60 * 91);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle(60 * 10);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void A_good_final_score_goes_in_the_table()
    {
        var folder = TempFolder();
        var g = new GameEngine(9, HighScoreTable.Load(folder)) { Silent = true };
        Press(g, new Controls(0, 1));           // a short game
        g.Tick(default, start: true);
        g.Human!.Money = 99000;
        for (int i = 0; i < 60 * 60 * 40 && g.State == GameState.Playing; i++)
        {
            if (g.Developer is { Computer: false }) Visit(g, "PUB");
            g.Tick(new Controls(0, 0, i % 120 == 0));
        }
        Assert.Equal(GameState.GameOver, g.State);
        Until(g, x => x.State != GameState.GameOver, 30);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('L');
        g.Initials.Type('W');
        g.Initials.Type('M');
        g.Initials.Finish();
        g.Tick(default);
        Assert.Equal("LWM", HighScoreTable.Load(folder).Entries[0].Initials);
    }

    [Fact]
    public void Every_sound_has_a_file_and_every_picture_is_16_by_16()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Sounds");
        foreach (var s in Enum.GetValues<Sfx>().Where(s => s != Sfx.MusicStop))
            Assert.True(File.Exists(Path.Combine(dir, s + ".wav")), s.ToString());
        foreach (var (name, rows) in AVAMule.Views.Sprites.All)
        {
            Assert.Equal(16, rows.Length);
            Assert.All(rows, r => Assert.True(r.Length == 16, $"{name}: {r}"));
        }
        foreach (var r in Enum.GetValues<Race>()) Assert.True(AVAMule.Views.Sprites.All.ContainsKey(r.ToString()));
    }
}
