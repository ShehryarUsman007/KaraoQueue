using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Uno.UI.Hosting;

namespace  Singer_Selector;



public class Program:Application
{
    //the UI is laid out at this size, then scaled as a whole to fit the window,
    //so it keeps the same proportions on HD, 1440p, 4K, etc.
    private const double DesignWidth = 800;
    private const double DesignHeight = 900;

    //fraction of the screen's usable height the window takes up
    private const double ScreenFraction = 0.85;

    private Window? _window;
    private Selector _selector = null!;          //backend: singers + turn order
    private ColorSettings _settings = null!;     //colours from the Options menu

    private Grid _stage = null!;                 //holds the main layout grid + any open menu
    private GradientStop _gradientColor = null!; //the editable (bottom) colour of the background
    private StackPanel _namesPanel = null!;      //the list of names inside the scrollable pane
    private readonly List<TransparentButton> _buttons = new List<TransparentButton>();

    private class TransparentButton : Button
    {
        public TransparentButton(string label, Color textColor) : base()
        {
            this.DefaultStyleKey = typeof(Button);
            this.Content = label;
            this.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            this.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            this.HorizontalAlignment = HorizontalAlignment.Stretch;
            this.VerticalAlignment = VerticalAlignment.Stretch;
            this.BorderThickness = new Thickness(2);
            this.CornerRadius = new CornerRadius(8);
            this.FontSize = 20;
            SetTextColor(textColor);
        }

        /// <summary>Change the button's text colour (also while hovered/pressed).</summary>
        public void SetTextColor(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            this.Foreground = brush;

            //the default button style swaps the text colour on hover/press; keep ours instead
            this.Resources["ButtonForegroundPointerOver"] = brush;
            this.Resources["ButtonForegroundPressed"] = brush;
        }
    }

    public Program()
    {
        //dark theme so menus, check boxes and number boxes match the dark background
        this.RequestedTheme = ApplicationTheme.Dark;
    }

    [STAThread]
    public static void Main(string[] args)
    {
        UnoPlatformHostBuilder.Create()
            .App(() => new Program())
            .UseX11().UseMacOS().UseWin32()     //Linux, MacOS, Windows
            .Build()
            .Run();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());

        //singers file + colour settings live in %AppData%\Singer_Selector
        _selector = new Selector(Selector.DefaultFileName());
        _settings = new ColorSettings(Path.Combine(Path.GetDirectoryName(_selector.FileName)!, "settings.txt"));

        _window = new Window();
        _window.Title = "Singer_Selector";

        AppWindow appWindow = _window.AppWindow;
        appWindow.Title = "Singer_Selector";
        SizeWindow(appWindow);


