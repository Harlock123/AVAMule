using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAMule.Engine;
using AVAMule.Views;
using Controls = AVAMule.Engine.Controls;

namespace AVAMule.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set MULE_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avamule-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.Advance(0.016);
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("MULE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAMule-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void Until(GameEngine g, Func<GameEngine, bool> done, int seconds = 900)
    {
        for (int i = 0; i < seconds * 60 && !done(g); i++)
        {
            if (g.Developer is { Computer: false } && g.Walker.TimeLeft < g.Walker.TimeTotal - 1)
            {
                var (x, y, w, h) = GameEngine.Door("PUB");
                g.Walker.InTown = true; g.Walker.X = x + w / 2; g.Walker.Y = y + h / 2;
            }
            g.Tick(new Controls(0, 0, g.Phase == Phase.Grant && g.PhaseTime > 4 && i % 30 == 0));
        }
    }

    [AvaloniaFact]
    public void The_title()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        Shot(w, c, "title");
    }

    [AvaloniaFact]
    public void A_month_in_the_colony()
    {
        var (w, c, g) = Open(12);
        g.Tick(default, start: true);
        g.Tick(default);
        // A few months in, so the map is busy.
        Until(g, x => x.Month == 4 && x.Phase == Phase.Grant && x.PhaseTime > 4);
        Shot(w, c, "grant");
        Until(g, x => x.Developer is { Computer: false });
        var (dx, dy, dw, dh) = GameEngine.Door("MULES");
        g.Walker.X = dx + dw / 2; g.Walker.Y = dy + dh / 2;
        g.Tick(default);
        g.Walker.X = 300; g.Walker.Y = 260;
        for (int i = 0; i < 20; i++) g.Tick(new Controls(1, 0));
        Shot(w, c, "town");
        (dx, dy, dw, dh) = GameEngine.Door("FOOD");
        g.Walker.X = dx + dw / 2; g.Walker.Y = dy + dh / 2;
        g.Tick(default);
        g.Walker.InTown = false;
        g.Walker.X = 2.6 * GameEngine.PlotSize; g.Walker.Y = 1.5 * GameEngine.PlotSize;
        g.Walker.MuleX = g.Walker.X - 40; g.Walker.MuleY = g.Walker.Y;
        g.Tick(default);
        Shot(w, c, "map");
        Until(g, x => x.Phase == Phase.Produce && x.PhaseTime > 1);
        Shot(w, c, "production");
        Until(g, x => x.Phase == Phase.Auction && x.Stage == AuctionStage.Status && x.StageTime > 1);
        Shot(w, c, "books");
        Until(g, x => x.Phase == Phase.Auction && x.AuctionGood == Good.Food && x.Stage == AuctionStage.Trade && x.StageTime > 3.5);
        Shot(w, c, "auction");
        Until(g, x => x.Phase == Phase.Summary && x.PhaseTime > 1);
        Shot(w, c, "summary");
    }

    [AvaloniaFact]
    public void The_end()
    {
        var (w, c, g) = Open(13);
        g.Tick(default, start: true);
        g.Tick(default);
        Until(g, x => x.State == GameState.GameOver, 60 * 60);
        Shot(w, c, "final");
    }
}
