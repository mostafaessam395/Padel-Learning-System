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
/// Users tab is the primary focus: full grid + inline edit form.
/// </summary>
public class ContentManagerPage : Form, TuioListener
{
    // ── services ──────────────────────────────────────────────────────────
    private readonly TuioClient     _tc;
    private readonly ContentService _contentSvc = new ContentService();
    private readonly UserService    _userSvc    = new UserService();

    // ── users tab state ───────────────────────────────────────────────────
    private DataGridView  _uGrid;
    private List<UserData> _uItems = new List<UserData>();
    // form fields
    private TextBox       _uId, _uName, _uBt, _uFace;
    private NumericUpDown _uAge;
    private ComboBox      _uGender, _uLevel, _uRole;
    private CheckBox      _uActive;
    // buttons
    private Button _uBtnAdd, _uBtnSave, _uBtnDel, _uBtnDeact, _uBtnClr;

    // ── TUIO marker state ─────────────────────────────────────────────────
    // Marker 36 rotation → user selection
    private float  _marker36LastAngle  = float.NaN;   // last processed angle (radians)
    private DateTime _marker36LastMove = DateTime.MinValue; // debounce timestamp
    // Action marker debounce (31-35, 20)
    private int    _lastActionMarker   = -1;
    private DateTime _lastActionTime   = DateTime.MinValue;
    private const int ACTION_COOLDOWN_MS = 800;   // ms between same-marker actions
    private const float ROTATION_THRESHOLD = 0.45f; // ~26 degrees in radians

    // ── content tab state ─────────────────────────────────────────────────
    private DataGridView  _cGrid;
    private List<PadelContentItem> _cItems = new List<PadelContentItem>();
    private TextBox  _cId, _cTitle, _cDesc, _cTip, _cZone;
    private ComboBox _cLevel, _cMarker, _cModule, _cActivity, _cDiff;
    private CheckBox _cActive;
    private Button   _cBtnAdd, _cBtnSave, _cBtnDeact, _cBtnDel, _cBtnClr;

