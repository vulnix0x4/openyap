using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace OpenYap
{
    sealed class MainForm : Form
    {
        readonly SessionModel model;
        readonly int width;
        readonly Panel scroller;
        readonly FlowLayoutPanel content;
        Label pill, bluetoothNote, browserNote, monitorNote, defaultNote, status, detail, micFor, micForCaption, masterCaption;
        Picker musicPicker, destinationPicker, outputPicker, virtualPicker, micPicker;
        CheckBox includeVoice, monitorDestination, showSynthesized;
        Card setupCard, duckingCard, cableCard;
        MixerStrip musicHear, gameHear, musicSend, voiceSend, master, boardSend;
        Button maxSharing, startButton, stopButton, testButton, soundboardToggle, advancedToggle, importButton, playVoice;
        LevelMeter sendMeter, effectsMeter;
        FlatSlider effectsSlider, deviceSlider;
        Label effectsLabel, soundStatus, libraryMessage, deviceTitle, deviceCaption, deviceError, livePeaks, buffers, routes, speechCount;
        FlowLayoutPanel soundboardBody, padArea, advancedBody;
        int soundboardInner;
        TextBox search, message;
        Picker voicePicker, stylePicker;
        string deviceIdShown;
        bool syncing;
        SetupTestForm testForm;
        string padSignature = "";
        readonly Dictionary<string, Button> pads = new Dictionary<string, Button>();
        readonly ToolTip tips = new ToolTip();

        public MainForm(SessionModel model)
        {
            this.model = model;
            Text = "OpenYap — Share your sound";
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            width = Theme.S(640);
            int clientWidth = width + Theme.S(48) + SystemInformation.VerticalScrollBarWidth;
            int clientHeight = Math.Min(Theme.S(880), Screen.PrimaryScreen.WorkingArea.Height - Theme.S(80));
            ClientSize = new Size(clientWidth, clientHeight);
            scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background };
            content = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Location = new Point(Theme.S(24), Theme.S(20)), BackColor = Theme.Background, Padding = new Padding(0, 0, 0, Theme.S(24)),
            };
            scroller.Controls.Add(content);
            Controls.Add(scroller);
            Build();
            model.Changed += UpdateView;
            model.SoundLibrary.Changed += () => { if (!IsDisposed) BeginInvoke((Action)UpdateView); };
            UpdateView();
            FormClosing += (s, e) => model.Shutdown();
            DarkTitleBar();
        }

        void Add(Control c) { content.Controls.Add(c); }

        void Build()
        {
            // Header
            var header = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Width = width, MinimumSize = new Size(width, 0), Margin = new Padding(0, 0, 0, Theme.S(16)) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(0), WrapContents = false };
            var title = Theme.Label("OpenYap", Theme.Title, Theme.Text, width);
            title.Margin = new Padding(0);
            titles.Controls.Add(title);
            titles.Controls.Add(Theme.Label("Your music. Their soundtrack.", Theme.Body, Theme.Secondary, width));
            pill = new Label { AutoSize = true, Font = Theme.Headline, Padding = new Padding(Theme.S(12), Theme.S(6), Theme.S(12), Theme.S(6)), Anchor = AnchorStyles.Right, UseMnemonic = false };
            pill.Paint += PaintPill;
            header.Controls.Add(titles, 0, 0);
            header.Controls.Add(pill, 1, 0);
            Add(header);

            // Routing
            var routing = new Card(width);
            int field = routing.Inner;
            musicPicker = Row(routing, "Music app", field);
            destinationPicker = Row(routing, "Share with", field);
            outputPicker = Row(routing, "Listen through", field);
            virtualPicker = Row(routing, "Virtual microphone (send to)", field);
            includeVoice = Theme.Check("Include my voice");
            routing.Controls.Add(includeVoice);
            micPicker = Row(routing, "Microphone", field);
            Add(routing);
            musicPicker.SelectionChangeCommitted += (s, e) => { model.MusicApp = musicPicker.SelectedId; UpdateView(); };
            destinationPicker.SelectionChangeCommitted += (s, e) => { model.DestinationApp = destinationPicker.SelectedId; UpdateView(); };
            outputPicker.SelectionChangeCommitted += (s, e) => { model.OutputId = outputPicker.SelectedId ?? ""; model.Refresh(); UpdateView(); };
            virtualPicker.SelectionChangeCommitted += (s, e) => { model.VirtualId = virtualPicker.SelectedId ?? ""; UpdateView(); };
            micPicker.SelectionChangeCommitted += (s, e) => { model.MicId = micPicker.SelectedId ?? ""; UpdateView(); };
            includeVoice.CheckedChanged += (s, e) => { if (!syncing) { model.IncludeVoice = includeVoice.Checked; model.ApplyGains(); UpdateView(); } };

            cableCard = new Card(width, Color.FromArgb(48, 36, 24));
            cableCard.Controls.Add(Theme.Label("Virtual cable needed", Theme.Headline, Theme.Orange, cableCard.Inner));
            cableCard.Controls.Add(Theme.Label("Windows has no BlackHole. Install the free VB-CABLE driver from VB-Audio, then restart OpenYap. It adds “CABLE Input” (OpenYap sends here) and “CABLE Output” (choose it as the microphone in your game or call app). OpenYap never installs drivers itself.", Theme.Caption, Theme.Secondary, cableCard.Inner));
            var cableButton = Theme.Button("Open vb-audio.com/Cable", Theme.Field, Theme.Text);
            cableButton.Click += (s, e) => { try { Process.Start("https://vb-audio.com/Cable/"); } catch (Exception) { } };
            cableCard.Controls.Add(cableButton);
            Add(cableCard);

            bluetoothNote = Note("Your default call microphone is a Bluetooth headset. Apps that use it switch your headphones into low-quality headset mode. Choose your separate microphone as the default communications device; OpenYap will not switch it for you.", Theme.Orange);
            browserNote = Note("", Theme.Secondary);
            monitorNote = Note("Call audio plays normally at its own volume. Adjust it in the call app; OpenYap’s master controls only your music and effects.", Theme.Secondary);
            defaultNote = Note("", Theme.Secondary);

            duckingCard = new Card(width, Color.FromArgb(48, 36, 24));
            duckingCard.Controls.Add(Theme.Label("Windows lowers other sounds during calls", Theme.Headline, Theme.Orange, duckingCard.Inner));
            duckingCard.Controls.Add(Theme.Label("When a call app starts, Windows can turn down your music app, which also lowers the music others hear. In Sound settings → Communications, choose “Do nothing”.", Theme.Caption, Theme.Secondary, duckingCard.Inner));
            var ducking = Theme.Button("Open Communications settings", Theme.Field, Theme.Text);
            ducking.Click += (s, e) => SessionModel.OpenSoundSettings(3);
            duckingCard.Controls.Add(ducking);
            Add(duckingCard);

            setupCard = new Card(width, Color.FromArgb(48, 36, 24));
            setupCard.Controls.Add(Theme.Label("One-time setup", Theme.Headline, Theme.Orange, setupCard.Inner));
            setupCard.Controls.Add(Theme.Label("Your Windows default output is a virtual cable, so everything you hear would also be shared. Make your chosen listening device the normal output first. The cable itself stays installed and unchanged.", Theme.Caption, Theme.Secondary, setupCard.Inner));
            var useHeadphones = Theme.Button("Use headphones for normal audio", Theme.Orange, Color.Black);
            useHeadphones.Click += (s, e) => model.UseSelectedAsNormalOutput();
            setupCard.Controls.Add(useHeadphones);
            Add(setupCard);

            // Mixer: just for you / to others
            var mixer = new Card(width);
            int column = (mixer.Inner - Theme.S(20)) / 2;
            var columns = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0), BackColor = Theme.Card };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, column + Theme.S(20)));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, column));
            var left = Column();
            var right = Column();
            left.Controls.Add(Theme.Label("🎧  JUST FOR YOU", Theme.Small, Theme.Secondary, column));
            musicHear = Strip(left, "Music I hear", column, Theme.Mint, 0);
            gameHear = Strip(left, "Call / game audio I hear", column, Theme.Mint, 3);
            right.Controls.Add(Theme.Label("📣  TO OTHERS", Theme.Small, Theme.Secondary, column));
            musicSend = Strip(right, "Music + effects others hear", column, Theme.Orange, 1);
            maxSharing = Theme.Button("Max sharing", Theme.Field, Theme.Text);
            maxSharing.Margin = new Padding(0, 0, 0, Theme.S(12));
            maxSharing.Click += (s, e) => { model.MaxSharing = !model.MaxSharing; UpdateView(); };
            tips.SetToolTip(maxSharing, "Locks shared music and effects at 100% with +6 dB boost. Mute still works. The receiving app may limit loudness.");
            maxSharing.AccessibleName = "Max sharing, locks music and effects at full volume with boost";
            right.Controls.Add(maxSharing);
            voiceSend = Strip(right, "My voice to others", column, Theme.Orange, 2);
            columns.Controls.Add(left, 0, 0);
            columns.Controls.Add(right, 1, 0);
            mixer.Controls.Add(columns);
            Add(mixer);

            BuildSoundboard();

            var masterCard = new Card(width);
            master = Strip(masterCard, "Master listening volume", masterCard.Inner, Theme.Mint, 4);
            masterCaption = Theme.Label("", Theme.Caption, Theme.Secondary, masterCard.Inner);
            masterCard.Controls.Add(masterCaption);
            Add(masterCard);

            var deviceCard = new Card(width);
            deviceTitle = Theme.Label("Speaker / headphone volume", Theme.Headline, Theme.Text, deviceCard.Inner);
            deviceCard.Controls.Add(deviceTitle);
            deviceSlider = new FlatSlider(deviceCard.Inner, "Speaker / headphone volume");
            deviceSlider.Margin = new Padding(0, 0, 0, Theme.S(6));
            deviceSlider.ValueChanged += ChangeDeviceVolume;
            deviceCard.Controls.Add(deviceSlider);
            deviceCaption = Theme.Label("", Theme.Caption, Theme.Secondary, deviceCard.Inner);
            deviceError = Theme.Label("", Theme.Caption, Theme.Orange, deviceCard.Inner);
            deviceError.Visible = false;
            deviceCard.Controls.Add(deviceCaption);
            deviceCard.Controls.Add(deviceError);
            Add(deviceCard);
            var deviceTimer = new Timer { Interval = 1000 };
            deviceTimer.Tick += (s, e) => RefreshDeviceVolume(false);
            deviceTimer.Start();

            var micRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Width = width, MinimumSize = new Size(width, 0), Margin = new Padding(0, 0, 0, Theme.S(14)) };
            micRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            micRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(70)));
            var micText = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            micFor = Theme.Label("", Theme.Headline, Theme.Text, width - Theme.S(80));
            micForCaption = Theme.Label("Select this in the receiving app’s input settings. Its voice processing may alter music.", Theme.Caption, Theme.Secondary, width - Theme.S(80));
            micText.Controls.Add(micFor);
            micText.Controls.Add(micForCaption);
            sendMeter = new LevelMeter(Theme.S(60)) { Tint = Theme.Orange, Anchor = AnchorStyles.Right };
            micRow.Controls.Add(micText, 0, 0);
            micRow.Controls.Add(sendMeter, 1, 0);
            Add(micRow);

            status = Theme.Label("", Theme.Headline, Theme.Text, width);
            detail = Theme.Label("", Theme.Body, Theme.Secondary, width);
            detail.Margin = new Padding(0, 0, 0, Theme.S(14));
            Add(status);
            Add(detail);

            var buttons = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Width = width, MinimumSize = new Size(width, 0), Margin = new Padding(0, 0, 0, Theme.S(14)) };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            testButton = Theme.Button("✓  Test my setup", Theme.Field, Theme.Text);
            testButton.Click += (s, e) => ShowTest();
            var right2 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Right };
            startButton = Theme.Button("▶  Start sharing music", Theme.Mint, Color.Black);
            startButton.Font = Theme.Headline;
            startButton.Padding = new Padding(Theme.S(18), Theme.S(8), Theme.S(18), Theme.S(8));
            startButton.Margin = new Padding(0);
            startButton.Click += async (s, e) => { await model.Start(); };
            stopButton = Theme.Button("■  Stop sharing", Theme.Red, Color.Black);
            stopButton.Font = Theme.Headline;
            stopButton.Padding = startButton.Padding;
            stopButton.Margin = new Padding(0);
            stopButton.Click += (s, e) => model.Stop();
            right2.Controls.Add(startButton);
            right2.Controls.Add(stopButton);
            buttons.Controls.Add(testButton, 0, 0);
            buttons.Controls.Add(right2, 1, 0);
            Add(buttons);

            BuildAdvanced();
        }

        Picker Row(Card card, string label, int field)
        {
            var l = Theme.Label(label, Theme.Caption, Theme.Secondary, field);
            l.Margin = new Padding(0, 0, 0, Theme.S(2));
            card.Controls.Add(l);
            var picker = new Picker(field, label);
            card.Controls.Add(picker);
            return picker;
        }

        Label Note(string text, Color color)
        {
            var label = Theme.Label(text, Theme.Caption, color, width);
            label.Margin = new Padding(0, 0, 0, Theme.S(10));
            Add(label);
            return label;
        }

        FlowLayoutPanel Column()
        {
            return new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Theme.Card };
        }

        MixerStrip Strip(Control parent, string name, int w, Color tint, int control)
        {
            var strip = new MixerStrip(name, w, tint) { BackColor = parent.BackColor == Theme.Background ? Theme.Card : parent.BackColor };
            strip.Changed += () =>
            {
                if (syncing) return;
                model.Gains[control] = strip.Gain;
                model.Muted[control] = strip.Muted;
                model.ApplyGains();
                UpdateView();
            };
            parent.Controls.Add(strip);
            return strip;
        }

        void BuildSoundboard()
        {
            var card = new Card(width);
            soundboardToggle = Toggle("Soundboard");
            soundboardToggle.Click += (s, e) =>
            {
                soundboardBody.Visible = !soundboardBody.Visible;
                if (soundboardBody.Visible) model.SoundLibrary.Refresh();
                UpdateView();
            };
            card.Controls.Add(soundboardToggle);
            int inner = card.Inner;
            soundboardInner = inner;
            soundboardBody = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Visible = false, Margin = new Padding(0, Theme.S(10), 0, 0), BackColor = Theme.Card };
            var top = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, Theme.S(10)) };
            importButton = Theme.Button("+  Import sounds…", Theme.Field, Theme.Text);
            importButton.Margin = new Padding(0);
            search = new TextBox { Width = inner - Theme.S(150), BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body, Margin = new Padding(0, Theme.S(2), Theme.S(10), 0), AccessibleName = "Find a sound" };
            SetCue(search, "Find a sound…");
            search.TextChanged += (s, e) => UpdateView();
            importButton.Click += async (s, e) =>
            {
                using (var dialog = new OpenFileDialog { Multiselect = true, Title = "Choose audio clips up to 15 seconds each. Files are copied into OpenYap.", Filter = "Audio files|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.flac;*.aif;*.aiff;*.ogg|All files|*.*" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) await model.SoundLibrary.Import(dialog.FileNames);
            };
            top.Controls.Add(search);
            top.Controls.Add(importButton);
            soundboardBody.Controls.Add(top);
            padArea = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = Theme.Card };
            soundboardBody.Controls.Add(padArea);

            // Say something
            var speech = new Card(inner, Theme.Field) { Margin = new Padding(0, Theme.S(6), 0, Theme.S(12)) };
            speech.Controls.Add(Theme.Label("💬  Say something", Theme.Headline, Theme.Text, speech.Inner));
            message = new TextBox { Multiline = true, Height = Theme.S(48), Width = speech.Inner, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body, MaxLength = 400, AccessibleName = "Message to speak", Margin = new Padding(0, 0, 0, Theme.S(8)) };
            message.TextChanged += (s, e) => UpdateView();
            speech.Controls.Add(message);
            var speechRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            voicePicker = new Picker(speech.Inner - Theme.S(270), "Voice") { Margin = new Padding(0, 0, Theme.S(8), 0) };
            stylePicker = new Picker(Theme.S(110), "Style") { Margin = new Padding(0, 0, Theme.S(8), 0) };
            stylePicker.SetItems(new[] { Tuple.Create("0", "Natural"), Tuple.Create("1", "Deep"), Tuple.Create("2", "Tiny") }, "0");
            try
            {
                var voices = SpeechRenderer.Voices();
                voicePicker.SetItems(voices.Select(v => Tuple.Create(v, SpeechRenderer.Describe(v))).ToList(), voices.FirstOrDefault());
            }
            catch (Exception) { }
            playVoice = Theme.Button("▶  Play voice", Theme.Orange, Color.Black);
            playVoice.Margin = new Padding(0);
            playVoice.Click += (s, e) => model.PlaySpeech(message.Text, voicePicker.SelectedId, int.Parse(stylePicker.SelectedId ?? "0"));
            speechRow.Controls.Add(voicePicker);
            speechRow.Controls.Add(stylePicker);
            speechRow.Controls.Add(playVoice);
            speech.Controls.Add(speechRow);
            speechCount = Theme.Label("", Theme.Caption, Theme.Secondary, speech.Inner);
            speechCount.Margin = new Padding(0, Theme.S(8), 0, 0);
            speech.Controls.Add(speechCount);
            soundboardBody.Controls.Add(speech);

            int column = (inner - Theme.S(20)) / 2;
            var levels = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0), BackColor = Theme.Card };
            levels.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, column + Theme.S(20)));
            levels.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, column));
            var effects = Column();
            effectsLabel = Theme.Label("", Theme.Body, Theme.Text, column);
            effectsSlider = new FlatSlider(column - Theme.S(34), "Effects I hear");
            effectsSlider.ValueChanged += () => { model.SoundHear = effectsSlider.Value; model.ApplySoundGains(); UpdateView(); };
            effectsMeter = new LevelMeter(column - Theme.S(34));
            effects.Controls.Add(effectsLabel);
            effects.Controls.Add(effectsSlider);
            effects.Controls.Add(effectsMeter);
            var sendColumn = Column();
            boardSend = Strip(sendColumn, "Music + effects to others", column, Theme.Orange, 1);
            levels.Controls.Add(effects, 0, 0);
            levels.Controls.Add(sendColumn, 1, 0);
            soundboardBody.Controls.Add(levels);

            var statusRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Width = inner, MinimumSize = new Size(inner, 0), Margin = new Padding(0, 0, 0, Theme.S(6)), BackColor = Theme.Card };
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            soundStatus = Theme.Label("", Theme.Caption, Theme.Secondary, inner - Theme.S(140));
            var stopSounds = Theme.Button("■  Stop sounds", Theme.Field, Theme.Orange);
            stopSounds.Margin = new Padding(0);
            stopSounds.Anchor = AnchorStyles.Right;
            stopSounds.Click += (s, e) => { model.StopSounds(); UpdateView(); };
            statusRow.Controls.Add(soundStatus, 0, 0);
            statusRow.Controls.Add(stopSounds, 1, 0);
            soundboardBody.Controls.Add(statusRow);
            showSynthesized = Theme.Check("Show synthesized demo sounds");
            showSynthesized.Font = Theme.Caption;
            showSynthesized.CheckedChanged += (s, e) => { if (!syncing) model.SoundLibrary.ShowSynthesized = showSynthesized.Checked; };
            soundboardBody.Controls.Add(showSynthesized);
            libraryMessage = Theme.Label("", Theme.Caption, Theme.Secondary, inner);
            soundboardBody.Controls.Add(libraryMessage);
            card.Controls.Add(soundboardBody);
            Add(card);
        }

        void BuildAdvanced()
        {
            advancedToggle = Toggle("Advanced & recovery");
            advancedToggle.BackColor = Theme.Background;
            advancedToggle.Font = Theme.Body;
            advancedToggle.ForeColor = Theme.Secondary;
            advancedToggle.Click += (s, e) => { advancedBody.Visible = !advancedBody.Visible; UpdateView(); };
            Add(advancedToggle);
            advancedBody = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Visible = false, Margin = new Padding(0, Theme.S(6), 0, 0) };
            monitorDestination = Theme.Check("Control call/game audio in OpenYap");
            monitorDestination.Checked = true;
            monitorDestination.CheckedChanged += (s, e) => { if (!syncing) { model.MonitorDestination = monitorDestination.Checked; UpdateView(); } };
            advancedBody.Controls.Add(monitorDestination);
            advancedBody.Controls.Add(Theme.Label("Turn this off for recorders or when you prefer to set call volume inside that app. Music and voice sharing still work.", Theme.Caption, Theme.Secondary, width));
            routes = Theme.Label("", Theme.Mono, Theme.Secondary, width);
            advancedBody.Controls.Add(routes);
            advancedBody.Controls.Add(Theme.Label("While sharing, OpenYap sets the music app’s and call app’s own Windows volumes for “I hear” and divides the music app’s volume back out of what it sends, so the two levels stay independent. Windows captures app audio after that volume, so the music app is never set fully silent (−74 dB instead). The original volumes are saved to disk first and put back when you stop, after a crash, or with Restore normal audio. Your default output and the other app’s microphone choice are untouched.", Theme.Caption, Theme.Secondary, width));
            livePeaks = Theme.Label("", Theme.Mono, Theme.Secondary, width);
            buffers = Theme.Label("", Theme.Caption, Theme.Secondary, width);
            advancedBody.Controls.Add(livePeaks);
            advancedBody.Controls.Add(buffers);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, Theme.S(6), 0, 0), MaximumSize = new Size(width, 0) };
            var restore = Theme.Button("Restore normal audio", Theme.Field, Theme.Text);
            restore.Click += (s, e) => model.Restore();
            var privacy = Theme.Button("Microphone privacy", Theme.Field, Theme.Text);
            privacy.Click += (s, e) => SessionModel.OpenPrivacy();
            var sound = Theme.Button("Sound settings", Theme.Field, Theme.Text);
            sound.Click += (s, e) => SessionModel.OpenSoundSettings(0);
            var refresh = Theme.Button("Refresh devices", Theme.Field, Theme.Text);
            refresh.Click += (s, e) => { model.Refresh(); UpdateView(); };
            row.Controls.AddRange(new Control[] { restore, privacy, sound, refresh });
            advancedBody.Controls.Add(row);
            Add(advancedBody);
        }

        Button Toggle(string text)
        {
            var b = new Button
            {
                Text = "▸  " + text, FlatStyle = FlatStyle.Flat, Font = Theme.Headline, ForeColor = Theme.Text, BackColor = Theme.Card,
                AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0), Cursor = Cursors.Hand, Tag = text, UseMnemonic = false,
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = b.BackColor;
            b.FlatAppearance.MouseDownBackColor = b.BackColor;
            return b;
        }

        void UpdateView()
        {
            if (IsDisposed) return;
            syncing = true;
            SuspendLayout();
            try { Sync(); }
            finally { syncing = false; ResumeLayout(); }
        }

        void Sync()
        {
            var m = model;
            bool locked = m.Active || m.Busy;
            pill.Text = m.Interrupted ? "Paused" : m.Active ? "● Live" : "Standby";
            pill.ForeColor = m.Active && !m.Interrupted ? Theme.Mint : Theme.Secondary;
            pill.Invalidate();

            musicPicker.SetItems(m.MusicApps.Select(a => Tuple.Create(a.Id, a.Name)).ToList(), m.MusicApp);
            destinationPicker.SetItems(m.DestinationApps.Select(a => Tuple.Create(a.Id, a.Name)).ToList(), m.DestinationApp);
            var outputs = new List<Tuple<string, string>> { Tuple.Create("", "Choose a device…") };
            outputs.AddRange(m.Outputs.Select(d => Tuple.Create(d.Id, d.Name)));
            outputPicker.SetItems(outputs, m.OutputId);
            var cables = m.VirtualOutputs.Select(d => Tuple.Create(d.Id, d.Name)).ToList();
            if (cables.Count == 0) cables.Add(Tuple.Create("", "No virtual cable found"));
            virtualPicker.SetItems(cables, m.VirtualOutput != null ? m.VirtualId : "");
            var mics = new List<Tuple<string, string>> { Tuple.Create("", "Choose a microphone…") };
            mics.AddRange(m.Microphones.Select(d => Tuple.Create(d.Id, d.Name)));
            micPicker.SetItems(mics, m.MicId);
            foreach (Control c in new Control[] { musicPicker, destinationPicker, outputPicker, virtualPicker, micPicker, includeVoice, monitorDestination })
                c.Enabled = !locked;
            includeVoice.Checked = m.IncludeVoice;
            micPicker.Visible = m.IncludeVoice;
            micPicker.Parent.Controls[micPicker.Parent.Controls.IndexOf(micPicker) - 1].Visible = m.IncludeVoice;

            cableCard.Visible = m.VirtualOutputs.Count == 0;
            bluetoothNote.Visible = m.BluetoothDefaultMic;
            browserNote.Text = "Choose " + m.VirtualInputName + " as the microphone in the receiving app. For web calls, choose the browser and play music in a different app; Windows captures whole apps, not individual tabs.";
            monitorNote.Visible = !m.MonitorDestination;
            var selected = m.SelectedOutput;
            defaultNote.Text = selected != null && m.DefaultOutputName != "" && m.DefaultOutputName != selected.Name && !m.NormalOutputNeedsSetup
                ? "Your music and call apps play through their own output (Windows default: " + m.DefaultOutputName + "). Soundboard effects you hear play through " + selected.Name + "."
                : "";
            defaultNote.Visible = defaultNote.Text != "";
            duckingCard.Visible = m.DuckingActive;
            setupCard.Visible = m.NormalOutputNeedsSetup && !m.Active;

            musicHear.Gain = m.Gains[0]; musicHear.Muted = m.Muted[0];
            gameHear.Gain = m.Gains[3]; gameHear.Muted = m.Muted[3];
            gameHear.Enabled = m.MonitorDestination;
            foreach (var send in new[] { musicSend, boardSend })
            {
                send.Gain = m.Gains[1]; send.Muted = m.Muted[1]; send.Locked = m.MaxSharing;
            }
            voiceSend.Gain = m.Gains[2]; voiceSend.Muted = m.Muted[2];
            voiceSend.Enabled = m.IncludeVoice;
            master.Gain = m.Gains[4]; master.Muted = m.Muted[4];
            musicHear.Meter.Level = m.Meters[0] * m.EffectiveGain(0) * m.EffectiveGain(4);
            gameHear.Meter.Level = 0;
            musicSend.Meter.Level = m.Meters[0] * m.EffectiveGain(1);
            voiceSend.Meter.Level = m.IncludeVoice ? m.Meters[1] * m.EffectiveGain(2) : 0;
            master.Meter.Level = Math.Max(m.Meters[0] * m.EffectiveGain(0), m.Meters[3]) * m.EffectiveGain(4);
            maxSharing.Text = m.MaxSharing ? "🔊  Max sharing · ON" : "🔊  Max sharing";
            maxSharing.BackColor = m.MaxSharing ? Theme.Orange : Theme.Field;
            maxSharing.ForeColor = m.MaxSharing ? Color.Black : Theme.Text;
            masterCaption.Text = m.MonitorDestination
                ? "Controls your music + call/game + effects. Never changes what others hear."
                : "Controls your music and effects only. Call audio stays controlled by the call app.";

            // Soundboard
            soundboardToggle.Text = (soundboardBody.Visible ? "▾  " : "▸  ") + "Soundboard";
            if (soundboardBody.Visible) SyncPads();
            effectsLabel.Text = "Effects I hear · " + (int)(m.SoundHear * 100) + "%";
            if (!effectsSlider.Dragging) effectsSlider.Value = m.SoundHear;
            effectsMeter.Level = m.SoundMeters[0];
            boardSend.Meter.Level = m.SoundMeters[1];
            soundStatus.Text = m.Active ? m.SoundStatus : "Start sharing to play sounds to your selected device and the virtual microphone.";
            importButton.Enabled = !m.SoundLibrary.Importing;
            showSynthesized.Checked = m.SoundLibrary.ShowSynthesized;
            libraryMessage.Text = m.SoundLibrary.Message;
            int length = message.Text.Length;
            speechCount.Text = length + "/220 characters · Up to 15 seconds. Uses the effects listening level and shared music + effects volume, mute, and Max. Stop sounds stops speech too.";
            playVoice.Enabled = m.Active && !m.Busy && !m.Interrupted && voicePicker.SelectedId != null && message.Text.Trim().Length > 0 && length <= 220;

            var cable = m.VirtualOutput;
            var device = m.SelectedOutput;
            micFor.Text = "Microphone for " + m.DestinationName + ": " + m.VirtualInputName;
            sendMeter.Level = m.Meters[4];
            if (device == null || device.Id != deviceIdShown) RefreshDeviceVolume(true);

            status.Text = m.Status;
            detail.Text = m.Detail;
            startButton.Visible = !(m.Active || m.Busy);
            stopButton.Visible = m.Active || m.Busy;
            stopButton.Text = m.Busy ? "■  Cancel connection" : "■  Stop sharing";
            bool startable = !m.Busy && !m.NormalOutputNeedsSetup && cable != null;
            startButton.Enabled = startable;
            startButton.BackColor = startable ? Theme.Mint : Color.FromArgb(40, 90, 70);

            advancedToggle.Text = (advancedBody.Visible ? "▾  " : "▸  ") + "Advanced & recovery";
            monitorDestination.Checked = m.MonitorDestination;
            routes.Text = "Music → " + m.AppName(m.MusicApp) + "’s own volume (you) + " + (cable != null ? cable.Name : "virtual cable") + " (others)\n" +
                "Selected microphone → virtual cable only\nSoundboard → listening device + virtual cable\n" +
                m.DestinationName + " → " + (m.MonitorDestination ? "its own volume, set by OpenYap; never captured" : "normal app playback; untouched");
            livePeaks.Text = string.Format("Live peaks: music {0:0.000} · mic {1:0.000} · effects {2:0.000} · send {3:0.000} · return {4:0.000}", m.Meters[0], m.Meters[1], m.Meters[3], m.Meters[4], m.Meters[5]);
            buffers.Text = "Separate clock buffers • adaptive drift correction • −0.18 dBFS ceiling • buffer recoveries: " + m.Dropouts +
                (m.RecoveryPending ? " • app volumes waiting to be restored" : "");
            if (testForm != null && !testForm.IsDisposed) testForm.UpdateView();
        }

        void SyncPads()
        {
            string query = search.Text.Trim();
            var clips = model.SoundLibrary.Clips.Where(c => query.Length == 0 || c.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            string signature = string.Join("|", clips.Select(c => c.Id));
            if (signature != padSignature)
            {
                padSignature = signature;
                padArea.SuspendLayout();
                foreach (Control c in padArea.Controls.Cast<Control>().ToList()) c.Dispose();
                padArea.Controls.Clear();
                pads.Clear();
                int inner = soundboardInner;
                int padWidth = (inner - Theme.S(16)) / 3 - 2;
                foreach (var reaction in SoundClip.Reactions)
                {
                    var group = clips.Where(c => c.Reaction == reaction).ToList();
                    if (group.Count == 0) continue;
                    var heading = Theme.Label(reaction, Theme.Small, Theme.Secondary, inner);
                    heading.BackColor = Theme.Card;
                    heading.AccessibleRole = AccessibleRole.StaticText;
                    padArea.Controls.Add(heading);
                    var grid = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(inner + Theme.S(8) + 6, 0), Margin = new Padding(0, 0, 0, Theme.S(8)), BackColor = Theme.Card };
                    foreach (var clip in group)
                    {
                        var c = clip;
                        var pad = new Button
                        {
                            Text = c.Name, Width = padWidth, Height = Theme.S(42), FlatStyle = FlatStyle.Flat, BackColor = Theme.Field, ForeColor = Theme.Text,
                            Font = Theme.Body, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, Theme.S(8), Theme.S(8)),
                            Padding = new Padding(Theme.S(6), 0, Theme.S(4), 0), Cursor = Cursors.Hand, UseMnemonic = false,
                            AccessibleName = "Play " + c.Name + " to me and others",
                        };
                        pad.FlatAppearance.BorderSize = 0;
                        pad.Click += (s, e) => { model.PlaySound(c); UpdateView(); };
                        if (c.Path != null)
                        {
                            var menu = new ContextMenuStrip();
                            menu.Items.Add("Remove sound", null, (s, e) => model.SoundLibrary.Remove(c));
                            pad.ContextMenuStrip = menu;
                        }
                        grid.Controls.Add(pad);
                        pads[c.Id] = pad;
                    }
                    padArea.Controls.Add(grid);
                }
                if (clips.Count == 0)
                {
                    var empty = Theme.Label(model.SoundLibrary.Clips.Count == 0
                        ? "No sounds yet. Import your own clips, or show the synthesized demo sounds below."
                        : "No sounds found. Try another search or import a clip.", Theme.Body, Theme.Secondary, inner);
                    empty.BackColor = Theme.Card;
                    padArea.Controls.Add(empty);
                }
                padArea.ResumeLayout();
            }
            // Pads stay focusable and readable while idle; PlaySound ignores clicks until sharing starts.
            bool enabled = model.Active && !model.Interrupted && !model.Busy;
            foreach (var pair in pads)
            {
                bool playing = model.PlayingSoundId == pair.Key;
                pair.Value.BackColor = playing ? Color.FromArgb(96, 64, 30) : Theme.Field;
                pair.Value.ForeColor = enabled ? Theme.Text : Theme.Secondary;
                pair.Value.Cursor = enabled ? Cursors.Hand : Cursors.Default;
                pair.Value.AccessibleDescription = enabled ? null : "Available after sharing starts";
            }
        }

        void RefreshDeviceVolume(bool force)
        {
            var device = model.SelectedOutput;
            deviceIdShown = device != null ? device.Id : null;
            if (deviceSlider.Dragging && !force) return;
            float? volume = device != null ? Hardware.OutputVolume(device) : null;
            deviceSlider.Visible = volume.HasValue;
            if (volume.HasValue)
            {
                deviceSlider.Value = volume.Value;
                deviceTitle.Text = "Speaker / headphone volume · " + (int)Math.Round(volume.Value * 100) + "%";
                deviceCaption.Text = device.Name + " · Changes all sound you hear on this device, never the sharing mix. Your adjustment stays after sharing stops.";
            }
            else
            {
                deviceTitle.Text = "Speaker / headphone volume";
                deviceCaption.Text = device == null ? "Choose a listening device." : "Use the volume buttons on " + device.Name + "; a writable device volume is unavailable.";
            }
        }

        void ChangeDeviceVolume()
        {
            var device = model.SelectedOutput;
            try
            {
                Hardware.SetOutputVolume(device, deviceSlider.Value);
                deviceTitle.Text = "Speaker / headphone volume · " + (int)Math.Round(deviceSlider.Value * 100) + "%";
                deviceError.Text = "";
                deviceError.Visible = false;
            }
            catch (Exception)
            {
                deviceError.Text = "Could not change device volume. Check that your listening device is connected.";
                deviceError.Visible = true;
                RefreshDeviceVolume(true);
            }
        }

        public void SaveSnapshot(string path, bool expand)
        {
            if (expand)
            {
                soundboardBody.Visible = advancedBody.Visible = true;
                model.SoundLibrary.ShowSynthesized = true;
                UpdateView();
                content.PerformLayout();
                Application.DoEvents();
            }
            var size = content.PreferredSize;
            using (var bitmap = new Bitmap(size.Width + Theme.S(48), size.Height + Theme.S(20)))
            {
                using (var g = Graphics.FromImage(bitmap)) g.Clear(Theme.Background);
                content.DrawToBitmap(bitmap, new Rectangle(Theme.S(24), Theme.S(20), size.Width, size.Height));
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        void ShowTest()
        {
            model.TestSetup();
            if (testForm == null || testForm.IsDisposed) testForm = new SetupTestForm(model);
            testForm.UpdateView();
            testForm.Show(this);
            testForm.Activate();
        }

        void PaintPill(object sender, PaintEventArgs e)
        {
            var label = (Label)sender;
            e.Graphics.Clear(Theme.Background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(Theme.Field))
            using (var path = Theme.Rounded(new RectangleF(0, 0, label.Width - 1, label.Height - 1), label.Height / 2f))
                e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(e.Graphics, label.Text, label.Font, label.ClientRectangle, label.ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_POWERBROADCAST = 0x218, PBT_APMSUSPEND = 4;
            if (m.Msg == WM_POWERBROADCAST && m.WParam.ToInt32() == PBT_APMSUSPEND) model.PauseForSleep();
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.OemPeriod)) { model.Stop(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        static void SetCue(TextBox box, string cue)
        {
            box.HandleCreated += (s, e) => SendMessage(box.Handle, 0x1501, (IntPtr)1, cue);
        }

        void DarkTitleBar()
        {
            HandleCreated += (s, e) =>
            {
                int on = 1;
                try { DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch (Exception) { }
            };
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
