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

        // O log guarda cada texto ditado; acima de 1 MB vira voice-en.old.log (uma geracao so).
        public static void Rotate()
        {
            try
            {
                string current = Path.Combine(Dir, "voice-en.log");
                if (!File.Exists(current) || new FileInfo(current).Length < 1024 * 1024) return;
                string old = Path.Combine(Dir, "voice-en.old.log");
                File.Delete(old);
                File.Move(current, old);
            }
            catch (IOException) { }
        }

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

    // Idioma falado e idioma do texto, salvo em %LOCALAPPDATA%\VoiceEn\mode.txt.
    // Os codigos sao os do server.py (translate.MODES).
    static class Mode
    {
        public static readonly string[] Codes = { "pt-en", "pt-pt", "en-en" };
        public const string Default = "pt-en";

        static string FilePath { get { return Path.Combine(Log.Dir, "mode.txt"); } }

        public static string Label(string code)
        {
            switch (code)
            {
                case "pt-pt": return "Portugues -> Portugues";
                case "en-en": return "Ingles -> Ingles";
                default: return "Portugues -> Ingles";
            }
        }

        public static string Short(string code) { return code.ToUpperInvariant().Replace("-", " > "); }

        public static string Load()
        {
            try
            {
                string code = File.ReadAllText(FilePath).Trim();
                if (Array.IndexOf(Codes, code) >= 0) return code;
            }
            catch (Exception) { }
            return Default;
        }

        public static void Save(string code)
        {
            try { Directory.CreateDirectory(Log.Dir); File.WriteAllText(FilePath, code); }
            catch (Exception err) { Log.Write("falha ao salvar o modo: " + err.Message); }
        }
    }

    // Como o texto chega na janela: digitado (padrao) ou colado com Ctrl+V. Salvo em
    // %LOCALAPPDATA%\VoiceEn\output.txt ("digitar" ou "colar").
    static class Output
    {
        static string FilePath { get { return Path.Combine(Log.Dir, "output.txt"); } }

        public static bool Paste
        {
            get
            {
                try { return File.ReadAllText(FilePath).Trim() == "colar"; }
                catch (Exception) { return false; }
            }
            set
            {
                try { Directory.CreateDirectory(Log.Dir); File.WriteAllText(FilePath, value ? "colar" : "digitar"); }
                catch (Exception err) { Log.Write("falha ao salvar a saida: " + err.Message); }
            }
        }
    }

    // Correcao da fala (desligada por padrao): o servidor tira hesitacoes e repeticoes e conserta
    // palavras mal reconhecidas. Vale para PT > EN e PT > PT e exige a chave da Groq. Salvo em
    // %LOCALAPPDATA%\VoiceEn\correct.txt ("sim" ou "nao").
    static class Correction
    {
        static string FilePath { get { return Path.Combine(Log.Dir, "correct.txt"); } }

        public static bool AppliesTo(string mode) { return mode == "pt-en" || mode == "pt-pt"; }

        public static bool Enabled
        {
            get
            {
                try { return File.ReadAllText(FilePath).Trim() == "sim"; }
                catch (Exception) { return false; }
            }
            set
            {
                try { Directory.CreateDirectory(Log.Dir); File.WriteAllText(FilePath, value ? "sim" : "nao"); }
                catch (Exception err) { Log.Write("falha ao salvar a correcao: " + err.Message); }
            }
        }
    }

    // Teclado sintetico via SendInput: digitar texto (KEYEVENTF_UNICODE) ou mandar Ctrl+V.
    static class Keyboard
    {
        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_UNICODE = 0x0004;
        const ushort VK_CONTROL = 0x11;
        const ushort VK_V = 0x56;
        static readonly int[] Modifiers = { 0x10, 0x11, 0x12, 0x5B, 0x5C }; // Shift, Ctrl, Alt, Win esq./dir.

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        // a uniao precisa do MOUSEINPUT (o maior membro) para o INPUT ter o tamanho que o Windows espera
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion u; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vk);

        static INPUT Key(ushort vk, char ch, uint flags)
        {
            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.u.ki.wVk = vk;
            input.u.ki.wScan = ch;
            input.u.ki.dwFlags = flags;
            return input;
        }

        static bool Send(INPUT[] inputs)
        {
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (sent == inputs.Length) return true;
            Log.Write("SendInput enviou " + sent + " de " + inputs.Length + " eventos (erro " + Marshal.GetLastWin32Error() + ")");
            return false;
        }

        // Com um atalho como Ctrl+Alt+Espaco e uma traducao rapida, o usuario ainda pode estar com
        // Ctrl/Alt apertados; o texto digitado viraria uma serie de atalhos na janela.
        public static void WaitForModifiersUp(int timeoutMs)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeoutMs)
            {
                bool down = false;
                foreach (int vk in Modifiers) down |= (GetAsyncKeyState(vk) & 0x8000) != 0;
                if (!down) return;
                Thread.Sleep(20);
            }
        }

        // Digitado, o texto nao passa pelo "colar" do programa em foco: o Claude Code, por exemplo, troca
        // uma colagem de mais de 800 caracteres por "[Pasted text #N]". Os lotes pequenos com pausa evitam
        // que o terminal entregue tudo num bloco so.
        // Devolve quantos caracteres foram digitados (text.Length quando tudo foi).
        public static int Type(string text)
        {
            const int batch = 32;
            int count;
            for (int start = 0; start < text.Length; start += count)
            {
                count = Math.Min(batch, text.Length - start);
                if (count < text.Length - start && char.IsHighSurrogate(text[start + count - 1])) count++; // nao parte um emoji
                INPUT[] inputs = new INPUT[count * 2];
                for (int i = 0; i < count; i++)
                {
                    char ch = text[start + i];
                    inputs[2 * i] = Key(0, ch, KEYEVENTF_UNICODE);
                    inputs[2 * i + 1] = Key(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
                }
                if (!Send(inputs)) return start;
                Thread.Sleep(8);
            }
            return text.Length;
        }

        public static bool CtrlV()
        {
            return Send(new INPUT[] {
                Key(VK_CONTROL, '\0', 0), Key(VK_V, '\0', 0),
                Key(VK_V, '\0', KEYEVENTF_KEYUP), Key(VK_CONTROL, '\0', KEYEVENTF_KEYUP) });
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
        readonly System.Windows.Forms.Timer restartTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer watchdog = new System.Windows.Forms.Timer();
        readonly Icon iconIdle = MakeIcon(Color.Gray);
        readonly Icon iconReady = MakeIcon(Color.MediumSeaGreen);
        readonly Icon iconRec = MakeIcon(Color.Crimson);
        readonly Icon iconBusy = MakeIcon(Color.Goldenrod);
        readonly string wavPath = Path.Combine(Path.GetTempPath(),
            "voice-en-" + Process.GetCurrentProcess().Id + ".wav");
        readonly string selfTestWav;
        readonly ToolStripItem menuHeader;

        Hotkey hotkey = Hotkey.Load();
        string mode = Mode.Load();
        Process server;
        DateTime serverStarted;
        int quickDeaths; // quedas seguidas logo depois de iniciar; na terceira o reinicio automatico para
        string retryWav; // gravacao que estava sendo traduzida quando o servidor caiu; reenviada uma vez
        bool retrying;
        State state = State.Idle;
        bool ready;
        bool announced;
        bool restartNotice;
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
            menuHeader = menu.Items.Add(HeaderText());
            menuHeader.Enabled = false;
            ToolStripMenuItem modeMenu = new ToolStripMenuItem("Idioma");
            foreach (string code in Mode.Codes)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(Mode.Label(code));
                item.Tag = code;
                item.Checked = code == mode;
                item.Click += delegate(object sender, EventArgs e) { ChangeMode((string)((ToolStripItem)sender).Tag, modeMenu); };
                modeMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(modeMenu);
            ToolStripMenuItem fix = new ToolStripMenuItem("Corrigir a fala (PT > EN e PT > PT)");
            fix.Checked = Correction.Enabled;
            fix.Click += delegate
            {
                Correction.Enabled = !Correction.Enabled;
                fix.Checked = Correction.Enabled;
                menuHeader.Text = HeaderText();
                Log.Write("correcao " + (fix.Checked ? "ligada" : "desligada"));
                Flash("Correcao da fala " + (fix.Checked ? "ligada" : "desligada"), Color.MediumSeaGreen, 2000);
            };
            menu.Items.Add(fix);
            menu.Items.Add("Mudar atalho...", null, delegate { ChangeHotkey(); });
            menu.Items.Add("Copiar ultima traducao", null, delegate { CopyLast(); });
            ToolStripMenuItem paste = new ToolStripMenuItem("Colar com Ctrl+V em vez de digitar");
            paste.Checked = Output.Paste;
            paste.Click += delegate { Output.Paste = !Output.Paste; paste.Checked = Output.Paste; };
            menu.Items.Add(paste);
            menu.Items.Add("Reiniciar servidor", null, delegate { if (state != State.Recording) { retryWav = null; RestartServer("pedido no menu"); } });
            ToolStripMenuItem autostart = new ToolStripMenuItem("Iniciar com o Windows");
            autostart.Checked = Autostart.Enabled;
            autostart.Click += delegate { Autostart.Enabled = !Autostart.Enabled; autostart.Checked = Autostart.Enabled; };
            menu.Items.Add(autostart);
            menu.Items.Add("Sair", null, delegate { Quit(); });
            tray.ContextMenuStrip = menu;
            tray.Visible = selfTestWav == null;

            hideTimer.Tick += delegate { hideTimer.Stop(); overlay.Hide(); };
            restoreTimer.Interval = 2000; // tempo para a janela em foco ler a area de transferencia depois do Ctrl+V
            restoreTimer.Tick += delegate { restoreTimer.Stop(); RestoreClipboard(); };
            maxTimer.Interval = Config.MaxSeconds * 1000;
            maxTimer.Tick += delegate { if (state == State.Recording) StopRecording(); };
            restartTimer.Tick += delegate { restartTimer.Stop(); if (!exiting) StartServer(); };
            watchdog.Tick += delegate { watchdog.Stop(); OnTranslationTimeout(); };

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
                        menuHeader.Text = HeaderText();
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

        bool Correcting { get { return Correction.Enabled && Correction.AppliesTo(mode); } }

        string HeaderText()
        {
            return "VoiceEn  (" + hotkey + ", " + Mode.Short(mode) + (Correcting ? " + correcao" : "") + ")";
        }

        void ChangeMode(string code, ToolStripMenuItem modeMenu)
        {
            mode = code;
            Mode.Save(code);
            foreach (ToolStripMenuItem item in modeMenu.DropDownItems) item.Checked = (string)item.Tag == code;
            menuHeader.Text = HeaderText();
            Log.Write("modo alterado: " + code);
            Flash("Idioma: " + Mode.Label(code), Color.MediumSeaGreen, 2000);
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
            serverStarted = DateTime.Now;
            Process p;
            try { p = Process.Start(psi); }
            catch (Exception err)
            {
                Log.Write("falha ao iniciar o servidor: " + err.Message);
                OnServerExit();
                return;
            }
            server = p;
            tray.Icon = iconIdle;
            tray.Text = "VoiceEn: iniciando o servidor...";
            Log.Write("servidor iniciado (pid " + p.Id + ")");

            // Cada leitor so fala pelo processo que o criou: depois de um reinicio, o fim do servidor
            // antigo nao pode derrubar o novo.
            Thread stdout = new Thread(delegate()
            {
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    string captured = line;
                    BeginInvoke((MethodInvoker)delegate { if (p == server) OnServerLine(captured); });
                }
                if (!exiting) BeginInvoke((MethodInvoker)delegate { if (p == server) OnServerExit(); });
            });
            stdout.IsBackground = true;
            stdout.Start();

            Thread stderr = new Thread(delegate()
            {
                string line;
                while ((line = p.StandardError.ReadLine()) != null) Log.Write("[servidor] " + line);
            });
            stderr.IsBackground = true;
            stderr.Start();
        }

        // Mata o servidor atual (se houver) e sobe outro. Uma traducao em andamento e descartada.
        void RestartServer(string reason)
        {
            Log.Write("reiniciando o servidor: " + reason);
            restartTimer.Stop();
            watchdog.Stop();
            Process old = server;
            server = null;
            ready = false;
            if (state == State.Translating) state = State.Idle;
            if (old != null)
            {
                try { if (!old.HasExited) old.Kill(); } catch (Exception) { }
            }
            quickDeaths = 0;
            StartServer();
        }

        bool ServerAlive
        {
            get
            {
                try { return server != null && !server.HasExited; }
                catch (Exception) { return false; }
            }
        }

        void OnServerLine(string line)
        {
            if (line == "READY")
            {
                ready = true;
                tray.Icon = iconReady;
                tray.Text = "VoiceEn pronto: " + hotkey;
                Log.Write("servidor de traducao pronto");
                if (selfTestWav != null) { SendToServer(selfTestWav); return; }
                if (retryWav != null)
                {
                    string wav = retryWav;
                    retryWav = null;
                    retrying = true;
                    restartNotice = false;
                    Log.Write("reenviando a gravacao que estava sendo traduzida");
                    tray.Icon = iconBusy;
                    Flash("Traduzindo de novo...", Color.Gold, 0);
                    SendToServer(wav);
                    return;
                }
                // no reinicio so avisa se havia um aviso de "reiniciando" na tela
                if (state == State.Recording) return; // o servidor voltou no meio de uma gravacao
                if (!announced || restartNotice) Flash("VoiceEn pronto  -  " + hotkey + " para falar", Color.MediumSeaGreen, 2500);
                announced = true;
                restartNotice = false;
                return;
            }
            if (state != State.Translating) { Log.Write("linha inesperada: " + line); return; }

            watchdog.Stop();
            state = State.Idle;
            retrying = false;
            tray.Icon = iconReady;
            string elapsed = (translateClock.ElapsedMilliseconds / 1000.0).ToString("0.0") + "s";
            if (line.StartsWith("OK\t"))
            {
                string text = line.Substring(3).Trim();
                Log.Write("traduzido em " + elapsed + ": " + text);
                if (selfTestWav != null) { Quit(); return; }
                if (text.Length == 0) { Flash("Nenhuma fala detectada", Color.Khaki, 2000); return; }
                lastText = text;
                overlay.Hide();
                Deliver(text);
            }
            else
            {
                Log.Write("erro do servidor: " + line);
                if (selfTestWav != null) { Quit(); return; }
                Flash("Erro na traducao (veja o log)", Color.Salmon, 3000);
            }
        }

        // O servidor pode cair sozinho (o WSL encerra na suspensao do Windows, num "wsl --shutdown",
        // numa atualizacao). Antes o app ficava em "carregando" para sempre; agora ele sobe de novo.
        void OnServerExit()
        {
            bool wasTranslating = state == State.Translating;
            watchdog.Stop();
            ready = false;
            // uma gravacao em curso continua: ao terminar, ela espera o servidor novo (StopRecording)
            if (state != State.Recording)
            {
                state = State.Idle;
                tray.Icon = iconIdle;
            }
            Log.Write("servidor encerrou inesperadamente");
            if (selfTestWav != null) { Quit(); return; }

            quickDeaths = (DateTime.Now - serverStarted).TotalSeconds < 30 ? quickDeaths + 1 : 1;
            if (quickDeaths >= 3)
            {
                tray.Text = "VoiceEn: servidor parou";
                Log.Write("servidor caiu " + quickDeaths + " vezes seguidas; reinicio automatico suspenso");
                if (retryWav != null) Log.Write("gravacao pendente descartada");
                retryWav = null;
                restartNotice = false;
                if (state != State.Recording) Flash("VoiceEn: o servidor de traducao nao sobe (veja o log); " + hotkey + " tenta de novo", Color.Salmon, 6000);
                return;
            }
            if (wasTranslating && !retrying)
            {
                retryWav = wavPath;
                Notice("O servidor caiu durante a traducao; reiniciando...");
            }
            else if (wasTranslating)
            {
                Log.Write("servidor caiu de novo no reenvio; gravacao perdida");
                retrying = false;
                restartNotice = false;
                Flash("O servidor caiu de novo; a gravacao se perdeu, fale de novo", Color.Salmon, 6000);
            }
            tray.Text = "VoiceEn: reiniciando o servidor...";
            restartTimer.Interval = 1500 * quickDeaths;
            restartTimer.Start();
        }

        void OnTranslationTimeout()
        {
            if (state != State.Translating) return;
            Log.Write("traducao sem resposta ha " + (translateClock.ElapsedMilliseconds / 1000) + "s");
            state = State.Idle;
            retrying = false;
            RestartServer("traducao travada");
            Flash("A traducao travou; servidor reiniciado. Fale de novo", Color.Salmon, 6000);
        }

        void SendToServer(string windowsPath)
        {
            string full = Path.GetFullPath(windowsPath);
            string linux = "/mnt/" + char.ToLowerInvariant(full[0]) + full.Substring(2).Replace('\\', '/');
            state = State.Translating;
            translateClock = Stopwatch.StartNew();
            // folga para a nuvem cair (20 s), a correcao falhar (10 s) e a retraducao ou o modelo local
            // carregar e traduzir (~0,6 s por segundo de audio)
            double audioSeconds = new FileInfo(full).Length / 32000.0;
            watchdog.Interval = 90000 + (int)(audioSeconds * 2000);
            watchdog.Start();
            try
            {
                // em bytes UTF-8: o StandardInput do .NET 4 usa a pagina ANSI, e o Python le UTF-8
                byte[] request = Encoding.UTF8.GetBytes(mode + "\t" + (Correcting ? "corrigir\t" : "") + linux + "\n");
                server.StandardInput.BaseStream.Write(request, 0, request.Length);
                server.StandardInput.BaseStream.Flush();
            }
            catch (Exception err)
            {
                Log.Write("falha ao enviar ao servidor: " + err.Message);
                if (!retrying) retryWav = full;
                RestartServer("servidor nao aceitou o pedido");
                Notice("Reiniciando o servidor de traducao...");
            }
        }

        // Aviso que fica na tela ate o servidor novo dizer READY (ai vira "pronto" ou o reenvio).
        void Notice(string text)
        {
            restartNotice = true;
            Flash(text, Color.Khaki, 0);
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
            if (state == State.Translating) { Flash("Ainda traduzindo, aguarde...", Color.Gold, 0); return; }
            if (state == State.Recording) { StopRecording(); return; }
            if (!ready)
            {
                if (!ServerAlive && !restartTimer.Enabled)
                {
                    retryWav = null;
                    RestartServer("atalho apertado com o servidor parado");
                    Flash("Reiniciando o servidor de traducao...", Color.Khaki, 3000);
                }
                else Flash("VoiceEn ainda iniciando o servidor...", Color.Khaki, 2000);
                return;
            }
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
            retrying = false;
            tray.Icon = iconRec;
            maxTimer.Start();
            Flash("●  Gravando (" + Mode.Short(mode) + ")...  " + hotkey + " para terminar", Color.Tomato, 0);
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
            if (!ready)
            {
                retryWav = wavPath;
                retrying = false;
                if (!ServerAlive && !restartTimer.Enabled) RestartServer("gravacao terminada com o servidor parado");
                Notice("Esperando o servidor de traducao voltar...");
                return;
            }
            string doing = mode == "pt-en" ? "Traduzindo para ingles" : "Transcrevendo";
            Flash(doing + (Correcting ? " e corrigindo..." : "..."), Color.Gold, 0);
            SendToServer(wavPath);
        }

        // ---- saida ----

        void Deliver(string text)
        {
            Keyboard.WaitForModifiersUp(3000);
            if (Output.Paste) { Paste(text); return; }
            int typed = Keyboard.Type(text);
            if (typed == text.Length) return;
            // SendInput recusado (ex.: tela de bloqueio): deixa o resto do texto pronto para colar
            try { Clipboard.SetDataObject(text.Substring(typed), true, 10, 100); Flash("Nao consegui digitar; texto copiado, cole com Ctrl+V", Color.Khaki, 4000); }
            catch (Exception) { Flash("Nao consegui digitar; use Copiar ultima traducao", Color.Salmon, 4000); }
        }

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
            if (!Keyboard.CtrlV())
            {
                clipboardBackup = null;
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
            Log.Rotate();
            Log.Write(selfTest == null ? "iniciando" : "selftest: " + selfTest);
            App app = new App(selfTest);
            Application.Run();
            GC.KeepAlive(mutex);
            GC.KeepAlive(app);
        }
    }
}