    // ── colours ───────────────────────────────────────────────────────────
    static readonly Color BG        = Color.FromArgb(18, 24, 42);
    static readonly Color PANEL     = Color.FromArgb(24, 32, 56);
    static readonly Color PANEL2    = Color.FromArgb(28, 38, 64);
    static readonly Color HEADER    = Color.FromArgb(10, 14, 30);
    static readonly Color ACCENT    = Color.FromArgb(50, 115, 255);
    static readonly Color ACCENT2   = Color.FromArgb(30, 160, 100);
    static readonly Color TXT       = Color.FromArgb(215, 225, 245);
    static readonly Color TXT_DIM   = Color.FromArgb(130, 150, 195);
    static readonly Color FIELD_BG  = Color.FromArgb(32, 42, 72);
    static readonly Color ROW_ALT   = Color.FromArgb(22, 30, 52);
    static readonly Color SEL_BG    = Color.FromArgb(50, 110, 220);

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
                GestureRouter.OnGestureMarker += OnGesture;
                ReloadUsers();
                ReloadContent();
            }
            catch (Exception ex) { LogErr(ex); }
        };
        FormClosed += (s, e) =>
        {
            GestureRouter.OnGestureMarker -= OnGesture;
            if (_tc != null) _tc.removeTuioListener(this);
        };
    }

    // ═════════════════════════════════════════════════════════════════════
    //  TOP-LEVEL LAYOUT
    // ═════════════════════════════════════════════════════════════════════
    private void Build()
    {
        // ── header ────────────────────────────────────────────────────────
        var hdr = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = HEADER };
        var hLbl = new Label {
            Text = "Admin Management Panel",
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = Color.White, AutoSize = false,
            Size = new Size(480, 36), Location = new Point(18, 8),
            BackColor = Color.Transparent };
        hdr.Controls.Add(hLbl);
        Controls.Add(hdr);

        // ── tab control ───────────────────────────────────────────────────
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

        tabs.TabPages.Add(tUsers);    // Users is first / default
        tabs.TabPages.Add(tContent);

        BuildUsersTab(tUsers);
        BuildContentTab(tContent);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  USERS TAB  — grid takes ~60 % height, form takes ~40 %
    // ═════════════════════════════════════════════════════════════════════
    private void BuildUsersTab(TabPage tab)
    {
        // Outer layout: title+filter row | TUIO hint row | grid | form
        var outer = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            BackColor = PANEL };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));   // row 0: title + filters
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // row 1: grid
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));  // row 2: form
        tab.Controls.Add(outer);

        // ── row 0: title only ─────────────────────────────────────────────
        var filterBar = new Panel { Dock = DockStyle.Fill, BackColor = HEADER,
            Padding = new Padding(10, 10, 10, 8) };
        outer.Controls.Add(filterBar, 0, 0);

        filterBar.Controls.Add(new Label {
            Text = "Users Management",
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = ACCENT2, AutoSize = true, BackColor = Color.Transparent,
            Location = new Point(10, 14) });

        // ── row 1: users grid ─────────────────────────────────────────────
        var gridWrap = new Panel { Dock = DockStyle.Fill,
            BackColor = PANEL, Padding = new Padding(12, 8, 12, 4) };
        outer.Controls.Add(gridWrap, 0, 1);

        _uGrid = MkGrid();
        _uGrid.Dock = DockStyle.Fill;
        _uGrid.SelectionChanged += OnURowSelected;
        gridWrap.Controls.Add(_uGrid);

        // ── row 2: edit form ──────────────────────────────────────────────
        outer.RowStyles[2] = new RowStyle(SizeType.Absolute, 250);
        outer.RowStyles[1] = new RowStyle(SizeType.Percent, 100);

        var formWrap = new Panel { Dock = DockStyle.Fill,
            BackColor = PANEL2, Padding = new Padding(8, 4, 8, 4) };
        outer.Controls.Add(formWrap, 0, 2);

        // ── Fields ────────────────────────────────────────────────────────
        _uId     = MkTxt(0); _uName = MkTxt(0);
        _uAge    = new NumericUpDown { Minimum = 1, Maximum = 120, Value = 18,
            Font = new Font("Segoe UI", 9) };
        _uGender = MkCbo(new[] { "Male", "Female", "Other" }, 0);
        _uLevel  = MkCbo(new[] { "Beginner", "Intermediate", "Advanced",
            "Primary", "HighSchool" }, 0);
        _uBt     = MkTxt(0); _uFace = MkTxt(0);
        _uRole   = MkCbo(new[] { "Player", "Admin" }, 0);
        _uActive = new CheckBox { Text = "Active", Checked = true,
            Font = new Font("Segoe UI", 9), ForeColor = TXT,
            AutoSize = false, BackColor = Color.Transparent };

        // ── Buttons removed — TUIO marker actions only ────────────────────
        // Fields still referenced by DispatchMarker; no click handlers needed.
        _uBtnAdd = _uBtnSave = _uBtnDeact = _uBtnDel = _uBtnClr = null;

        // ── Build a single TableLayoutPanel: 7 rows × 3 cols ─────────────
        // row 0 = title (spans 3 cols)
        // row 1 = labels:  User ID | Name | Age
        // row 2 = inputs:  _uId    | _uName | _uAge
        // row 3 = labels:  Gender  | Level  | Role
        // row 4 = inputs:  _uGender| _uLevel| _uRole
        // row 5 = labels:  BT ID   | Face ID| Active
        // row 6 = inputs:  _uBt    | _uFace | _uActive
        // row 7 = buttons (spans 3 cols)
        var tbl = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 3, RowCount = 8,
            BackColor = Color.Transparent,
            Padding = new Padding(0) };

        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // 0 title
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));  // 1 labels row 1
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // 2 inputs row 1
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));  // 3 labels row 2
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // 4 inputs row 2
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));  // 5 labels row 3
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // 6 inputs row 3
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));  // 7 buttons

        // Helper: label cell
        Label L(string t) => new Label {
            Text = t, Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(160, 185, 230),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(4, 0, 4, 1) };

        // Helper: style and place an input
        void I(Control c, int col, int row) {
            c.Dock = DockStyle.Fill;
            c.Margin = new Padding(4, 2, 4, 2);
            if (c is TextBox tb) {
                tb.BackColor = FIELD_BG; tb.ForeColor = TXT;
                tb.BorderStyle = BorderStyle.FixedSingle;
                tb.Font = new Font("Segoe UI", 9);
            }
            if (c is NumericUpDown nud) nud.Font = new Font("Segoe UI", 9);
            if (c is ComboBox cb)       cb.Font  = new Font("Segoe UI", 9);
            if (c is CheckBox chk) { chk.ForeColor = TXT; chk.Font = new Font("Segoe UI", 9); }
            tbl.Controls.Add(c, col, row);
        }

        // Row 0: title spanning all 3 columns
        var titleLbl = new Label {
            Text = "Add / Edit User", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = ACCENT2, BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(4, 0, 0, 0) };
        tbl.Controls.Add(titleLbl, 0, 0);
        tbl.SetColumnSpan(titleLbl, 3);

        // Row 1: labels
        tbl.Controls.Add(L("User ID"),       0, 1);
        tbl.Controls.Add(L("Name"),          1, 1);
        tbl.Controls.Add(L("Age"),           2, 1);
        // Row 2: inputs
        I(_uId,    0, 2); I(_uName, 1, 2); I(_uAge, 2, 2);

        // Row 3: labels
        tbl.Controls.Add(L("Gender"),        0, 3);
        tbl.Controls.Add(L("Level"),         1, 3);
        tbl.Controls.Add(L("Role"),          2, 3);
        // Row 4: inputs
        I(_uGender, 0, 4); I(_uLevel, 1, 4); I(_uRole, 2, 4);

        // Row 5: labels
        tbl.Controls.Add(L("Bluetooth ID"),  0, 5);
        tbl.Controls.Add(L("Face ID"),       1, 5);
        tbl.Controls.Add(L("Active"),        2, 5);
        // Row 6: inputs
        I(_uBt, 0, 6); I(_uFace, 1, 6); I(_uActive, 2, 6);

        // Row 7: marker instruction cards (spans 3 cols) — no buttons
        var markerFlow = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = Color.Transparent,
            Margin = new Padding(2, 4, 0, 0) };

        var markerDefs = new (string Id, string Action, Color Clr)[] {
            ("31", "Add",        Color.FromArgb(26, 130, 60)),
            ("32", "Save",       ACCENT),
            ("33", "Deactivate", Color.FromArgb(170, 100, 14)),
            ("34", "Delete",     Color.FromArgb(175, 35, 35)),
            ("35", "Clear",      Color.FromArgb(60, 68, 100)),
            ("36", "↻ Select",   Color.FromArgb(80, 140, 220)),
            ("37", "Gaze Hist.", Color.FromArgb(0, 190, 160)),
            ("20", "Back",       Color.FromArgb(100, 60, 100)),
        };
        foreach (var (mid, action, clr) in markerDefs)
        {
            var card = new Panel {
                Size = new Size(82, 34), BackColor = Color.FromArgb(22, 32, 58),
                Margin = new Padding(0, 0, 5, 0), Cursor = Cursors.Default };
            card.Paint += (s, e) => {
                var g = e.Graphics;
                using (var pen = new System.Drawing.Pen(clr, 1.2f))
                    g.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                using (var b = new System.Drawing.SolidBrush(clr))
                    g.FillRectangle(b, 0, 0, card.Width, 4);
            };
            card.Controls.Add(new Label {
                Text = $"M{mid}", Font = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = clr, BackColor = Color.Transparent,
                Location = new Point(4, 5), AutoSize = true });
            card.Controls.Add(new Label {
                Text = action, Font = new Font("Segoe UI", 8),
                ForeColor = TXT, BackColor = Color.Transparent,
                Location = new Point(4, 18), AutoSize = true });
            markerFlow.Controls.Add(card);
        }
        tbl.Controls.Add(markerFlow, 0, 7);
        tbl.SetColumnSpan(markerFlow, 3);

        formWrap.Controls.Add(tbl);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  CONTENT TAB
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

        // Filter bar
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

        // Grid
        var gridWrap = new Panel { Dock = DockStyle.Fill, BackColor = PANEL,
            Padding = new Padding(12, 8, 12, 4) };
        outer.Controls.Add(gridWrap, 0, 1);
        _cGrid = MkGrid();
        _cGrid.Dock = DockStyle.Fill;
        _cGrid.SelectionChanged += OnCRowSelected;
        gridWrap.Controls.Add(_cGrid);

        // Form
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

        void ACF(string lbl, Control ctrl, int col, int row, bool span = false) {
            cGrid.Controls.Add(new Label {
                Text = lbl, Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = TXT_DIM, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0, 0, 6, 0), BackColor = Color.Transparent }, col, row);
            ctrl.Dock = DockStyle.Fill; ctrl.Margin = new Padding(0, 3, 12, 3);
            if (ctrl is TextBox tb) { tb.BackColor = FIELD_BG; tb.ForeColor = TXT; tb.BorderStyle = BorderStyle.FixedSingle; }
            cGrid.Controls.Add(ctrl, col + 1, row);
            if (span) cGrid.SetColumnSpan(ctrl, 5);
        }

        ACF("ID",       _cId,       0, 0); ACF("Title",    _cTitle,    2, 0); ACF("Level",  _cLevel,  4, 0);
        ACF("Marker",   _cMarker,   0, 1); ACF("Module",   _cModule,   2, 1); ACF("Activity",_cActivity,4,1);
        ACF("Desc",     _cDesc,     0, 2, false); ACF("Coach Tip", _cTip, 2, 2, false);
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
            // Auto-select first row so form is populated immediately
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
            if (u != null) FillUserForm(u);
        } catch { }
    }

    private void FillUserForm(UserData u)
    {
        _uId.Text   = u.UserId ?? "";
        _uName.Text = u.Name   ?? "";
        _uAge.Value = Math.Max(1, Math.Min(120, u.Age == 0 ? 18 : u.Age));
        SetCbo(_uGender, u.Gender ?? "Male");
        SetCbo(_uLevel,  u.Level  ?? "Beginner");
        _uBt.Text   = u.BluetoothId ?? "";
        _uFace.Text = u.FaceId      ?? "";
        SetCbo(_uRole, u.Role ?? "Player");
        _uActive.Checked = u.IsActive;
    }

    private UserData UserFromForm()
    {
        string id = _uId.Text.Trim();
        if (string.IsNullOrWhiteSpace(id))
            id = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var existing = _uItems.FirstOrDefault(x => x.UserId == id);
        return new UserData {
            UserId      = id,
            Name        = _uName.Text.Trim(),
            Age         = (int)_uAge.Value,
            Gender      = _uGender.SelectedItem?.ToString() ?? "Male",
            Level       = _uLevel.SelectedItem?.ToString()  ?? "Beginner",
            BluetoothId = _uBt.Text.Trim(),
            FaceId      = _uFace.Text.Trim(),
            Role        = _uRole.SelectedItem?.ToString()   ?? "Player",
            IsActive    = _uActive.Checked,
            GazeProfile = existing?.GazeProfile ?? new GazeProfile()
        };
    }

    private void ClearUserForm()
    {
        _uId.Text = _uName.Text = _uBt.Text = _uFace.Text = "";
        _uAge.Value = 18; _uActive.Checked = true;
        if (_uGender.Items.Count > 0) _uGender.SelectedIndex = 0;
        if (_uLevel.Items.Count  > 0) _uLevel.SelectedIndex  = 0;
        if (_uRole.Items.Count   > 0) _uRole.SelectedIndex   = 0;
    }

    private void OnUserAdd(object sender, EventArgs e)
    {
        try {
            _uId.Text = "";
            var u = UserFromForm();
            if (string.IsNullOrWhiteSpace(u.Name)) { ShowErr("Validation", "Name is required."); return; }
            _userSvc.AddUser(u); ReloadUsers();
            MessageBox.Show($"User '{u.Name}' added.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) { LogErr(ex); ShowErr("Error", ex.Message); }
    }

    private void OnUserSave(object sender, EventArgs e)
    {
        try {
            var u = UserFromForm();
            if (string.IsNullOrWhiteSpace(u.Name)) { ShowErr("Validation", "Name is required."); return; }
            _userSvc.UpdateUser(u); ReloadUsers();
            MessageBox.Show($"User '{u.Name}' saved.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) { LogErr(ex); ShowErr("Error", ex.Message); }
    }

    private void OnUserDeact(object sender, EventArgs e)
    {
        try {
            string id = _uId.Text.Trim();
            if (string.IsNullOrWhiteSpace(id)) { ShowErr("Validation", "Select a user first."); return; }
            _userSvc.DeactivateUser(id); ReloadUsers();
            MessageBox.Show($"User '{id}' deactivated.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) { LogErr(ex); ShowErr("Error", ex.Message); }
    }

    private void OnUserDel(object sender, EventArgs e)
    {
        try {
            string id = _uId.Text.Trim();
            if (string.IsNullOrWhiteSpace(id)) { ShowErr("Validation", "Select a user first."); return; }
            var u = _uItems.FirstOrDefault(x => x.UserId == id);
            if (MessageBox.Show($"Delete '{u?.Name ?? id}'?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _userSvc.DeleteUser(id); ReloadUsers(); ClearUserForm();
        } catch (Exception ex) { LogErr(ex); ShowErr("Error", ex.Message); }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  CONTENT CRUD
    // ═════════════════════════════════════════════════════════════════════
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
            Font = new Font("Segoe UI", 9),
            ColumnHeadersHeight = 30,
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
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            Size = new Size(w, h) };
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

    // ── TUIO ──────────────────────────────────────────────────────────────
    private void OnGesture(int id) {
        if (!Visible || IsDisposed) return;
        BeginInvoke((MethodInvoker)(() => DispatchMarker(id)));
    }

    public void addTuioObject(TuioObject o)
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke((MethodInvoker)(() => DispatchMarker(o.SymbolID)));
    }

    /// <summary>
    /// updateTuioObject fires continuously while a marker is on the table.
    /// We use it to track Marker 36 rotation for user selection.
    /// </summary>
    public void updateTuioObject(TuioObject o)
    {
        if (o.SymbolID != 36) return;
        if (!IsHandleCreated || IsDisposed) return;

        float angle = o.Angle;   // radians, 0..2π
        BeginInvoke((MethodInvoker)(() => HandleMarker36Rotation(angle)));
    }

    private void HandleMarker36Rotation(float angle)
    {
        // Debounce: skip if moved too recently
        if ((DateTime.Now - _marker36LastMove).TotalMilliseconds < 350) return;

        if (float.IsNaN(_marker36LastAngle))
        {
            // First detection — just record angle, don't move yet
            _marker36LastAngle = angle;
            return;
        }

        // Compute signed angular delta (handle wrap-around at 0/2π)
        float delta = angle - _marker36LastAngle;
        if (delta >  (float)Math.PI) delta -= 2f * (float)Math.PI;
        if (delta < -(float)Math.PI) delta += 2f * (float)Math.PI;

        if (Math.Abs(delta) < ROTATION_THRESHOLD) return;

        // delta > 0 = clockwise = next user; delta < 0 = counter-clockwise = previous
        int direction = delta > 0 ? 1 : -1;
        _marker36LastAngle = angle;
        _marker36LastMove  = DateTime.Now;

        MoveUserSelection(direction);
    }

    private void MoveUserSelection(int direction)
    {
        if (_uGrid == null || _uGrid.Rows.Count == 0) return;

        int current = _uGrid.SelectedRows.Count > 0
            ? _uGrid.SelectedRows[0].Index
            : -1;

        int next = current + direction;
        next = Math.Max(0, Math.Min(_uGrid.Rows.Count - 1, next));

        if (next == current) return;

        _uGrid.ClearSelection();
        _uGrid.Rows[next].Selected = true;
        _uGrid.FirstDisplayedScrollingRowIndex = next;
        Console.WriteLine($"[ContentManager] Marker 36 → row {next}");
    }

    private void DispatchMarker(int id)
    {
        Console.WriteLine($"[ContentManager] Marker {id} detected");

        // Marker 36 handled via updateTuioObject rotation — reset angle on add
        if (id == 36) { _marker36LastAngle = float.NaN; return; }

        // Marker 20 = Back (no cooldown needed)
        if (id == 20) { Close(); return; }

        // Action markers 31-35: debounce
        bool sameCooldown = id == _lastActionMarker &&
            (DateTime.Now - _lastActionTime).TotalMilliseconds < ACTION_COOLDOWN_MS;
        if (sameCooldown) return;
        _lastActionMarker = id;
        _lastActionTime   = DateTime.Now;

        switch (id)
        {
            case 31:
                Console.WriteLine("[ContentManager] Marker 31 → Add User");
                OnUserAdd(this, EventArgs.Empty);
                break;
            case 32:
                Console.WriteLine("[ContentManager] Marker 32 → Save/Update User");
                OnUserSave(this, EventArgs.Empty);
                break;
            case 33:
                Console.WriteLine("[ContentManager] Marker 33 → Deactivate User");
                OnUserDeact(this, EventArgs.Empty);
                break;
            case 34:
                Console.WriteLine("[ContentManager] Marker 34 → Delete User");
                OnUserDel(this, EventArgs.Empty);
                break;
            case 35:
                Console.WriteLine("[ContentManager] Marker 35 → Clear Form");
                ClearUserForm();
                break;
            case 37:
                Console.WriteLine("[ContentManager] Marker 37 → Show Gaze History");
                ShowGazeHistory();
                break;
        }
    }

    /// <summary>
    /// Opens a popup showing the last 5 gaze session reports for the selected user.
    /// Triggered by Marker 37 in the admin Users tab.
    /// </summary>
    private void ShowGazeHistory()
    {
        // Get selected user
        if (_uGrid == null || _uGrid.SelectedRows.Count == 0)
        {
            MessageBox.Show("Select a user first (rotate Marker 36).",
                "Gaze History", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        int idx = _uGrid.SelectedRows[0].Index;
        if (idx < 0 || idx >= _uItems.Count) return;
        var user = _uItems[idx];

        var sessions = GazeReportService.GetRecent(user.UserId, 5);
        var trends = GazeReportService.GetTrends(user.UserId, 5);

        // Build the popup form
        var popup = new Form
        {
            Text = $"Gaze History \u2014 {user.Name}",
            Size = new Size(620, 500),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(14, 22, 44),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
        };

        var scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding = new Padding(16, 12, 16, 12)
        };
        popup.Controls.Add(scrollPanel);

        int y = 0;

        // Title
        scrollPanel.Controls.Add(new Label
        {
            Text = $"\ud83d\udcc8  Gaze Tracking History for {user.Name}",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = false, Size = new Size(560, 32),
            Location = new Point(0, y),
            BackColor = Color.Transparent,
        });
        y += 36;

        // Current GazeProfile summary
        var gp = user.GazeProfile ?? new GazeProfile();
        scrollPanel.Controls.Add(new Label
        {
            Text = $"Current Profile:  Strokes={gp.Strokes_Score}  Rules={gp.Rules_Score}  " +
                   $"Practice={gp.Practice_Score}  Quiz={gp.Quiz_Score}  " +
                   $"Spelling={gp.Spelling_Score}  Competition={gp.Competition_Score}",
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = Color.FromArgb(140, 175, 225),
            AutoSize = false, Size = new Size(560, 22),
            Location = new Point(0, y),
            BackColor = Color.Transparent,
        });
        y += 26;

        // Trend indicators
        string trendLine = "Trends: ";
        foreach (var kvp in trends)
        {
            string arrow = kvp.Value > 0 ? "\u2191" : kvp.Value < 0 ? "\u2193" : "\u2192";
            trendLine += $"{kvp.Key}{arrow}  ";
        }
        scrollPanel.Controls.Add(new Label
        {
            Text = trendLine,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(60, 210, 160),
            AutoSize = false, Size = new Size(560, 20),
            Location = new Point(0, y),
            BackColor = Color.Transparent,
        });
        y += 28;

        // Separator
        scrollPanel.Controls.Add(new Panel
        {
            Size = new Size(560, 1), Location = new Point(0, y),
            BackColor = Color.FromArgb(45, 65, 100)
        });
        y += 8;

        if (sessions.Count == 0)
        {
            scrollPanel.Controls.Add(new Label
            {
                Text = "No gaze sessions recorded yet for this user.",
                Font = new Font("Segoe UI", 11),
                ForeColor = Color.FromArgb(160, 180, 210),
                AutoSize = false, Size = new Size(560, 30),
                Location = new Point(0, y),
                BackColor = Color.Transparent,
            });
        }
        else
        {
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                var s = sessions[i];
                string when = s.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                string dur = TimeSpan.FromSeconds(s.DurationSeconds).ToString(@"mm\:ss");

                // Session header
                scrollPanel.Controls.Add(new Label
                {
                    Text = $"Session {i + 1}  \u2022  {when}  \u2022  {dur}  \u2022  {s.TotalFixations} fixations",
                    Font = new Font("Segoe UI", 10, FontStyle.Bold),
                    ForeColor = Color.White,
                    AutoSize = false, Size = new Size(560, 24),
                    Location = new Point(0, y),
                    BackColor = Color.Transparent,
                });
                y += 24;

                // Dominant + neglected
                string info = $"Dominant: {s.DominantCategory}";
                if (s.NeglectedCategories != null && s.NeglectedCategories.Count > 0)
                    info += $"  |  Neglected: {string.Join(", ", s.NeglectedCategories)}";

                scrollPanel.Controls.Add(new Label
                {
                    Text = info,
                    Font = new Font("Segoe UI", 9),
                    ForeColor = Color.FromArgb(130, 165, 215),
                    AutoSize = false, Size = new Size(560, 20),
                    Location = new Point(0, y),
                    BackColor = Color.Transparent,
                });
                y += 20;

                // Score bars
                if (s.SessionScores != null)
                {
                    string scores = "Scores: ";
                    foreach (var sc in s.SessionScores)
                        scores += $"{sc.Key}={sc.Value}  ";

                    scrollPanel.Controls.Add(new Label
                    {
                        Text = scores,
                        Font = new Font("Segoe UI", 8),
                        ForeColor = Color.FromArgb(100, 140, 195),
                        AutoSize = false, Size = new Size(560, 18),
                        Location = new Point(0, y),
                        BackColor = Color.Transparent,
                    });
                    y += 18;
                }

                // Separator
                scrollPanel.Controls.Add(new Panel
                {
                    Size = new Size(540, 1), Location = new Point(10, y + 4),
                    BackColor = Color.FromArgb(35, 55, 85)
                });
                y += 14;
            }
        }

        popup.ShowDialog(this);
    }

    public void removeTuioObject(TuioObject o)
    {
        // Reset marker 36 angle when it leaves the table
        if (o.SymbolID == 36) _marker36LastAngle = float.NaN;
    }

    public void addTuioCursor(TuioCursor c)    { }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { }
    public void addTuioBlob(TuioBlob b)        { }
    public void updateTuioBlob(TuioBlob b)     { }
    public void removeTuioBlob(TuioBlob b)     { }
    public void refresh(TuioTime t)            { }
}
