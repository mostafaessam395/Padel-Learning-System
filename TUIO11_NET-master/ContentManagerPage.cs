using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TUIO;
using TuioDemo;

/// <summary>
/// Admin Management — two tabs: Training Content + Users Management.
/// Users tab: fully TUIO-marker driven editing (no mouse/keyboard required).
/// </summary>
public class ContentManagerPage : Form, TuioListener
{
    // ── services ──────────────────────────────────────────────────────────
    private readonly TuioClient     _tc;
    private readonly ContentService _contentSvc = new ContentService();
    private readonly UserService    _userSvc    = new UserService();

    // ── users tab state ───────────────────────────────────────────────────
    private DataGridView   _uGrid;
    private List<UserData> _uItems = new List<UserData>();

    // Read-only display labels (no textbox/combobox input for users)
    private Label _uId, _uName, _uAge, _uGender, _uLevel, _uBt, _uFace, _uRole, _uActive;

    // TUIO editing state
    private int    _selectedFieldIdx  = 0;   // which field is selected (Marker 38)
    private bool   _textEditMode      = false; // true when editing a text field char-by-char
    private string _textEditBuffer    = "";    // current text being built
    private int    _charWheelIdx      = 0;    // index into char wheel (Marker 39)

    // Field names in order
    private static readonly string[] FIELD_NAMES = {
        "User ID", "Name", "Age", "Gender", "Level",
        "Bluetooth ID", "Face ID", "Role", "Active" };

    // Dropdown options per field index
    private static readonly string[][] FIELD_OPTIONS = {
        null,                                                                    // 0 User ID  (text)
        null,                                                                    // 1 Name     (text)
        null,                                                                    // 2 Age      (numeric)
        new[]{"Male","Female","Other"},                                          // 3 Gender
        new[]{"Beginner","Intermediate","Advanced","Primary","HighSchool"},      // 4 Level
        null,                                                                    // 5 BT ID    (text)
        null,                                                                    // 6 Face ID  (text)
        new[]{"Player","Admin"},                                                 // 7 Role
        new[]{"true","false"},                                                   // 8 Active
    };

    // Current dropdown index per field (for fields with options)
    private int[] _fieldOptionIdx = new int[9];

    // Character wheel for text fields
    private static readonly char[] CHAR_WHEEL =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 _:-".ToCharArray();

    // Status / HUD labels
    private Label _hudUser, _hudField, _hudValue, _hudMode, _hudChars, _hudInstructions;
    private Panel _hudPanel;
    // Editor card controls (rebuilt each UpdateHud call)
    private Label _edCurrentText;   // big box showing text being built
    private Label _edSelectedChar;  // big box showing selected character
    private Panel _edCharRow;       // row of character tiles
    private Label _edModeLabel;     // "Text Editing" / "Value Select"
    private Label _edValueLabel;    // for non-text fields: current / new value
    private Panel _editorCard;      // the whole right card

    // Marker rotation state
    private float    _m36Angle   = float.NaN;
    private float    _m38Angle   = float.NaN;
    private float    _m39Angle   = float.NaN;
    private DateTime _m36Time    = DateTime.MinValue;
    private DateTime _m38Time    = DateTime.MinValue;
    private DateTime _m39Time    = DateTime.MinValue;
    private int      _lastAction = -1;
    private DateTime _lastActTime= DateTime.MinValue;

    private const int   ACTION_CD_MS  = 600;
    private const float ROT_THRESHOLD = 0.30f;  // ~17 degrees

    // ── content tab state ─────────────────────────────────────────────────
    private DataGridView  _cGrid;
    private List<PadelContentItem> _cItems = new List<PadelContentItem>();
    private TextBox  _cId, _cTitle, _cDesc, _cTip, _cZone;
    private ComboBox _cLevel, _cMarker, _cModule, _cActivity, _cDiff;
    private CheckBox _cActive;
    private Button   _cBtnAdd, _cBtnSave, _cBtnDeact, _cBtnDel, _cBtnClr;

    // ── colours (aligned with PadelTheme) ─────────────────────────────────
    static readonly Color BG       = PadelTheme.BgDeep;
    static readonly Color PANEL    = PadelTheme.BgPanel;
    static readonly Color PANEL2   = PadelTheme.BgPanelAlt;
    static readonly Color HEADER   = Color.FromArgb(10, 14, 30);
    static readonly Color ACCENT   = PadelTheme.Primary;
    static readonly Color ACCENT2  = PadelTheme.Accent;
    static readonly Color TXT      = PadelTheme.TextHi;
    static readonly Color TXT_DIM  = PadelTheme.TextLo;
    static readonly Color FIELD_BG = PadelTheme.BgElevated;
    static readonly Color ROW_ALT  = Color.FromArgb(22, 30, 52);
    static readonly Color SEL_BG   = PadelTheme.PrimaryDeep;
    static readonly Color EDIT_HL  = PadelTheme.Gold;
    static readonly Color EDIT_ACT = PadelTheme.Ok;

    // ── unused field kept to avoid CS0414 ────────────────────────────────
    private Color _uBtnClr = Color.FromArgb(60, 68, 100);

    public ContentManagerPage(TuioClient tc = null)
    {
        _tc = tc;
        Text           = "Admin Management";
        WindowState    = FormWindowState.Maximized;
        StartPosition  = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        BackColor      = BG;
        MinimumSize    = new Size(1100, 700);

        try { Build(); }
        catch (Exception ex) { LogErr(ex); ShowErr("Build error", ex.Message); }

        Shown += (s, e) =>
        {
            try
            {
                if (_tc != null) _tc.addTuioListener(this);
                GestureRouter.ClaimFocus(this);
                GestureRouter.OnGestureMarker += OnGesture;
                ReloadUsers();
                ReloadContent();
            }
            catch (Exception ex) { LogErr(ex); }
        };
        FormClosed += (s, e) =>
        {
            GestureRouter.OnGestureMarker -= OnGesture;
            GestureRouter.ReleaseFocus(this);
            if (_tc != null) _tc.removeTuioListener(this);
        };
    }

