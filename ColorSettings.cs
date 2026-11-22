using Windows.UI;

namespace Singer_Selector;

/// <summary>
/// The colours the user can change in the Options menu.
/// Only R, G and B are editable; every alpha (A) value is fixed.
/// Saved next to the singers file as settings.txt so they survive restarts.
/// </summary>
public class ColorSettings
{
    //fixed alpha values
    public const byte ButtonTextAlpha = 255;
    public const byte GradientAlpha = 255;
    public const byte HighlightAlpha = 140;     //see-through so the gradient shows behind the highlight
    public const byte NameTextAlpha = 255;

    //editable colours (defaults match the original design)
    public Color ButtonText = Color.FromArgb(ButtonTextAlpha, 247, 236, 8);
    public Color Gradient = Color.FromArgb(GradientAlpha, 255, 0, 0);
    public Color Highlight = Color.FromArgb(HighlightAlpha, 247, 236, 8);
    public Color NameText = Color.FromArgb(NameTextAlpha, 255, 255, 255);

    private readonly string _fileName;

    public ColorSettings(string fileName)
    {
        _fileName = fileName;
        Load();
    }

    /// <summary>Read saved colours. Missing or broken lines keep their default value.</summary>
    private void Load()
    {
        if (!File.Exists(_fileName)) return;

        foreach (string line in File.ReadAllLines(_fileName))
        {
            //format:  Name=R,G,B
            string[] parts = line.Split('=');
            if (parts.Length != 2) continue;

            string[] rgb = parts[1].Split(',');
            if (rgb.Length != 3
                || !byte.TryParse(rgb[0], out byte r)
                || !byte.TryParse(rgb[1], out byte g)
                || !byte.TryParse(rgb[2], out byte b))
            {
                continue;
            }

            switch (parts[0].Trim())
            {
                case "ButtonText": ButtonText = Color.FromArgb(ButtonTextAlpha, r, g, b); break;
                case "Gradient":   Gradient   = Color.FromArgb(GradientAlpha, r, g, b);   break;
                case "Highlight":  Highlight  = Color.FromArgb(HighlightAlpha, r, g, b);  break;
                case "NameText":   NameText   = Color.FromArgb(NameTextAlpha, r, g, b);   break;
            }
        }
    }

    /// <summary>Write the current colours to settings.txt.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_fileName)!);
        File.WriteAllLines(_fileName, new[]
        {
            $"ButtonText={ButtonText.R},{ButtonText.G},{ButtonText.B}",
            $"Gradient={Gradient.R},{Gradient.G},{Gradient.B}",
            $"Highlight={Highlight.R},{Highlight.G},{Highlight.B}",
            $"NameText={NameText.R},{NameText.G},{NameText.B}",
        });
    }
}
