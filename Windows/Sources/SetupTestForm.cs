using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenYap
{
    // The guided "Test my setup" sheet. Uses real audio only; never sends a test tone to the receiving app.
    sealed class SetupTestForm : Form
    {
        readonly SessionModel model;
        readonly FlowLayoutPanel content;
        readonly Check[] checks = new Check[4];
        readonly CheckBox heardMusic, heardByPlayer;
        readonly Label returnMessage, finalTitle, finalText;
        readonly Button enableReturn, startForTest;
        bool syncing;

        sealed class Check
        {
            public Label Badge, Title, Detail, State;
        }

        public SetupTestForm(SessionModel model)
        {
            this.model = model;
            Text = "Test your setup";
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            int width = Theme.S(500);
            content = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(Theme.S(26), Theme.S(22)), Padding = new Padding(0, 0, 0, Theme.S(22)) };
            Controls.Add(content);
            content.Controls.Add(Theme.Label("Test your setup", Theme.Title, Theme.Text, width));
            content.Controls.Add(Theme.Label("Start sharing first. These checks use real audio; OpenYap never sends a test tone into your receiving app.", Theme.Body, Theme.Secondary, width));
            for (int i = 0; i < 4; i++) checks[i] = AddCheck(i + 1, width);
            heardMusic = Theme.Check("I can hear the music through my chosen device");
            heardMusic.Margin = new Padding(Theme.S(38), 0, 0, Theme.S(10));
            heardMusic.CheckedChanged += (s, e) => { if (!syncing) { model.HeardMusic = heardMusic.Checked; UpdateView(); } };
            InsertAfter(heardMusic, checks[1].Title.Parent.Parent);
            returnMessage = Theme.Label("", Theme.Caption, Theme.Secondary, width);
            content.Controls.Add(returnMessage);
            enableReturn = Theme.Button("Enable virtual input check", Theme.Field, Theme.Text);
            enableReturn.Margin = new Padding(0, 0, 0, Theme.S(12));
            enableReturn.Click += async (s, e) => { await model.EnableReturnCheck(); };
            content.Controls.Add(enableReturn);
            finalTitle = Theme.Label("", Theme.Headline, Theme.Text, width);
            finalText = Theme.Label("", Theme.Body, Theme.Text, width);
            content.Controls.Add(finalTitle);
            content.Controls.Add(finalText);
            heardByPlayer = Theme.Check("The receiver or recording confirmed the audio");
            heardByPlayer.CheckedChanged += (s, e) => { if (!syncing) { model.HeardByPlayer = heardByPlayer.Checked; UpdateView(); } };
            content.Controls.Add(heardByPlayer);
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, Theme.S(8), 0, 0) };
            startForTest = Theme.Button("Start sharing for test", Theme.Mint, Color.Black);
            startForTest.Click += async (s, e) => { await model.Start(); };
            var reset = Theme.Button("Reset checks", Theme.Field, Theme.Text);
            reset.Click += (s, e) => model.TestSetup();
            var done = Theme.Button("Done", Theme.Field, Theme.Text);
            done.Click += (s, e) => Close();
            buttons.Controls.AddRange(new Control[] { startForTest, reset, done });
            content.Controls.Add(buttons);
            AcceptButton = done;
            ClientSize = new Size(width + Theme.S(52), content.PreferredSize.Height + Theme.S(30));
            HandleCreated += (s, e) => { int on = 1; try { DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch (Exception) { } };
        }

        void InsertAfter(Control control, Control anchor)
        {
            content.Controls.Add(control);
            content.Controls.SetChildIndex(control, content.Controls.GetChildIndex(anchor) + 1);
        }

        Check AddCheck(int number, int width)
        {
            var row = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Width = width, MinimumSize = new Size(width, 0), Margin = new Padding(0, Theme.S(8), 0, Theme.S(6)) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(38)));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var check = new Check
            {
                Badge = new Label { Size = new Size(Theme.S(26), Theme.S(26)), Font = Theme.Body, Text = number.ToString(), Margin = new Padding(0) },
                Title = Theme.Label("", Theme.Headline, Theme.Text, width - Theme.S(110)),
                Detail = Theme.Label("", Theme.Body, Theme.Secondary, width - Theme.S(110)),
                State = Theme.Label("Pending", Theme.Caption, Theme.Secondary, Theme.S(70)),
            };
            check.Badge.Paint += PaintBadge;
            var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            text.Controls.Add(check.Title);
            text.Controls.Add(check.Detail);
            row.Controls.Add(check.Badge, 0, 0);
            row.Controls.Add(text, 1, 0);
            row.Controls.Add(check.State, 2, 0);
            content.Controls.Add(row);
            return check;
        }

        void PaintBadge(object sender, PaintEventArgs e)
        {
            var label = (Label)sender;
            bool passed = label.Tag is bool && (bool)label.Tag;
            e.Graphics.Clear(Theme.Background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(passed ? Color.FromArgb(38, Theme.Mint) : Theme.Field))
                e.Graphics.FillEllipse(brush, 0, 0, label.Width - 1, label.Height - 1);
            TextRenderer.DrawText(e.Graphics, passed ? "✓" : label.Text, label.Font, label.ClientRectangle, passed ? Theme.Mint : Theme.Secondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        void Set(int index, string title, string detail, bool passed)
        {
            var c = checks[index];
            c.Title.Text = title;
            c.Detail.Text = detail;
            c.State.Text = passed ? "Verified" : "Pending";
            c.State.ForeColor = passed ? Theme.Mint : Theme.Secondary;
            if (!(c.Badge.Tag is bool) || (bool)c.Badge.Tag != passed) { c.Badge.Tag = passed; c.Badge.Invalidate(); }
        }

        public void UpdateView()
        {
            if (IsDisposed) return;
            syncing = true;
            var m = model;
            Set(0, "Music capture", "Play a song in " + m.AppName(m.MusicApp) + ".", m.Observed[0]);
            Set(1, "Headphone playback", "Music is playing at " + m.AppName(m.MusicApp) + "’s own volume, set by “Music I hear”. Only your ears can confirm the correct device.", m.HeardMusic);
            Set(2, m.IncludeVoice ? "Microphone capture" : "Microphone excluded",
                m.IncludeVoice ? "Speak into the selected microphone. If silent, check its mute switch and Windows microphone privacy settings."
                    : "To test a mic, stop sharing and enable Include my voice.", m.IncludeVoice && m.Observed[1]);
            Set(3, "Virtual microphone receives signal",
                (m.Observed[4] ? "Mix is being written." : "No mix written yet.") + " " +
                (m.Observed[5] ? "Signal was read back from " + m.VirtualInputName + "." : "Waiting for the cable's input; silence may also mean Windows microphone access is blocked."),
                m.Observed[4] && m.Observed[5]);
            heardMusic.Checked = m.HeardMusic;
            heardByPlayer.Checked = m.HeardByPlayer;
            returnMessage.Text = m.ReturnCheckMessage;
            enableReturn.Visible = !m.ReturnCheckEnabled;
            enableReturn.Enabled = m.Active;
            finalTitle.Text = "Final check in " + m.DestinationName;
            finalText.Text = "Select " + m.VirtualInputName + " as input and your headphones as output. Ask another person to confirm music and voice, or check a recording in the receiving app. Local meters cannot verify delivery to a call or recording.";
            startForTest.Visible = !m.Active;
            startForTest.Enabled = !m.Busy;
            syncing = false;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
