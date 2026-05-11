using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TUIO;

public class Competitor
{
    public string PlayerId { get; set; }
    public string PlayerName { get; set; }
    public int TotalScore { get; set; }
    public int CurrentQuestionScore { get; set; }
    public TimeSpan TotalTime { get; set; }
    public TimeSpan CurrentQuestionTime { get; set; }
    public int QuestionsAnswered { get; set; }
    public int CorrectAnswers { get; set; }
    public CompetitorStatus Status { get; set; }
    public TaskCompletionSource<string> AnswerTcs { get; set; }
    public DateTime JoinedAt { get; set; }
    public int Rank { get; set; }
    public int MarkerId { get; set; }
    public string LastAnswer { get; set; }
    public DateTime? AnswerSubmittedAt { get; set; }
}

public enum CompetitorStatus
{
    Waiting,
    Ready,
    Answering,
    Answered,
    Finished
}

public class AnswerSubmission
{
    public string PlayerId { get; set; }
    public string Answer { get; set; }
    public TimeSpan TimeTaken { get; set; }
    public bool IsCorrect { get; set; }
    public int PointsEarned { get; set; }
    public DateTime SubmittedAt { get; set; }
    public int MarkerId { get; set; }
}

public class LeaderboardUpdate
{
    public List<Competitor> Rankings { get; set; }
    public int CurrentQuestion { get; set; }
    public int TotalQuestions { get; set; }
    public DateTime Timestamp { get; set; }
}

public class QuestionData
{
    public string CorrectWord { get; set; }
    public string ImageName { get; set; }
    public string[] Options { get; set; }
    public int CorrectIndex { get; set; }
}

public class CompetitionMode : Form, TuioListener
{
    private const int TOTAL_QUESTIONS = 6;
    private const int MIN_PLAYERS = 2;
    private const int MAX_PLAYERS = 20;
    private const int QUESTION_TIME_SECONDS = 100;
    private const int HOSTING_TIME_SECONDS = 120;
    private const int LEADERBOARD_TIME_SECONDS = 5;
    private const int REVEAL_TIME_SECONDS = 8;
    private const int PLAYER_MARKER_START = 30;
    private const int PLAYER_MARKER_END = 49;
    private const int HOST_START_MARKER = 50;
    private const int BACK_MARKER = 20;

    private readonly ConcurrentDictionary<string, Competitor> _competitors = new ConcurrentDictionary<string, Competitor>();
    private readonly ConcurrentQueue<AnswerSubmission> _answerQueue = new ConcurrentQueue<AnswerSubmission>();
    private readonly ConcurrentDictionary<int, QuestionData> _questions = new ConcurrentDictionary<int, QuestionData>();
    private readonly CancellationTokenSource _competitionCts = new CancellationTokenSource();

    private bool _isClosing = false;
    private int _currentQuestionIndex = 0;
    private bool _hostingPhase = true;
    private bool _competitionActive = false;
    private bool _questionActive = false;
    private string _levelName;
    private TuioClient _client;
    private Random _rng = new Random();
    private TaskCompletionSource<bool> _speechCompletion = new TaskCompletionSource<bool>();

    private class PlayerRotationState
    {
        public float AnchorAngle { get; set; } = -1f;
        public int CurrentSelection { get; set; } = -1;
        public int LastSubmittedSelection { get; set; } = -1;
        public Color PlayerColor { get; set; }
        public RoundedShadowPanel SelectionPanel { get; set; }
        public Label SelectionLabel { get; set; }
        public DateTime LastRotationTime { get; set; } = DateTime.MinValue;
    }

    private Dictionary<int, PlayerRotationState> _playerRotationStates = new Dictionary<int, PlayerRotationState>();
    private HashSet<int> _playersWhoSubmitted = new HashSet<int>();
    private HashSet<int> _usedMarkerIds = new HashSet<int>();
    private const float ROTATION_STEP = 30f;
    private const int ROTATION_COOLDOWN_MS = 150;

    private RoundedShadowPanel _lobbyPanel;
    private RoundedShadowPanel _questionPanel;
    private RoundedShadowPanel _leaderboardPanel;
    private RoundedShadowPanel _revealPanel;
    private FlowLayoutPanel _playersFlow;
    private Label _countdownLabel;
    private Label _statusLabel;
    private Label _questionLabel;
    private PictureBox _questionImage;
    private RoundedShadowPanel[] _answerSlots = new RoundedShadowPanel[3];
    private Label[] _answerLabels = new Label[3];
    private ProgressBar _timerBar;
    private Label _timerLabel;
    private FlowLayoutPanel _playerStatusPanel;
    private Label _tuioExplanationLabel;
    private ProgressBar _hostingProgressBar;
    private Label _hostingTimerLabel;
    private Label _currentRateLabel;

    private SpeechSynthesizer _synth;
    private readonly Dictionary<string, Image> _imageCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

    private DateTime _questionStartTime;
    private int _currentTimerValue;
    private DateTime _hostingStartTime;

    private Color[] _playerColors = new Color[]
    {
        Color.FromArgb(255, 100, 100),
        Color.FromArgb(100, 255, 100),
        Color.FromArgb(100, 100, 255),
        Color.FromArgb(255, 255, 100),
        Color.FromArgb(255, 100, 255),
        Color.FromArgb(100, 255, 255),
        Color.FromArgb(255, 200, 100),
        Color.FromArgb(200, 100, 255),
        Color.FromArgb(100, 200, 255),
        Color.FromArgb(255, 150, 150),
        Color.FromArgb(150, 255, 150),
        Color.FromArgb(150, 150, 255),
        Color.FromArgb(255, 150, 200),
        Color.FromArgb(200, 200, 100),
        Color.FromArgb(200, 100, 200),
        Color.FromArgb(100, 200, 200),
        Color.FromArgb(255, 200, 150),
        Color.FromArgb(150, 200, 255),
        Color.FromArgb(200, 150, 255),
        Color.FromArgb(255, 150, 100)
    };

    public CompetitionMode(string level, TuioClient client)
    {
        _levelName = level;
        _client = client;

        this.Text = "Competition Mode - " + level;
        this.WindowState = FormWindowState.Maximized;
        this.BackColor = Color.FromArgb(245, 248, 255);
        this.DoubleBuffered = true;
        this.StartPosition = FormStartPosition.CenterScreen;

        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);

        this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        this.SetStyle(ControlStyles.UserPaint, true);
        this.UpdateStyles();

        InitializeSpeech();
        BuildUI();
        LoadQuestions();

        _client.addTuioListener(this);
        NavHelper.AddNavBar(this, "Padel Competition", true);

