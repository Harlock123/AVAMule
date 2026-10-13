namespace AVAMule.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>Where the colony is in its month.</summary>
public enum Phase
{
    /// <summary>A cursor sweeps the map; each colonist may claim one free plot.</summary>
    Grant,
    /// <summary>Each colonist in turn buys, outfits and places mules (and maybe visits the pub).</summary>
    Develop,
    /// <summary>The mules work; something may happen to the colony.</summary>
    Produce,
    /// <summary>Each good is traded in turn: the books, then who buys and who sells, then the trading.</summary>
    Auction,
    /// <summary>The month's scores.</summary>
    Summary,
}

public enum AuctionStage { Status, Declare, Trade }

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Tick, Claim, Buy, Outfit, Install, Bray, Runaway, Sample, Assay, Pub, Coin, Produce, Event, Trade, Gong, Step, Menu,
    /// <summary>The colony's tune, looping.</summary>
    Music, MusicStop,
    TitleTune, Fanfare, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it
/// runs in fixed 60 Hz steps, so it plays the same at any frame rate and a seed always plays the
/// same game.
/// </summary>
public sealed partial class GameEngine
{
    public const double Step = 1.0 / 60;
    public const double TitleIdleSeconds = 20;
    public const double AttractSeconds = 90;
    public const double HighScoreSeconds = 9;
    public static readonly uint[] Colours = [0xFFE04040, 0xFF4080F0, 0xFFE0C040, 0xFF40C060];
    public static readonly string[] ComputerNames = ["Vexa", "Ottoline", "Brask", "Quill"];

    private Random _rng;
    private double _accumulator;
    private bool _startPrev, _firePrev, _upPrev, _downPrev, _leftPrev, _rightPrev;
    private bool _titleTunePending = true;
    private bool _music;

    public GameEngine(int seed = 1983, HighScoreTable? table = null)
    {
        _rng = new Random(seed);
        HighScores = table ?? HighScoreTable.Load();
        NewColony(allComputer: true);
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public double StateTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }

    /// <summary>The title screen's choices for the human colonist.</summary>
    public Race ChosenRace { get; private set; } = Race.Human;
    public int ChosenMonths { get; private set; } = 12;

    public Plot[,] Map { get; private set; } = new Plot[Colony.Cols, Colony.Rows];
    public Store Store { get; private set; } = new();
    public List<Colonist> Colonists { get; } = [];
    public int Month { get; private set; } = 1;
    public int Months { get; private set; } = 12;
    public Phase Phase { get; private set; } = Phase.Grant;
    public double PhaseTime { get; private set; }

    /// <summary>A line across the screen, and how long it stays.</summary>
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }
    public Colonist? Human => Colonists.FirstOrDefault(c => !c.Computer);

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, double seconds = 3)
    {
        Message = text;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls controls, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Step)
        {
            _accumulator -= Step;
            Tick(controls, start);
        }
    }

    // Edges: true only on the step a button goes down.
    private bool _fireEdge, _upEdge, _downEdge, _leftEdge, _rightEdge;

    public void Tick(Controls c, bool start = false)
    {
        double dt = Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        _fireEdge = c.Fire && !_firePrev; _firePrev = c.Fire;
        _upEdge = c.Dy < 0 && !_upPrev; _upPrev = c.Dy < 0;
        _downEdge = c.Dy > 0 && !_downPrev; _downPrev = c.Dy > 0;
        _leftEdge = c.Dx < 0 && !_leftPrev; _leftPrev = c.Dx < 0;
        _rightEdge = c.Dx > 0 && !_rightPrev; _rightPrev = c.Dx > 0;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }

        switch (State)
        {
            case GameState.Title:
                if (_titleTunePending) { _titleTunePending = false; Play(Sfx.TitleTune); }
                if (_leftEdge || _rightEdge) { ChosenRace = (Race)(((int)ChosenRace + (_rightEdge ? 1 : 3)) % 4); Play(Sfx.Menu); StateTime = 0; }
                if (_upEdge || _downEdge) { ChosenMonths = ChosenMonths == 12 ? 6 : 12; Play(Sfx.Menu); StateTime = 0; }
                if (startPressed) StartGame();
                else if (StateTime > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, default);
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > 4 && (startPressed || _fireEdge || StateTime > 20))
                {
                    int score = Human?.Score ?? 0;
                    if (HighScores.Qualifies(score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Human?.Score ?? 0, Months);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    public void StartGame()
    {
        State = GameState.Playing;
        StateTime = 0;
        NewColony(allComputer: false);
        Play(Sfx.MusicStop);
        Play(Sfx.Fanfare);
    }

    /// <summary>Test hook: a whole game played by four computer colonists, with no time limit.</summary>
    internal void StartAllComputer(int months = 12)
    {
        State = GameState.Playing;
        StateTime = 0;
        NewColony(allComputer: true);
        Months = months;
    }

    private void StartAttract()
    {
        State = GameState.Attract;
        StateTime = 0;
        NewColony(allComputer: true);
    }

    /// <summary>A new planet, four colonists, the store stocked, month one.</summary>
    private void NewColony(bool allComputer)
    {
        Map = Colony.Generate(_rng);
        Store = new Store();
        Colonists.Clear();
        Months = allComputer ? 12 : ChosenMonths;
        for (int i = 0; i < 4; i++)
        {
            bool computer = allComputer || i > 0;
            var race = computer ? (Race)_rng.Next(4) : ChosenRace;
            Colonists.Add(new Colonist
            {
                Index = i, Computer = computer, Race = race, Colour = Colours[i], Money = Colonist.StartMoney(race),
                Name = computer ? ComputerNames[i] : "YOU", Goods = [4, 2, 0, 0],
            });
        }
        Month = 1;
        Message = null;
        UpdateScores();
        StartGrant();
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _titleTunePending = true;
        Music(false);
        Message = null;
        NewColony(allComputer: true);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
        Music(false);
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    /// <summary>One step of the colony's life.</summary>
    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Grant: GrantStep(dt); break;
            case Phase.Develop: DevelopStep(dt, c); break;
            case Phase.Produce: ProduceStep(); break;
            case Phase.Auction: AuctionStep(dt, c); break;
            case Phase.Summary: SummaryStep(); break;
        }
    }

    /// <summary>Score: money, plus land, mules and goods at what the store would pay.</summary>
    public void UpdateScores()
    {
        foreach (var c in Colonists)
        {
            int land = 0, mules = 0;
            foreach (var (_, _, p) in Colony.Plots(Map))
            {
                if (p.Owner != c.Index) continue;
                land += Colony.PlotValue;
                if (p.Mule is not null) mules += Store.MulePrice;
            }
            int goods = 0;
            for (int g = 0; g < 4; g++) goods += c.Goods[g] * Store.Price[g];
            c.Score = c.Money + land + mules + goods;
        }
    }

    public int ColonyScore => Colonists.Sum(c => c.Score);

    /// <summary>How the colony as a whole is doing, judged at the end.</summary>
    public string Rating
    {
        get
        {
            int s = ColonyScore * 12 / Math.Max(1, Months);
            return s switch
            {
                < 20000 => "The colony failed. The ships took the colonists home.",
                < 40000 => "The colony survives, just. Life on Verdance is hard.",
                < 60000 => "A fair colony. The homeworld is satisfied.",
                < 80000 => "A fine colony! Settlers queue for passage to Verdance.",
                _ => "A magnificent colony! Verdance will be a great world.",
            };
        }
    }
}
