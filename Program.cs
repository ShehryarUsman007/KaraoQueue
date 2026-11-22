using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;
using Uno.UI.Hosting;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace  Singer_Selector;



public class Program:Application
{
    //the UI is laid out at this size, then scaled as a whole to fit the window,
    //so it keeps the same proportions on HD, 1440p, 4K, etc.
    private const double DesignWidth = 440;
    private const double DesignHeight = 900;

    //space between the window edge and the buttons / names pane
    private const double LayoutPadding = 12;

    //Linux/macOS only report the full screen height, so leave room for the top bar / menu bar, dock and title bar
    private const double FullScreenFraction = 0.9;

    private Window? _window;
    private Selector _selector = null!;          //backend: singers + turn order
    private AppSettings _settings = null!;       //colours and text sizes from the Options menu

    private Grid _stage = null!;                 //holds the main layout grid + any open menu
    private GradientStop _gradientColor = null!; //the editable (bottom) colour of the background
    private StackPanel _namesPanel = null!;      //the list of names inside the scrollable pane
    private readonly List<TransparentButton> _buttons = new List<TransparentButton>();

    //picture in the bottom-left corner, chosen with Options → Add/Change Image
    //largest picture size in pixels: about a button's shape (133 × 72 in the layout), big enough to stay sharp on 4K.
    //Smaller pictures are centred on a transparent canvas of this size, so they keep their relative size.
    private const int LogoWidth = 400;
    private const int LogoHeight = 225;
    private Image _logo = null!;
    private string LogoPath => Path.Combine(Path.GetDirectoryName(_selector.FileName)!, "logo.png");

    //display tracking: the window is resized when it's dragged onto a screen with a different height
    private DispatcherTimer _displayWatcher = null!;
    private int _sizedForHeight;                 //usable screen height the window was last sized for
    private PointInt32 _lastPosition;            //window position at the previous check

    //Linux/macOS: restoring the saved position happens in the first checks after the window appears
    //(the system places new windows itself, and ignores moves before that)
    private bool _restoreDone;
    private int _restoreTries;

    private class TransparentButton : Button
    {
        public TransparentButton(string label, Color textColor, double fontSize) : base()
        {
            this.DefaultStyleKey = typeof(Button);
            //the Viewbox shrinks the label only if it doesn't fit, so big text sizes never get cut off
            this.Content = new Viewbox()
            {
                StretchDirection = StretchDirection.DownOnly,
                Child = new TextBlock() { Text = label },
            };
            this.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            this.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            this.HorizontalAlignment = HorizontalAlignment.Stretch;
            this.VerticalAlignment = VerticalAlignment.Stretch;
            this.BorderThickness = new Thickness(2);
            this.CornerRadius = new CornerRadius(8);
            this.FontSize = fontSize;
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
        //dark theme so menus and check boxes match the dark background
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

        //singers file + settings live in %AppData%\KaraoQueue
        _selector = new Selector(Selector.DefaultFileName());
        _settings = new AppSettings(Path.Combine(Path.GetDirectoryName(_selector.FileName)!, "settings.txt"));

        _window = new Window();
        _window.Title = "KaraoQueue";

        AppWindow appWindow = _window.AppWindow;
        appWindow.Title = "KaraoQueue";
        if (OperatingSystem.IsWindows())
        {
            RestorePosition(appWindow);     //back where it was last closed (first, so it's sized for that screen)
        }
        FitToDisplay(appWindow);


        //restrict sizing of window
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
        }

        _window.Content = BuildUi();
        RefreshNames();
        LoadLogo();
        _window.Activate();
        SetWindowIcon(appWindow);       //after Activate: on Linux the window only exists from here
        //stay above other windows, even when another app has focus.
        //Set once the window is really on screen: on Linux it has no effect before that.
        bool onTopSet = false;
        _window.Activated += (_, _) =>
        {
            if (onTopSet || appWindow.Presenter is not OverlappedPresenter shownPresenter) return;
            shownPresenter.IsAlwaysOnTop = true;
            onTopSet = true;
        };

        //remember where the window was when the app is closed
        _window.Closed += (_, _) => SavePosition(appWindow);

