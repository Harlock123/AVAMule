using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAMule.Engine;

namespace AVAMule.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAMule/Assets/Fonts#VT323"));
    /// <summary>Where the map (and the town, and the auctions) sit on the screen.</summary>
    private const double MapX = 44, MapY = 100;

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width);
        return p;
    }

    private const uint White = 0xFFF2F2F2, Yellow = 0xFFFFE14A, Red = 0xFFFF5A4A, Green = 0xFF60E070, Dim = 0xFF9AA2B8, Ink = 0xFFE8E4D8, Panel = 0xFF161A24;

    public static uint GoodColour(Good g) => g switch
    {
        Good.Food => 0xFF60C040, Good.Energy => 0xFFF0D040, Good.Ore => 0xFFB08050, _ => 0xFF60E0F0,
    };

    private static void Text(DrawingContext dc, string text, double x, double y, double size, uint colour, int align = 0)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double left = align == 0 ? x : align == 1 ? x - ft.Width / 2 : x - ft.Width;
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0x90000000)), new Point(left + 2, y + 2));
        dc.DrawText(ft, new Point(left, y));
    }

    private static void Sprite(DrawingContext dc, string name, uint tint, double x, double y, double scale)
    {
        var rows = Sprites.All[name];
        for (int r = 0; r < 16; r++)
        {
            string row = rows[r];
            int c = 0;
            while (c < 16)
            {
                uint col = Sprites.Colour(row[c], tint);
                int start = c;
                while (c < 16 && Sprites.Colour(row[c], tint) == col) c++;
                if (col != 0) dc.FillRectangle(Brush(col), new Rect(x + start * scale, y + r * scale, (c - start) * scale + 0.3, scale + 0.3));
            }
        }
    }

    private static uint Mix(uint a, uint b, double t)
    {
        uint C(int s) => (uint)Math.Round(((a >> s) & 0xFF) * (1 - t) + ((b >> s) & 0xFF) * t);
        return 0xFF000000 | (C(16) << 16) | (C(8) << 8) | C(0);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0C1018), new Rect(0, 0, ScreenWidth, ScreenHeight));
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); return;
            case GameState.HighScores: DrawHighScores(dc); return;
            case GameState.EnterInitials: DrawInitials(dc); return;
            case GameState.GameOver: DrawFinal(dc); return;
        }
        DrawHeader(dc);
        using (dc.PushClip(new Rect(MapX - 4, MapY - 4, GameEngine.MapWidth + 8, GameEngine.MapHeight + 8)))
        using (dc.PushTransform(Matrix.CreateTranslation(MapX, MapY)))
        {
            switch (g.Phase)
            {
                case Phase.Auction: DrawAuction(dc); break;
                case Phase.Summary: DrawSummary(dc); break;
                default:
                    if (g.Developer is { Computer: false } && g.Walker.InTown) DrawTown(dc);
                    else DrawMap(dc);
                    break;
            }
        }
        dc.DrawRectangle(Pen(0xFF4A5A70, 3), new Rect(MapX - 3, MapY - 3, GameEngine.MapWidth + 6, GameEngine.MapHeight + 6));
        DrawColonists(dc);
        if (g.Message is { } msg) Text(dc, msg, ScreenWidth / 2, 60, 28, Yellow, 1);
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, ScreenWidth, ScreenHeight));
            Text(dc, "PAUSED", 512, 320, 80, Yellow, 1);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", 512, 410, 28, White, 1);
        }
        if (g.State == GameState.Attract)
        {
            dc.FillRectangle(Brush(0xA0000000), new Rect(332, 300, 360, 70));
            Text(dc, "DEMONSTRATION", 512, 304, 36, Yellow, 1);
            Text(dc, "PRESS ENTER TO PLAY", 512, 338, 26, White, 1);
        }
    }

    private void DrawHeader(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "AVA", 20, 6, 40, 0xFFFF9A40);
        Text(dc, "MULE", 76, 6, 40, Yellow);
        string phase = g.Phase switch
        {
            Phase.Grant => "LAND GRANT",
            Phase.Develop => g.Developer is { } d ? $"DEVELOPMENT: {d.Name.ToUpperInvariant()}" : "DEVELOPMENT",
            Phase.Produce => "PRODUCTION",
            Phase.Auction => $"AUCTION: {g.AuctionGood.ToString().ToUpperInvariant()}",
            _ => "THE MONTH'S RESULTS",
        };
        Text(dc, $"MONTH {g.Month} OF {g.Months}", ScreenWidth - 20, 6, 30, White, 2);
        Text(dc, phase, ScreenWidth / 2, 8, 34, 0xFF80D0FF, 1);
        if (g.Developer is { Computer: false })
        {
            var w = g.Walker;
            double frac = Math.Clamp(w.TimeLeft / Math.Max(1, w.TimeTotal), 0, 1);
            var bar = new Rect(380, 46, 264, 10);
            dc.FillRectangle(Brush(0xFF202838), bar);
            dc.FillRectangle(Brush(frac > 0.3 ? Green : Red), new Rect(bar.X, bar.Y, bar.Width * frac, bar.Height));
        }
    }

    // ───────────────────────────── the map ─────────────────────────────

    private void DrawMap(DrawingContext dc)
    {
        var g = Engine;
        const double P = GameEngine.PlotSize;
        var human = g.Human;
        foreach (var (x, y, p) in Colony.Plots(g.Map))
        {
            var r = new Rect(x * P, y * P, P, P);
            DrawPlot(dc, r, p, x, y);
            if (p.Owner >= 0)
            {
                uint col = g.Colonists[p.Owner].Colour;
                dc.DrawRectangle(Pen(col, 5), r.Deflate(3));
            }
            if (p.Mule is { } m)
            {
                Sprite(dc, "mule", GoodColour(m), r.X + 20, r.Y + 26, 4);
                if (g.Phase == Phase.Produce && p.Output >= 0)
                {
                    for (int i = 0; i < p.Output; i++)
                        dc.FillRectangle(Brush(GoodColour(m)), new Rect(r.X + 10 + i * 11, r.Bottom - 18, 8, 8));
                    if (p.Output == 0) Text(dc, "IDLE", r.Center.X, r.Bottom - 26, 20, Red, 1);
                }
            }
            if (human is not null && human.Assayed.Contains((x, y)))
                Text(dc, $"G{p.Glow}", r.Right - 8, r.Y + 6, 20, GoodColour(Good.Glowstone), 2);
        }
        if (g.Phase == Phase.Grant && g.PhaseTime > 1.5)
        {
            var r = new Rect(g.CursorX * P, g.CursorY * P, P, P);
            double pulse = 0.5 + 0.5 * Math.Sin(_clock * 10);
            dc.DrawRectangle(Pen(Mix(0xFF808080, 0xFFFFFFFF, pulse), 6), r.Deflate(1));
            if (g.Human is { } me && !g.Granted.Contains(me.Index)) Text(dc, "PRESS FIRE TO CLAIM THIS PLOT", GameEngine.MapWidth / 2, GameEngine.MapHeight - 30, 26, White, 1);
        }
        if (g.Developer is { } dev)
        {
            if (!dev.Computer) DrawWalker(dc, dev, g.Walker);
            else if (g.PlansDone > 0 && g.PlansDone <= g.Plans.Count)
            {
                var plan = g.Plans[g.PlansDone - 1];
                Sprite(dc, dev.Race.ToString(), dev.Colour, plan.X * P + 50, plan.Y * P + 50, 3);
            }
        }
        if (g.Phase == Phase.Produce && g.EventText is { } ev)
        {
            dc.FillRectangle(Brush(0xD0101828), new Rect(40, GameEngine.MapHeight / 2 - 40, GameEngine.MapWidth - 80, 80));
            Text(dc, ev, GameEngine.MapWidth / 2, GameEngine.MapHeight / 2 - 16, 30, Yellow, 1);
        }
    }

    private void DrawPlot(DrawingContext dc, Rect r, Plot p, int x, int y)
    {
        uint h = (uint)(x * 73856093 ^ y * 19349663);
        switch (p.Terrain)
        {
            case Terrain.River:
                dc.FillRectangle(Brush(0xFF7AA050), r);
                dc.FillRectangle(Brush(0xFF3A70C0), new Rect(r.X + 30, r.Y, 44, r.Height));
                for (int i = 0; i < 4; i++) dc.DrawLine(Pen(0x80A0D0FF, 2), new Point(r.X + 36, r.Y + 14 + i * 24), new Point(r.X + 50, r.Y + 18 + i * 24));
                break;
            case Terrain.Town:
                dc.FillRectangle(Brush(0xFF8A8070), r);
                for (int i = 0; i < 3; i++)
                {
                    dc.FillRectangle(Brush(0xFFC0B090), new Rect(r.X + 8 + i * 32, r.Y + 14, 26, 30));
                    dc.FillRectangle(Brush(0xFF8A4030), new Rect(r.X + 6 + i * 32, r.Y + 8, 30, 8));
                    dc.FillRectangle(Brush(0xFFC0B090), new Rect(r.X + 8 + i * 32, r.Y + 62, 26, 30));
                }
                Text(dc, "TOWN", r.Center.X, r.Y + 40, 22, White, 1);
                break;
            default:
                dc.FillRectangle(Brush(Mix(0xFF9AB060, 0xFF8AA050, (h % 10) / 10.0)), r);
                for (int i = 0; i < 6; i++)
                {
                    uint t = h >> (i * 4);
                    dc.FillRectangle(Brush(0x40306020), new Rect(r.X + t % 90 + 4, r.Y + (t >> 7) % 90 + 4, 6, 3));
                }
                if (p.Hills)
                {
                    int peaks = p.Terrain - Terrain.Hills1 + 1;
                    for (int i = 0; i < peaks; i++)
                    {
                        double cx = r.X + 20 + i * 30 + (peaks == 1 ? 32 : peaks == 2 ? 16 : 0), baseY = r.Y + 84;
                        var hill = new StreamGeometry();
                        using (var s = hill.Open())
                        {
                            s.BeginFigure(new Point(cx - 26, baseY), true);
                            s.LineTo(new Point(cx, baseY - 52));
                            s.LineTo(new Point(cx + 26, baseY));
                            s.EndFigure(true);
                        }
                        dc.DrawGeometry(Brush(0xFF7A6A58), Pen(0xFF4A3A30, 2), hill);
                        var cap = new StreamGeometry();
                        using (var s = cap.Open())
                        {
                            s.BeginFigure(new Point(cx - 9, baseY - 34), true);
                            s.LineTo(new Point(cx, baseY - 52));
                            s.LineTo(new Point(cx + 9, baseY - 34));
                            s.EndFigure(true);
                        }
                        dc.DrawGeometry(Brush(0xFFE8E8F0), null, cap);
                    }
                }
                break;
        }
    }

    private void DrawWalker(DrawingContext dc, Colonist me, Walker w)
    {
        if (w.HasMule) Sprite(dc, "mule", w.Mule is { } m ? GoodColour(m) : 0xFF606060, w.MuleX - 32, w.MuleY - 32, 4);
        Sprite(dc, me.Race.ToString(), me.Colour, w.X - 24, w.Y - 40, 3);
        if (w.Sample is not null) dc.FillRectangle(Brush(0xFFC0A060), new Rect(w.X + 14, w.Y - 20, 10, 10));
    }

    // ───────────────────────────── the town ─────────────────────────────

    private void DrawTown(DrawingContext dc)
    {
        var g = Engine;
        double W = GameEngine.MapWidth, H = GameEngine.MapHeight;
        dc.FillRectangle(Brush(0xFF2A2420), new Rect(0, 0, W, H));
        dc.FillRectangle(Brush(0xFF6A6050), new Rect(0, GameEngine.StreetTop - 10, W, GameEngine.StreetBottom - GameEngine.StreetTop + 20));
        for (double x = 0; x < W; x += 60) dc.FillRectangle(Brush(0xFF8A7E68), new Rect(x + 10, (GameEngine.StreetTop + GameEngine.StreetBottom) / 2 - 3, 30, 6));
        double bw = W / GameEngine.Shops.Length;
        for (int i = 0; i < GameEngine.Shops.Length; i++)
        {
            string name = GameEngine.Shops[i];
            uint col = i == 0 ? 0xFF8A6A4A : GoodColour((Good)(i - 1));
            var front = new Rect(i * bw + 8, 20, bw - 16, GameEngine.StreetTop - 40);
            dc.FillRectangle(Brush(Sprites.Shade(col, 0.55)), front);
            dc.DrawRectangle(Pen(Sprites.Shade(col, 1.2), 3), front);
            Text(dc, name, front.Center.X, front.Y + 10, 26, Sprites.Shade(col, 1.5), 1);
            string price = i == 0 ? $"${g.Store.MulePrice}  ({g.Store.Mules} left)" : $"outfit ${Store.Outfit((Good)(i - 1))}";
            Text(dc, price, front.Center.X, front.Y + 42, 20, Ink, 1);
            if (i == 0) Sprite(dc, "mule", 0xFF606060, front.Center.X - 32, front.Y + 70, 4);
            else dc.DrawEllipse(Brush(col), Pen(0xFF101010, 2), new Point(front.Center.X, front.Y + 100), 16, 16);
            var (dx, dy, dw, dh) = GameEngine.Door(name);
            dc.FillRectangle(Brush(0xFF1A1410), new Rect(dx, dy, dw, dh));
        }
        foreach (var (name, col) in new[] { ("ASSAY", 0xFFC0A060u), ("PUB", 0xFFC05080u) })
        {
            var (dx, dy, dw, dh) = GameEngine.Door(name);
            var front = new Rect(dx - 120, GameEngine.StreetBottom + 20, 300, H - GameEngine.StreetBottom - 30);
            dc.FillRectangle(Brush(Sprites.Shade(col, 0.55)), front);
            dc.DrawRectangle(Pen(Sprites.Shade(col, 1.2), 3), front);
            Text(dc, name == "ASSAY" ? "ASSAY OFFICE" : "THE PUB", front.Center.X, front.Y + 50, 30, Sprites.Shade(col, 1.6), 1);
            Text(dc, name == "ASSAY" ? "bring a sample from any plot" : "gamble the rest of your turn", front.Center.X, front.Y + 88, 20, Ink, 1);
            dc.FillRectangle(Brush(0xFF1A1410), new Rect(dx, dy, dw, dh));
        }
        Text(dc, "< MAP", 6, GameEngine.StreetTop + 6, 22, Dim);
        Text(dc, "MAP >", W - 6, GameEngine.StreetTop + 6, 22, Dim, 2);
        if (g.Developer is { Computer: false } me) DrawWalker(dc, me, g.Walker);
    }

    // ───────────────────────────── the colonists ─────────────────────────────

    private void DrawColonists(DrawingContext dc)
    {
        var g = Engine;
        for (int i = 0; i < g.Colonists.Count; i++)
        {
            var c = g.Colonists[i];
            var r = new Rect(16 + i * 250, 630, 242, 130);
            bool active = g.Developer == c;
            dc.FillRectangle(Brush(Panel), r);
            dc.DrawRectangle(Pen(active ? Yellow : Sprites.Shade(c.Colour, 0.7), active ? 3 : 2), r.Deflate(1));
            Sprite(dc, c.Race.ToString(), c.Colour, r.X + 6, r.Y + 8, 3);
            Text(dc, c.Name.ToUpperInvariant(), r.X + 60, r.Y + 4, 28, Sprites.Shade(c.Colour, 1.3));
            Text(dc, c.Race.ToString().ToUpperInvariant() + (c.Computer ? "" : " (YOU)"), r.X + 60, r.Y + 32, 18, Dim);
            Text(dc, $"${c.Money}", r.Right - 10, r.Y + 6, 26, Yellow, 2);
            for (int k = 0; k < 4; k++)
            {
                var good = (Good)k;
                double x = r.X + 10 + k * 58, y = r.Y + 64;
                dc.FillRectangle(Brush(GoodColour(good)), new Rect(x, y + 6, 12, 12));
                Text(dc, c.Goods[k].ToString(), x + 18, y, 24, Ink);
            }
            Text(dc, $"SCORE {c.Score}", r.X + 10, r.Y + 96, 22, Dim);
            int plots = Colony.Plots(g.Map).Count(p => p.P.Owner == c.Index);
            Text(dc, $"{plots} PLOTS", r.Right - 10, r.Y + 96, 22, Dim, 2);
        }
    }

    // ───────────────────────────── the auction ─────────────────────────────

    private void DrawAuction(DrawingContext dc)
    {
        var g = Engine;
        var good = g.AuctionGood;
        double W = GameEngine.MapWidth, H = GameEngine.MapHeight;
        dc.FillRectangle(Brush(0xFF101420), new Rect(0, 0, W, H));
        uint col = GoodColour(good);
        Text(dc, $"{good.ToString().ToUpperInvariant()}", 20, 10, 44, col);
        Text(dc, $"STORE BUYS AT ${g.Store.Buy(good)}  -  SELLS AT ${g.Store.Sell(good)}  -  STORE HAS {g.Store.Stock[(int)good]}", W - 20, 22, 24, Ink, 2);
        if (g.Stage == AuctionStage.Status)
        {
            string[] heads = ["", "BEFORE", "USED", "SPOILED", "MADE", "NOW", "NEEDS", "SPARE"];
            for (int i = 0; i < heads.Length; i++) Text(dc, heads[i], 200 + (i - 1) * 100, 90, 24, Dim, 1);
            for (int r = 0; r < g.Colonists.Count; r++)
            {
                var c = g.Colonists[r];
                double y = 140 + r * 70;
                Sprite(dc, c.Race.ToString(), c.Colour, 20, y - 10, 3);
                Text(dc, c.Name.ToUpperInvariant(), 70, y, 28, Sprites.Shade(c.Colour, 1.3));
                int k = (int)good;
                int need = g.Need(c, good), spare = g.Surplus(c, good);
                int[] vals = [c.Previous[k], c.Used[k], c.Spoiled[k], c.Produced[k], c.Goods[k], need];
                for (int i = 0; i < vals.Length; i++) Text(dc, vals[i].ToString(), 200 + (i + 0) * 100, y, 30, Ink, 1);
                Text(dc, (spare > 0 ? "+" : "") + spare, 800, y, 30, spare < 0 ? Red : spare > 0 ? Green : Ink, 1);
            }
            Text(dc, g.Human is null ? "" : "FIRE TO GO ON", W / 2, H - 40, 24, Dim, 1);
            return;
        }
        // The trading floor: price up the screen, one lane per colonist.
        double top = 110, bottom = H - 70;
        double lo = g.Store.Buy(good), hi = g.Store.Sell(good);
        double Y(double price) => bottom - (price - lo) / Math.Max(1, hi - lo) * (bottom - top);
        dc.DrawLine(Pen(Red, 3), new Point(20, top), new Point(W - 20, top));
        Text(dc, $"STORE SELLS ${(int)hi}", 24, top - 28, 22, Red);
        dc.DrawLine(Pen(Green, 3), new Point(20, bottom), new Point(W - 20, bottom));
        Text(dc, $"STORE BUYS ${(int)lo}", 24, bottom + 4, 22, Green);
        for (int i = 0; i < g.Colonists.Count; i++)
        {
            var c = g.Colonists[i];
            var t = g.Traders[i];
            double x = 260 + i * 190;
            dc.DrawLine(Pen(0x30FFFFFF, 1), new Point(x, top), new Point(x, bottom));
            Text(dc, c.Name.ToUpperInvariant(), x, bottom + 10, 24, Sprites.Shade(c.Colour, 1.3), 1);
            Text(dc, $"has {c.Goods[(int)good]}", x, bottom + 34, 20, Dim, 1);
            string role = t.Role switch { Role.Seller => "SELLING", Role.Buyer => "BUYING", _ => "WAITING" };
            double price = g.Stage == AuctionStage.Trade ? t.Price : t.Role == Role.Seller ? hi : lo;
            double y = t.Role == Role.None ? (top + bottom) / 2 : Y(price);
            Sprite(dc, c.Race.ToString(), c.Colour, x - 24, y - 24, 3);
            Text(dc, role, x + 28, y - 22, 20, t.Role == Role.Seller ? 0xFFFFA060 : t.Role == Role.Buyer ? 0xFF80C0FF : Dim);
            if (t.Role != Role.None && g.Stage == AuctionStage.Trade) Text(dc, $"${(int)price}", x + 28, y - 2, 22, White);
            if (t.Traded > 0) Text(dc, $"x{t.Traded}", x - 28, y - 2, 22, Yellow, 2);
        }
        if (g.LastTrade is { Age: < 0.2 } lt)
        {
            double y = Y(lt.Price);
            double x1 = lt.Seller < 0 ? 30 : 260 + lt.Seller * 190, x2 = lt.Buyer < 0 ? W - 30 : 260 + lt.Buyer * 190;
            dc.DrawLine(Pen(Yellow, 4), new Point(x1, y), new Point(x2, y));
        }
        if (g.Stage == AuctionStage.Declare)
        {
            Text(dc, g.Human is null ? "WHO WILL BUY, AND WHO WILL SELL?" : "UP: SELL      DOWN: BUY", W / 2, 70, 30, Yellow, 1);
        }
        else
        {
            double frac = Math.Clamp(1 - g.StageTime / GameEngine.TradeSeconds, 0, 1);
            dc.FillRectangle(Brush(0xFF202838), new Rect(300, 76, 336, 8));
            dc.FillRectangle(Brush(Yellow), new Rect(300, 76, 336 * frac, 8));
            if (g.Human is not null) Text(dc, "HOLD UP OR DOWN TO MOVE YOUR PRICE", W / 2, 46, 22, Dim, 1);
        }
    }

    // ───────────────────────────── the month's end, and the game's ─────────────────────────────

    private void DrawSummary(DrawingContext dc)
    {
        var g = Engine;
        double W = GameEngine.MapWidth;
        dc.FillRectangle(Brush(0xFF101420), new Rect(0, 0, W, GameEngine.MapHeight));
        Text(dc, $"THE END OF MONTH {g.Month}", W / 2, 20, 44, Yellow, 1);
        int max = Math.Max(1, g.Colonists.Max(c => c.Score));
        var ranked = g.Colonists.OrderByDescending(c => c.Score).ToList();
        for (int i = 0; i < ranked.Count; i++)
        {
            var c = ranked[i];
            double y = 100 + i * 80;
            Sprite(dc, c.Race.ToString(), c.Colour, 30, y - 6, 3);
            Text(dc, $"{i + 1}. {c.Name.ToUpperInvariant()}", 90, y, 30, Sprites.Shade(c.Colour, 1.3));
            dc.FillRectangle(Brush(c.Colour), new Rect(300, y + 6, 480.0 * c.Score / max, 26));
            Text(dc, c.Score.ToString(), 790, y + 2, 30, White);
        }
        Text(dc, $"THE COLONY: {g.ColonyScore}", W / 2, 430, 32, 0xFF80D0FF, 1);
        Text(dc, $"STORE: {g.Store.Mules} MULES - FOOD {g.Store.Stock[0]}  ENERGY {g.Store.Stock[1]}  ORE {g.Store.Stock[2]}  GLOWSTONE {g.Store.Stock[3]}", W / 2, 470, 22, Dim, 1);
    }

    private void DrawFinal(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "THE SHIPS RETURN", 512, 60, 72, Yellow, 1);
        var ranked = g.Colonists.OrderByDescending(c => c.Score).ToList();
        Text(dc, $"{ranked[0].Name.ToUpperInvariant()} {(ranked[0].Computer ? "IS" : "ARE")} THE FIRST CITIZEN OF VERDANCE", 512, 150, 34, Sprites.Shade(ranked[0].Colour, 1.3), 1);
        for (int i = 0; i < ranked.Count; i++)
        {
            var c = ranked[i];
            Sprite(dc, c.Race.ToString(), c.Colour, 300, 214 + i * 64, 3);
            Text(dc, $"{i + 1}. {c.Name.ToUpperInvariant(),-10} {c.Score,8}", 360, 220 + i * 64, 34, White);
        }
        Text(dc, $"THE COLONY: {g.ColonyScore}", 512, 500, 34, 0xFF80D0FF, 1);
        Text(dc, g.Rating, 512, 548, 28, Ink, 1);
        Text(dc, "PRESS ENTER", 512, 680, 30, Dim, 1);
    }

    private void DrawTitle(DrawingContext dc)
    {
        var g = Engine;
        // A strip of the planet: plains, a river, hills.
        for (int i = 0; i < 10; i++) dc.FillRectangle(Brush(i == 4 ? 0xFF3A70C0 : 0xFF8AA050), new Rect(i * 104, 640, 104, 128));
        Text(dc, "AVA MULE", 512, 40, 112, Yellow, 1);
        Text(dc, "AFTER THE 1983 CLASSIC OF FRONTIER TRADING", 512, 146, 28, Dim, 1);
        Text(dc, "FOUR COLONISTS. ONE PLANET. TWELVE MONTHS. EVERYTHING HAS A PRICE.", 512, 186, 26, White, 1);
        Text(dc, "CHOOSE YOUR PEOPLE  (LEFT / RIGHT)", 512, 240, 28, 0xFF80D0FF, 1);
        string[] blurbs = ["$600, steady on any ground", "$1000, fast in the hills", "$1000, quickest on foot", "$1600, slow and stately"];
        for (int i = 0; i < 4; i++)
        {
            var race = (Race)i;
            double x = 152 + i * 240;
            bool chosen = race == g.ChosenRace;
            var box = new Rect(x - 100, 280, 200, 200);
            dc.FillRectangle(Brush(chosen ? 0xFF2A2A14u : 0xFF141820u), box);
            dc.DrawRectangle(Pen(chosen ? Yellow : 0xFF3A4A5Au, chosen ? 3 : 1), box);
            Sprite(dc, race.ToString(), GameEngine.Colours[0], x - 40, 296, 5);
            Text(dc, race.ToString().ToUpperInvariant(), x, 384, 30, chosen ? Yellow : White, 1);
            Text(dc, blurbs[i], x, 420, 20, Dim, 1);
        }
        Text(dc, $"GAME LENGTH: {(g.ChosenMonths == 12 ? "STANDARD, 12 MONTHS" : "SHORT, 6 MONTHS")}  (UP / DOWN)", 512, 506, 26, White, 1);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO LAND", 512, 556, 40, Yellow, 1);
        Text(dc, $"BEST SCORE {g.HighScores.TopScore}", 512, 604, 24, Dim, 1);
        Sprite(dc, "mule", GoodColour(Good.Food), 180, 662, 5);
        Sprite(dc, "mule", GoodColour(Good.Ore), 760, 662, 5);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "THE FIRST CITIZENS", 512, 60, 72, Yellow, 1);
        Text(dc, "RANK NAME    SCORE  MONTHS", 512, 150, 30, Dim, 1);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,2}", 512, 200 + i * 44, 36, mine ? Red : i == 0 ? Yellow : White, 1);
        }
        Text(dc, "PRESS ENTER TO PLAY", 512, 700, 30, White, 1);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "A NEW HIGH SCORE!", 512, 160, 64, Yellow, 1);
        Text(dc, (g.Human?.Score ?? 0).ToString(), 512, 240, 52, White, 1);
        Text(dc, "ENTER YOUR INITIALS", 512, 320, 34, Dim, 1);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), 452 + i * 60, 380, 80, cur ? Red : Yellow, 1);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(430 + i * 60, 470, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", 512, 540, 24, White, 1);
    }
}
