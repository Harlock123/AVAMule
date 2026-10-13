namespace AVAMule.Views;

/// <summary>
/// The pictures: 16×16, one letter per pixel. 'a' is the tint (a colonist's colour, or a mule's
/// outfit), 'b' a darker and 'c' a lighter shade of it; other letters are fixed colours; '.' is clear.
/// All original drawings.
/// </summary>
public static class Sprites
{
    public static readonly Dictionary<char, uint> Fixed = new()
    {
        ['k'] = 0xFF141218, ['w'] = 0xFFF0EEE6, ['s'] = 0xFFE0B090, ['g'] = 0xFF9A9AA4, ['G'] = 0xFF5A5A66, ['n'] = 0xFF6A4828,
    };

    public static readonly Dictionary<string, string[]> All = new()
    {
        ["mule"] =
        [
            "................",
            "................",
            "..gg............",
            ".gggg...........",
            ".gkgg...........",
            "..gGgggggggggg..",
            "...ggggggggggggg",
            "...gaaaaaaaaaag.",
            "...ggggggggggg..",
            "...g.g....g.g...",
            "...g.g....g.g...",
            "..GG.GG..GG.GG..",
            "................",
            "................",
            "................",
            "................",
        ],
        ["Human"] =
        [
            "................",
            "......ssss......",
            ".....ssssss.....",
            ".....skssks.....",
            "......ssss......",
            ".......ss.......",
            "....aaaaaaaa....",
            "...aaaaaaaaaa...",
            "..s.aaaaaaaa.s..",
            "....aaaaaaaa....",
            "....aaaaaaaa....",
            ".....aa..aa.....",
            ".....aa..aa.....",
            "....kkk..kkk....",
            "................",
            "................",
        ],
        ["Burrowkin"] =
        [
            "................",
            ".....nnnnnn.....",
            "....nnnnnnnn....",
            "...nnknnnnknn...",
            "...nnnnnnnnnn...",
            "....nnwwwwnn....",
            "....aaaaaaaa....",
            "..wwaaaaaaaaww..",
            "..w.aaaaaaaa.w..",
            "....aaaaaaaa....",
            "....aaaaaaaa....",
            "....nnn..nnn....",
            "...nnnn..nnnn...",
            "................",
            "................",
            "................",
        ],
        ["Skitter"] =
        [
            "....g......g....",
            ".....g....g.....",
            "......aaaa......",
            ".....akaaka.....",
            ".....aaaaaa.....",
            "......aaaa......",
            "...g..aaaa..g...",
            "....g.aaaa.g....",
            ".....aaaaaa.....",
            "...g.aaaaaa.g...",
            "..g..aaaaaa..g..",
            "......aaaa......",
            ".....g....g.....",
            "....g......g....",
            "................",
            "................",
        ],
        ["Floatling"] =
        [
            ".....aaaaaa.....",
            "...aaaaaaaaaa...",
            "..aaccaaaaaaaa..",
            "..aaakaaaakaaa..",
            "..aaaaaaaaaaaa..",
            "..aaaaawwaaaaa..",
            "...aaaaaaaaaa...",
            "....aaaaaaaa....",
            "......aaaa......",
            ".......aa.......",
            "......g..g......",
            ".....g....g.....",
            "....g......g....",
            "................",
            "................",
            "................",
        ],
    };

    public static uint Shade(uint argb, double f)
    {
        uint r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        uint S(uint v) => (uint)Math.Clamp(f >= 1 ? v + (255 - v) * (f - 1) : v * f, 0, 255);
        return (argb & 0xFF000000) | (S(r) << 16) | (S(g) << 8) | S(b);
    }

    public static uint Colour(char ch, uint tint) => ch switch
    {
        '.' => 0,
        'a' => tint,
        'b' => Shade(tint, 0.62),
        'c' => Shade(tint, 1.45),
        _ => Fixed.GetValueOrDefault(ch, 0xFFFF00FF),
    };
}