    // ═════════════════════════════════════════════════════════════════════
    //  TOP-LEVEL LAYOUT
    // ═════════════════════════════════════════════════════════════════════
    private void Build()
    {
        var hdr = new GradientHeader
        {
            Title        = "Content Management",
            Subtitle     = "TUIO-driven user editing · training content library",
            Icon         = "📋",
            Height       = 118,
            GradientFrom = PadelTheme.PrimaryDeep,
            GradientTo   = PadelTheme.Accent,
            AccentColor  = PadelTheme.Accent,
            Dock         = DockStyle.Top,
        };
        Controls.Add(hdr);

        var tabs = new TabControl {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Padding = new Point(22, 6),
            ItemSize = new Size(200, 32) };
        Controls.Add(tabs);

        var tUsers   = new TabPage("  Users Management  ")
            { BackColor = PANEL, UseVisualStyleBackColor = false };
        var tContent = new TabPage("  Training Content  ")
            { BackColor = PANEL, UseVisualStyleBackColor = false };

        tabs.TabPages.Add(tUsers);
        tabs.TabPages.Add(tContent);

        BuildUsersTab(tUsers);
        BuildContentTab(tContent);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  USERS TAB  — grid (top) + TUIO editor panel (bottom)
    // ═════════════════════════════════════════════════════════════════════
    private void BuildUsersTab(TabPage tab)
    {
        var outer = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            BackColor = PANEL };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // title bar
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // grid
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 340));  // TUIO editor
        tab.Controls.Add(outer);

        // ── title bar ─────────────────────────────────────────────────────
        var titleBar = new Panel { Dock = DockStyle.Fill, BackColor = HEADER,
            Padding = new Padding(16, 10, 12, 8) };
        titleBar.Controls.Add(new Label {
            Text = "Users Management  —  TUIO Marker Control",
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = ACCENT2, AutoSize = true, BackColor = Color.Transparent,
            Location = new Point(16, 10) });
        outer.Controls.Add(titleBar, 0, 0);

        // ── grid ──────────────────────────────────────────────────────────
        var gridWrap = new Panel { Dock = DockStyle.Fill,
            BackColor = PANEL, Padding = new Padding(12, 8, 12, 4) };
        outer.Controls.Add(gridWrap, 0, 1);
        _uGrid = MkGrid();
        _uGrid.Dock = DockStyle.Fill;
        _uGrid.SelectionChanged += OnURowSelected;
        gridWrap.Controls.Add(_uGrid);

        // ── TUIO editor panel ─────────────────────────────────────────────
        var editorPanel = new Panel { Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 28, 50),
            Padding = new Padding(14, 10, 14, 10) };
        outer.Controls.Add(editorPanel, 0, 2);
        BuildTuioEditorPanel(editorPanel);
    }

    private void BuildTuioEditorPanel(Panel parent)
    {
        // Split: left (60%) = field display + marker cards, right (40%) = HUD
        var split = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = Color.Transparent, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        parent.Controls.Add(split);

        // ── LEFT: field display + marker cards ───────────────────────────
        var leftPanel = new Panel { Dock = DockStyle.Fill,
            BackColor = Color.Transparent, Padding = new Padding(0, 0, 12, 0) };
        split.Controls.Add(leftPanel, 0, 0);

        // Section title
        var fieldTitle = new Label {
            Text = "User Data  —  edit using markers below",
            Dock = DockStyle.Top, Height = 26,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = ACCENT2, BackColor = Color.Transparent,
            Padding = new Padding(2, 4, 0, 0) };
        leftPanel.Controls.Add(fieldTitle);

        // Separator line under title
        var sep1 = new Panel { Dock = DockStyle.Top, Height = 2,
            BackColor = Color.FromArgb(40, 60, 100), Margin = new Padding(0, 2, 0, 6) };
        leftPanel.Controls.Add(sep1);

        // Field grid: 3 columns of (label + value), 3 rows for 9 fields
        var fieldGrid = new TableLayoutPanel {
            Dock = DockStyle.Top, Height = 138,
            ColumnCount = 6, RowCount = 3,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 4),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        // 3 pairs: label col (fixed) + value col (percent)
        fieldGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        fieldGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  33.3f));
        fieldGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        fieldGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  33.3f));
        fieldGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        fieldGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  33.3f));
        // 3 rows, each 42px tall for breathing room
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        leftPanel.Controls.Add(fieldGrid);

        // Create display labels
        _uId     = MkFieldLabel(); _uName   = MkFieldLabel();
        _uAge    = MkFieldLabel(); _uGender = MkFieldLabel();
        _uLevel  = MkFieldLabel(); _uBt     = MkFieldLabel();
        _uFace   = MkFieldLabel(); _uRole   = MkFieldLabel();
        _uActive = MkFieldLabel();

        var fieldDefs = new (string Lbl, Label Ctrl)[] {
            ("User ID",     _uId),   ("Name",        _uName),   ("Age",    _uAge),
            ("Gender",      _uGender),("Level",       _uLevel),  ("BT ID",  _uBt),
            ("Face ID",     _uFace), ("Role",         _uRole),   ("Active", _uActive)
        };

        int col = 0, row = 0;
        foreach (var (lbl, ctrl) in fieldDefs) {
            fieldGrid.Controls.Add(new Label {
                Text = lbl, Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = TXT_DIM, BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(4, 4, 8, 4) }, col, row);
            fieldGrid.Controls.Add(ctrl, col + 1, row);
            col += 2;
            if (col >= 6) { col = 0; row++; }
        }

        // Separator before marker cards
        var sep2 = new Panel { Dock = DockStyle.Top, Height = 2,
            BackColor = Color.FromArgb(40, 60, 100), Margin = new Padding(0, 4, 0, 6) };
        leftPanel.Controls.Add(sep2);

        // Marker cards label
        leftPanel.Controls.Add(new Label {
            Text = "Marker Controls",
            Dock = DockStyle.Top, Height = 20,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(120, 150, 200),
            BackColor = Color.Transparent,
            Padding = new Padding(2, 0, 0, 0) });

        // Marker cards flow
        var markerFlow = new FlowLayoutPanel {
            Dock = DockStyle.Top, Height = 96,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, BackColor = Color.Transparent,
            Padding = new Padding(0, 6, 0, 0) };

        var markerDefs = new (string Id, string Line1, string Line2, Color Clr)[] {
            ("36", "M36 Rotate",  "Select User",   Color.FromArgb(80,  140, 220)),
            ("38", "M38 Rotate",  "Select Field",  Color.FromArgb(180, 100, 220)),
            ("39", "M39 Rotate",  "Change Value",  Color.FromArgb(220, 140,  40)),
            ("40", "M40 Tap",     "Confirm Value", Color.FromArgb(40,  200, 100)),
            ("41", "M41 Tap",     "Backspace",     Color.FromArgb(200,  80,  80)),
            ("42", "M42 Tap",     "Finish Field",  Color.FromArgb(40,  180, 200)),
            ("31", "M31 Tap",     "Add User",      Color.FromArgb(26,  130,  60)),
            ("32", "M32 Tap",     "Save User",     ACCENT),
            ("33", "M33 Tap",     "Deactivate",    Color.FromArgb(170, 100,  14)),
            ("34", "M34 Tap",     "Delete User",   Color.FromArgb(175,  35,  35)),
            ("35", "M35 Tap",     "Clear",         Color.FromArgb(60,   68, 100)),
            ("37", "M37 Tap",     "Gaze History",  Color.FromArgb(0,   190, 160)),
            ("20", "M20 Tap",     "Back",          Color.FromArgb(100,  60, 100)),
        };

        foreach (var (mid, line1, line2, clr) in markerDefs) {
            var card = new Panel {
                Size = new Size(104, 40),
                BackColor = Color.FromArgb(18, 26, 50),
                Margin = new Padding(0, 0, 7, 5) };
            card.Paint += (s, e) => {
                var g = e.Graphics;
                // top accent bar
                using (var b = new System.Drawing.SolidBrush(clr))
                    g.FillRectangle(b, 0, 0, card.Width, 4);
                // border
                using (var p = new System.Drawing.Pen(Color.FromArgb(55, clr.R, clr.G, clr.B), 1f))
                    g.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
            };
            card.Controls.Add(new Label {
                Text = line1,
                Font = new Font("Segoe UI", 7, FontStyle.Bold),
                ForeColor = clr, BackColor = Color.Transparent,
                Location = new Point(6, 6), AutoSize = true });
            card.Controls.Add(new Label {
                Text = line2,
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.FromArgb(210, 220, 242),
                BackColor = Color.Transparent,
                Location = new Point(6, 22), AutoSize = true });
            markerFlow.Controls.Add(card);
        }
        leftPanel.Controls.Add(markerFlow);

        // ── RIGHT: editor card ───────────────────────────────────────────
        _editorCard = new Panel { Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(12, 18, 38),
            Padding = new Padding(12, 8, 12, 8) };
        split.Controls.Add(_editorCard, 1, 0);

        _editorCard.Paint += (s, e) => {
            using (var pen = new System.Drawing.Pen(Color.FromArgb(50, 90, 180), 1.5f))
                e.Graphics.DrawRectangle(pen, 1, 1, _editorCard.Width - 3, _editorCard.Height - 3);
            using (var b = new System.Drawing.SolidBrush(ACCENT))
                e.Graphics.FillRectangle(b, 1, 1, 4, _editorCard.Height - 2);
        };

        // Use TableLayoutPanel so rows never overflow
        var edTbl = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 11,
            BackColor = Color.Transparent, Padding = new Padding(6, 0, 0, 0) };
        edTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        edTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // 0 selected user
        edTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));  // 1 selected field
        edTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));  // 2 editing mode
        edTbl.RowStyles.Add(new RowStyle(SizeType.Absolute,  6));  // 3 separator
        edTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));  // 4 "Current Text:" label
        edTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));  // 5 current text value
        edTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));  // 6 "Selected Character:" label
        edTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // 7 selected char value
        edTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));  // 8 "Character Preview:" label
        edTbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // 9 char wheel row
        edTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 10 instructions (fills rest)
        _editorCard.Controls.Add(edTbl);

        // Row 0: selected user
        _hudUser = new Label {
            Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.White, BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
            Padding = new Padding(2, 0, 0, 0), Text = "Selected User: —" };
        edTbl.Controls.Add(_hudUser, 0, 0);

        // Row 1: selected field
        _hudField = new Label {
            Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = EDIT_HL, BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
            Padding = new Padding(2, 0, 0, 0), Text = "Selected Field: —" };
        edTbl.Controls.Add(_hudField, 0, 1);

        // Row 2: editing mode
        _edModeLabel = new Label {
            Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8, FontStyle.Italic),
            ForeColor = TXT_DIM, BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 0, 0, 0), Text = "Editing Mode: Idle" };
        edTbl.Controls.Add(_edModeLabel, 0, 2);

        // Row 3: separator
        edTbl.Controls.Add(new Panel { Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(40, 60, 100), Margin = new Padding(0, 2, 0, 2) }, 0, 3);

        // Row 4: "Current Text:" caption
        edTbl.Controls.Add(new Label {
            Dock = DockStyle.Fill, Text = "Current Text:",
            Font = new Font("Segoe UI", 7, FontStyle.Bold),
            ForeColor = TXT_DIM, BackColor = Color.Transparent,
            TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(2, 0, 0, 0) }, 0, 4);

        // Row 5: current text value
        _edCurrentText = new Label {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = EDIT_ACT, BackColor = Color.FromArgb(20, 32, 58),
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 1, 0, 1),
            AutoEllipsis = true, Text = "—" };
        edTbl.Controls.Add(_edCurrentText, 0, 5);

        // Row 6: "Selected Character:" caption
        edTbl.Controls.Add(new Label {
            Dock = DockStyle.Fill, Text = "Selected Character:",
            Font = new Font("Segoe UI", 7, FontStyle.Bold),
            ForeColor = TXT_DIM, BackColor = Color.Transparent,
            TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(2, 0, 0, 0) }, 0, 6);

        // Row 7: selected character — compact, not huge
        _edSelectedChar = new Label {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = Color.Black, BackColor = EDIT_HL,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 1, 0, 1),
            AutoEllipsis = true, Text = "—" };
        edTbl.Controls.Add(_edSelectedChar, 0, 7);

        // Row 8: "Character Preview:" caption
        edTbl.Controls.Add(new Label {
            Dock = DockStyle.Fill, Text = "Character Preview:",
            Font = new Font("Segoe UI", 7, FontStyle.Bold),
            ForeColor = TXT_DIM, BackColor = Color.Transparent,
            TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(2, 0, 0, 0) }, 0, 8);

        // Row 9: character wheel row (panel, filled dynamically)
        _edCharRow = new Panel {
            Dock = DockStyle.Fill, BackColor = Color.Transparent,
            Margin = new Padding(0, 1, 0, 1) };
        edTbl.Controls.Add(_edCharRow, 0, 9);

        // Row 10: instructions (fills remaining space)
        _hudInstructions = new Label {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 7),
            ForeColor = Color.FromArgb(90, 120, 170),
            BackColor = Color.FromArgb(14, 20, 36),
            Text = BuildInstructionText(),
            Padding = new Padding(6, 4, 4, 4),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 4, 0, 0) };
        edTbl.Controls.Add(_hudInstructions, 0, 10);

        // value label for non-text fields (replaces rows 6-9 content)
        _edValueLabel = new Label {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = EDIT_ACT, BackColor = Color.FromArgb(20, 32, 58),
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 1, 0, 1),
            AutoEllipsis = true, Text = "—", Visible = false };
        // not added to table — shown via overlay when needed

        _hudValue = _hudMode = _hudChars = null;
        _hudPanel = _editorCard;
    }

    private static string BuildInstructionText() =>
        "M36 Rotate → Select user\r\n" +
        "M38 Rotate → Select field\r\n" +
        "M39 Rotate → Scroll character / change value\r\n" +
        "M40 Tap    → Add selected character\r\n" +
        "M41 Tap    → Backspace\r\n" +
        "M42 Tap    → Finish field\r\n" +
        "M31 Tap    → Add user\r\n" +
        "M32 Tap    → Save user\r\n" +
        "M33 Tap    → Deactivate  |  M34 → Delete\r\n" +
        "M35 Tap    → Clear  |  M20 → Back";

    private Label MkHudLabel(string text, float size, FontStyle style, Color color)
    {
        return new Label {
            Text = text, Dock = DockStyle.Top, Height = 30,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color, BackColor = Color.Transparent,
            AutoEllipsis = true, Padding = new Padding(8, 4, 0, 0) };
    }

    private static Label MkFieldLabel()
    {
        return new Label {
            Text = "—", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9),
            ForeColor = TXT, BackColor = Color.FromArgb(28, 38, 65),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(4, 4, 8, 4),
            AutoEllipsis = true,
            BorderStyle = BorderStyle.FixedSingle };
    }

    // ═════════════════════════════════════════════════════════════════════
    //  USERS CRUD
    // ═════════════════════════════════════════════════════════════════════
    private void ReloadUsers(string search = "", string role = null, string activeFilter = null)
    {
        try
        {
            _uItems = _userSvc.LoadAll();
            var view = _uItems.ToList();

            if (!string.IsNullOrEmpty(search))
                view = view.Where(u =>
                    (u.Name  ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (u.UserId ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (u.BluetoothId ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                ).ToList();

            if (role != null && role != "All Roles")
                view = view.Where(u => string.Equals(u.Role, role, StringComparison.OrdinalIgnoreCase)).ToList();

            if (activeFilter == "Active")   view = view.Where(u => u.IsActive).ToList();
            if (activeFilter == "Inactive") view = view.Where(u => !u.IsActive).ToList();

            _uGrid.SelectionChanged -= OnURowSelected;
            _uGrid.Columns.Clear();
            _uGrid.Rows.Clear();

            foreach (var (n, h, fw) in new[] {
                ("uId","User ID",90), ("uName","Name",120), ("uAge","Age",40),
                ("uGender","Gender",65), ("uLevel","Level",90), ("uBt","Bluetooth ID",130),
                ("uFace","Face ID",80), ("uRole","Role",65), ("uActive","Active",50) })
                _uGrid.Columns.Add(new DataGridViewTextBoxColumn {
                    Name = n, HeaderText = h, FillWeight = fw, MinimumWidth = 36,
                    SortMode = DataGridViewColumnSortMode.Automatic });

            foreach (var u in view)
            {
                int idx = _uGrid.Rows.Add(
                    u.UserId, u.Name, u.Age, u.Gender, u.Level,
                    u.BluetoothId, u.FaceId, u.Role,
                    u.IsActive ? "Yes" : "No");

                if (!u.IsActive)
                    _uGrid.Rows[idx].DefaultCellStyle.ForeColor = Color.FromArgb(200, 80, 80);
                else if (string.Equals(u.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                    _uGrid.Rows[idx].DefaultCellStyle.ForeColor = Color.FromArgb(100, 200, 255);
            }

            _uGrid.SelectionChanged += OnURowSelected;
            if (_uGrid.Rows.Count > 0) _uGrid.Rows[0].Selected = true;
        }
        catch (Exception ex) { LogErr(ex); ShowErr("Users load error", ex.Message); }
    }

    private void OnURowSelected(object sender, EventArgs e)
    {
        try {
            if (_uGrid.SelectedRows.Count == 0) return;
            string id = _uGrid.SelectedRows[0].Cells["uId"].Value?.ToString() ?? "";
            var u = _uItems.FirstOrDefault(x => x.UserId == id);
            if (u != null) FillUserDisplay(u);
        } catch { }
    }

    private void FillUserDisplay(UserData u)
    {
        _uId.Text     = u.UserId      ?? "—";
        _uName.Text   = u.Name        ?? "—";
        _uAge.Text    = u.Age.ToString();
        _uGender.Text = u.Gender      ?? "—";
        _uLevel.Text  = u.Level       ?? "—";
        _uBt.Text     = u.BluetoothId ?? "—";
        _uFace.Text   = u.FaceId      ?? "—";
        _uRole.Text   = u.Role        ?? "—";
        _uActive.Text = u.IsActive ? "true" : "false";

        // Sync dropdown indices for fields that have options
        SyncFieldOptionIdx(3, u.Gender      ?? "Male");
        SyncFieldOptionIdx(4, u.Level       ?? "Beginner");
        SyncFieldOptionIdx(7, u.Role        ?? "Player");
        SyncFieldOptionIdx(8, u.IsActive ? "true" : "false");

        UpdateHud();
    }

    private void SyncFieldOptionIdx(int fieldIdx, string value)
    {
        var opts = FIELD_OPTIONS[fieldIdx];
        if (opts == null) return;
        for (int i = 0; i < opts.Length; i++)
            if (string.Equals(opts[i], value, StringComparison.OrdinalIgnoreCase))
            { _fieldOptionIdx[fieldIdx] = i; return; }
        _fieldOptionIdx[fieldIdx] = 0;
    }

    // Returns the UserData currently shown in the display, with edits applied
    private UserData UserFromDisplay()
    {
        string id = _uId.Text.Trim();
        if (string.IsNullOrWhiteSpace(id) || id == "—")
            id = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var existing = _uItems.FirstOrDefault(x => x.UserId == id);
        int.TryParse(_uAge.Text, out int age);
        bool.TryParse(_uActive.Text, out bool active);
        return new UserData {
            UserId      = id,
            Name        = _uName.Text == "—" ? "" : _uName.Text,
            Age         = age == 0 ? 18 : age,
            Gender      = _uGender.Text == "—" ? "Male" : _uGender.Text,
            Level       = _uLevel.Text  == "—" ? "Beginner" : _uLevel.Text,
            BluetoothId = _uBt.Text     == "—" ? "" : _uBt.Text,
            FaceId      = _uFace.Text   == "—" ? "" : _uFace.Text,
            Role        = _uRole.Text   == "—" ? "Player" : _uRole.Text,
            IsActive    = active,
            GazeProfile = existing?.GazeProfile ?? new GazeProfile()
        };
    }

    private void ClearUserDisplay()
    {
        _uId.Text = _uName.Text = _uAge.Text = _uGender.Text = _uLevel.Text =
        _uBt.Text = _uFace.Text = _uRole.Text = _uActive.Text = "—";
        _textEditMode = false; _textEditBuffer = ""; _charWheelIdx = 0;
        UpdateHud();
    }

    // ── TUIO field editing ────────────────────────────────────────────────

    private Label GetFieldLabel(int idx)
    {
        switch (idx) {
            case 0: return _uId;   case 1: return _uName;
            case 2: return _uAge;  case 3: return _uGender;
            case 4: return _uLevel;case 5: return _uBt;
            case 6: return _uFace; case 7: return _uRole;
            case 8: return _uActive;
        }
        return null;
    }

    // Highlight the currently selected field label
    private void HighlightSelectedField()
    {
        for (int i = 0; i < FIELD_NAMES.Length; i++) {
            var lbl = GetFieldLabel(i);
            if (lbl == null) continue;
            lbl.BackColor = (i == _selectedFieldIdx)
                ? (_textEditMode ? Color.FromArgb(50, 30, 10) : Color.FromArgb(30, 50, 80))
                : FIELD_BG;
            lbl.ForeColor = (i == _selectedFieldIdx)
                ? (_textEditMode ? EDIT_HL : EDIT_ACT)
                : TXT;
        }
    }

    // Apply a value change to the selected field (dropdown/numeric)
    private void ApplyFieldValueChange(int direction)
    {
        int fi = _selectedFieldIdx;
        var opts = FIELD_OPTIONS[fi];

        if (opts != null) {
            // Dropdown field
            _fieldOptionIdx[fi] = (_fieldOptionIdx[fi] + direction + opts.Length) % opts.Length;
            GetFieldLabel(fi).Text = opts[_fieldOptionIdx[fi]];
        }
        else if (fi == 2) {
            // Age: numeric
            int.TryParse(_uAge.Text, out int age);
            age = Math.Max(1, Math.Min(120, age + direction));
            _uAge.Text = age.ToString();
        }
        else {
            // Text field: enter text edit mode
            if (!_textEditMode) {
                _textEditMode = true;
                string cur = GetFieldLabel(fi).Text;
                _textEditBuffer = (cur == "—") ? "" : cur;
                _charWheelIdx = 0;
            }
            // Scroll char wheel
            _charWheelIdx = (_charWheelIdx + direction + CHAR_WHEEL.Length) % CHAR_WHEEL.Length;
        }
        UpdateHud();
    }

    // Confirm current char (M40) — only for text fields
    private void ConfirmChar()
    {
        if (!_textEditMode) return;
        _textEditBuffer += CHAR_WHEEL[_charWheelIdx];
        GetFieldLabel(_selectedFieldIdx).Text = _textEditBuffer + "█";
        UpdateHud();
    }

    // Delete last char (M41)
    private void DeleteChar()
    {
        if (!_textEditMode) return;
        if (_textEditBuffer.Length > 0)
            _textEditBuffer = _textEditBuffer.Substring(0, _textEditBuffer.Length - 1);
        GetFieldLabel(_selectedFieldIdx).Text =
            (_textEditBuffer.Length > 0 ? _textEditBuffer : "—") + (_textEditMode ? "█" : "");
        UpdateHud();
    }

    // Finish editing field (M42)
    private void FinishField()
    {
        if (_textEditMode) {
            GetFieldLabel(_selectedFieldIdx).Text =
                _textEditBuffer.Length > 0 ? _textEditBuffer : "—";
            _textEditMode = false;
            _textEditBuffer = "";
        }
        UpdateHud();
    }

    private void UpdateHud()
    {
        if (_hudUser == null || _editorCard == null) return;

        // ── top info ──────────────────────────────────────────────────────
        string userName = "—";
        if (_uGrid?.SelectedRows.Count > 0)
            userName = _uGrid.SelectedRows[0].Cells["uName"].Value?.ToString() ?? "—";
        _hudUser.Text  = $"Selected User:  {userName}";
        _hudField.Text = $"Selected Field:  {FIELD_NAMES[_selectedFieldIdx]}";

        bool isTextField = FIELD_OPTIONS[_selectedFieldIdx] == null && _selectedFieldIdx != 2;

        // Current text always shows the real field value (strip cursor marker)
        string fieldVal = GetFieldLabel(_selectedFieldIdx)?.Text?.Replace("█", "") ?? "—";

        if (isTextField) {
            _edModeLabel.Text      = _textEditMode ? "Editing Mode:  Text Editing" : "Editing Mode:  Text Field";
            _edModeLabel.ForeColor = _textEditMode ? EDIT_HL : TXT_DIM;

            // Current text: show buffer while editing, else real value
            _edCurrentText.Text    = _textEditMode
                ? (_textEditBuffer.Length > 0 ? _textEditBuffer : "(empty)")
                : (fieldVal == "—" || string.IsNullOrEmpty(fieldVal) ? "(empty)" : fieldVal);
            _edCurrentText.Visible = true;

            // Selected character: only meaningful while editing
            _edSelectedChar.Text    = _textEditMode
                ? CharDisplay(CHAR_WHEEL[_charWheelIdx])
                : "—  (rotate M39 to start)";
            _edSelectedChar.Font    = new Font("Segoe UI", 11, FontStyle.Bold);
            _edSelectedChar.Visible = true;

            _edCharRow.Visible = _textEditMode;
            if (_textEditMode) BuildCharWheelTiles();
        }
        else {
            _edModeLabel.Text      = "Editing Mode:  Value Select";
            _edModeLabel.ForeColor = EDIT_ACT;

            _edCurrentText.Text    = fieldVal;
            _edCurrentText.Visible = true;

            _edSelectedChar.Text    = "Rotate M39 to change";
            _edSelectedChar.Font    = new Font("Segoe UI", 9, FontStyle.Italic);
            _edSelectedChar.Visible = true;
            _edCharRow.Visible      = false;
        }

        HighlightSelectedField();
        _editorCard.Invalidate();
    }

    private static string CharDisplay(char c)
    {
        if (c == ' ')  return "SPACE";
        if (c == '_')  return "_";
        if (c == ':')  return ":";
        if (c == '-')  return "-";
        return c.ToString();
    }

    private void BuildCharWheelTiles()
    {
        _edCharRow.Controls.Clear();
        int len   = CHAR_WHEEL.Length;
        int total = 9;   // show 9 chars: 4 before, current, 4 after
        int half  = total / 2;
        int tileW = Math.Max(28, (_edCharRow.Width - 4) / total);
        int tileH = _edCharRow.Height - 4;

        for (int i = -half; i <= half; i++) {
            int idx = (_charWheelIdx + i + len) % len;
            char c  = CHAR_WHEEL[idx];
            bool isCurrent = (i == 0);

            var tile = new Label {
                Text      = CharDisplay(c),
                Size      = new Size(tileW, tileH),
                Location  = new Point(2 + (i + half) * tileW, 2),
                Font      = new Font("Segoe UI", isCurrent ? 11 : 8,
                                     isCurrent ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = isCurrent ? Color.Black : Color.FromArgb(160, 180, 220),
                BackColor = isCurrent ? EDIT_HL : Color.FromArgb(22, 32, 58),
                TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle };
            _edCharRow.Controls.Add(tile);
        }
    }

    // ── User CRUD actions ─────────────────────────────────────────────────
    private void DoUserAdd()
    {
        try {
            var u = UserFromDisplay();
            u.UserId = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            if (string.IsNullOrWhiteSpace(u.Name) || u.Name == "—")
            { ShowErr("Validation", "Name is required."); return; }
            _userSvc.AddUser(u); ReloadUsers();
            MessageBox.Show($"User '{u.Name}' added.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) { LogErr(ex); ShowErr("Error", ex.Message); }
    }

    private void DoUserSave()
    {
        try {
            var u = UserFromDisplay();
            if (string.IsNullOrWhiteSpace(u.Name) || u.Name == "—")
            { ShowErr("Validation", "Name is required."); return; }
            _userSvc.UpdateUser(u); ReloadUsers();
            MessageBox.Show($"User '{u.Name}' saved.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) { LogErr(ex); ShowErr("Error", ex.Message); }
    }

    private void DoUserDeact()
    {
        try {
            string id = _uId.Text.Trim();
            if (string.IsNullOrWhiteSpace(id) || id == "—")
            { ShowErr("Validation", "Select a user first."); return; }
            _userSvc.DeactivateUser(id); ReloadUsers();
            MessageBox.Show($"User '{id}' deactivated.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) { LogErr(ex); ShowErr("Error", ex.Message); }
    }

    private void DoUserDel()
    {
        try {
            string id = _uId.Text.Trim();
            if (string.IsNullOrWhiteSpace(id) || id == "—")
            { ShowErr("Validation", "Select a user first."); return; }
            var u = _uItems.FirstOrDefault(x => x.UserId == id);
            if (MessageBox.Show($"Delete '{u?.Name ?? id}'?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _userSvc.DeleteUser(id); ReloadUsers(); ClearUserDisplay();
        } catch (Exception ex) { LogErr(ex); ShowErr("Error", ex.Message); }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  CONTENT TAB  (unchanged)
    // ═════════════════════════════════════════════════════════════════════
    private void BuildContentTab(TabPage tab)
    {
        var outer = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            BackColor = PANEL };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        tab.Controls.Add(outer);

        var filterBar = new Panel { Dock = DockStyle.Fill, BackColor = HEADER,
            Padding = new Padding(10, 7, 10, 7) };
        outer.Controls.Add(filterBar, 0, 0);

        var fSearch = MkTxt(160); fSearch.ForeColor = TXT_DIM; fSearch.Text = "Search...";
        fSearch.GotFocus  += (s, e) => { if (fSearch.Text == "Search...") { fSearch.Text = ""; fSearch.ForeColor = TXT; } };
        fSearch.LostFocus += (s, e) => { if (fSearch.Text == "") { fSearch.Text = "Search..."; fSearch.ForeColor = TXT_DIM; } };
        var fLevel  = MkCbo(new[] { "All Levels","Beginner","Intermediate","Advanced" }, 130);
        var fModule = MkCbo(new[] { "All Modules","Padel Shots","Padel Rules","Practice",
            "Quick Challenge","Speed Mode","Competition","AI Vision Coach" }, 155);
        var fActive = new CheckBox { Text = "Active only", Checked = true,
            Font = new Font("Segoe UI", 9), ForeColor = TXT,
            AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(4, 7, 8, 0) };
        var bRef = MkBtn("Refresh", ACCENT, 80, 28);
        bRef.Click += (s, e) => ReloadContent(
            fSearch.Text == "Search..." ? "" : fSearch.Text,
            fLevel.SelectedItem?.ToString(), fModule.SelectedItem?.ToString(), fActive.Checked);

        var ff = new FlowLayoutPanel { Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            BackColor = Color.Transparent };
        void AF(Control c, int g = 8) { c.Margin = new Padding(0, 0, g, 0); ff.Controls.Add(c); }
        AF(new Label { Text = "Training Content", Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = ACCENT, AutoSize = true, BackColor = Color.Transparent,
            Margin = new Padding(0, 5, 20, 0) });
        AF(fSearch); AF(fLevel); AF(fModule); ff.Controls.Add(fActive); AF(bRef, 0);
        filterBar.Controls.Add(ff);

        var gridWrap = new Panel { Dock = DockStyle.Fill, BackColor = PANEL,
            Padding = new Padding(12, 8, 12, 4) };
        outer.Controls.Add(gridWrap, 0, 1);
        _cGrid = MkGrid();
        _cGrid.Dock = DockStyle.Fill;
        _cGrid.SelectionChanged += OnCRowSelected;
        gridWrap.Controls.Add(_cGrid);

        var formWrap = new Panel { Dock = DockStyle.Fill, BackColor = PANEL2,
            Padding = new Padding(12, 8, 12, 8), AutoScroll = true };
        outer.Controls.Add(formWrap, 0, 2);

        formWrap.Controls.Add(new Label {
            Text = "Add / Edit Training Content", Dock = DockStyle.Top, Height = 26,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = ACCENT, BackColor = Color.Transparent });

        _cId = MkTxt(0); _cTitle = MkTxt(0);
        _cLevel  = MkCbo(new[] { "Beginner","Intermediate","Advanced" }, 0);
        _cMarker = MkCbo(new[] { "3","4","5","6","7","8" }, 0);
        _cModule = MkCbo(new[] { "Padel Shots","Padel Rules","Practice",
            "Quick Challenge","Speed Mode","Competition","AI Vision Coach" }, 0);
        _cActivity = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown,
            Font = new Font("Segoe UI", 9), Dock = DockStyle.Fill };
        _cActivity.Items.AddRange(new object[] { "Shot Training","Rules Training",
            "Court Positioning","Net Control","Smash Position","Forehand Position",
            "Backhand Position","Volley","Wall Practice","Serve Practice",
            "Reaction Training","Competition Movement" });
        _cDesc = MkTxt(0, true); _cTip = MkTxt(0, true);
        _cZone = MkTxt(0);
        _cDiff = MkCbo(new[] { "Beginner","Intermediate","Advanced" }, 0);
        _cActive = new CheckBox { Text = "Active", Checked = true,
            Font = new Font("Segoe UI", 9), ForeColor = TXT,
            AutoSize = true, BackColor = Color.Transparent };

        var cGrid = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 4,
            BackColor = Color.Transparent, Padding = new Padding(0, 4, 0, 4) };
        for (int i = 0; i < 6; i++)
            cGrid.ColumnStyles.Add(new ColumnStyle(
                i % 2 == 0 ? SizeType.Absolute : SizeType.Percent,
                i % 2 == 0 ? 90 : 33.3f));
        cGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        cGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        cGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        cGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        void ACF(string lbl, Control ctrl, int col, int row) {
            cGrid.Controls.Add(new Label {
                Text = lbl, Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = TXT_DIM, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0, 0, 6, 0), BackColor = Color.Transparent }, col, row);
            ctrl.Dock = DockStyle.Fill; ctrl.Margin = new Padding(0, 3, 12, 3);
            if (ctrl is TextBox tb) { tb.BackColor = FIELD_BG; tb.ForeColor = TXT; tb.BorderStyle = BorderStyle.FixedSingle; }
            cGrid.Controls.Add(ctrl, col + 1, row);
        }

        ACF("ID",       _cId,       0, 0); ACF("Title",    _cTitle,    2, 0); ACF("Level",  _cLevel,  4, 0);
        ACF("Marker",   _cMarker,   0, 1); ACF("Module",   _cModule,   2, 1); ACF("Activity",_cActivity,4,1);
        ACF("Desc",     _cDesc,     0, 2); ACF("Coach Tip", _cTip,     2, 2);
        ACF("Zone",     _cZone,     4, 2);
        ACF("Difficulty",_cDiff,    0, 3);
        _cActive.Margin = new Padding(4, 8, 0, 0);
        cGrid.Controls.Add(_cActive, 3, 3);
        formWrap.Controls.Add(cGrid);

        _cBtnAdd   = MkBtn("➕ Add",        Color.FromArgb(26, 130, 60),  110, 32);
        _cBtnSave  = MkBtn("💾 Save",       ACCENT,                       110, 32);
        _cBtnDeact = MkBtn("⏸ Deactivate", Color.FromArgb(170, 100, 14), 120, 32);
        _cBtnDel   = MkBtn("🗑 Delete",      Color.FromArgb(175, 35, 35),  100, 32);
        _cBtnClr   = MkBtn("✖ Clear",        Color.FromArgb(60, 68, 100),  90, 32);
        _cBtnAdd.Click += OnContentAdd; _cBtnSave.Click += OnContentSave;
        _cBtnDeact.Click += OnContentDeact; _cBtnDel.Click += OnContentDel;
        _cBtnClr.Click += (s, e) => ClearContentForm();

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 0) };
        foreach (var b in new[] { _cBtnAdd, _cBtnSave, _cBtnDeact, _cBtnDel, _cBtnClr }) {
            b.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            b.Margin = new Padding(0, 0, 8, 0);
            btnRow.Controls.Add(b);
        }
        formWrap.Controls.Add(btnRow);
    }

    private void ReloadContent(string search = "", string level = null,
                               string module = null, bool activeOnly = true)
    {
        try
        {
            var all = _contentSvc.LoadAll();
            if (!string.IsNullOrEmpty(search))
                all = all.Where(i =>
                    i.Title.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    i.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (level != null && level != "All Levels")
                all = all.Where(i => string.Equals(i.Level, level, StringComparison.OrdinalIgnoreCase)).ToList();
            if (module != null && module != "All Modules")
                all = all.Where(i => string.Equals(i.Module, module, StringComparison.OrdinalIgnoreCase)).ToList();
            if (activeOnly) all = all.Where(i => i.IsActive).ToList();
            _cItems = all;

            _cGrid.SelectionChanged -= OnCRowSelected;
            _cGrid.Columns.Clear(); _cGrid.Rows.Clear();

            foreach (var (n, h, fw) in new[] {
                ("cId","ID",55),("cTitle","Title",150),("cLevel","Level",80),
                ("cMarker","Marker",52),("cModule","Module",110),("cActivity","Activity",110),
                ("cDiff","Difficulty",72),("cZone","Zone",80),("cActive","Active",46) })
                _cGrid.Columns.Add(new DataGridViewTextBoxColumn {
                    Name = n, HeaderText = h, FillWeight = fw, MinimumWidth = 36,
                    SortMode = DataGridViewColumnSortMode.Automatic });

            foreach (var item in _cItems) {
                int idx = _cGrid.Rows.Add(item.Id, item.Title, item.Level, item.MarkerId,
                    item.Module, item.Activity, item.Difficulty, item.TargetZone,
                    item.IsActive ? "Yes" : "No");
                if (!item.IsActive) {
                    _cGrid.Rows[idx].DefaultCellStyle.ForeColor = Color.FromArgb(200, 80, 80);
                    _cGrid.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(40, 22, 22);
                }
            }
            _cGrid.SelectionChanged += OnCRowSelected;
            if (_cGrid.Rows.Count > 0) _cGrid.Rows[0].Selected = true;
        }
        catch (Exception ex) { LogErr(ex); ShowErr("Content load error", ex.Message); }
    }

    private void OnCRowSelected(object sender, EventArgs e)
    {
        try {
            if (_cGrid.SelectedRows.Count == 0) return;
            string id = _cGrid.SelectedRows[0].Cells["cId"].Value?.ToString() ?? "";
            var item = _cItems.FirstOrDefault(i => i.Id == id);
            if (item != null) FillContentForm(item);
        } catch { }
    }

    private void FillContentForm(PadelContentItem i)
    {
        _cId.Text = i.Id; _cTitle.Text = i.Title;
        SetCbo(_cLevel, i.Level); SetCbo(_cMarker, i.MarkerId.ToString());
        SetCbo(_cModule, i.Module); _cActivity.Text = i.Activity;
        _cDesc.Text = i.Description; _cTip.Text = i.CoachTip;
        _cZone.Text = i.TargetZone; SetCbo(_cDiff, i.Difficulty);
        _cActive.Checked = i.IsActive;
    }

    private PadelContentItem ContentFromForm()
    {
        string id = _cId.Text.Trim();
        if (string.IsNullOrWhiteSpace(id)) {
            string pfx = (_cLevel.SelectedItem?.ToString() ?? "itm").Substring(0, 3).ToLower();
            id = pfx + "_" + (DateTime.Now.Ticks % 1_000_000_000L);
        }
        int.TryParse(_cMarker.SelectedItem?.ToString(), out int mid);
        return new PadelContentItem {
            Id = id, Level = _cLevel.SelectedItem?.ToString() ?? "",
            MarkerId = mid, Module = _cModule.SelectedItem?.ToString() ?? "",
            Activity = _cActivity.Text.Trim(), Title = _cTitle.Text.Trim(),
            Description = _cDesc.Text.Trim(), CoachTip = _cTip.Text.Trim(),
            TargetZone = _cZone.Text.Trim(), Difficulty = _cDiff.SelectedItem?.ToString() ?? "",
            IsActive = _cActive.Checked };
    }

    private void ClearContentForm()
    {
        _cId.Text = _cTitle.Text = _cDesc.Text = _cTip.Text = _cZone.Text = _cActivity.Text = "";
        _cActive.Checked = true;
        if (_cLevel.Items.Count  > 0) _cLevel.SelectedIndex  = 0;
        if (_cMarker.Items.Count > 0) _cMarker.SelectedIndex = 0;
        if (_cModule.Items.Count > 0) _cModule.SelectedIndex = 0;
        if (_cDiff.Items.Count   > 0) _cDiff.SelectedIndex   = 0;
    }

    private void OnContentAdd(object sender, EventArgs e)
    {
        try {
            _cId.Text = "";
            var item = ContentFromForm();
            if (string.IsNullOrWhiteSpace(item.Title)) { ShowErr("Validation", "Title is required."); return; }
            _contentSvc.AddItem(item); ReloadContent();
            MessageBox.Show($"'{item.Title}' added.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) { LogErr(ex); ShowErr("Error", ex.Message); }
    }
    private void OnContentSave(object sender, EventArgs e)
    {
        try {
            var item = ContentFromForm();
            if (string.IsNullOrWhiteSpace(item.Title)) { ShowErr("Validation", "Title is required."); return; }
            _contentSvc.UpdateItem(item); ReloadContent();
            MessageBox.Show($"'{item.Title}' saved.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) { LogErr(ex); ShowErr("Error", ex.Message); }
    }
    private void OnContentDeact(object sender, EventArgs e)
    {
        try {
            string id = _cId.Text.Trim();
            if (string.IsNullOrWhiteSpace(id)) { ShowErr("Validation", "Select a row first."); return; }
            _contentSvc.DeactivateItem(id); ReloadContent();
            MessageBox.Show($"'{id}' deactivated.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) { LogErr(ex); ShowErr("Error", ex.Message); }
    }
    private void OnContentDel(object sender, EventArgs e)
    {
        try {
            string id = _cId.Text.Trim();
            if (string.IsNullOrWhiteSpace(id)) { ShowErr("Validation", "Select a row first."); return; }
            if (MessageBox.Show($"Delete '{id}'?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _contentSvc.DeleteItem(id); ReloadContent(); ClearContentForm();
        } catch (Exception ex) { LogErr(ex); ShowErr("Error", ex.Message); }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  TUIO MARKER HANDLING
    // ═════════════════════════════════════════════════════════════════════
    private void OnGesture(int id) {
        if (!Visible || IsDisposed) return;
        if (!GestureRouter.HasFocus(this)) return;
        BeginInvoke((MethodInvoker)(() => DispatchMarker(id)));
    }

    public void addTuioObject(TuioObject o)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (!GestureRouter.HasFocus(this)) return;
        BeginInvoke((MethodInvoker)(() => DispatchMarker(o.SymbolID)));
    }

    public void updateTuioObject(TuioObject o)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (!GestureRouter.HasFocus(this)) return;
        int id = o.SymbolID;
        float angle = o.Angle;

        if (id == 36)
            BeginInvoke((MethodInvoker)(() => HandleRotation(ref _m36Angle, ref _m36Time, angle, MoveUserSelection)));
        else if (id == 38)
            BeginInvoke((MethodInvoker)(() => HandleRotation(ref _m38Angle, ref _m38Time, angle, MoveFieldSelection)));
        else if (id == 39)
            BeginInvoke((MethodInvoker)(() => HandleRotation(ref _m39Angle, ref _m39Time, angle, ApplyFieldValueChange)));
    }

    private void HandleRotation(ref float lastAngle, ref DateTime lastTime,
                                 float angle, Action<int> onStep)
    {
        if ((DateTime.Now - lastTime).TotalMilliseconds < 300) return;

        if (float.IsNaN(lastAngle)) { lastAngle = angle; return; }

        float delta = angle - lastAngle;
        if (delta >  (float)Math.PI) delta -= 2f * (float)Math.PI;
        if (delta < -(float)Math.PI) delta += 2f * (float)Math.PI;

        if (Math.Abs(delta) < ROT_THRESHOLD) return;

        int dir = delta > 0 ? 1 : -1;
        lastAngle = angle;
        lastTime  = DateTime.Now;
        onStep(dir);
    }

    private void MoveUserSelection(int direction)
    {
        if (_uGrid == null || _uGrid.Rows.Count == 0) return;
        int current = _uGrid.SelectedRows.Count > 0 ? _uGrid.SelectedRows[0].Index : -1;
        int next = Math.Max(0, Math.Min(_uGrid.Rows.Count - 1, current + direction));
        if (next == current) return;
        _uGrid.ClearSelection();
        _uGrid.Rows[next].Selected = true;
        _uGrid.FirstDisplayedScrollingRowIndex = next;
    }

    private void MoveFieldSelection(int direction)
    {
        _selectedFieldIdx = (_selectedFieldIdx + direction + FIELD_NAMES.Length) % FIELD_NAMES.Length;
        // Exit text edit mode when switching fields
        if (_textEditMode) FinishField();
        UpdateHud();
    }

    private void DispatchMarker(int id)
    {
        // Rotation markers handled in updateTuioObject — reset on add
        if (id == 36) { _m36Angle = float.NaN; return; }
        if (id == 38) { _m38Angle = float.NaN; return; }
        if (id == 39) { _m39Angle = float.NaN; return; }

        if (id == 20) { Close(); return; }

        // Debounce tap markers
        bool sameCd = id == _lastAction &&
            (DateTime.Now - _lastActTime).TotalMilliseconds < ACTION_CD_MS;
        if (sameCd) return;
        _lastAction  = id;
        _lastActTime = DateTime.Now;

        switch (id)
        {
            case 31: DoUserAdd();    break;
            case 32: DoUserSave();   break;
            case 33: DoUserDeact();  break;
            case 34: DoUserDel();    break;
            case 35: ClearUserDisplay(); break;
            case 37: ShowGazeHistory();  break;
            // Text editing markers
            case 40: ConfirmChar();  break;
            case 41: DeleteChar();   break;
            case 42: FinishField();  break;
        }
    }

    public void removeTuioObject(TuioObject o)
    {
        if (o.SymbolID == 36) _m36Angle = float.NaN;
        if (o.SymbolID == 38) _m38Angle = float.NaN;
        if (o.SymbolID == 39) _m39Angle = float.NaN;
    }

    public void addTuioCursor(TuioCursor c)    { }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { }
    public void addTuioBlob(TuioBlob b)        { }
    public void updateTuioBlob(TuioBlob b)     { }
    public void removeTuioBlob(TuioBlob b)     { }
    public void refresh(TuioTime t)            { }

    // ═════════════════════════════════════════════════════════════════════
    //  GAZE HISTORY POPUP
    // ═════════════════════════════════════════════════════════════════════
    private void ShowGazeHistory()
    {
        if (_uGrid == null || _uGrid.SelectedRows.Count == 0) {
            MessageBox.Show("Select a user first (rotate Marker 36).",
                "Gaze History", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        int idx = _uGrid.SelectedRows[0].Index;
        if (idx < 0 || idx >= _uItems.Count) return;
        var user = _uItems[idx];

        var sessions = GazeReportService.GetRecent(user.UserId, 5);
        var trends   = GazeReportService.GetTrends(user.UserId, 5);

        var popup = new Form {
            Text = $"Gaze History — {user.Name}",
            Size = new Size(620, 500),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(14, 22, 44),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false };

        var scrollPanel = new Panel {
            Dock = DockStyle.Fill, AutoScroll = true,
            BackColor = Color.Transparent, Padding = new Padding(16, 12, 16, 12) };
        popup.Controls.Add(scrollPanel);

        int y = 0;
        void AddLbl(string text, float size, FontStyle style, Color color, int height) {
            scrollPanel.Controls.Add(new Label {
                Text = text, Font = new Font("Segoe UI", size, style),
                ForeColor = color, AutoSize = false,
                Size = new Size(560, height), Location = new Point(0, y),
                BackColor = Color.Transparent });
            y += height + 2;
        }

        AddLbl($"📈  Gaze Tracking History for {user.Name}", 14, FontStyle.Bold, Color.White, 32);

        var gp = user.GazeProfile ?? new GazeProfile();
        AddLbl($"Current Profile:  Strokes={gp.Strokes_Score}  Rules={gp.Rules_Score}  " +
               $"Practice={gp.Practice_Score}  Quiz={gp.Quiz_Score}  " +
               $"Spelling={gp.Spelling_Score}  Competition={gp.Competition_Score}",
               9, FontStyle.Italic, Color.FromArgb(140, 175, 225), 22);

        string trendLine = "Trends: ";
        foreach (var kvp in trends) {
            string arrow = kvp.Value > 0 ? "↑" : kvp.Value < 0 ? "↓" : "→";
            trendLine += $"{kvp.Key}{arrow}  ";
        }
        AddLbl(trendLine, 9, FontStyle.Bold, Color.FromArgb(60, 210, 160), 20);

        scrollPanel.Controls.Add(new Panel {
            Size = new Size(560, 1), Location = new Point(0, y),
            BackColor = Color.FromArgb(45, 65, 100) });
        y += 8;

        if (sessions.Count == 0) {
            AddLbl("No gaze sessions recorded yet for this user.", 11, FontStyle.Regular,
                Color.FromArgb(160, 180, 210), 30);
        } else {
            for (int i = sessions.Count - 1; i >= 0; i--) {
                var s = sessions[i];
                string when = s.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                string dur  = TimeSpan.FromSeconds(s.DurationSeconds).ToString(@"mm\:ss");
                AddLbl($"Session {i+1}  •  {when}  •  {dur}  •  {s.TotalFixations} fixations",
                    10, FontStyle.Bold, Color.White, 24);
                string info = $"Dominant: {s.DominantCategory}";
                if (s.NeglectedCategories?.Count > 0)
                    info += $"  |  Neglected: {string.Join(", ", s.NeglectedCategories)}";
                AddLbl(info, 9, FontStyle.Regular, Color.FromArgb(130, 165, 215), 20);
                if (s.SessionScores != null) {
                    string scores = "Scores: ";
                    foreach (var sc in s.SessionScores) scores += $"{sc.Key}={sc.Value}  ";
                    AddLbl(scores, 8, FontStyle.Regular, Color.FromArgb(100, 140, 195), 18);
                }
                scrollPanel.Controls.Add(new Panel {
                    Size = new Size(540, 1), Location = new Point(10, y + 4),
                    BackColor = Color.FromArgb(35, 55, 85) });
                y += 14;
            }
        }
        popup.ShowDialog(this);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═════════════════════════════════════════════════════════════════════
    private static DataGridView MkGrid()
    {
        var g = new DataGridView {
            ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.FromArgb(18, 26, 48),
            GridColor = Color.FromArgb(36, 50, 84),
            BorderStyle = BorderStyle.None, RowHeadersVisible = false,
            Font = new Font("Segoe UI", 9), ColumnHeadersHeight = 30,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal };
        g.RowTemplate.Height = 26;
        g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(12, 18, 40);
        g.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(130, 175, 255);
        g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        g.EnableHeadersVisualStyles = false;
        g.DefaultCellStyle.BackColor = Color.FromArgb(24, 34, 58);
        g.DefaultCellStyle.ForeColor = Color.FromArgb(210, 220, 242);
        g.DefaultCellStyle.SelectionBackColor = SEL_BG;
        g.DefaultCellStyle.SelectionForeColor = Color.White;
        g.AlternatingRowsDefaultCellStyle.BackColor = ROW_ALT;
        return g;
    }

    private static Button MkBtn(string text, Color back, int w, int h)
    {
        var b = new Button { Text = text, BackColor = back, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Size = new Size(w, h) };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    private static TextBox MkTxt(int w, bool multi = false)
    {
        var t = new TextBox { Font = new Font("Segoe UI", 9), Multiline = multi,
            BackColor = FIELD_BG, ForeColor = TXT, BorderStyle = BorderStyle.FixedSingle };
        if (w > 0) t.Width = w;
        if (multi) t.ScrollBars = ScrollBars.Vertical;
        return t;
    }

    private static ComboBox MkCbo(string[] items, int w)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9) };
        if (w > 0) c.Width = w;
        c.Items.AddRange(items);
        if (c.Items.Count > 0) c.SelectedIndex = 0;
        return c;
    }

    private static void SetCbo(ComboBox c, string val)
    {
        for (int i = 0; i < c.Items.Count; i++)
            if (string.Equals(c.Items[i].ToString(), val, StringComparison.OrdinalIgnoreCase))
            { c.SelectedIndex = i; return; }
        if (c.Items.Count > 0) c.SelectedIndex = 0;
    }

    private static void ShowErr(string title, string msg) =>
        MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private static void LogErr(Exception ex)
    {
        try {
            string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "content_manager_error_log.txt");
            File.AppendAllText(log,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n{new string('-',60)}\n");
        } catch { }
    }
}