        //twice a second, check whether the window was dragged onto a different screen
        _lastPosition = appWindow.Position;
        _displayWatcher = new DispatcherTimer() { Interval = TimeSpan.FromMilliseconds(500) };
        _displayWatcher.Tick += (_, _) => CheckDisplay(appWindow);
        _displayWatcher.Start();
    }

    /// <summary>
    /// Title bar / taskbar icon. The icon files are copied next to the program when it's built
    /// (Windows uses the .ico, Linux and macOS the .png).
    /// </summary>
    private static void SetWindowIcon(AppWindow appWindow)
    {
        string icon = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "icon.ico" : "icon.png");
        if (File.Exists(icon))
        {
            appWindow.SetIcon(icon);
        }
    }

    // ---------- window size + display tracking ----------

    /// <summary>
    /// Usable height of the screen the window is on, in real pixels.
    /// Windows: the screen minus the taskbar, and that area itself (so the window can be moved inside it).
    /// Linux/macOS: 90% of the full screen height (no work area available); no area.
    /// </summary>
    private static (int height, Rect? workArea) GetDisplay(AppWindow appWindow)
    {
        if (OperatingSystem.IsWindows())
        {
            //the screen containing the middle of the window
            PointInt32 pos = appWindow.Position;
            SizeInt32 size = appWindow.Size;
            IntPtr monitor = MonitorFromPoint(new WinPoint { X = pos.X + size.Width / 2, Y = pos.Y + size.Height / 2 },
                                              MonitorDefaultToNearest);

            MonitorInfo info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                return (info.WorkArea.Bottom - info.WorkArea.Top, info.WorkArea);
            }
        }

        //Uno's build check says this isn't implemented, but the desktop runtime does return it
#pragma warning disable Uno0001
        ulong fullHeight = Windows.Graphics.Display.DisplayInformation.GetForCurrentView().ScreenHeightInRawPixels;
