// VoiceEn: atalho global no Windows -> grava o microfone -> traduz PT->EN (Whisper no WSL)
// -> cola o texto na janela em foco. Compilado com o csc.exe do .NET Framework (C# 5).
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VoiceEn
{
    static class Config
    {
        public const string Distro = "@DISTRO@";
        public const string Python = "@ROOT@/.venv/bin/python";
        public const string Server = "@ROOT@/server.py";
        public const int MaxSeconds = 120;
    }

    static class Log
    {
        static readonly object gate = new object();
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceEn");

        public static void Write(string message)
        {
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    File.AppendAllText(Path.Combine(Dir, "voice-en.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
                }
                catch (IOException) { }
            }
        }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "VoiceEn";

        public static bool Enabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && key.GetValue(ValueName) != null;
            }
            set
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(ValueName, false);
                }
            }
        }
    }

    // Atalho global escolhido pelo usuario, salvo em %LOCALAPPDATA%\VoiceEn\hotkey.txt (ex.: "Ctrl+Alt+Space").
    class Hotkey
    {
        public bool Ctrl, Alt, Shift;
        public Keys Key;

        static string FilePath { get { return Path.Combine(Log.Dir, "hotkey.txt"); } }

        public static Hotkey Default()
        {
            Hotkey hk = new Hotkey();
            hk.Ctrl = true; hk.Alt = true; hk.Key = Keys.Space;
            return hk;
        }

        public static Hotkey FromKeyData(Keys keyData)
        {
            Hotkey hk = new Hotkey();
            hk.Ctrl = (keyData & Keys.Control) != 0;
            hk.Alt = (keyData & Keys.Alt) != 0;
            hk.Shift = (keyData & Keys.Shift) != 0;
            hk.Key = keyData & Keys.KeyCode;
            return hk;
        }

        public uint Modifiers { get { return (Alt ? 1u : 0u) | (Ctrl ? 2u : 0u) | (Shift ? 4u : 0u); } }

        public bool IsModifierOnly
        {
            get
            {
                return Key == Keys.None || Key == Keys.ControlKey || Key == Keys.Menu || Key == Keys.ShiftKey
                    || Key == Keys.LWin || Key == Keys.RWin;
            }
        }

        // Tecla sozinha so vale se for de funcao; senao o atalho sequestraria a digitacao normal.
        public bool IsValid
        {
            get
            {
                if (IsModifierOnly) return false;
                bool functionKey = (Key >= Keys.F1 && Key <= Keys.F24) || Key == Keys.Pause || Key == Keys.Scroll;
                return functionKey || Ctrl || Alt;
            }
        }

        public override string ToString()
        {
            string name = Key.ToString();
            if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) name = name.Substring(1);
            return (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + name;
        }

        public static bool TryParse(string text, out Hotkey hotkey)
        {
            hotkey = new Hotkey();
            foreach (string raw in text.Trim().Split('+'))
            {
                string part = raw.Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) hotkey.Ctrl = true;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) hotkey.Alt = true;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) hotkey.Shift = true;
                else
                {
                    if (part.Length == 1 && char.IsDigit(part[0])) part = "D" + part;
                    Keys key;
                    if (!Enum.TryParse(part, true, out key)) return false;
                    hotkey.Key = key;
                }
            }
            return hotkey.IsValid;
        }

        public static Hotkey Load()
        {
            try
            {
                Hotkey hotkey;
                if (File.Exists(FilePath) && TryParse(File.ReadAllText(FilePath), out hotkey)) return hotkey;
            }
            catch (IOException) { }
            return Default();
        }

        public void Save()
        {
            Directory.CreateDirectory(Log.Dir);
            File.WriteAllText(FilePath, ToString());
        }
    }

    // Janela "aperte a combinacao": captura qualquer tecla via ProcessCmdKey (inclui combos com Alt).
    class HotkeyDialog : Form
    {
        readonly Label label = new Label();
        readonly Button ok = new Button();
        readonly string current;
        public Hotkey Result;

        public HotkeyDialog(Hotkey current)
        {
            this.current = current.ToString();
            Text = "VoiceEn - escolher atalho";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(420, 170);

            label.SetBounds(16, 12, 388, 100);
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Font = new Font("Segoe UI", 11f);
            Controls.Add(label);

            ok.Text = "Salvar";
            ok.Enabled = false;
            ok.SetBounds(228, 124, 84, 30);
            ok.Click += delegate { DialogResult = DialogResult.OK; };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Cancelar";
            cancel.SetBounds(320, 124, 84, 30);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(cancel);

            ShowText("Aperte a nova combinacao de teclas");
        }

        void ShowText(string headline)
        {
            label.Text = headline + "\n\nAtual: " + current + "\nEnter salva, Esc cancela";
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            if (keyData == Keys.Enter) { if (Result != null) DialogResult = DialogResult.OK; return true; }

            Hotkey candidate = Hotkey.FromKeyData(keyData);
            if (candidate.IsModifierOnly) return true;
            if (candidate.IsValid)
            {
                Result = candidate;
                ok.Enabled = true;
                ShowText("Novo atalho: " + candidate);
            }
            else
            {
                Result = null;
                ok.Enabled = false;
                ShowText("Use Ctrl ou Alt junto (ou uma tecla F1-F24)");
            }
            return true;
        }
    }

    // Aviso flutuante que nunca rouba o foco da janela onde o texto sera colado.
    class Overlay : Form
    {
        readonly Label label = new Label();

        public Overlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(32, 32, 32);
            Size = new Size(460, 44);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            Controls.Add(label);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008; // NOACTIVATE | TOOLWINDOW | TOPMOST
                return cp;
            }
        }

        public void ShowMessage(string text, Color color)
        {
            label.Text = text;
            label.ForeColor = color;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 24);
            if (!Visible) Show();
            Update();
        }
    }

    class App : Form
    {
        enum State { Idle, Recording, Translating }

        const int WM_HOTKEY = 0x0312;
        const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string command, StringBuilder ret, int retLen, IntPtr callback);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern bool mciGetErrorString(int err, StringBuilder text, int len);
        [DllImport("user32.dll")]
        static extern IntPtr GetOpenClipboardWindow();
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        readonly Overlay overlay = new Overlay();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer maxTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer restoreTimer = new System.Windows.Forms.Timer();
        readonly Icon iconIdle = MakeIcon(Color.Gray);
        readonly Icon iconReady = MakeIcon(Color.MediumSeaGreen);
        readonly Icon iconRec = MakeIcon(Color.Crimson);
        readonly Icon iconBusy = MakeIcon(Color.Goldenrod);
        readonly string wavPath = Path.Combine(Path.GetTempPath(),
            "voice-en-" + Process.GetCurrentProcess().Id + ".wav");
        readonly string selfTestWav;
        readonly ToolStripItem menuHeader;

        Hotkey hotkey = Hotkey.Load();
        Process server;
        State state = State.Idle;
        bool ready;
        bool exiting;
        string lastText = "";
        DataObject clipboardBackup;
        Stopwatch translateClock;

        public App(string selfTestWav)
        {
            this.selfTestWav = selfTestWav;
            IntPtr handle = Handle; // forca a criacao da janela oculta (hotkey + BeginInvoke)

            tray.Icon = iconIdle;
            tray.Text = "VoiceEn: carregando modelo...";
            ContextMenuStrip menu = new ContextMenuStrip();
            menuHeader = menu.Items.Add("VoiceEn  (" + hotkey + ")");
            menuHeader.Enabled = false;
            menu.Items.Add("Mudar atalho...", null, delegate { ChangeHotkey(); });
            menu.Items.Add("Copiar ultima traducao", null, delegate { CopyLast(); });
            ToolStripMenuItem autostart = new ToolStripMenuItem("Iniciar com o Windows");
            autostart.Checked = Autostart.Enabled;
            autostart.Click += delegate { Autostart.Enabled = !Autostart.Enabled; autostart.Checked = Autostart.Enabled; };
            menu.Items.Add(autostart);
            menu.Items.Add("Sair", null, delegate { Quit(); });
            tray.ContextMenuStrip = menu;
            tray.Visible = selfTestWav == null;

            hideTimer.Tick += delegate { hideTimer.Stop(); overlay.Hide(); };
            restoreTimer.Interval = 800; // tempo para a janela em foco consumir o Ctrl+V
            restoreTimer.Tick += delegate { restoreTimer.Stop(); RestoreClipboard(); };
            maxTimer.Interval = Config.MaxSeconds * 1000;
            maxTimer.Tick += delegate { if (state == State.Recording) StopRecording(); };

            if (selfTestWav == null)
            {
                bool ok = Register(hotkey);
                Log.Write(ok ? "atalho registrado: " + hotkey
                             : "FALHA ao registrar o atalho " + hotkey + " (em uso por outro programa?)");
                if (!ok) Flash("VoiceEn: atalho " + hotkey + " ja esta em uso; troque no menu da bandeja", Color.Salmon, 5000);
            }
            StartServer();
        }

        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) OnHotkey();
            base.WndProc(ref m);
        }

        bool Register(Hotkey hk)
        {
            return RegisterHotKey(Handle, 1, hk.Modifiers | MOD_NOREPEAT, (uint)hk.Key);
        }

        void ChangeHotkey()
        {
            if (state != State.Idle) return;
            UnregisterHotKey(Handle, 1); // senao apertar o atalho atual na janela dispararia a gravacao
            using (HotkeyDialog dialog = new HotkeyDialog(hotkey))
            {
                if (dialog.ShowDialog() == DialogResult.OK && dialog.Result != null)
                {
                    if (Register(dialog.Result))
                    {
                        hotkey = dialog.Result;
                        hotkey.Save();
                        menuHeader.Text = "VoiceEn  (" + hotkey + ")";
                        if (ready) tray.Text = "VoiceEn pronto: " + hotkey;
                        Log.Write("atalho alterado: " + hotkey);
                        Flash("Novo atalho: " + hotkey, Color.MediumSeaGreen, 2500);
                        return;
                    }
                    Log.Write("atalho recusado pelo Windows: " + dialog.Result);
                    Flash(dialog.Result + " ja esta em uso por outro programa", Color.Salmon, 4000);
                }
            }
            Register(hotkey);
        }

        static Icon MakeIcon(Color color)
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            using (Brush brush = new SolidBrush(color))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                g.FillEllipse(brush, 1, 1, 14, 14);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        void Flash(string text, Color color, int ms)
        {
            overlay.ShowMessage(text, color);
            hideTimer.Stop();
            if (ms > 0) { hideTimer.Interval = ms; hideTimer.Start(); }
        }

        // ---- servidor de traducao (WSL) ----

        void StartServer()
        {
            ProcessStartInfo psi = new ProcessStartInfo("wsl.exe",
                "-d " + Config.Distro + " -e " + Config.Python + " -u " + Config.Server);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            server = Process.Start(psi);
            Log.Write("servidor iniciado (pid " + server.Id + ")");

            Thread stdout = new Thread(delegate()
            {
                string line;
                while ((line = server.StandardOutput.ReadLine()) != null)
                {
                    string captured = line;
                    BeginInvoke((MethodInvoker)delegate { OnServerLine(captured); });
                }
                if (!exiting) BeginInvoke((MethodInvoker)delegate { OnServerExit(); });
            });
            stdout.IsBackground = true;
            stdout.Start();

            Thread stderr = new Thread(delegate()
            {
                string line;
                while ((line = server.StandardError.ReadLine()) != null) Log.Write("[servidor] " + line);
            });
            stderr.IsBackground = true;
            stderr.Start();
        }

        void OnServerLine(string line)
        {
            if (line == "READY")
            {
                ready = true;
                tray.Icon = iconReady;
                tray.Text = "VoiceEn pronto: " + hotkey;
                Log.Write("servidor de traducao pronto");
                if (selfTestWav != null) SendToServer(selfTestWav);
                else Flash("VoiceEn pronto  -  " + hotkey + " para falar", Color.MediumSeaGreen, 2500);
                return;
            }
            if (state != State.Translating) { Log.Write("linha inesperada: " + line); return; }

            state = State.Idle;
            tray.Icon = iconReady;
            string elapsed = (translateClock.ElapsedMilliseconds / 1000.0).ToString("0.0") + "s";
            if (line.StartsWith("OK\t"))
            {
                string text = line.Substring(3).Trim();
                Log.Write("traduzido em " + elapsed + ": " + text);
                if (selfTestWav != null) { Quit(); return; }
                if (text.Length == 0) { Flash("Nenhuma fala detectada", Color.Khaki, 2000); return; }
                lastText = text;
                Paste(text);
                overlay.Hide();
            }
            else
            {
                Log.Write("erro do servidor: " + line);
                if (selfTestWav != null) { Quit(); return; }
                Flash("Erro na traducao (veja o log)", Color.Salmon, 3000);
            }
        }

        void OnServerExit()
        {
            ready = false;
            state = State.Idle;
            tray.Icon = iconIdle;
            tray.Text = "VoiceEn: servidor parou";
            Log.Write("servidor encerrou inesperadamente");
            if (selfTestWav != null) { Quit(); return; }
            Flash("VoiceEn: o servidor de traducao parou (veja o log)", Color.Salmon, 5000);
        }

        void SendToServer(string windowsPath)
        {
            string full = Path.GetFullPath(windowsPath);
            string linux = "/mnt/" + char.ToLowerInvariant(full[0]) + full.Substring(2).Replace('\\', '/');
            state = State.Translating;
            translateClock = Stopwatch.StartNew();
            server.StandardInput.WriteLine(linux);
            server.StandardInput.Flush();
        }

        // ---- gravacao (MCI) ----

        bool Mci(string command)
        {
            int err = mciSendString(command, null, 0, IntPtr.Zero);
            if (err == 0) return true;
            StringBuilder text = new StringBuilder(256);
            mciGetErrorString(err, text, text.Capacity);
            Log.Write("MCI '" + command + "': " + text);
            return false;
        }

        void OnHotkey()
        {
            if (state == State.Translating) return;
            if (state == State.Recording) { StopRecording(); return; }
            if (!ready) { Flash("VoiceEn ainda carregando o modelo...", Color.Khaki, 2000); return; }
            StartRecording();
        }

        void StartRecording()
        {
            mciSendString("close voiceen", null, 0, IntPtr.Zero); // sobra de uma gravacao interrompida
            bool ok = Mci("open new type waveaudio alias voiceen")
                && Mci("set voiceen time format ms bitspersample 16 channels 1 samplespersec 16000 bytespersec 32000 alignment 2")
                && Mci("record voiceen");
            if (!ok)
            {
                mciSendString("close voiceen", null, 0, IntPtr.Zero);
                Flash("Nao consegui abrir o microfone (veja o log)", Color.Salmon, 3000);
                return;
            }
            state = State.Recording;
            tray.Icon = iconRec;
            maxTimer.Start();
            Flash("●  Gravando...  " + hotkey + " para terminar", Color.Tomato, 0);
        }

        void StopRecording()
        {
            maxTimer.Stop();
            bool ok = Mci("stop voiceen") && Mci("save voiceen \"" + wavPath + "\"");
            mciSendString("close voiceen", null, 0, IntPtr.Zero);
            state = State.Idle;
            tray.Icon = iconReady;
            if (!ok) { Flash("Falha ao salvar a gravacao (veja o log)", Color.Salmon, 3000); return; }

            // menos de ~0,5 s de audio (16 kHz, 16 bit): nada a traduzir
            if (new FileInfo(wavPath).Length < 16000) { Flash("Gravacao muito curta", Color.Khaki, 1500); return; }

            tray.Icon = iconBusy;
            Flash("Traduzindo para ingles...", Color.Gold, 0);
            SendToServer(wavPath);
        }

        // ---- saida ----

        // Cola via area de transferencia e depois devolve o que o usuario tinha copiado antes.
        void Paste(string text)
        {
            restoreTimer.Stop();
            DataObject backup = clipboardBackup ?? BackupClipboard();
            if (!PutOnClipboard(text))
            {
                clipboardBackup = null;
                Flash("Nao consegui copiar; use Copiar ultima traducao", Color.Salmon, 4000);
                return;
            }
            try { SendKeys.SendWait("^v"); }
            catch (Exception err)
            {
                clipboardBackup = null;
                Log.Write("falha ao colar: " + err.Message);
                Flash("Texto copiado; cole com Ctrl+V", Color.Khaki, 3000);
                return;
            }
            clipboardBackup = backup;
            if (backup != null) restoreTimer.Start();
        }

        // Outro programa pode estar com a area de transferencia aberta (historico do Win+V,
        // terminal); o SetDataObject as vezes lanca excecao depois de ja ter gravado o texto,
        // entao confere o conteudo antes de desistir de colar.
        static bool PutOnClipboard(string text)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { Clipboard.SetDataObject(text, true, 10, 100); return true; }
                catch (Exception err)
                {
                    if (ClipboardHas(text))
                    {
                        Log.Write("area de transferencia reclamou mas recebeu o texto: " + err.Message);
                        return true;
                    }
                    Log.Write("area de transferencia ocupada por " + ClipboardHolder() + ": " + err.Message);
                }
            }
            return false;
        }

        static bool ClipboardHas(string text)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { return Clipboard.ContainsText() && Clipboard.GetText() == text; }
                catch (Exception) { Thread.Sleep(100); }
            }
            return false;
        }

        static string ClipboardHolder()
        {
            try
            {
                IntPtr hwnd = GetOpenClipboardWindow();
                if (hwnd == IntPtr.Zero) return "ninguem";
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                return Process.GetProcessById((int)pid).ProcessName + " (pid " + pid + ")";
            }
            catch (Exception) { return "?"; }
        }

        static DataObject BackupClipboard()
        {
            try
            {
                IDataObject current = Clipboard.GetDataObject();
                if (current == null) return null;
                DataObject copy = new DataObject();
                bool any = false;
                foreach (string format in current.GetFormats(false))
                {
                    try
                    {
                        object data = current.GetData(format, false);
                        if (data != null) { copy.SetData(format, data); any = true; }
                    }
                    catch (Exception) { } // formato que o dono da area de transferencia nao entrega mais
                }
                return any ? copy : null;
            }
            catch (Exception) { return null; }
        }

        void RestoreClipboard()
        {
            DataObject backup = clipboardBackup;
            clipboardBackup = null;
            if (backup == null) return;
            try { Clipboard.SetDataObject(backup, true, 10, 100); }
            catch (Exception err) { Log.Write("falha ao restaurar a area de transferencia: " + err.Message); }
        }

        void CopyLast()
        {
            if (lastText.Length == 0) { Flash("Ainda nao ha traducao nesta sessao", Color.Khaki, 2000); return; }
            restoreTimer.Stop();
            clipboardBackup = null;
            try { Clipboard.SetDataObject(lastText, true, 10, 100); Flash("Ultima traducao copiada", Color.MediumSeaGreen, 1500); }
            catch (Exception err) { Log.Write("falha ao copiar: " + err.Message); }
        }

        void Quit()
        {
            exiting = true;
            UnregisterHotKey(Handle, 1);
            tray.Visible = false;
            try { server.StandardInput.Close(); if (!server.WaitForExit(2000)) server.Kill(); }
            catch (Exception) { }
            try { File.Delete(wavPath); } catch (IOException) { }
            Application.Exit();
        }

        [STAThread]
        static void Main(string[] args)
        {
            string selfTest = args.Length == 2 && args[0] == "--selftest" ? args[1] : null;
            bool created = true;
            Mutex mutex = selfTest == null ? new Mutex(true, "VoiceEn.SingleInstance", out created) : null;
            if (!created) { Log.Write("ja existe uma instancia em execucao"); return; }

            Application.EnableVisualStyles();
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Write("excecao: " + e.Exception); };
            Log.Write(selfTest == null ? "iniciando" : "selftest: " + selfTest);
            App app = new App(selfTest);
            Application.Run();
            GC.KeepAlive(mutex);
            GC.KeepAlive(app);
        }
    }
}
