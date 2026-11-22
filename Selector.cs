using System.Runtime.InteropServices;

namespace Singer_Selector;

/// <summary>
/// Backend for the singer rotation.
/// Keeps track of every singer saved in the file, who is present tonight,
/// the current singing order, and whose turn it is.
/// </summary>
public class Selector
{
    private SortedSet<string> _present;   //singers that have been added to tonight's rotation (kept alphabetical)
    private SortedSet<string> _everyone;  //every singer saved in the singers file (kept alphabetical)
    private List<string> _currentRound;   //the singing order shown in the pane
    private List<string> _nextRound;      //late singers: shown in the order but skipped until the next round
    private string _fileName;             //full path to the singers file
    private int _index;                   //position in _currentRound of whoever is singing (-1 = nobody)

    //shared random number generator for the shuffle
    private static readonly Random Rng = new Random();

    //alphabetical order that ignores upper/lower case ("amy" next to "Amy", not after "Zoe")
    private static readonly StringComparer Alphabetical = StringComparer.CurrentCultureIgnoreCase;

    // ---------- read-only views for the UI ----------

    /// <summary>The singing order (includes late singers who are waiting).</summary>
    public IReadOnlyList<string> Order => _currentRound;

    /// <summary>Index of the singer whose turn it is, -1 if the list is empty.</summary>
    public int Index => _index;

    /// <summary>Full path of the singers file.</summary>
    public string FileName => _fileName;

    /// <summary>True if this singer joined late and is waiting for the next round.</summary>
    public bool IsWaiting(string singer) => _nextRound.Contains(singer);

    /// <summary>Saved singers that are not in the rotation yet, alphabetical.</summary>
    public List<string> Absent()
    {
        ReadFile();     //re-read in case the file was edited with "Edit File"
        return _everyone.Where(s => !_present.Contains(s)).ToList();
    }

    /// <summary>Singers currently in the rotation, alphabetical.</summary>
    public List<string> Present() => _present.ToList();

    // ---------- file handling ----------

    /// <summary>Default file location: %AppData%\KaraoQueue\singers.txt on Windows (~/.config/KaraoQueue on Linux).</summary>
    public static string DefaultFileName()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = Path.Combine(appData, "KaraoQueue");
        CopyOldData(Path.Combine(appData, "Singer_Selector"), folder);
        return Path.Combine(folder, "singers.txt");
    }

    /// <summary>
    /// Version 1.0.0 (named Karaoke Shuffler) kept its files in a "Singer_Selector" folder.
    /// The first time the renamed app runs, copy them over so nobody loses their singer list.
    /// </summary>
    private static void CopyOldData(string oldFolder, string newFolder)
    {
        if (Directory.Exists(newFolder) || !Directory.Exists(oldFolder)) return;

        Directory.CreateDirectory(newFolder);
        foreach (string file in new[] { "singers.txt", "settings.txt" })
        {
            string oldFile = Path.Combine(oldFolder, file);
            if (File.Exists(oldFile))
            {
                File.Copy(oldFile, Path.Combine(newFolder, file));
            }
        }
    }

    //create the folder and an empty file if they don't exist yet
    private void FileInit()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_fileName)!);
        if (!File.Exists(_fileName))
        {
            File.Create(_fileName).Close();
        }
    }

    //load one singer per line, ignoring blank lines and surrounding spaces
    private void ReadFile()
    {
        FileInit();             //in case the file was deleted while the app was open
        _everyone.Clear();
        using (StreamReader sr = new StreamReader(_fileName))
        {
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _everyone.Add(line.Trim());
                }
            }
        }
    }

    private void SetUp()
    {
        FileInit();
        ReadFile();
    }

    // ---------- rotation logic ----------

    /// <summary>
    /// Add singers to the rotation.
    /// If nobody is in the rotation yet, the new singers are shuffled to make the first round.
    /// Otherwise they are shuffled among themselves and inserted after the current singer (behind any
    /// late singers already waiting there), and skipped until the next round.
    /// </summary>
    public void InsertSingers(IEnumerable<string> singers)
    {
        //ignore anyone already present
        List<string> toAdd = singers.Where(s => !_present.Contains(s)).ToList();
        if (toAdd.Count == 0) return;

        if (_currentRound.Count == 0)
        {
            //first add of the night: start a fresh shuffled round
            _currentRound.AddRange(toAdd);
            _present.UnionWith(toAdd);
            Shuffle();
            return;
        }

        //late singers added together are shuffled among themselves
        Rng.Shuffle(CollectionsMarshal.AsSpan(toAdd));

        //they go after whoever is singing AND after any late singers already waiting there,
        //so earlier arrivals keep their place in the queue
        int insertAt = _index + 1;
        while (insertAt < _currentRound.Count && _nextRound.Contains(_currentRound[insertAt]))
        {
            insertAt++;
        }

        _currentRound.InsertRange(insertAt, toAdd);
        _nextRound.AddRange(toAdd);
        _present.UnionWith(toAdd);
    }

    /// <summary>
    /// Take singers out of the rotation. They go back into the "Add Singer" list.
    /// If the current singer is removed, the turn moves on to the next singer.
    /// </summary>
    public void RemoveSingers(IEnumerable<string> singers)
    {
        foreach (string singer in singers.ToList())
        {
            int pos = _currentRound.IndexOf(singer);
            if (pos < 0) continue;

            _currentRound.RemoveAt(pos);
            _nextRound.Remove(singer);
            _present.Remove(singer);

            if (pos < _index)
            {
                _index--;            //everything after the removed singer shifted left by one
            }
            else if (pos == _index)
            {
                _index--;            //step back so Next() lands on whoever took this spot
                Next();
            }
        }

        if (_currentRound.Count == 0)
        {
            _index = -1;
            _nextRound.Clear();
        }
    }

    /// <summary>
    /// Fisher–Yates shuffle of everyone in the rotation.
    /// Starts a new round, so late singers are no longer skipped.
    /// </summary>
    public void Shuffle()
    {
        //Random.Shuffle is .NET's built-in Fisher–Yates; AsSpan lets it shuffle the list in place
        Rng.Shuffle(CollectionsMarshal.AsSpan(_currentRound));

        _nextRound.Clear();
        _index = _currentRound.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// Move to the next singer, skipping late singers.
    /// After the last singer a new round starts from the top and late singers join in.
    /// </summary>
    public void Next()
    {
        if (_currentRound.Count == 0)
        {
            _index = -1;
            return;
        }

        for (int i = _index + 1; i < _currentRound.Count; i++)
        {
            if (!_nextRound.Contains(_currentRound[i]))
            {
                _index = i;
                return;
            }
        }

        //reached the end of the round: everyone (including late singers) is in the new round
        _nextRound.Clear();
        _index = 0;
    }

    /// <summary>Go back to the previous singer in this round (stops at the first singer).</summary>
    public void Previous()
    {
        for (int i = _index - 1; i >= 0; i--)
        {
            if (!_nextRound.Contains(_currentRound[i]))
            {
                _index = i;
                return;
            }
        }
    }

    public Selector(string fileName)
    {
        _fileName = fileName;
        _present = new SortedSet<string>(Alphabetical);
        _everyone = new SortedSet<string>(Alphabetical);
        _currentRound = new List<string>();
        _nextRound = new List<string>();
        _index = -1;     //nobody is singing until singers are added

        SetUp();
    }
}