#pragma warning restore Uno0001
        return ((int)(fullHeight * FullScreenFraction), null);
    }

    /// <summary>
    /// Make the window as tall as the usable screen, keeping the 800:900 shape.
    /// The layout inside is in a Viewbox, so everything scales with the window.
    /// </summary>
    private void FitToDisplay(AppWindow appWindow)
    {
        (int height, Rect? workArea) = GetDisplay(appWindow);
        if (height <= 0)
        {
            height = (int)DesignHeight;     //screen size unknown: use the design size
        }

        _sizedForHeight = height;
        int width = (int)(height * DesignWidth / DesignHeight);
        appWindow.Resize(new SizeInt32(width, height));

        //Windows: line the window up with the top of the screen, fully on it
        if (workArea is Rect area)
        {
            int x = Math.Clamp(appWindow.Position.X, area.Left, Math.Max(area.Left, area.Right - width));
            appWindow.Move(new PointInt32(x, area.Top));
        }
    }

    /// <summary>
    /// Windows: move the window to where it was when the app was last closed, if that spot is still on a screen
    /// (e.g. not on a monitor that has since been unplugged). Otherwise the system places it as usual.
    /// </summary>
    private void RestorePosition(AppWindow appWindow)
    {
        if (_settings.WindowX is not int x || _settings.WindowY is not int y) return;

        //Windows: check a point just inside the window's top-left is on a connected screen
        if (OperatingSystem.IsWindows()
            && MonitorFromPoint(new WinPoint { X = x + 50, Y = y + 50 }, MonitorDefaultToNull) == IntPtr.Zero)
        {
            return;
        }

        appWindow.Move(new PointInt32(x, y));
    }

    /// <summary>
    /// Linux/macOS version of RestorePosition, run by the twice-a-second check until it's done:
    /// move to the saved spot, then look where the window landed on the next check.
    /// - Off by a little (the title bar / frame: moving and reading the position measure slightly different
    ///   corners there): move once more to make up for it, so the window doesn't creep every time it's reopened.
    /// - Off by a lot: the system was still placing the new window and overrode the move, so move again
    ///   (a few tries at most).
    /// </summary>
    private void RestorePositionLater(AppWindow appWindow)
    {
        const int FrameSlack = 100;     //largest title bar / frame difference expected, in pixels
        const int MaxTries = 4;

        if (_settings.WindowX is not int x || _settings.WindowY is not int y || _restoreTries >= MaxTries)
        {
            _restoreDone = true;
            return;
        }

        if (_restoreTries == 0)
        {
            appWindow.Move(new PointInt32(x, y));
            _restoreTries++;
            return;
        }

        PointInt32 landed = appWindow.Position;
        int offsetX = landed.X - x;
        int offsetY = landed.Y - y;
        if (Math.Abs(offsetX) > FrameSlack || Math.Abs(offsetY) > FrameSlack)
        {
            appWindow.Move(new PointInt32(x, y));       //overridden by the system: try again
            _restoreTries++;
            return;
        }

        if (offsetX != 0 || offsetY != 0)
        {
            appWindow.Move(new PointInt32(x - offsetX, y - offsetY));
        }
        _restoreDone = true;
    }

    /// <summary>Save the window's current position to settings.txt (called when the app closes).</summary>
    private void SavePosition(AppWindow appWindow)
    {
        _settings.WindowX = appWindow.Position.X;
        _settings.WindowY = appWindow.Position.Y;
        try
        {
            _settings.Save();
        }
        catch (Exception)
        {
            //not worth an error message while closing; it just opens in the default spot next time
        }
    }

    /// <summary>
    /// Runs twice a second. Once the window has stopped moving, re-fit it if the screen it's on
    /// has a different height than the one it was sized for (e.g. dragged from a 1080p to a 4K screen).
    /// </summary>
    private void CheckDisplay(AppWindow appWindow)
    {
        if (!OperatingSystem.IsWindows() && !_restoreDone)
        {
            RestorePositionLater(appWindow);
            _lastPosition = appWindow.Position;     //our own moves aren't a drag
            return;
        }

        PointInt32 position = appWindow.Position;
        bool stillMoving = position.X != _lastPosition.X || position.Y != _lastPosition.Y;
        _lastPosition = position;
        if (stillMoving) return;        //wait until the drag is finished

        (int height, _) = GetDisplay(appWindow);
        if (height > 0 && Math.Abs(height - _sizedForHeight) > 2)
        {
            FitToDisplay(appWindow);
            _lastPosition = appWindow.Position;     //our own move isn't a drag
        }
    }

    // ---------- Windows screen info ----------

    //rectangle used by GetMonitorInfo
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    //point used by MonitorFromPoint
    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint { public int X, Y; }

    //screen size info: whole screen and the part not covered by the taskbar
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Screen; public Rect WorkArea; public uint Flags; }

    private const uint MonitorDefaultToNull = 0;        //no screen at that point → returns zero
    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(WinPoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    // ---------- main screen ----------

    private UIElement BuildUi()
    {
        Grid g = new Grid()
        {
            Padding = new Thickness(LayoutPadding),
            RowSpacing = 8,
            ColumnSpacing = 8,

        };

        //11 rows, 9 columns (each button is 3 columns wide)
        for (int x = 0; x < 11; x++)
        {
            g.RowDefinitions.Add(new RowDefinition{ Height = new GridLength(1, GridUnitType.Star) });
        }
        for (int x = 0; x < 9; x++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition{ Width = new GridLength(1, GridUnitType.Star) });
        }

        List<UIElement> elements = new List<UIElement>();

        TransparentButton addUser = new TransparentButton("Add", _settings.ButtonText, _settings.ButtonFontSize);
        Grid.SetRow(addUser, 0);
        Grid.SetColumn(addUser, 6);
        Grid.SetColumnSpan(addUser, 3);
        addUser.Click += (_, _) => OpenAddMenu();
        elements.Add(addUser);

        TransparentButton removeUser = new TransparentButton("Remove", _settings.ButtonText, _settings.ButtonFontSize);
        Grid.SetRow(removeUser, 0);
        Grid.SetColumn(removeUser, 3);
        Grid.SetColumnSpan(removeUser, 3);
        removeUser.Click += (_, _) => OpenRemoveMenu();
        elements.Add(removeUser);

        TransparentButton next = new TransparentButton("Next", _settings.ButtonText, _settings.ButtonFontSize);
        Grid.SetRow(next, 10);
        Grid.SetColumn(next, 6);
        Grid.SetColumnSpan(next, 3);
        next.Click += (_, _) => { _selector.Next(); RefreshNames(); };
        elements.Add(next);

        //picture in the bottom-left corner (empty until one is chosen in Options)
        _logo = new Image()
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Grid.SetRow(_logo, 10);
        Grid.SetColumn(_logo, 0);
        Grid.SetColumnSpan(_logo, 3);
        elements.Add(_logo);

        TransparentButton prev = new TransparentButton("Previous", _settings.ButtonText, _settings.ButtonFontSize);
        Grid.SetRow(prev, 10);
        Grid.SetColumn(prev, 3);
        Grid.SetColumnSpan(prev, 3);
        prev.Click += (_, _) => { _selector.Previous(); RefreshNames(); };
        elements.Add(prev);

        TransparentButton options = new TransparentButton("Options", _settings.ButtonText, _settings.ButtonFontSize);
        Grid.SetRow(options, 0);
        Grid.SetColumn(options, 0);
        Grid.SetColumnSpan(options, 3);
        //drop-down list: Settings (colours, text sizes, Shuffle), Edit File (singer list in the text editor)
        //and Add/Change Image / Remove Image (picture in the bottom-left corner)
        MenuFlyoutItem settingsItem = new MenuFlyoutItem() { Text = "Settings" };
        settingsItem.Click += (_, _) => OpenOptionsMenu();
        MenuFlyoutItem editFileItem = new MenuFlyoutItem() { Text = "Edit File" };
        editFileItem.Click += (_, _) => OpenFile();
        MenuFlyoutItem imageItem = new MenuFlyoutItem() { Text = $"Add/Change Image (up to {LogoWidth}×{LogoHeight} px)" };
        imageItem.Click += (_, _) => ChangeImage();
        MenuFlyoutItem removeImageItem = new MenuFlyoutItem() { Text = "Remove Image" };
        removeImageItem.Click += (_, _) => RemoveImage();
        MenuFlyout optionsList = new MenuFlyout()
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            Items = { settingsItem, editFileItem, imageItem, removeImageItem },
        };
        //the drop-down is drawn outside the scaled layout, so scale its text the same way the window is scaled
        optionsList.Opening += (_, _) =>
        {
            double scale = _window?.Content is FrameworkElement root && root.ActualHeight > 0 ? root.ActualHeight / DesignHeight : 1;
            settingsItem.FontSize = 22 * scale;
            editFileItem.FontSize = 22 * scale;
            imageItem.FontSize = 22 * scale;
            removeImageItem.FontSize = 22 * scale;
            removeImageItem.IsEnabled = File.Exists(LogoPath);     //greyed out when there's no picture
        };
        options.Flyout = optionsList;
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
        Grid.SetColumnSpan(namesPane, 9);
        elements.Add(namesPane);

        //remember the buttons so their text colour and size can be changed from Options
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
                Text = "No singers yet. Click \"Add\" to start.",
                Foreground = new SolidColorBrush(_settings.NameText),
                FontSize = _settings.NameFontSize,
                TextWrapping = TextWrapping.Wrap,
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
                Text = $"{i + 1}.\t{order[i]}" + (isWaiting ? "   (next round)" : ""),
                Foreground = new SolidColorBrush(_settings.NameText),
                FontSize = _settings.NameFontSize,
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

            _namesPanel.Children.Add(row);
        }
    }

    // ---------- pop-up menus ----------

    /// <summary>
    /// Show a menu on top of the main screen (inside the scaled stage, so it scales too).
    /// It sits against the right edge of the window and is only as tall as its content: title at the top,
    /// content in the middle and the buttons side by side at the bottom. Once it reaches the window's height,
    /// the content (e.g. a long list of names) scrolls.
    /// Each button runs its action and then closes the menu.
    /// </summary>
    private void ShowMenu(string title, UIElement body, params (string label, Action action)[] actions)
    {
        ShowMenu(title, body, new Thickness(0), actions);
    }

    /// <summary>
    /// Same as above, with a gap around the menu (e.g. to line it up with a button: the Add and Remove
    /// menus use the window padding so their top and right edge match the Add button's).
    /// </summary>
    private void ShowMenu(string title, UIElement body, Thickness margin, params (string label, Action action)[] actions)
    {
        //dark see-through layer that blocks clicks on the screen behind
        Grid overlay = new Grid() { Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)) };

        //rows: title, content (as tall as it needs, up to the space left), buttons
        Grid content = new Grid() { RowSpacing = 16 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        content.Children.Add(new TextBlock()
        {
            Text = title,
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
        });

        Grid.SetRow((FrameworkElement)body, 1);
        content.Children.Add(body);

        //buttons in one row, equal widths
        Grid buttonRow = new Grid() { ColumnSpacing = 8 };
        for (int i = 0; i < actions.Length; i++)
        {
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            (string label, Action action) = actions[i];
            TransparentButton button = new TransparentButton(label, _settings.ButtonText, 18) { Height = 40 };
            button.Click += (_, _) =>
            {
                _stage.Children.Remove(overlay);
                action();
            };
            Grid.SetColumn(button, i);
            buttonRow.Children.Add(button);
        }
        Grid.SetRow(buttonRow, 2);
        content.Children.Add(buttonRow);

        overlay.Children.Add(new Border()
        {
            MinWidth = 320,
            MaxWidth = DesignWidth,                             //never wider than the window
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,          //grows downwards; can't be taller than the window
            Margin = margin,
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30)),
            Child = content,
        });

        _stage.Children.Add(overlay);
    }

    //Add / Remove menus: top and right edge line up with the Add button (bottom gap keeps a full-height menu inside the window)
    private static readonly Thickness AddButtonEdges = new Thickness(0, LayoutPadding, LayoutPadding, LayoutPadding);

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
                MaxWidth = 400,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Colors.White),
            };
            return (empty, () => new List<string>());
        }

        StackPanel list = new StackPanel();
        List<CheckBox> boxes = new List<CheckBox>();

        CheckBox selectAll = new CheckBox() { Content = "Select all", FontSize = 20, FontWeight = FontWeights.Bold };
        selectAll.Click += (_, _) =>
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

        ScrollViewer scroll = new ScrollViewer() { Content = list };
        return (scroll, () => boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content).ToList());
    }

    /// <summary>
    /// Add: pick saved singers who aren't in the rotation yet (alphabetical).
    /// The first add of the night shuffles automatically. After that, new singers go in after the
    /// current singer; a Shuffle button is only there if it's turned on in Options.
    /// </summary>
    private void OpenAddMenu()
    {
        (UIElement body, Func<List<string>> getChecked) = BuildCheckList(
            _selector.Absent(),
            "Everyone in the file is already added. Use Options → Edit File to add new names.");

        if (_selector.Order.Count == 0)
        {
            ShowMenu("Add Singer", body, AddButtonEdges,
                ("Add (auto shuffle)", () => { _selector.InsertSingers(getChecked()); RefreshNames(); }),
                ("Cancel", () => { }));
        }
        else if (_settings.ShowShuffle)
        {
            ShowMenu("Add Singer", body, AddButtonEdges,
                ("Add", () => { _selector.InsertSingers(getChecked()); RefreshNames(); }),
                ("Shuffle", () => { _selector.InsertSingers(getChecked()); _selector.Shuffle(); RefreshNames(); }),
                ("Cancel", () => { }));
        }
        else
        {
            ShowMenu("Add Singer", body, AddButtonEdges,
                ("Add", () => { _selector.InsertSingers(getChecked()); RefreshNames(); }),
                ("Cancel", () => { }));
        }
    }

    /// <summary>Remove: pick singers to take out of the rotation (alphabetical).</summary>
    private void OpenRemoveMenu()
    {
        (UIElement body, Func<List<string>> getChecked) = BuildCheckList(
            _selector.Present(),
            "Nobody has been added yet.");

        ShowMenu("Remove Singer", body, AddButtonEdges,
            ("Remove", () => { _selector.RemoveSingers(getChecked()); RefreshNames(); }),
            ("Cancel", () => { }));
    }

    /// <summary>Options: R, G, B boxes (0-255) for each colour, + / - for the text sizes, Shuffle on/off.</summary>
    private void OpenOptionsMenu()
    {
        Grid form = new Grid() { RowSpacing = 10, ColumnSpacing = 10 };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int c = 0; c < 3; c++)
        {
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        //white text for labels in the form
        TextBlock Label(string text, int row, int column)
        {
            TextBlock label = new TextBlock()
            {
                Text = text,
                FontSize = 20,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.White),
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, column);
            form.Children.Add(label);
            return label;
        }

        int AddRow()
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            return form.RowDefinitions.Count - 1;
        }

        //header row: R G B
        int headerRow = AddRow();
        string[] headers = { "R", "G", "B" };
        for (int c = 0; c < 3; c++)
        {
            Label(headers[c], headerRow, c + 1).HorizontalAlignment = HorizontalAlignment.Center;
        }

        //one row per colour; returns a function that reads the boxes back as a colour
        Func<byte, Color> AddColorRow(string label, Color current)
        {
            int row = AddRow();
            Label(label, row, 0);

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

        //one row per text size: [-] size [+], stopping at the limits; returns a function that reads the size
        Func<int> AddSizeRow(string label, int current, int min, int max)
        {
            int row = AddRow();
            Label(label, row, 0);

            int size = current;
            TransparentButton minus = new TransparentButton("−", _settings.ButtonText, 24) { Height = 40 };
            TransparentButton plus = new TransparentButton("+", _settings.ButtonText, 24) { Height = 40 };
            TextBlock value = Label("", row, 2);
            value.HorizontalAlignment = HorizontalAlignment.Center;

            void Show()
            {
                value.Text = size.ToString();
                minus.IsEnabled = size > min;
                plus.IsEnabled = size < max;
            }

            minus.Click += (_, _) => { size = Math.Max(min, size - AppSettings.FontSizeStep); Show(); };
            plus.Click += (_, _) => { size = Math.Min(max, size + AppSettings.FontSizeStep); Show(); };
            Show();

            Grid.SetRow(minus, row);
            Grid.SetColumn(minus, 1);
            Grid.SetRow(plus, row);
            Grid.SetColumn(plus, 3);
            form.Children.Add(minus);
            form.Children.Add(plus);

            return () => size;
        }

        Func<byte, Color> buttonText = AddColorRow("Button text", _settings.ButtonText);
        Func<byte, Color> gradient   = AddColorRow("Gradient", _settings.Gradient);
        Func<byte, Color> highlight  = AddColorRow("Current singer", _settings.Highlight);
        Func<byte, Color> nameText   = AddColorRow("Name text", _settings.NameText);

        Func<int> buttonSize = AddSizeRow("Button text size", _settings.ButtonFontSize,
                                          AppSettings.MinButtonFontSize, AppSettings.MaxButtonFontSize);
        Func<int> nameSize   = AddSizeRow("Name text size", _settings.NameFontSize,
                                          AppSettings.MinNameFontSize, AppSettings.MaxNameFontSize);

        //Shuffle button in the Add menu: on/off
        int shuffleRow = AddRow();
        Label("Shuffle in Add menu", shuffleRow, 0);
        CheckBox showShuffle = new CheckBox() { IsChecked = _settings.ShowShuffle };
        Grid.SetRow(showShuffle, shuffleRow);
        Grid.SetColumn(showShuffle, 1);
        form.Children.Add(showShuffle);

        //the form keeps its natural height at the top of the menu
        form.VerticalAlignment = VerticalAlignment.Top;

        ShowMenu("Settings", form,
            ("Apply", () =>
            {
                _settings.ButtonText     = buttonText(AppSettings.ButtonTextAlpha);
                _settings.Gradient       = gradient(AppSettings.GradientAlpha);
                _settings.Highlight      = highlight(AppSettings.HighlightAlpha);
                _settings.NameText       = nameText(AppSettings.NameTextAlpha);
                _settings.ButtonFontSize = buttonSize();
                _settings.NameFontSize   = nameSize();
                _settings.ShowShuffle    = showShuffle.IsChecked == true;
                _settings.Save();
                ApplySettings();
            }),
            ("Cancel", () => { }));
    }

    /// <summary>Push the colours and text sizes from settings onto the screen.</summary>
    private void ApplySettings()
    {
        foreach (TransparentButton button in _buttons)
        {
            button.SetTextColor(_settings.ButtonText);
            button.FontSize = _settings.ButtonFontSize;
        }
        _gradientColor.Color = _settings.Gradient;
        RefreshNames();     //redraws names with the new text colour, size and highlight
    }

    // ---------- Add/Change Image ----------

    /// <summary>
    /// Let the user pick a .png or .jpg of at most LogoWidth × LogoHeight pixels with the system's file picker,
    /// then save it as logo.png next to settings.txt (always the same name, so a new pick replaces the old one).
    /// A smaller picture is centred on a transparent LogoWidth × LogoHeight canvas, so the gaps around it show through.
    /// </summary>
    private async void ChangeImage()
    {
        FileOpenPicker picker = new FileOpenPicker()
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail,
        };
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file == null) return;       //cancelled

        try
        {
            using SKBitmap? image = SKBitmap.Decode(file.Path);
            if (image == null)
            {
                ShowMessage("Add/Change Image", $"Couldn't read this picture:\n{file.Path}");
                return;
            }
            if (image.Width > LogoWidth || image.Height > LogoHeight)
            {
                ShowMessage("Add/Change Image",
                    $"The picture can be at most {LogoWidth} × {LogoHeight} pixels.\nThis one is {image.Width} × {image.Height}.");
                return;
            }

            //full-size transparent canvas with the picture in the middle
            using SKBitmap canvasImage = new SKBitmap(LogoWidth, LogoHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (SKCanvas canvas = new SKCanvas(canvasImage))
            {
                canvas.Clear(SKColors.Transparent);
                using SKImage picture = SKImage.FromBitmap(image);
                canvas.DrawImage(picture, (LogoWidth - image.Width) / 2f, (LogoHeight - image.Height) / 2f, SKSamplingOptions.Default);   //drawn 1:1, not resized
            }

            //saved as PNG whatever the original format (PNG keeps the transparency), so the name never changes
            using SKData png = canvasImage.Encode(SKEncodedImageFormat.Png, 100);
            Directory.CreateDirectory(Path.GetDirectoryName(LogoPath)!);
            File.WriteAllBytes(LogoPath, png.ToArray());
            LoadLogo();
        }
        catch (Exception ex)
        {
            ShowMessage("Add/Change Image", $"Couldn't use this picture:\n{ex.Message}");
        }
    }

    /// <summary>Delete logo.png and clear the bottom-left corner.</summary>
    private void RemoveImage()
    {
        try
        {
            File.Delete(LogoPath);      //no error if it's already gone
        }
        catch (Exception ex)
        {
            ShowMessage("Remove Image", $"Couldn't remove the picture:\n{ex.Message}");
        }
        LoadLogo();
    }

    /// <summary>Show logo.png in the bottom-left corner, if there is one.</summary>
    private void LoadLogo()
    {
        if (!File.Exists(LogoPath))
        {
            _logo.Source = null;
            return;
        }

        //load from a copy in memory, so a replaced logo.png isn't shown from the old cached picture
        BitmapImage bitmap = new BitmapImage();
        bitmap.SetSource(new MemoryStream(File.ReadAllBytes(LogoPath)).AsRandomAccessStream());
        _logo.Source = bitmap;
    }

    /// <summary>A small menu with a message and an OK button.</summary>
    private void ShowMessage(string title, string text)
    {
        TextBlock message = new TextBlock()
        {
            Text = text,
            FontSize = 18,
            MaxWidth = 360,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Colors.White),
        };
        ShowMenu(title, message, ("OK", () => { }));
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
                MaxWidth = 500,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Colors.White),
            };
            ShowMenu("Edit File", message, ("OK", () => { }));
        }
    }
}