        _ = StartCompetitionAsync();
    }

    private void InitializeSpeech()
    {
        try
        {
            _synth = new SpeechSynthesizer();
            _synth.Rate = -1;
            _synth.Volume = 100;
            _synth.SpeakCompleted += (s, e) =>
            {
                if (_speechCompletion != null && !_speechCompletion.Task.IsCompleted)
                    _speechCompletion.TrySetResult(true);
            };

            foreach (InstalledVoice v in _synth.GetInstalledVoices())
            {
                if (v.VoiceInfo.Culture.Name.StartsWith("en"))
                {
                    _synth.SelectVoice(v.VoiceInfo.Name);
                    break;
                }
            }
        }
        catch
        {
            _synth = null;
        }
    }

    private async Task SpeakAndWait(string text)
    {
        if (_synth == null || _isClosing || AppSettings.IsMuted) return;
        _synth.Rate = AppSettings.VoiceRate;
        _speechCompletion = new TaskCompletionSource<bool>();
        _synth.SpeakAsync(text);
        await _speechCompletion.Task;
        await Task.Delay(300);
    }

    private void BuildUI()
    {
        _lobbyPanel = CreateRoundedPanel(Color.FromArgb(255, 250, 240), 40);
        _lobbyPanel.Size = new Size(900, 800);
        EnableDoubleBuffering(_lobbyPanel);

        Label lobbyTitle = new Label();
        lobbyTitle.Text = "🏆 COMPETITION LOBBY 🏆";
        lobbyTitle.Font = new Font("Arial", 28, FontStyle.Bold);
        lobbyTitle.ForeColor = Color.FromArgb(180, 100, 30);
        lobbyTitle.AutoSize = false;
        lobbyTitle.Size = new Size(800, 50);
        lobbyTitle.TextAlign = ContentAlignment.MiddleCenter;
        lobbyTitle.Location = new Point(50, 30);

        _statusLabel = new Label();
        _statusLabel.Text = $"Waiting for players... (Min: {MIN_PLAYERS}, Max: {MAX_PLAYERS})";
        _statusLabel.Font = new Font("Arial", 16, FontStyle.Regular);
        _statusLabel.ForeColor = Color.FromArgb(100, 100, 100);
        _statusLabel.AutoSize = false;
        _statusLabel.Size = new Size(800, 30);
        _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        _statusLabel.Location = new Point(50, 90);

        _countdownLabel = new Label();
        _countdownLabel.Text = "Place your marker (30-49) to join!";
        _countdownLabel.Font = new Font("Arial", 20, FontStyle.Bold);
        _countdownLabel.ForeColor = Color.FromArgb(80, 150, 80);
        _countdownLabel.AutoSize = false;
        _countdownLabel.Size = new Size(800, 40);
        _countdownLabel.TextAlign = ContentAlignment.MiddleCenter;
        _countdownLabel.Location = new Point(50, 130);

        _hostingProgressBar = new ProgressBar
        {
            Size = new Size(800, 25),
            Location = new Point(50, 180),
            Maximum = HOSTING_TIME_SECONDS,
            Value = HOSTING_TIME_SECONDS,
            ForeColor = Color.FromArgb(0, 120, 215),
            BackColor = Color.FromArgb(230, 230, 230),
            Style = ProgressBarStyle.Continuous
        };

        _hostingTimerLabel = new Label
        {
            Text = $"Time left: {HOSTING_TIME_SECONDS / 60:D2}:{HOSTING_TIME_SECONDS % 60:D2}",
            Font = new Font("Arial", 12, FontStyle.Regular),
            ForeColor = Color.FromArgb(80, 80, 80),
            AutoSize = false,
            Size = new Size(200, 25),
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(50, 210)
        };

        _playersFlow = new FlowLayoutPanel();
        _playersFlow.Size = new Size(800, 300);
        _playersFlow.Location = new Point(50, 245);
        _playersFlow.BackColor = Color.Transparent;
        _playersFlow.FlowDirection = FlowDirection.LeftToRight;
        _playersFlow.WrapContents = true;
        _playersFlow.AutoScroll = true;
        EnableDoubleBuffering(_playersFlow);

        _tuioExplanationLabel = new Label
        {
            Text = "📋 HOW TO PLAY:\n\n" +
                   "1️⃣ Place marker 30-49 to join as Player (each marker ID is unique)\n" +
                   "2️⃣ Host places marker 50 to start (or wait 2 minutes)\n" +
                   "3️⃣ During questions, ROTATE YOUR MARKER to select answer:\n" +
                   "      🔄 Rotate RIGHT → Move to next answer (A → B → C → A)\n" +
                   "      🔄 Rotate LEFT → Move to previous answer (A ← B ← C ← A)\n" +
                   "      🎨 Each player has their own unique color\n" +
                   "4️⃣ When ready, LIFT YOUR MARKER to submit your final answer!\n" +
                   "5️⃣ Fastest correct answer wins 100 points!\n" +
                   "6️⃣ Place marker 20 at any time to go back",
            Font = new Font("Arial", 11, FontStyle.Regular),
            ForeColor = Color.FromArgb(60, 60, 80),
            BackColor = Color.FromArgb(240, 245, 255),
            AutoSize = false,
            Size = new Size(800, 220),
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(50, 560),
            Padding = new Padding(15)
        };

        _tuioExplanationLabel.Paint += (s, e) =>
        {
            using (Pen pen = new Pen(Color.FromArgb(180, 180, 220), 2))
            using (GraphicsPath path = GetRoundedRectangle(new Rectangle(0, 0, _tuioExplanationLabel.Width - 1, _tuioExplanationLabel.Height - 1), 15))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawPath(pen, path);
            }
        };

        _lobbyPanel.Controls.Add(lobbyTitle);
        _lobbyPanel.Controls.Add(_statusLabel);
        _lobbyPanel.Controls.Add(_countdownLabel);
        _lobbyPanel.Controls.Add(_hostingProgressBar);
        _lobbyPanel.Controls.Add(_hostingTimerLabel);
        _lobbyPanel.Controls.Add(_playersFlow);
        _lobbyPanel.Controls.Add(_tuioExplanationLabel);

        _questionPanel = CreateRoundedPanel(Color.FromArgb(240, 248, 255), 40);
        _questionPanel.Size = new Size(1200, 800);
        _questionPanel.Visible = false;
        EnableDoubleBuffering(_questionPanel);

        _questionLabel = new Label();
        _questionLabel.Text = "What word matches this image?";
        _questionLabel.Font = new Font("Arial", 20, FontStyle.Bold);
        _questionLabel.ForeColor = Color.FromArgb(40, 60, 100);
        _questionLabel.AutoSize = false;
        _questionLabel.Size = new Size(1100, 35);
        _questionLabel.TextAlign = ContentAlignment.MiddleCenter;
        _questionLabel.Location = new Point(50, 15);

        _questionImage = new PictureBox();
        _questionImage.Size = new Size(240, 240);
        _questionImage.Location = new Point(480, 55);
        _questionImage.SizeMode = PictureBoxSizeMode.Zoom;
        _questionImage.BackColor = Color.White;

        _timerBar = new ProgressBar();
        _timerBar.Size = new Size(1100, 20);
        _timerBar.Location = new Point(50, 310);
        _timerBar.Maximum = QUESTION_TIME_SECONDS;
        _timerBar.Value = QUESTION_TIME_SECONDS;
        _timerBar.ForeColor = Color.FromArgb(0, 120, 215);
        _timerBar.BackColor = Color.FromArgb(230, 230, 230);

        _timerLabel = new Label();
        _timerLabel.Text = QUESTION_TIME_SECONDS.ToString();
        _timerLabel.Font = new Font("Arial", 14, FontStyle.Bold);
        _timerLabel.ForeColor = Color.FromArgb(80, 120, 180);
        _timerLabel.AutoSize = true;
        _timerLabel.Location = new Point(580, 333);

        _currentRateLabel = new Label();
        _currentRateLabel.Font = new Font("Arial", 12, FontStyle.Bold);
        _currentRateLabel.ForeColor = Color.FromArgb(255, 140, 0);
        _currentRateLabel.AutoSize = true;
        _currentRateLabel.Location = new Point(50, 345);
        _currentRateLabel.Text = "";

        Color[] slotColors =
        {
            Color.FromArgb(220, 60, 60),
            Color.FromArgb(60, 100, 220),
            Color.FromArgb(140, 60, 200)
        };

        string[] slotLetters = { "A", "B", "C" };

        for (int i = 0; i < 3; i++)
        {
            _answerSlots[i] = new RoundedShadowPanel
            {
                CornerRadius = 25,
                FillColor = slotColors[i],
                BorderColor = Color.White,
                BorderThickness = 3f,
                ShadowColor = Color.FromArgb(60, 0, 0, 0),
                DrawGloss = true,
                ShadowOffsetX = 5,
                ShadowOffsetY = 7,
                Size = new Size(350, 140),
                Location = new Point(50 + i * 380, 370)
            };

            _answerLabels[i] = new Label();
            _answerLabels[i].Text = $"{slotLetters[i]}\nOption will appear here";
            _answerLabels[i].Font = new Font("Arial", 14, FontStyle.Bold);
            _answerLabels[i].ForeColor = Color.White;
            _answerLabels[i].AutoSize = false;
            _answerLabels[i].Size = new Size(330, 120);
            _answerLabels[i].TextAlign = ContentAlignment.MiddleCenter;
            _answerLabels[i].Dock = DockStyle.Fill;

            _answerSlots[i].Controls.Add(_answerLabels[i]);
            _questionPanel.Controls.Add(_answerSlots[i]);
        }

        _playerStatusPanel = new FlowLayoutPanel
        {
            Size = new Size(1100, 180),
            Location = new Point(50, 530),
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true
        };
        EnableDoubleBuffering(_playerStatusPanel);

        _questionPanel.Controls.Add(_playerStatusPanel);
        _questionPanel.Controls.Add(_currentRateLabel);

        Label questionInstructions = new Label
        {
            Text = "🔄 ROTATE YOUR MARKER to select answer → 🖐️ LIFT MARKER to submit!\n🎨 Each player has their own unique color",
            Font = new Font("Arial", 12, FontStyle.Bold),
            ForeColor = Color.FromArgb(80, 80, 100),
            AutoSize = false,
            Size = new Size(1100, 50),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(50, 720),
            BackColor = Color.FromArgb(240, 245, 250),
            Padding = new Padding(5)
        };
        _questionPanel.Controls.Add(questionInstructions);

        _questionPanel.Controls.Add(_questionLabel);
        _questionPanel.Controls.Add(_questionImage);
        _questionPanel.Controls.Add(_timerBar);
        _questionPanel.Controls.Add(_timerLabel);

        _leaderboardPanel = CreateRoundedPanel(Color.FromArgb(255, 248, 240), 40);
        _leaderboardPanel.Size = new Size(900, 600);
        _leaderboardPanel.Visible = false;
        EnableDoubleBuffering(_leaderboardPanel);

        _revealPanel = CreateRoundedPanel(Color.FromArgb(255, 250, 240), 40);
        _revealPanel.Size = new Size(900, 700);
        _revealPanel.Visible = false;
        EnableDoubleBuffering(_revealPanel);

        this.Controls.Add(_lobbyPanel);
        this.Controls.Add(_questionPanel);
        this.Controls.Add(_leaderboardPanel);
        this.Controls.Add(_revealPanel);

        this.Load += (s, e) => ArrangeControls();
        this.Resize += (s, e) => ArrangeControls();
    }

    private void EnableDoubleBuffering(Control control)
    {
        if (control == null) return;

        typeof(Control).InvokeMember(
            "DoubleBuffered",
            System.Reflection.BindingFlags.SetProperty |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic,
            null,
            control,
            new object[] { true });

        foreach (Control child in control.Controls)
            EnableDoubleBuffering(child);
    }

    private void LoadQuestions()
    {
        string[][] words = QuizPage.GetLevelWords(_levelName);
        var shuffled = words.OrderBy(x => _rng.Next()).ToList();

        for (int i = 0; i < TOTAL_QUESTIONS && i < shuffled.Count; i++)
        {
            var word = shuffled[i];
            var options = GenerateOptions(word[0], words);

            _questions[i] = new QuestionData
            {
                CorrectWord = word[0],
                ImageName = word[1],
                Options = options,
                CorrectIndex = Array.IndexOf(options, word[0])
            };

            LoadImage(word[1]);
        }
    }

    private string[] GenerateOptions(string correct, string[][] allWords)
    {
        var options = new List<string> { correct };
        var wrong = allWords
            .Where(w => w[0] != correct)
            .OrderBy(x => _rng.Next())
            .Take(2)
            .Select(w => w[0])
            .ToList();

        options.AddRange(wrong);
        return options.OrderBy(x => _rng.Next()).ToArray();
    }

    private void LoadImage(string name)
    {
        if (string.IsNullOrEmpty(name) || _imageCache.ContainsKey(name)) return;

        try
        {
            string path = Path.Combine(Application.StartupPath, "Data", name);
            if (!File.Exists(path))
                path = Path.Combine(Application.StartupPath, name);
            if (!File.Exists(path))
                path = Path.Combine(Application.StartupPath, "Images", name);
            if (!File.Exists(path)) return;

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (Image img = Image.FromStream(fs))
                _imageCache[name] = new Bitmap(img);
        }
        catch { }
    }

    private async Task StartCompetitionAsync()
    {
        try
        {
            if (_synth != null && !AppSettings.IsMuted)
                _ = SpeakAndWait("Welcome to padel competition mode. Players, place markers thirty to forty nine to join. Host, place marker fifty to start.");

            await HostingPhaseAsync();

            if (_isClosing) return;

            if (_competitors.Count < MIN_PLAYERS)
            {
                SafeInvoke(() => _statusLabel.Text = "Not enough players. Closing...");
                await Task.Delay(3000);

                if (!_isClosing)
                    CloseCompetitionSafely();

                return;
            }

            _hostingPhase = false;
            _competitionActive = true;

            SafeInvoke(() =>
            {
                _lobbyPanel.Visible = false;
                _questionPanel.Visible = true;
            });

            await RunCompetitionAsync();
        }
        catch { }
    }

    private async Task HostingPhaseAsync()
    {
        _hostingStartTime = DateTime.UtcNow;

        var tcs = new TaskCompletionSource<bool>();
        System.Windows.Forms.Timer timer = null;

        timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (s, e) =>
        {
            if (_isClosing || tcs.Task.IsCompleted)
            {
                timer.Stop();
                tcs.TrySetResult(true);
                return;
            }

            var elapsed = DateTime.UtcNow - _hostingStartTime;
            var remaining = Math.Max(0, HOSTING_TIME_SECONDS - (int)elapsed.TotalSeconds);
            double percentRemaining = (double)remaining / HOSTING_TIME_SECONDS;
            Color barColor = percentRemaining <= 0.20 ? Color.Red : Color.FromArgb(0, 120, 215);

            SafeInvoke(() =>
            {
                _statusLabel.Text = $"Players joined: {_competitors.Count}/{MAX_PLAYERS} (Min: {MIN_PLAYERS})";
                _countdownLabel.Text = $"Join with markers {PLAYER_MARKER_START}-{PLAYER_MARKER_END} (each ID unique)";

                if (_hostingProgressBar.Maximum != HOSTING_TIME_SECONDS)
                    _hostingProgressBar.Maximum = HOSTING_TIME_SECONDS;

                _hostingProgressBar.Value = Math.Min(remaining, _hostingProgressBar.Maximum);
                _hostingProgressBar.ForeColor = barColor;
                _hostingTimerLabel.Text = $"Time left: {remaining / 60:D2}:{remaining % 60:D2}";

                RefreshPlayerCards();
            });

            if (remaining <= 0)
            {
                timer.Stop();
                tcs.TrySetResult(true);
            }
        };
        timer.Start();

        var cancellationTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(-1, _competitionCts.Token);
            }
            catch (TaskCanceledException) { }
        });

        await Task.WhenAny(tcs.Task, cancellationTask);

        try { timer.Stop(); timer.Dispose(); } catch { }
    }

    private void RefreshPlayerCards()
    {
        if (_playersFlow.Controls.Count != _competitors.Count)
        {
            _playersFlow.Controls.Clear();

            foreach (var c in _competitors.Values.OrderBy(x => x.MarkerId))
            {
                var card = CreatePlayerCard(c);
                _playersFlow.Controls.Add(card);
            }

            _playersFlow.PerformLayout();
            _playersFlow.Refresh();
        }
    }

    private RoundedShadowPanel CreatePlayerCard(Competitor c)
    {
        var colorIndex = (c.MarkerId - PLAYER_MARKER_START) % _playerColors.Length;
        var playerColor = _playerColors[colorIndex];

        var card = new RoundedShadowPanel
        {
            CornerRadius = 20,
            FillColor = Color.FromArgb(240, 240, 240),
            BorderColor = playerColor,
            BorderThickness = 3f,
            ShadowColor = Color.FromArgb(30, 0, 0, 0),
            ShadowOffsetX = 3,
            ShadowOffsetY = 5,
            Size = new Size(180, 110),
            Margin = new Padding(10)
        };

        var nameLabel = new Label
        {
            Text = c.PlayerName,
            Font = new Font("Arial", 12, FontStyle.Bold),
            ForeColor = Color.FromArgb(60, 60, 60),
            AutoSize = false,
            Size = new Size(160, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 15)
        };

        var markerLabel = new Label
        {
            Text = $"Marker {c.MarkerId}",
            Font = new Font("Arial", 10, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 100, 100),
            AutoSize = false,
            Size = new Size(160, 25),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 50)
        };

        var colorLabel = new Label
        {
            Text = "●",
            Font = new Font("Arial", 16, FontStyle.Bold),
            ForeColor = playerColor,
            AutoSize = false,
            Size = new Size(30, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(75, 75)
        };

        card.Controls.Add(nameLabel);
        card.Controls.Add(markerLabel);
        card.Controls.Add(colorLabel);

        return card;
    }

    private Control CreatePlayerStatusPanel(Competitor c)
    {
        var colorIndex = (c.MarkerId - PLAYER_MARKER_START) % _playerColors.Length;
        var playerColor = _playerColors[colorIndex];

        var panel = new RoundedShadowPanel
        {
            CornerRadius = 15,
            FillColor = Color.FromArgb(250, 250, 250),
            BorderColor = playerColor,
            BorderThickness = 2f,
            ShadowColor = Color.FromArgb(20, 0, 0, 0),
            ShadowOffsetX = 2,
            ShadowOffsetY = 3,
            Size = new Size(170, 130),
            Margin = new Padding(5)
        };

        var idLabel = new Label
        {
            Text = $"{c.PlayerName}\n(M{c.MarkerId})",
            Font = new Font("Arial", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(60, 60, 60),
            AutoSize = false,
            Size = new Size(160, 35),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(5, 5)
        };

        var selectionPanel = new RoundedShadowPanel
        {
            CornerRadius = 12,
            FillColor = playerColor,
            BorderColor = Color.White,
            BorderThickness = 2f,
            ShadowColor = Color.FromArgb(30, 0, 0, 0),
            ShadowOffsetX = 2,
            ShadowOffsetY = 2,
            Size = new Size(80, 45),
            Location = new Point(45, 45)
        };

        var selectionLabel = new Label
        {
            Text = "?",
            Font = new Font("Arial", 20, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = false,
            Size = new Size(70, 35),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(5, 5),
            BackColor = Color.Transparent
        };
        selectionPanel.Controls.Add(selectionLabel);

        var scoreLabel = new Label
        {
            Text = $"Score: {c.TotalScore}",
            Font = new Font("Arial", 9, FontStyle.Bold),
            ForeColor = playerColor,
            AutoSize = false,
            Size = new Size(160, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(5, 100)
        };

        panel.Controls.Add(idLabel);
        panel.Controls.Add(selectionPanel);
        panel.Controls.Add(scoreLabel);

        if (!_playerRotationStates.ContainsKey(c.MarkerId))
        {
            _playerRotationStates[c.MarkerId] = new PlayerRotationState
            {
                PlayerColor = playerColor,
                CurrentSelection = -1,
                SelectionPanel = selectionPanel,
                SelectionLabel = selectionLabel
            };
        }
        else
        {
            _playerRotationStates[c.MarkerId].SelectionPanel = selectionPanel;
            _playerRotationStates[c.MarkerId].SelectionLabel = selectionLabel;
        }

        return panel;
    }

    private async Task RunCompetitionAsync()
    {
        for (int q = 0; q < TOTAL_QUESTIONS; q++)
        {
            if (_isClosing) return;

            _currentQuestionIndex = q;
            await RunQuestionAsync(q);

            if (_isClosing) return;

            if (q < TOTAL_QUESTIONS - 1)
                await ShowLeaderboardAsync(q + 1);
        }

        if (!_isClosing)
            await ShowFinalCelebrationAsync();
    }

    private async Task RunQuestionAsync(int questionIndex)
    {
        var question = _questions[questionIndex];
        _questionActive = true;
        _questionStartTime = DateTime.UtcNow;
        _currentTimerValue = QUESTION_TIME_SECONDS;

        _playersWhoSubmitted.Clear();

        foreach (var state in _playerRotationStates.Values)
        {
            state.AnchorAngle = -1;
            state.CurrentSelection = -1;
            state.LastSubmittedSelection = -1;

            if (state.SelectionLabel != null)
            {
                SafeInvoke(() =>
                {
                    state.SelectionLabel.Text = "?";
                    state.SelectionLabel.ForeColor = Color.White;

                    if (state.SelectionPanel != null)
                    {
                        state.SelectionPanel.FillColor = state.PlayerColor;
                        state.SelectionPanel.BorderColor = Color.White;
                        state.SelectionPanel.BorderThickness = 2f;
                    }
                });
            }
        }

        foreach (var c in _competitors.Values)
        {
            c.Status = CompetitorStatus.Answering;
            c.AnswerTcs = new TaskCompletionSource<string>();
            c.CurrentQuestionScore = 0;
            c.LastAnswer = null;
            c.AnswerSubmittedAt = null;
        }

        while (_answerQueue.TryDequeue(out _)) { }

        SafeInvoke(() =>
        {
            _questionLabel.Text = $"Question {questionIndex + 1}/{TOTAL_QUESTIONS}: What word matches this image?";
            _questionImage.Image = _imageCache.ContainsKey(question.ImageName) ? _imageCache[question.ImageName] : null;

            for (int i = 0; i < 3; i++)
                _answerLabels[i].Text = $"{(char)('A' + i)}: {question.Options[i]}";

            _timerBar.Maximum = QUESTION_TIME_SECONDS;
            _timerBar.Value = QUESTION_TIME_SECONDS;
            _timerBar.ForeColor = Color.FromArgb(0, 120, 215);
            _timerLabel.Text = QUESTION_TIME_SECONDS.ToString();
            _timerLabel.ForeColor = Color.FromArgb(80, 120, 180);

            UpdatePlayerRatesDisplay();

            _playerStatusPanel.Controls.Clear();
            foreach (var c in _competitors.Values.OrderBy(x => x.MarkerId))
                _playerStatusPanel.Controls.Add(CreatePlayerStatusPanel(c));

            _playerStatusPanel.PerformLayout();
        });

        await AnnounceQuestionAsync(questionIndex);

        var timerCts = new CancellationTokenSource();
        var timerTask = RunTimerAsync(timerCts.Token);

        var answerTasks = _competitors.Values
            .Select(c => CollectAnswerAsync(c, question, _questionStartTime))
            .ToArray();

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(QUESTION_TIME_SECONDS));
        await Task.WhenAny(Task.WhenAll(answerTasks), timeoutTask);

        timerCts.Cancel();
        _questionActive = false;

        await Task.Delay(200);

        if (!_isClosing)
            await RevealAnswersAsync(question);
    }

    private async Task AnnounceQuestionAsync(int questionIndex)
    {
        if (_synth == null || _isClosing) return;

        _speechCompletion = new TaskCompletionSource<bool>();
        _synth.SpeakAsync($"Question {questionIndex + 1}. What shot matches this image?");
        await _speechCompletion.Task;
        await Task.Delay(500);
    }

    private void UpdatePlayerRatesDisplay()
    {
        var sorted = _competitors.Values
            .OrderByDescending(c => c.TotalScore)
            .ThenBy(c => c.TotalTime)
            .ToList();

        if (sorted.Count > 0)
            _currentRateLabel.Text = $"🏆 Current Leader: {sorted[0].PlayerName} ({sorted[0].TotalScore} pts)";
    }

    private async Task CollectAnswerAsync(Competitor competitor, QuestionData question, DateTime startTime)
    {
        try
        {
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(QUESTION_TIME_SECONDS));
            var answerTask = competitor.AnswerTcs.Task;

            var completed = await Task.WhenAny(answerTask, timeoutTask);

            if (_isClosing) return;

            if (completed == answerTask && answerTask.Result != null)
            {
                var timeTaken = DateTime.UtcNow - startTime;
                var isCorrect = answerTask.Result == question.CorrectWord;

                competitor.LastAnswer = answerTask.Result;
                competitor.AnswerSubmittedAt = DateTime.UtcNow;
                competitor.CurrentQuestionTime = timeTaken;
                competitor.QuestionsAnswered++;
                if (isCorrect) competitor.CorrectAnswers++;

                var submission = new AnswerSubmission
                {
                    PlayerId = competitor.PlayerId,
                    Answer = answerTask.Result,
                    TimeTaken = timeTaken,
                    IsCorrect = isCorrect,
                    SubmittedAt = DateTime.UtcNow,
                    MarkerId = competitor.MarkerId
                };

                _answerQueue.Enqueue(submission);

                SafeInvoke(() =>
                {
                    UpdatePlayerStatus(competitor, "✅ Submitted!", Color.Green);

                    foreach (Control panel in _playerStatusPanel.Controls)
                    {
                        var scoreLabel = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Score:"));
                        if (scoreLabel != null && panel.Controls.OfType<Label>().Any(l => l.Text.Contains(competitor.PlayerName)))
                        {
                            scoreLabel.Text = $"Score: {competitor.TotalScore}";
                            break;
                        }
                    }
                });
            }
            else
            {
                competitor.CurrentQuestionTime = TimeSpan.FromSeconds(QUESTION_TIME_SECONDS);
                SafeInvoke(() => UpdatePlayerStatus(competitor, "❌ Timeout", Color.Red));
            }

            competitor.Status = CompetitorStatus.Answered;
        }
        catch { }
    }

    private void UpdatePlayerStatus(Competitor competitor, string text, Color color)
    {
        foreach (Control panel in _playerStatusPanel.Controls)
        {
            var statusLabel = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text.Contains(competitor.PlayerName) == false && l.Font.Size == 9);
            if (statusLabel != null && panel.Controls.OfType<Label>().Any(l => l.Text.Contains(competitor.PlayerName)))
            {
                var status = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text == "⏳ Waiting..." || l.Text.StartsWith("✅") || l.Text.StartsWith("❌"));
                if (status != null)
                {
                    status.Text = text;
                    status.ForeColor = color;
                }
                break;
            }
        }
    }

    private async Task RunTimerAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>();
        System.Windows.Forms.Timer timer = null;

        timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (s, e) =>
        {
            if (_isClosing || ct.IsCancellationRequested || _currentTimerValue <= 0)
            {
                timer.Stop();
                tcs.TrySetResult(true);
                return;
            }

            var elapsed = (DateTime.UtcNow - _questionStartTime).TotalSeconds;
            _currentTimerValue = Math.Max(0, QUESTION_TIME_SECONDS - (int)elapsed);

            double percentRemaining = (double)_currentTimerValue / QUESTION_TIME_SECONDS;
            Color barColor = percentRemaining <= 0.20 ? Color.Red : Color.FromArgb(0, 120, 215);

            SafeInvoke(() =>
            {
                _timerBar.Value = _currentTimerValue;
                _timerBar.ForeColor = barColor;
                _timerLabel.Text = _currentTimerValue.ToString();
                _timerLabel.ForeColor = _currentTimerValue <= 10 ? Color.Red : Color.FromArgb(80, 120, 180);
            });

            if (_currentTimerValue <= 0)
            {
                timer.Stop();
                tcs.TrySetResult(true);
            }
        };
        timer.Start();

        await tcs.Task;
        try { timer.Dispose(); } catch { }
    }

    private async Task RevealAnswersAsync(QuestionData question)
    {
        CalculateQuestionScores(question);

        SafeInvoke(() =>
        {
            _revealPanel.Controls.Clear();
            _revealPanel.Visible = true;
            _questionPanel.Visible = false;

            int y = 20;

            Label title = new Label
            {
                Text = "⏰ TIME'S UP! ANSWERS REVEALED ⏰",
                Font = new Font("Arial", 24, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 100, 30),
                AutoSize = false,
                Size = new Size(800, 40),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(50, y)
            };
            _revealPanel.Controls.Add(title);
            y += 50;

            Label correctAnswer = new Label
            {
                Text = $"Correct Answer: {question.CorrectWord}",
                Font = new Font("Arial", 20, FontStyle.Bold),
                ForeColor = Color.Green,
                AutoSize = false,
                Size = new Size(800, 35),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(50, y)
            };
            _revealPanel.Controls.Add(correctAnswer);
            y += 50;

            var sortedSubmissions = _answerQueue.OrderBy(s => s.TimeTaken).ToList();
            var allPlayers = _competitors.Values.ToList();

            foreach (var player in allPlayers.OrderBy(p => p.MarkerId))
            {
                var submission = sortedSubmissions.FirstOrDefault(s => s.PlayerId == player.PlayerId);

                string resultText;
                Color resultColor;

                if (submission != null)
                {
                    string correctMark = submission.IsCorrect ? "✓" : "✗";
                    string pointsText = submission.IsCorrect ? $" (+{player.CurrentQuestionScore} pts)" : "";
                    resultText = $"{correctMark} {player.PlayerName} (M{player.MarkerId}): {submission.Answer} ({submission.TimeTaken.TotalSeconds:F1}s){pointsText}";
                    resultColor = submission.IsCorrect ? Color.Green : Color.Red;
                }
                else
                {
                    resultText = $"✗ {player.PlayerName} (M{player.MarkerId}): No answer (0 pts)";
                    resultColor = Color.Gray;
                }

                Label result = new Label
                {
                    Text = resultText,
                    Font = new Font("Arial", 14, FontStyle.Regular),
                    ForeColor = resultColor,
                    AutoSize = false,
                    Size = new Size(800, 28),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Location = new Point(50, y)
                };
                _revealPanel.Controls.Add(result);
                y += 32;
            }
        });

        if (_synth != null && !_isClosing)
        {
            await Task.Delay(500);

            var submissions = _answerQueue.OrderBy(s => s.TimeTaken).ToList();
            foreach (var sub in submissions)
            {
                if (_isClosing) return;

                var player = _competitors[sub.PlayerId];
                string result = sub.IsCorrect ? "correct" : "incorrect";

                _speechCompletion = new TaskCompletionSource<bool>();
                _synth.SpeakAsync($"{player.PlayerName} chose {sub.Answer}, which is {result}. They earned {player.CurrentQuestionScore} points.");
                await _speechCompletion.Task;
                await Task.Delay(800);
            }

            var leader = _competitors.Values
                .OrderByDescending(c => c.TotalScore)
                .ThenBy(c => c.TotalTime)
                .FirstOrDefault();

            if (leader != null && !_isClosing)
            {
                _speechCompletion = new TaskCompletionSource<bool>();
                _synth.SpeakAsync($"Current leader is {leader.PlayerName} with {leader.TotalScore} points.");
                await _speechCompletion.Task;
                await Task.Delay(500);
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(REVEAL_TIME_SECONDS));

        SafeInvoke(() => _revealPanel.Visible = false);
    }

    private void CalculateQuestionScores(QuestionData question)
    {
        var correctAnswers = _answerQueue
            .Where(a => a.IsCorrect)
            .OrderBy(a => a.TimeTaken)
            .ToList();

        for (int i = 0; i < correctAnswers.Count; i++)
        {
            var answer = correctAnswers[i];
            int points = i == 0 ? 100 : i == 1 ? 80 : i == 2 ? 60 : 40;

            if (_competitors.TryGetValue(answer.PlayerId, out var competitor))
            {
                competitor.CurrentQuestionScore = points;
                competitor.TotalScore += points;
                competitor.TotalTime += answer.TimeTaken;
            }
        }
    }

    private async Task ShowLeaderboardAsync(int completedQuestions)
    {
        SafeInvoke(() =>
        {
            _leaderboardPanel.Controls.Clear();
            _leaderboardPanel.Visible = true;
            _questionPanel.Visible = false;

            int y = 20;

            Label title = new Label
            {
                Text = $"🏆 Question {completedQuestions}/{TOTAL_QUESTIONS} Complete! 🏆",
                Font = new Font("Arial", 24, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 120, 40),
                AutoSize = false,
                Size = new Size(800, 40),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(50, y)
            };
            _leaderboardPanel.Controls.Add(title);
            y += 60;

            var rankings = _competitors.Values
                .OrderByDescending(c => c.TotalScore)
                .ThenBy(c => c.TotalTime)
                .ToList();

            for (int i = 0; i < Math.Min(3, rankings.Count); i++)
            {
                var c = rankings[i];
                c.Rank = i + 1;

                var podiumCard = CreatePodiumCard(c, i);
                podiumCard.Location = new Point(80 + i * 260, y);
                _leaderboardPanel.Controls.Add(podiumCard);
            }
        });

        if (_synth != null && !_isClosing)
        {
            var top3 = _competitors.Values
                .OrderByDescending(c => c.TotalScore)
                .ThenBy(c => c.TotalTime)
                .Take(3)
                .ToList();

            if (top3.Count > 0)
            {
                _speechCompletion = new TaskCompletionSource<bool>();
                _synth.SpeakAsync($"Current leader: {top3[0].PlayerName} with {top3[0].TotalScore} points");
                await _speechCompletion.Task;
                await Task.Delay(500);
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(LEADERBOARD_TIME_SECONDS));

        SafeInvoke(() =>
        {
            _leaderboardPanel.Visible = false;
            _questionPanel.Visible = true;
        });
    }

    private RoundedShadowPanel CreatePodiumCard(Competitor c, int position)
    {
        Color[] colors =
        {
            Color.FromArgb(255, 215, 0),
            Color.FromArgb(192, 192, 192),
            Color.FromArgb(205, 127, 50)
        };

        int[] heights = { 160, 140, 120 };

        var card = new RoundedShadowPanel
        {
            CornerRadius = 20,
            FillColor = Color.FromArgb(255, 250, 240),
            BorderColor = colors[position],
            BorderThickness = 3f,
            ShadowColor = Color.FromArgb(50, 0, 0, 0),
            ShadowOffsetX = 4,
            ShadowOffsetY = 6,
            Size = new Size(200, heights[position])
        };

        var rankLabel = new Label
        {
            Text = $"#{position + 1}",
            Font = new Font("Arial", 28, FontStyle.Bold),
            ForeColor = colors[position],
            AutoSize = false,
            Size = new Size(180, 40),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 10)
        };

        var nameLabel = new Label
        {
            Text = c.PlayerName,
            Font = new Font("Arial", 13, FontStyle.Bold),
            ForeColor = Color.FromArgb(60, 60, 60),
            AutoSize = false,
            Size = new Size(180, 25),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 55)
        };

        var scoreLabel = new Label
        {
            Text = $"{c.TotalScore} pts",
            Font = new Font("Arial", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(80, 120, 80),
            AutoSize = false,
            Size = new Size(180, 25),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 85)
        };

        var markerLabel = new Label
        {
            Text = $"M{c.MarkerId}",
            Font = new Font("Arial", 10, FontStyle.Regular),
            ForeColor = Color.Gray,
            AutoSize = false,
            Size = new Size(180, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 115)
        };

        double accuracy = c.QuestionsAnswered > 0 ? (c.CorrectAnswers * 100.0 / c.QuestionsAnswered) : 0;
        var accuracyLabel = new Label
        {
            Text = $"Acc: {accuracy:F0}%",
            Font = new Font("Arial", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 100, 100),
            AutoSize = false,
            Size = new Size(180, 18),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 135)
        };

        card.Controls.Add(rankLabel);
        card.Controls.Add(nameLabel);
        card.Controls.Add(scoreLabel);
        card.Controls.Add(markerLabel);
        card.Controls.Add(accuracyLabel);

        return card;
    }

    private async Task ShowFinalCelebrationAsync()
    {
        SafeInvoke(() =>
        {
            _questionPanel.Visible = false;
            _leaderboardPanel.Visible = true;
            _leaderboardPanel.Controls.Clear();

            Label title = new Label
            {
                Text = "🎉 COMPETITION COMPLETE! 🎉",
                Font = new Font("Arial", 38, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 100, 50),
                AutoSize = false,
                Size = new Size(1000, 80),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(50, 20)
            };
            _leaderboardPanel.Controls.Add(title);
        });

        var confetti = new ConfettiAnimation();
        SafeInvoke(() =>
        {
            this.Controls.Add(confetti);
            confetti.BringToFront();
            confetti.StartAnimation(8000);
        });

        await Task.Delay(8000);

        SafeInvoke(() =>
        {
            if (this.Controls.Contains(confetti))
                this.Controls.Remove(confetti);
        });

        CloseCompetitionSafely();
    }

    public void addTuioObject(TuioObject o)
    {
        if (_isClosing) return;

        if (o.SymbolID == BACK_MARKER)
        {
            CloseCompetitionSafely();
            return;
        }

        if (_hostingPhase && o.SymbolID == HOST_START_MARKER)
        {
            if (_competitors.Count >= MIN_PLAYERS)
            {
                try { _competitionCts.Cancel(); } catch { }
                return;
            }
            else
            {
                try
                {
                    if (_synth != null && !AppSettings.IsMuted)
                        _synth.SpeakAsync($"Need at least {MIN_PLAYERS} players to start.");
                }
                catch { }
                return;
            }
        }

        if (_hostingPhase && o.SymbolID >= PLAYER_MARKER_START && o.SymbolID <= PLAYER_MARKER_END)
        {
            string playerId = o.SessionID.ToString();

            if (_usedMarkerIds.Contains(o.SymbolID))
            {
                try
                {
                    if (_synth != null && !AppSettings.IsMuted)
                        _synth.SpeakAsync($"Marker {o.SymbolID} is already taken! Please use a different marker.");
                }
                catch { }
                return;
            }

            if (!_competitors.ContainsKey(playerId))
            {
                int playerNumber = _competitors.Count + 1;
                string playerName = "Player " + playerNumber;

                var competitor = new Competitor
                {
                    PlayerId = playerId,
                    PlayerName = playerName,
                    MarkerId = o.SymbolID,
                    JoinedAt = DateTime.UtcNow,
                    Status = CompetitorStatus.Ready,
                    AnswerTcs = new TaskCompletionSource<string>()
                };

                _competitors.TryAdd(playerId, competitor);
                _usedMarkerIds.Add(o.SymbolID);

                int colorIndex = (_playerRotationStates.Count % _playerColors.Length);
                _playerRotationStates[o.SymbolID] = new PlayerRotationState
                {
                    AnchorAngle = NormalizeAngle(o.AngleDegrees),
                    CurrentSelection = -1,
                    PlayerColor = _playerColors[colorIndex],
                    LastRotationTime = DateTime.UtcNow
                };

                SafeInvoke(() => RefreshPlayerCards());

                try
                {
                    if (_synth != null && !AppSettings.IsMuted)
                        _synth.SpeakAsync($"{playerName} joined with marker {o.SymbolID}!");
                }
                catch { }
            }

            return;
        }

        if (_competitionActive && _questionActive && o.SymbolID >= PLAYER_MARKER_START && o.SymbolID <= PLAYER_MARKER_END)
        {
            var competitor = _competitors.Values.FirstOrDefault(c => c.MarkerId == o.SymbolID);
            if (competitor != null && competitor.Status == CompetitorStatus.Answering)
            {
                if (!_playerRotationStates.ContainsKey(o.SymbolID))
                {
                    int colorIndex = (_playerRotationStates.Count % _playerColors.Length);
                    _playerRotationStates[o.SymbolID] = new PlayerRotationState
                    {
                        AnchorAngle = NormalizeAngle(o.AngleDegrees),
                        CurrentSelection = -1,
                        PlayerColor = _playerColors[colorIndex],
                        LastRotationTime = DateTime.UtcNow
                    };
                }
                else
                {
                    if (_playerRotationStates[o.SymbolID].AnchorAngle < 0)
                        _playerRotationStates[o.SymbolID].AnchorAngle = NormalizeAngle(o.AngleDegrees);
                }
            }
        }
    }

    public void updateTuioObject(TuioObject o)
    {
        if (_isClosing) return;

        if (_competitionActive && _questionActive && o.SymbolID >= PLAYER_MARKER_START && o.SymbolID <= PLAYER_MARKER_END)
        {
            var competitor = _competitors.Values.FirstOrDefault(c => c.MarkerId == o.SymbolID);
            if (competitor != null && competitor.Status == CompetitorStatus.Answering && !_playersWhoSubmitted.Contains(o.SymbolID))
            {
                if (_playerRotationStates.TryGetValue(o.SymbolID, out var state))
                {
                    float currentAngle = NormalizeAngle(o.AngleDegrees);

                    if (state.AnchorAngle < 0)
                    {
                        state.AnchorAngle = currentAngle;
                        return;
                    }

                    float delta = SmallestAngleDifference(currentAngle, state.AnchorAngle);

                    while (delta >= ROTATION_STEP)
                    {
                        state.CurrentSelection = (state.CurrentSelection + 1) % 3;
                        state.AnchorAngle = NormalizeAngle(state.AnchorAngle + ROTATION_STEP);
                        delta = SmallestAngleDifference(currentAngle, state.AnchorAngle);

                        UpdatePlayerSelectionDisplay(o.SymbolID, state.CurrentSelection);

                        if (_synth != null && (DateTime.UtcNow - state.LastRotationTime).TotalMilliseconds > ROTATION_COOLDOWN_MS)
                            state.LastRotationTime = DateTime.UtcNow;
                    }

                    while (delta <= -ROTATION_STEP)
                    {
                        state.CurrentSelection = (state.CurrentSelection - 1 + 3) % 3;
                        state.AnchorAngle = NormalizeAngle(state.AnchorAngle - ROTATION_STEP);
                        delta = SmallestAngleDifference(currentAngle, state.AnchorAngle);

                        UpdatePlayerSelectionDisplay(o.SymbolID, state.CurrentSelection);

                        if (_synth != null && (DateTime.UtcNow - state.LastRotationTime).TotalMilliseconds > ROTATION_COOLDOWN_MS)
                            state.LastRotationTime = DateTime.UtcNow;
                    }
                }
            }
        }
    }

    private void UpdatePlayerSelectionDisplay(int markerId, int selection)
    {
        SafeInvoke(() =>
        {
            if (_playerRotationStates.TryGetValue(markerId, out var state) && state.SelectionLabel != null)
            {
                string displayText = selection == -1 ? "?" : (selection == 0 ? "A" : (selection == 1 ? "B" : "C"));
                state.SelectionLabel.Text = displayText;

                if (selection == 0)
                    state.SelectionLabel.ForeColor = Color.FromArgb(255, 200, 200);
                else if (selection == 1)
                    state.SelectionLabel.ForeColor = Color.FromArgb(200, 200, 255);
                else if (selection == 2)
                    state.SelectionLabel.ForeColor = Color.FromArgb(255, 200, 255);
                else
                    state.SelectionLabel.ForeColor = Color.White;

                if (state.SelectionPanel != null)
                    state.SelectionPanel.Invalidate();
            }
        });
    }

    public void removeTuioObject(TuioObject o)
    {
        if (_isClosing) return;

        if (_competitionActive && _questionActive && o.SymbolID >= PLAYER_MARKER_START && o.SymbolID <= PLAYER_MARKER_END)
        {
            var competitor = _competitors.Values.FirstOrDefault(c => c.MarkerId == o.SymbolID);
            if (competitor != null && competitor.Status == CompetitorStatus.Answering && !_playersWhoSubmitted.Contains(o.SymbolID))
            {
                if (_playerRotationStates.TryGetValue(o.SymbolID, out var state))
                {
                    if (state.CurrentSelection >= 0 && state.CurrentSelection <= 2)
                    {
                        var question = _questions[_currentQuestionIndex];
                        string answer = question.Options[state.CurrentSelection];

                        if (competitor.AnswerTcs.TrySetResult(answer))
                        {
                            _playersWhoSubmitted.Add(o.SymbolID);
                            state.LastSubmittedSelection = state.CurrentSelection;

                            SafeInvoke(() =>
                            {
                                UpdatePlayerStatus(
                                    competitor,
                                    $"✅ Submitted {(state.CurrentSelection == 0 ? "A" : (state.CurrentSelection == 1 ? "B" : "C"))}",
                                    Color.Green);

                                if (state.SelectionLabel != null)
                                {
                                    state.SelectionLabel.Text = $"✓{state.SelectionLabel.Text}";
                                    state.SelectionLabel.ForeColor = Color.FromArgb(100, 255, 100);
                                }

                                if (state.SelectionPanel != null)
                                {
                                    state.SelectionPanel.BorderColor = Color.Green;
                                    state.SelectionPanel.BorderThickness = 3f;
                                }
                            });

                            try
                            {
                                if (_synth != null && !AppSettings.IsMuted)
                                    _synth.SpeakAsync($"{competitor.PlayerName} submitted answer {(state.CurrentSelection == 0 ? "A" : (state.CurrentSelection == 1 ? "B" : "C"))}");
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        try
                        {
                            _synth.Rate = AppSettings.VoiceRate;
                            if (_synth != null && !AppSettings.IsMuted)
                                _synth.SpeakAsync($"{competitor.PlayerName}, please rotate to select an answer first!");
                        }
                        catch { }
                    }
                }
            }
        }

        if (_playerRotationStates.ContainsKey(o.SymbolID) && !_playersWhoSubmitted.Contains(o.SymbolID))
            _playerRotationStates[o.SymbolID].AnchorAngle = -1;
    }

    private float NormalizeAngle(float angle)
    {
        while (angle < 0) angle += 360f;
        while (angle >= 360f) angle -= 360f;
        return angle;
    }

    private float SmallestAngleDifference(float a, float b)
    {
        float diff = a - b;
        while (diff > 180f) diff -= 360f;
        while (diff < -180f) diff += 360f;
        return diff;
    }

    public void addTuioCursor(TuioCursor c) { }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { }
    public void addTuioBlob(TuioBlob b) { }
    public void updateTuioBlob(TuioBlob b) { }
    public void removeTuioBlob(TuioBlob b) { }
    public void refresh(TuioTime frameTime) { }

    private RoundedShadowPanel CreateRoundedPanel(Color backColor, int radius)
    {
        return new RoundedShadowPanel
        {
            CornerRadius = radius,
            FillColor = backColor,
            BorderColor = Color.FromArgb(220, 228, 235),
            BorderThickness = 1.5f,
            ShadowColor = Color.FromArgb(30, 0, 0, 0),
            DrawGloss = false,
            ShadowOffsetX = 5,
            ShadowOffsetY = 8
        };
    }

    private void ArrangeControls()
    {
        int margin = 20;
        int availableWidth = this.ClientSize.Width - (margin * 2);
        int availableHeight = this.ClientSize.Height - 100;

        ScalePanelToFit(_lobbyPanel, availableWidth, availableHeight);
        ScalePanelToFit(_questionPanel, availableWidth, availableHeight);
        ScalePanelToFit(_leaderboardPanel, availableWidth, availableHeight);
        ScalePanelToFit(_revealPanel, availableWidth, availableHeight);

        int x = (this.ClientSize.Width - _lobbyPanel.Width) / 2;
        int y = 80;

        _lobbyPanel.Location = new Point(x, y);
        _questionPanel.Location = new Point(x, y);
        _leaderboardPanel.Location = new Point(x, y);
        _revealPanel.Location = new Point(x, y);
    }

    private void ScalePanelToFit(RoundedShadowPanel panel, int maxWidth, int maxHeight)
    {
        if (panel.Width > maxWidth) panel.Width = maxWidth;
        if (panel.Height > maxHeight) panel.Height = maxHeight;
    }

    private GraphicsPath GetRoundedRectangle(Rectangle rect, int radius)
    {
        GraphicsPath path = new GraphicsPath();
        int d = radius * 2;

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    private void SafeInvoke(Action action)
    {
        if (_isClosing || this.IsDisposed || !this.IsHandleCreated)
            return;

        try
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if (!_isClosing && !this.IsDisposed)
                        action();
                });
            }
            else
            {
                if (!_isClosing && !this.IsDisposed)
                    action();
            }
        }
        catch { }
    }

    private void CloseCompetitionSafely()
    {
        if (_isClosing) return;

        _isClosing = true;

        try { _competitionCts.Cancel(); } catch { }

        try
        {
            if (_client != null)
                _client.removeTuioListener(this);
        }
        catch { }

        try
        {
            if (_synth != null)
            {
                _synth.SpeakAsyncCancelAll();
                _synth.Dispose();
                _synth = null;
            }
        }
        catch { }

        try
        {
            foreach (var kv in _imageCache)
            {
                if (kv.Value != null)
                    kv.Value.Dispose();
            }
            _imageCache.Clear();
        }
        catch { }

        try
        {
            if (!this.IsDisposed)
            {
                if (this.InvokeRequired)
                    this.BeginInvoke((MethodInvoker)(() => this.Close()));
                else
                    this.Close();
            }
        }
        catch { }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _isClosing = true;

        try { _competitionCts.Cancel(); } catch { }
        try { if (_client != null) _client.removeTuioListener(this); } catch { }

        try
        {
            if (_synth != null)
            {
                _synth.SpeakAsyncCancelAll();
                _synth.Dispose();
                _synth = null;
            }
        }
        catch { }

        try
        {
            foreach (var kv in _imageCache)
            {
                if (kv.Value != null)
                    kv.Value.Dispose();
            }
            _imageCache.Clear();
        }
        catch { }

        base.OnFormClosed(e);
    }
}

