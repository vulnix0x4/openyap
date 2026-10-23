using System;
using System.Drawing;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenYap
{
    static class Theme
    {
        public static readonly Color Background = Color.FromArgb(17, 19, 21), Card = Color.FromArgb(27, 30, 34),
            Field = Color.FromArgb(36, 40, 46), Border = Color.FromArgb(48, 53, 60), Text = Color.FromArgb(236, 239, 241),
            Secondary = Color.FromArgb(152, 162, 173), Mint = Color.FromArgb(61, 220, 151), Orange = Color.FromArgb(255, 159, 67),
            Red = Color.FromArgb(255, 92, 92), Track = Color.FromArgb(52, 57, 64);
        public static float Scale = 1;
        public static int S(float px) { return (int)Math.Round(px * Scale); }
        public static Font Body = new Font("Segoe UI", 9.75f), Caption = new Font("Segoe UI", 8.75f),
            Headline = new Font("Segoe UI Semibold", 10.5f), Title = new Font("Segoe UI Semibold", 22f),
            Small = new Font("Segoe UI Semibold", 8f), Mono = new Font("Consolas", 8.75f), Icons = new Font("Segoe MDL2 Assets", 10f);

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Label Label(string text, Font font, Color color, int width)
        {
            return new Label
            {
                Text = text, Font = font, ForeColor = color, AutoSize = true, MaximumSize = new Size(width, 0),
                Margin = new Padding(0, 0, 0, S(4)), BackColor = Color.Transparent, UseMnemonic = false,
            };
        }

        public static Button Button(string text, Color fill, Color foreground)
        {
            var b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, BackColor = fill, ForeColor = foreground, Font = Body,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(S(10), S(4), S(10), S(4)),
                Margin = new Padding(0, 0, S(8), 0), Cursor = Cursors.Hand, UseMnemonic = false,
            };
            b.FlatAppearance.BorderColor = fill == Theme.Field ? Border : fill;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(fill, 0.15f);
            b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(fill, 0.05f);
            return b;
        }

        public static CheckBox Check(string text)
        {
            return new CheckBox
            {
                Text = text, ForeColor = Text, Font = Body, AutoSize = true, FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, S(2), 0, S(6)), UseMnemonic = false, Cursor = Cursors.Hand,
            };
        }
    }

    // A rounded card that hosts a top-down flow of rows.
    sealed class Card : FlowLayoutPanel
    {
        public Color Fill = Theme.Card;
        public Card(int width, Color? fill = null)
        {
            if (fill.HasValue) Fill = fill.Value;
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(Theme.S(14));
            Margin = new Padding(0, 0, 0, Theme.S(14));
            MinimumSize = new Size(width, 0);
            MaximumSize = new Size(width, 0);
            BackColor = Theme.Background;
            DoubleBuffered = true;
        }
        public int Inner { get { return MaximumSize.Width - Padding.Horizontal; } }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(Fill))
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(12)))
                e.Graphics.FillPath(brush, path);
        }
        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control.BackColor == Theme.Background || e.Control is Label || e.Control is CheckBox) e.Control.BackColor = Fill;
        }
    }

    sealed class LevelMeter : Control
    {
        float level;
        public Color Tint = Theme.Mint;
        public LevelMeter(int width)
        {
            Size = new Size(width, Theme.S(4));
            Margin = new Padding(0, Theme.S(2), 0, Theme.S(2));
            DoubleBuffered = true;
            AccessibleName = "Signal level";
        }
        public float Level
        {
            get { return level; }
            set
            {
                float v = float.IsNaN(value) ? 0 : value;
                if (Math.Abs(v - level) < 0.0005f) return;
                level = v;
                AccessibleDescription = v > 0.001f ? "Signal present" : "Silent";
                Invalidate();
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float fraction = level > 0 ? (float)Math.Max(0, Math.Min(1, (20 * Math.Log10(level) + 60) / 60)) : 0;
            float r = Height / 2f;
            using (var track = new SolidBrush(Theme.Track))
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), r))
                e.Graphics.FillPath(track, path);
            if (fraction > 0)
                using (var fill = new SolidBrush(level > 0.9f ? Theme.Orange : Tint))
                using (var path = Theme.Rounded(new RectangleF(0, 0, Math.Max(Height, (Width - 1) * fraction), Height - 1), r))
                    e.Graphics.FillPath(fill, path);
        }
    }

    // Keyboard and mouse slider, 0…1, painted to match the theme.
    sealed class FlatSlider : Control
    {
        float value;
        bool dragging, locked;
        public Color Tint = Theme.Mint;
        public event Action ValueChanged, DragEnded;
        public FlatSlider(int width, string name)
        {
            Size = new Size(width, Theme.S(22));
            DoubleBuffered = true;
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleName = name;
            AccessibleRole = AccessibleRole.Slider;
            Margin = new Padding(0);
        }
        public bool Dragging { get { return dragging; } }
        public bool Locked { get { return locked; } set { locked = value; Cursor = value ? Cursors.No : Cursors.Hand; Invalidate(); } }
        public float Value
        {
            get { return value; }
            set { SetValue(value, false); }
        }
        void SetValue(float v, bool user)
        {
            v = Math.Max(0, Math.Min(1, float.IsNaN(v) ? 0 : v));
            if (v == value) return;
            value = v;
            AccessibleDescription = (int)Math.Round(v * 100) + " percent";
            Invalidate();
            if (user && ValueChanged != null) ValueChanged();
        }
        float Usable { get { return Width - Height; } }
        void Track(int x) { if (Enabled && !locked) SetValue((x - Height / 2f) / Usable, true); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Left && !locked) { dragging = true; Capture = true; Track(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) Track(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { if (dragging) { dragging = false; Capture = false; if (DragEnded != null) DragEnded(); } }
        protected override void OnMouseWheel(MouseEventArgs e) { if (Enabled && !locked) SetValue(value + Math.Sign(e.Delta) * 0.02f, true); }
        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!Enabled || locked) return;
            float step = e.KeyCode == Keys.PageUp || e.KeyCode == Keys.PageDown ? 0.1f : 0.01f;
            if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.PageUp) SetValue(value + step, true);
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down || e.KeyCode == Keys.PageDown) SetValue(value - step, true);
            else if (e.KeyCode == Keys.Home) SetValue(0, true);
            else if (e.KeyCode == Keys.End) SetValue(1, true);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float h = Theme.S(4), cy = Height / 2f, x0 = Height / 2f, x1 = Width - Height / 2f;
            float x = x0 + Usable * value;
            Color tint = Enabled && !locked ? Tint : Color.FromArgb(110, Tint);
            if (!Enabled) tint = Theme.Secondary;
            using (var track = new SolidBrush(Theme.Track)) g.FillRectangle(track, x0, cy - h / 2, x1 - x0, h);
            using (var fill = new SolidBrush(tint)) g.FillRectangle(fill, x0, cy - h / 2, x - x0, h);
            float r = Height / 2f - Theme.S(3);
            using (var thumb = new SolidBrush(Enabled ? Theme.Text : Theme.Secondary)) g.FillEllipse(thumb, x - r, cy - r, r * 2, r * 2);
            if (Focused) using (var pen = new Pen(Tint, Theme.S(2))) g.DrawEllipse(pen, x - r - 1, cy - r - 1, r * 2 + 2, r * 2 + 2);
        }
    }

    // Title, percentage, slider, mute button, and meter — the SwiftUI MixerStrip.
    sealed class MixerStrip : FlowLayoutPanel
    {
        readonly Label title, value;
        readonly Button mute;
        public readonly FlatSlider Slider;
        public readonly LevelMeter Meter;
        bool muted, locked;
        readonly string name;
        public event Action Changed;

        public MixerStrip(string name, int width, Color tint)
        {
            this.name = name;
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoSize = true;
            Margin = new Padding(0, 0, 0, Theme.S(12));
            var header = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Width = width, Margin = new Padding(0), MinimumSize = new Size(width, 0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            title = Theme.Label(name, Theme.Body, Theme.Text, width - Theme.S(90));
            value = Theme.Label("", Theme.Caption, Theme.Secondary, Theme.S(110));
            value.Anchor = AnchorStyles.Right;
            header.Controls.Add(title, 0, 0);
            header.Controls.Add(value, 1, 0);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            Slider = new FlatSlider(width - Theme.S(34), name) { Tint = tint };
            mute = new Button
            {
                FlatStyle = FlatStyle.Flat, Font = Theme.Icons, Size = new Size(Theme.S(28), Theme.S(22)), Margin = new Padding(Theme.S(4), 0, 0, 0),
                ForeColor = Theme.Secondary, Cursor = Cursors.Hand, TabStop = true, Text = "",
            };
            mute.FlatAppearance.BorderSize = 0;
            mute.FlatAppearance.MouseOverBackColor = Theme.Field;
            row.Controls.Add(Slider);
            row.Controls.Add(mute);
            Meter = new LevelMeter(width - Theme.S(34)) { Tint = tint };
            Controls.Add(header);
            Controls.Add(row);
            Controls.Add(Meter);
            Slider.ValueChanged += () => { Render(); if (Changed != null) Changed(); };
            mute.Click += (s, e) => { muted = !muted; Render(); if (Changed != null) Changed(); };
            Render();
        }

        public float Gain { get { return Slider.Value; } set { if (!Slider.Dragging) { Slider.Value = value; Render(); } } }
        public bool Muted { get { return muted; } set { if (muted != value) { muted = value; Render(); } } }
        public bool Locked { get { return locked; } set { if (locked != value) { locked = value; Slider.Locked = value; Render(); } } }

        void Render()
        {
            value.Text = muted ? "Muted" : locked ? "100% · Locked" : (int)Math.Round(Slider.Value * 100) + "%";
            mute.Text = muted ? "" : "";
            mute.ForeColor = muted ? Theme.Red : Theme.Secondary;
            mute.AccessibleName = (muted ? "Unmute " : "Mute ") + name;
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            title.ForeColor = Enabled ? Theme.Text : Theme.Secondary;
        }

        protected override void OnParentBackColorChanged(EventArgs e) { base.OnParentBackColorChanged(e); BackColor = Parent.BackColor; }
    }

    // Dark, owner-drawn drop-down list of (id, name) items.
    sealed class Picker : ComboBox
    {
        public Picker(int width, string name)
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Field;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Width = width;
            ItemHeight = Theme.S(22);
            AccessibleName = name;
            Margin = new Padding(0, 0, 0, Theme.S(8));
        }

        string signature = "";
        public void SetItems(System.Collections.Generic.IList<Tuple<string, string>> items, string selected)
        {
            string next = string.Join("\n", items.Select(i => i.Item1 + "\t" + i.Item2));
            if (next != signature)
            {
                signature = next;
                BeginUpdate();
                Items.Clear();
                foreach (var item in items) Items.Add(item);
                EndUpdate();
            }
            int index = -1;
            for (int i = 0; i < items.Count; i++) if (items[i].Item1 == selected) index = i;
            if (SelectedIndex != index && !DroppedDown) SelectedIndex = index;
        }

        public string SelectedId { get { var t = SelectedItem as Tuple<string, string>; return t != null ? t.Item1 : null; } }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var back = new SolidBrush(selected ? Theme.Border : Theme.Field)) e.Graphics.FillRectangle(back, e.Bounds);
            if (e.Index >= 0)
            {
                var item = (Tuple<string, string>)Items[e.Index];
                TextRenderer.DrawText(e.Graphics, item.Item2, Font, new Rectangle(e.Bounds.X + Theme.S(4), e.Bounds.Y, e.Bounds.Width - Theme.S(4), e.Bounds.Height),
                    Enabled ? Theme.Text : Theme.Secondary, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

}
