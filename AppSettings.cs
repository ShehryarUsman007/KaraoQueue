using Windows.UI;

namespace Singer_Selector;

/// <summary>
/// The settings the user can change in the Options menu: colours, text sizes and the Shuffle button.
/// Only R, G and B are editable for colours; every alpha (A) value is fixed.
/// Saved next to the singers file as settings.txt so they survive restarts.
/// </summary>
public class AppSettings
{
    //fixed alpha values
    public const byte ButtonTextAlpha = 255;
    public const byte GradientAlpha = 255;
    public const byte HighlightAlpha = 140;     //see-through so the gradient shows behind the highlight
    public const byte NameTextAlpha = 255;

    //text size limits for the + / - buttons in Options
    public const int MinButtonFontSize = 14;
    public const int MaxButtonFontSize = 40;
    public const int MinNameFontSize = 14;
    public const int MaxNameFontSize = 48;
    public const int FontSizeStep = 2;

    //editable colours (defaults match the original design)
    public Color ButtonText = Color.FromArgb(ButtonTextAlpha, 247, 236, 8);
    public Color Gradient = Color.FromArgb(GradientAlpha, 255, 0, 0);
    public Color Highlight = Color.FromArgb(HighlightAlpha, 247, 236, 8);
    public Color NameText = Color.FromArgb(NameTextAlpha, 255, 255, 255);

    //editable text sizes
    public int ButtonFontSize = 26;
    public int NameFontSize = 26;

    //whether the Add menu has a Shuffle button (the first add of the night always shuffles)
    public bool ShowShuffle = false;

    private readonly string _fileName;

    public AppSettings(string fileName)
    {
        _fileName = fileName;
        Load();
    }

    /// <summary>Read saved settings. Missing or broken lines keep their default value.</summary>
    private void Load()
    {
        if (!File.Exists(_fileName)) return;

        foreach (string line in File.ReadAllLines(_fileName))
        {
            //format:  Name=R,G,B   or   Name=size   or   Name=true/false
            string[] parts = line.Split('=');
            if (parts.Length != 2) continue;
            string key = parts[0].Trim();

            //on/off settings
            if (bool.TryParse(parts[1], out bool on))
            {
                if (key == "ShowShuffle") ShowShuffle = on;
                continue;
            }

            //text sizes: one number, kept within the limits
            if (int.TryParse(parts[1], out int size))
            {
                switch (key)
                {
                    case "ButtonFontSize": ButtonFontSize = Math.Clamp(size, MinButtonFontSize, MaxButtonFontSize); break;
                    case "NameFontSize":   NameFontSize   = Math.Clamp(size, MinNameFontSize, MaxNameFontSize);     break;
                }
                continue;
            }

            //colours: three numbers 0-255
            string[] rgb = parts[1].Split(',');
            if (rgb.Length != 3
                || !byte.TryParse(rgb[0], out byte r)
                || !byte.TryParse(rgb[1], out byte g)
                || !byte.TryParse(rgb[2], out byte b))
            {
                continue;
            }

            switch (key)
            {
                case "ButtonText": ButtonText = Color.FromArgb(ButtonTextAlpha, r, g, b); break;
                case "Gradient":   Gradient   = Color.FromArgb(GradientAlpha, r, g, b);   break;
                case "Highlight":  Highlight  = Color.FromArgb(HighlightAlpha, r, g, b);  break;
                case "NameText":   NameText   = Color.FromArgb(NameTextAlpha, r, g, b);   break;
            }
        }
    }

    /// <summary>Write the current settings to settings.txt.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_fileName)!);
        File.WriteAllLines(_fileName, new[]
        {
            $"ButtonText={ButtonText.R},{ButtonText.G},{ButtonText.B}",
            $"Gradient={Gradient.R},{Gradient.G},{Gradient.B}",
            $"Highlight={Highlight.R},{Highlight.G},{Highlight.B}",
            $"NameText={NameText.R},{NameText.G},{NameText.B}",
            $"ButtonFontSize={ButtonFontSize}",
            $"NameFontSize={NameFontSize}",
            $"ShowShuffle={ShowShuffle}",
        });
    }
}