public class ConfettiAnimation : Panel
{
    private struct Particle
    {
        public float X, Y, VX, VY;
        public Color Color;
        public int Size;
    }

    private List<Particle> _particles = new List<Particle>();
    private Random _rng = new Random();
    private System.Windows.Forms.Timer _timer;
    private CancellationTokenSource _cts;

    public ConfettiAnimation()
    {
        this.Dock = DockStyle.Fill;
        this.BackColor = Color.Transparent;
        this.DoubleBuffered = true;
        this.Visible = true;

        for (int i = 0; i < 150; i++)
        {
            _particles.Add(new Particle
            {
                X = _rng.Next(0, 1920),
                Y = _rng.Next(-500, 0),
                VX = (float)(_rng.NextDouble() * 8 - 4),
                VY = (float)(_rng.NextDouble() * 6 + 3),
                Color = Color.FromArgb(
                    _rng.Next(100, 255),
                    _rng.Next(100, 255),
                    _rng.Next(100, 255)),
                Size = _rng.Next(8, 20)
            });
        }
    }

    public void StartAnimation(int durationMs = 8000)
    {
        _cts = new CancellationTokenSource();
        _timer = new System.Windows.Forms.Timer { Interval = 30 };
        _timer.Tick += (s, e) =>
        {
            UpdateParticles();
            this.Invalidate();
        };
        _timer.Start();

        Task.Delay(durationMs, _cts.Token).ContinueWith(_ =>
        {
            try { _timer?.Stop(); } catch { }

            if (!this.IsDisposed && this.IsHandleCreated)
            {
                try
                {
                    this.BeginInvoke((MethodInvoker)(() => this.Visible = false));
                }
                catch { }
            }
        }, TaskScheduler.Default);
    }

    private void UpdateParticles()
    {
        int width = this.Width == 0 ? 1920 : this.Width;
        int height = this.Height == 0 ? 1080 : this.Height;

        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            p.X += p.VX;
            p.Y += p.VY;
            p.VY += 0.15f;

            if (p.Y > height + 50)
            {
                p.Y = -50;
                p.X = _rng.Next(0, width);
                p.VY = (float)(_rng.NextDouble() * 4 + 2);
            }

            _particles[i] = p;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        foreach (var p in _particles)
        {
            using (var brush = new SolidBrush(p.Color))
                g.FillRectangle(brush, p.X, p.Y, p.Size, p.Size);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _cts?.Cancel(); } catch { }
            try { _timer?.Stop(); } catch { }
            try { _timer?.Dispose(); } catch { }
            try { _cts?.Dispose(); } catch { }
        }

        base.Dispose(disposing);
    }
}