        //restrict sizing of window
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
        }

        _window.Content = BuildUi();
        RefreshNames();
        _window.Activate();
    }

    /// <summary>
    /// Size the window relative to the screen instead of a fixed pixel size, keeping the 800:900 shape.
    /// Windows: 85% of the screen's usable height (taskbar excluded), so HD, 1440p and 4K all look the same.
    /// Linux/macOS: 85% of the full screen height.
    /// The layout inside is in a Viewbox, so everything scales with the window.
    /// </summary>
    private void SizeWindow(AppWindow appWindow)
    {
        double height = DesignHeight;

        try
        {
            if (OperatingSystem.IsWindows() && GetWorkAreaHeight() is int screenHeight && screenHeight > 0)
            {
                height = screenHeight * ScreenFraction;
            }
            else
            {
                //Uno's build check says this isn't implemented, but the desktop runtime does return it
#pragma warning disable Uno0001
                ulong fullHeight = Windows.Graphics.Display.DisplayInformation.GetForCurrentView().ScreenHeightInRawPixels;
#pragma warning restore Uno0001
                if (fullHeight > 0)
                {
                    height = fullHeight * ScreenFraction;
                }
            }
        }
        catch (Exception)
        {
            //keep the 800x900 fallback
        }

        int width = (int)(height * DesignWidth / DesignHeight);
        appWindow.Resize(new Windows.Graphics.SizeInt32(width, (int)height));
    }

    // ---------- Windows screen size ----------

    //rectangle returned by SystemParametersInfo
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    private const uint SpiGetWorkArea = 0x0030;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref Rect rect, uint winIni);

    /// <summary>Height in pixels of the main screen minus the taskbar (Windows only).</summary>
    private static int? GetWorkAreaHeight()
    {
        Rect rect = new Rect();
        return SystemParametersInfo(SpiGetWorkArea, 0, ref rect, 0) ? rect.Bottom - rect.Top : null;
    }

    private UIElement BuildUi()
    {
        Grid g = new Grid()
        {
            Padding = new Thickness(50),
            RowSpacing = 10,
            ColumnSpacing = 10,

        };

        for (int x = 0; x < 11; x++)
        {
            g.RowDefinitions.Add(new RowDefinition{ Height = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition{ Width = new GridLength(1, GridUnitType.Star) });
        }

        List<UIElement> elements = new List<UIElement>();

        TransparentButton addUser = new TransparentButton("Add Singer", _settings.ButtonText);
        Grid.SetRow(addUser, 0);
        Grid.SetColumn(addUser, 8);
        Grid.SetColumnSpan(addUser, 3);
        addUser.Click += (s, e) => OpenAddMenu();
        elements.Add(addUser);

        TransparentButton removeUser = new TransparentButton("Remove Singer", _settings.ButtonText);
        Grid.SetRow(removeUser, 0);
        Grid.SetColumn(removeUser, 5);
        Grid.SetColumnSpan(removeUser, 3);
        removeUser.Click += (s, e) => OpenRemoveMenu();
        elements.Add(removeUser);

        TransparentButton next = new TransparentButton("Next", _settings.ButtonText);
        Grid.SetRow(next, 10);
        Grid.SetColumn(next, 8);
        Grid.SetColumnSpan(next, 3);
        next.Click += (s, e) => { _selector.Next(); RefreshNames(); };
        elements.Add(next);

        TransparentButton prev = new TransparentButton("Previous", _settings.ButtonText);
        Grid.SetRow(prev, 10);
        Grid.SetColumn(prev, 5);
        Grid.SetColumnSpan(prev, 3);
        prev.Click += (s, e) => { _selector.Previous(); RefreshNames(); };
        elements.Add(prev);

        TransparentButton editFile = new TransparentButton("Edit File", _settings.ButtonText);
        Grid.SetRow(editFile, 10);
        Grid.SetColumn(editFile, 0);
        Grid.SetColumnSpan(editFile, 3);
        editFile.Click += (s, e) => OpenFile();
        elements.Add(editFile);

        TransparentButton options = new TransparentButton("Options", _settings.ButtonText);
        Grid.SetRow(options, 0);
        Grid.SetColumn(options, 0);
        Grid.SetColumnSpan(options, 3);
        options.Click += (s, e) => OpenOptionsMenu();
        elements.Add(options);

        //scrollable pane with the singing order (rows 1-9, full width)
        _namesPanel = new StackPanel() { Spacing = 4, Padding = new Thickness(10) };
        Border namesPane = new Border()
        {
            Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Child = new ScrollViewer() { Content = _namesPanel },
        };
        Grid.SetRow(namesPane, 1);
        Grid.SetRowSpan(namesPane, 9);
        Grid.SetColumn(namesPane, 0);
        Grid.SetColumnSpan(namesPane, 11);
        elements.Add(namesPane);

        //remember the buttons so their text colour can be changed from Options
        _buttons.AddRange(elements.OfType<TransparentButton>());

        AddAllElements(g, elements);
        elements.Clear();

        //fixed-size stage: the layout grid plus room for menus to open on top of it
        _stage = new Grid() { Width = DesignWidth, Height = DesignHeight };
        _stage.Children.Add(g);

        //background gradient fills the whole window; the stage scales inside it
        _gradientColor = new GradientStop { Color = _settings.Gradient, Offset = 1 };
        Grid root = new Grid()
        {
            Background = new LinearGradientBrush()
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(100, 0, 0, 0), Offset = 0.25 },
                    _gradientColor,
                }
            },
            BackgroundSizing = BackgroundSizing.InnerBorderEdge,
        };
        root.Children.Add(new Viewbox() { Stretch = Stretch.Uniform, Child = _stage });

        return root;
    }

    private void AddAllElements(Grid g, List<UIElement> elements)
    {
        foreach (UIElement element in elements)
        {
            g.Children.Add(element);
        }
    }

    // ---------- names pane ----------

    /// <summary>Redraw the singing order, highlight whose turn it is and scroll to them.</summary>
    private void RefreshNames()
    {
        _namesPanel.Children.Clear();
        IReadOnlyList<string> order = _selector.Order;

        if (order.Count == 0)
        {
            _namesPanel.Children.Add(new TextBlock()
            {
                Text = "No singers yet. Click \"Add Singer\" to start.",
                Foreground = new SolidColorBrush(_settings.NameText),
                FontSize = 22,
                Opacity = 0.7,
            });
            return;
        }

        for (int i = 0; i < order.Count; i++)
        {
            bool isCurrent = i == _selector.Index;
            bool isWaiting = _selector.IsWaiting(order[i]);

            TextBlock name = new TextBlock()
            {
                Text = $"{i + 1}. {order[i]}" + (isWaiting ? "   (next round)" : ""),
                Foreground = new SolidColorBrush(_settings.NameText),
                FontSize = 26,
                FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = isWaiting ? FontStyle.Italic : FontStyle.Normal,
                Opacity = isWaiting ? 0.6 : 1,          //late singers are dimmed
            };

            Border row = new Border()
            {
                Child = name,
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(isCurrent ? _settings.Highlight : Colors.Transparent),
            };

            if (isCurrent)
            {
                //once drawn, scroll the pane so the current singer is visible
                row.Loaded += (s, e) => row.StartBringIntoView();
            }

            _namesPanel.Children.Add(row);
        }
    }

    // ---------- pop-up menus ----------

    /// <summary>
    /// Show a menu on top of the main screen (inside the scaled stage, so it scales too).
    /// Each button runs its action and then closes the menu.
    /// </summary>
    private void ShowMenu(string title, UIElement body, params (string label, Action action)[] actions)
    {
        //dark see-through layer that blocks clicks on the screen behind
        Grid overlay = new Grid() { Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)) };

        StackPanel content = new StackPanel() { Spacing = 16 };
        content.Children.Add(new TextBlock()
        {
            Text = title,
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
        });
        content.Children.Add(body);

        //button row, all buttons the same width
        Grid buttonRow = new Grid() { ColumnSpacing = 10, Height = 50 };
        for (int i = 0; i < actions.Length; i++)
        {
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            (string label, Action action) = actions[i];
            TransparentButton button = new TransparentButton(label, _settings.ButtonText);
            button.Click += (s, e) =>
            {
                _stage.Children.Remove(overlay);
                action();
            };
            Grid.SetColumn(button, i);
            buttonRow.Children.Add(button);
        }
        content.Children.Add(buttonRow);

        overlay.Children.Add(new Border()
        {
            Width = 560,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(24),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Child = content,
        });

        _stage.Children.Add(overlay);
    }

    /// <summary>
    /// Scrollable list of check boxes (with "Select all" on top).
    /// Returns the panel and a function that gives back the ticked names.
    /// </summary>
    private (UIElement body, Func<List<string>> getChecked) BuildCheckList(List<string> names, string emptyMessage)
    {
        if (names.Count == 0)
        {
            TextBlock empty = new TextBlock()
            {
                Text = emptyMessage,
                FontSize = 20,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Colors.White),
            };
            return (empty, () => new List<string>());
        }

        StackPanel list = new StackPanel();
        List<CheckBox> boxes = new List<CheckBox>();

        CheckBox selectAll = new CheckBox() { Content = "Select all", FontSize = 20, FontWeight = FontWeights.Bold };
        selectAll.Click += (s, e) =>
        {
            foreach (CheckBox box in boxes)
            {
                box.IsChecked = selectAll.IsChecked;
            }
        };
        list.Children.Add(selectAll);

        foreach (string name in names)
        {
            CheckBox box = new CheckBox() { Content = name, FontSize = 20 };
            boxes.Add(box);
            list.Children.Add(box);
        }

        ScrollViewer scroll = new ScrollViewer() { Content = list, MaxHeight = 450 };
        return (scroll, () => boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content).ToList());
    }

    /// <summary>Add Singer: pick saved singers who aren't in the rotation yet.</summary>
    private void OpenAddMenu()
    {
        (UIElement body, Func<List<string>> getChecked) = BuildCheckList(
            _selector.Absent(),
            "Everyone in the file is already added. Use \"Edit File\" to add new names.");

        ShowMenu("Add Singer", body,
            ("Add", () => { _selector.InsertSingers(getChecked()); RefreshNames(); }),
            ("Shuffle", () => { _selector.InsertSingers(getChecked()); _selector.Shuffle(); RefreshNames(); }),
            ("Cancel", () => { }));
    }

    /// <summary>Remove Singer: pick singers to take out of the rotation.</summary>
    private void OpenRemoveMenu()
    {
        (UIElement body, Func<List<string>> getChecked) = BuildCheckList(
            _selector.Present(),
            "Nobody has been added yet.");

        ShowMenu("Remove Singer", body,
            ("Remove", () => { _selector.RemoveSingers(getChecked()); RefreshNames(); }),
            ("Shuffle", () => { _selector.RemoveSingers(getChecked()); _selector.Shuffle(); RefreshNames(); }),
            ("Cancel", () => { }));
    }

    /// <summary>Options: R, G, B boxes (0-255) for each editable colour.</summary>
    private void OpenOptionsMenu()
    {
        Grid form = new Grid() { RowSpacing = 10, ColumnSpacing = 10 };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        for (int c = 0; c < 3; c++)
        {
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        //header row: R G B
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] headers = { "R", "G", "B" };
        for (int c = 0; c < 3; c++)
        {
            TextBlock header = new TextBlock()
            {
                Text = headers[c],
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.White),
            };
            Grid.SetColumn(header, c + 1);
            form.Children.Add(header);
        }

        //one row per colour; returns a function that reads the boxes back as a colour
        Func<byte, Color> AddColorRow(string label, Color current)
        {
            int row = form.RowDefinitions.Count;
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock text = new TextBlock()
            {
                Text = label,
                FontSize = 20,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.White),
            };
            Grid.SetRow(text, row);
            form.Children.Add(text);

            byte[] values = { current.R, current.G, current.B };
            NumberBox[] boxes = new NumberBox[3];
            for (int c = 0; c < 3; c++)
            {
                boxes[c] = new NumberBox()
                {
                    Value = values[c],
                    Minimum = 0,
                    Maximum = 255,
                    FontSize = 18,
                    ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
                };
                Grid.SetRow(boxes[c], row);
                Grid.SetColumn(boxes[c], c + 1);
                form.Children.Add(boxes[c]);
            }

            //empty or invalid boxes count as 0; anything else is clamped to 0-255
            byte ReadBox(NumberBox box) => double.IsNaN(box.Value) ? (byte)0 : (byte)Math.Clamp(Math.Round(box.Value), 0, 255);
            return alpha => Color.FromArgb(alpha, ReadBox(boxes[0]), ReadBox(boxes[1]), ReadBox(boxes[2]));
        }

        Func<byte, Color> buttonText = AddColorRow("Button text", _settings.ButtonText);
        Func<byte, Color> gradient   = AddColorRow("Gradient", _settings.Gradient);
        Func<byte, Color> highlight  = AddColorRow("Current singer", _settings.Highlight);
        Func<byte, Color> nameText   = AddColorRow("Name text", _settings.NameText);

        ShowMenu("Options", form,
            ("Apply", () =>
            {
                _settings.ButtonText = buttonText(ColorSettings.ButtonTextAlpha);
                _settings.Gradient   = gradient(ColorSettings.GradientAlpha);
                _settings.Highlight  = highlight(ColorSettings.HighlightAlpha);
                _settings.NameText   = nameText(ColorSettings.NameTextAlpha);
                _settings.Save();
                ApplyColors();
            }),
            ("Cancel", () => { }));
    }

    /// <summary>Push the colours from settings onto the screen.</summary>
    private void ApplyColors()
    {
        foreach (TransparentButton button in _buttons)
        {
            button.SetTextColor(_settings.ButtonText);
        }
        _gradientColor.Color = _settings.Gradient;
        RefreshNames();     //redraws names with the new text + highlight colours
    }

    // ---------- Edit File ----------

    /// <summary>Open the singers file in the system's default text editor (one name per line).</summary>
    private void OpenFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_selector.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            TextBlock message = new TextBlock()
            {
                Text = $"Couldn't open the file:\n{_selector.FileName}\n\n{ex.Message}",
                FontSize = 18,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Colors.White),
            };
            ShowMenu("Edit File", message, ("OK", () => { }));
        }
    }
}
