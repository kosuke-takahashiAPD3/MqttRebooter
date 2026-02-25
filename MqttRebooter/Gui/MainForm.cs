using System;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace Nis.Gui
{
    public partial class MainForm : Form
    {
        private readonly string _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private readonly string _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "emqx-watchdog.log");
        private readonly object _logFileLock = new object();

        private NotifyIcon _notify;
        private ContextMenuStrip _trayMenu;

        private Icon _iconOk;
        private Icon _iconNg;

        private readonly Timer _timer = new Timer();

        private Config _cfg;
        private int _consecutiveFails = 0;
        private DateTime _lastCheck = DateTime.MinValue;
        private bool _lastProbeOk = true;

        public MainForm()
        {
            InitializeComponent();

            Directory.CreateDirectory(_logDir);
            _cfg = LoadConfig();

            LoadIcons();
            SetupTray();

            // タイマー設定
            _timer.Interval = Math.Max(1, _cfg.IntervalSeconds) * 1000;
            _timer.Tick += (s, e) => DoCheckOnce();
            _timer.Start();

            // 初回チェック即実行（ハンドル作成後に 1 回だけ実行）
            this.Shown += MainForm_Shown;

            UpdateStatusUI("起動しました。", isOk: true);
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            // Shown は再表示時にも呼ばれる可能性があるので、一度実行したら解除する
            this.Shown -= MainForm_Shown;
            BeginInvoke(new Action(() => DoCheckOnce()));
        }

        private void LoadIcons()
        {
            // ok.ico / ng.ico を exe と同じフォルダに置く想定
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string okPath = Path.Combine(baseDir, "ok.ico");
            string ngPath = Path.Combine(baseDir, "ng.ico");

            _iconOk = File.Exists(okPath) ? new Icon(okPath) : SystemIcons.Information;
            _iconNg = File.Exists(ngPath) ? new Icon(ngPath) : SystemIcons.Error;

            this.Icon = _iconOk;
        }

        private void SetupTray()
        {
            _trayMenu = new ContextMenuStrip();

            var mShow = new ToolStripMenuItem("画面を表示", null, (s, e) => ShowFromTray());
            var mStart = new ToolStripMenuItem("EMQX 起動", null, (s, e) => StartEmqx());
            var mStop = new ToolStripMenuItem("EMQX 停止", null, (s, e) => StopEmqx());
            var mRestart = new ToolStripMenuItem("EMQX 再起動", null, (s, e) => RestartEmqx());
            var mCheckNow = new ToolStripMenuItem("今すぐチェック", null, (s, e) => DoCheckOnce());
            var mExit = new ToolStripMenuItem("終了", null, (s, e) => ExitApp());

            _trayMenu.Items.AddRange(new ToolStripItem[]
            {
                mShow,
                new ToolStripSeparator(),
                mCheckNow,
                new ToolStripSeparator(),
                mStart,
                mStop,
                mRestart,
                new ToolStripSeparator(),
                mExit
            });

            _notify = new NotifyIcon
            {
                Visible = true,
                Icon = _iconOk,
                Text = "EMQX Watchdog",
                ContextMenuStrip = _trayMenu
            };

            _notify.DoubleClick += (s, e) => ShowFromTray();
        }

        private void ExitApp()
        {
            try
            {
                _timer.Stop();
                if (_notify != null)
                {
                    _notify.Visible = false;
                    _notify.Dispose();
                }
            }
            finally
            {
                Application.Exit();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // ×で閉じたら終了ではなくトレイへ
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        private void HideToTray()
        {
            this.Hide();
            this.ShowInTaskbar = false;
            _notify.BalloonTipTitle = "EMQX Watchdog";
            _notify.BalloonTipText = "タスクトレイに格納しました。";
            _notify.ShowBalloonTip(1000);
        }

        private void ShowFromTray()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.Activate();
        }

        private void btnHide_Click(object sender, EventArgs e) => HideToTray();
        private void btnCheckNow_Click(object sender, EventArgs e) => DoCheckOnce();
        private void btnStart_Click(object sender, EventArgs e) => StartEmqx();
        private void btnStop_Click(object sender, EventArgs e) => StopEmqx();
        private void btnRestart_Click(object sender, EventArgs e) => RestartEmqx();
        private void btnClear_Click(object sender, EventArgs e) => txtLog.Clear();

        private async void DoCheckOnce()
        {
            // チェック中に連打されても良いように軽くガード
            _timer.Stop();
            try
            {
                _lastCheck = DateTime.Now;

                // アプリ側でも明示的なタイムアウトをかける
                int timeoutMs = Math.Max(1, _cfg.TimeoutSeconds) * 1000;

                var probeTask = System.Threading.Tasks.Task.Run(
                    () => Probe(_cfg.ProbeUrl, _cfg.TimeoutSeconds));

                var completed = await System.Threading.Tasks.Task.WhenAny(
                    probeTask,
                    System.Threading.Tasks.Task.Delay(timeoutMs));

                bool ok;
                if (completed == probeTask)
                {
                    // Probe が正常に（成功/失敗いずれかで）終了
                    ok = probeTask.Result;
                }
                else
                {
                    // アプリ側のタイムアウト超過とみなす
                    AppendLog($"Probe TIMEOUT (> {_cfg.TimeoutSeconds} sec).");
                    ok = false;
                }

                if (ok)
                {
                    if (!_lastProbeOk) AppendLog("Probe OK (recovered).");
                    _consecutiveFails = 0;
                    UpdateStatusUI("OK", true);
                }
                else
                {
                    _consecutiveFails++;
                    AppendLog($"Probe NG. consecutiveFails={_consecutiveFails}/{_cfg.FailThreshold}");
                    UpdateStatusUI("NG", false);

                    if (_consecutiveFails >= _cfg.FailThreshold)
                    {
                        AppendLog("FailThreshold reached. Restarting EMQX...");
                        RestartEmqx();
                        _consecutiveFails = 0; // 再起動後にリセット
                    }
                }

                _lastProbeOk = ok;
                UpdateStatusLabels();
            }
            catch (Exception ex)
            {
                AppendLog("Check ERROR: " + ex);
                UpdateStatusUI("ERROR", false);
            }
            finally
            {
                _timer.Start();
            }
        }

        private bool Probe(string url, int timeoutSeconds)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutSeconds * 1000;
                req.ReadWriteTimeout = timeoutSeconds * 1000;

                // UseProxyForProbe = true の場合は設定されたプロキシ（デフォルト: http://localhost:8080）を使用
                // false の場合はプロキシを無効化して直接疎通を確認
                if (_cfg.UseProxyForProbe)
                {
                    if (!string.IsNullOrWhiteSpace(_cfg.ProbeProxyAddress))
                    {
                        // .NET Framework 4.x では文字列だけを受け取る WebProxy コンストラクタを使用
                        req.Proxy = new WebProxy(_cfg.ProbeProxyAddress);
                    }
                }
                else
                {
                    req.Proxy = null;
                }

                req.AllowAutoRedirect = true;

                using (var res = (HttpWebResponse)req.GetResponse())
                {
                    int code = (int)res.StatusCode;
                    return code >= 200 && code <= 399;
                }
            }
            catch (WebException wex)
            {
                AppendLog($"Probe WebException: {wex.Status} / {wex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                AppendLog($"Probe Exception: {ex.GetType().Name} / {ex.Message}");
                return false;
            }
        }

        private void StartEmqx()
        {
            try
            {
                if (IsEmqxRunning())
                {
                    AppendLog("EMQX is already running.");
                    return;
                }

                // wsl, cmd, bash 等は PATH から解決されるため存在チェックをスキップ
                bool isLauncherCommand = _cfg.EmqxExePath.IndexOf(Path.DirectorySeparatorChar) < 0
                    && _cfg.EmqxExePath.IndexOf('/') < 0;
                if (!isLauncherCommand && !File.Exists(_cfg.EmqxExePath))
                    throw new FileNotFoundException("emqx executable not found", _cfg.EmqxExePath);

                string workDir = _cfg.EmqxWorkingDirectory;
                if (string.IsNullOrWhiteSpace(workDir) && !isLauncherCommand)
                {
                    var dir = Path.GetDirectoryName(_cfg.EmqxExePath);
                    if (!string.IsNullOrEmpty(dir)) workDir = dir;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = _cfg.EmqxExePath,
                    Arguments = _cfg.EmqxArgs,
                    UseShellExecute = _cfg.EmqxUseVisibleWindow,
                    CreateNoWindow = !_cfg.EmqxUseVisibleWindow,
                    WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? null : workDir
                };

                Process.Start(psi);
                AppendLog("EMQX started.");
            }
            catch (Exception ex)
            {
                AppendLog("StartEmqx ERROR: " + ex.Message);
            }
            finally
            {
                UpdateStatusLabels();
            }
        }

        private void StopEmqx()
        {
            try
            {
                bool stopped = false;

                // EmqxStopArgs が設定されている場合、emqx stop コマンドを実行（WSL/リモート等でプロセスが見えない場合に有効）
                if (!string.IsNullOrWhiteSpace(_cfg.EmqxStopArgs))
                {
                    AppendLog("Running emqx stop command...");
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = _cfg.EmqxExePath,
                            Arguments = _cfg.EmqxStopArgs,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            WorkingDirectory = string.IsNullOrWhiteSpace(_cfg.EmqxWorkingDirectory) ? null : _cfg.EmqxWorkingDirectory
                        };
                        using (var p = Process.Start(psi))
                        {
                            if (p != null)
                            {
                                p.WaitForExit(30000); // 最大30秒待機
                                AppendLog($"emqx stop exited with code {p.ExitCode}");
                                stopped = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendLog("emqx stop command failed: " + ex.Message);
                    }
                }

                // プロセス名で停止を試行（Windows ネイティブで beam が検出できる場合）
                if (!stopped)
                {
                    int killed = 0;
                    foreach (var p in Process.GetProcessesByName(_cfg.EmqxProcessName))
                    {
                        try
                        {
                            AppendLog($"Killing EMQX: pid={p.Id}");
                            p.Kill();
                            p.WaitForExit(5000);
                            killed++;
                            stopped = true;
                        }
                        catch (Exception ex)
                        {
                            AppendLog("Kill failed: " + ex.Message);
                        }
                    }
                    if (!stopped)
                        AppendLog("EMQX process not found (WSL の場合は EmqxStopArgs の設定を推奨).");
                }

                AppendLog(stopped ? "EMQX stopped." : "EMQX may still be running. Check manually.");
            }
            finally
            {
                UpdateStatusLabels();
            }
        }

        private void RestartEmqx()
        {
            StopEmqx();
            int delayMs = Math.Max(0, _cfg.EmqxRestartDelaySeconds) * 1000;
            if (delayMs > 0)
            {
                AppendLog($"Waiting {_cfg.EmqxRestartDelaySeconds} sec before restart...");
                System.Threading.Thread.Sleep(delayMs);
            }
            StartEmqx();
        }

        private bool IsEmqxRunning()
        {
            // EmqxRunningCheck=probe の場合は HTTP でダッシュボード疎通を確認（WSL 等でプロセスが見えない場合に有効）
            if (string.Equals(_cfg.EmqxRunningCheck, "probe", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return Probe(_cfg.ProbeUrl, Math.Min(3, _cfg.TimeoutSeconds));
                }
                catch
                {
                    return false;
                }
            }
            return Process.GetProcessesByName(_cfg.EmqxProcessName).Length > 0;
        }

        private void UpdateStatusUI(string message, bool isOk)
        {
            // トレイアイコン切り替え
            _notify.Icon = isOk ? _iconOk : _iconNg;
            this.Icon = isOk ? _iconOk : _iconNg;

            // ステータス表示
            lblStatus.Text = message;
            lblStatus.ForeColor = isOk ? Color.DarkGreen : Color.DarkRed;

            // トレイのツールチップ（短く）
            _notify.Text = isOk ? "EMQX Watchdog (OK)" : "EMQX Watchdog (NG)";
        }

        private void UpdateStatusLabels()
        {
            lblLastCheck.Text = _lastCheck == DateTime.MinValue ? "-" : _lastCheck.ToString("yyyy-MM-dd HH:mm:ss");
            lblFailCount.Text = _consecutiveFails.ToString();
            lblEmqxRunning.Text = IsEmqxRunning() ? "Running" : "Stopped";
        }

        private void AppendLog(string msg)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}";

            // txtLog は UI スレッドからしか触れないので、必要に応じてマーシャリングする
            if (txtLog.InvokeRequired)
            {
                txtLog.BeginInvoke(new Action<string>(AppendLog), msg);
            }
            else
            {
                txtLog.AppendText(line + Environment.NewLine);
            }

            // 複数スレッドからの同時書き込みを防ぐ
            lock (_logFileLock)
            {
                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }

        private Config LoadConfig()
        {
            return new Config
            {
                ProbeUrl = Get("ProbeUrl", "http://localhost:18083/"),
                TimeoutSeconds = GetInt("TimeoutSeconds", 5),
                IntervalSeconds = GetInt("IntervalSeconds", 60),
                FailThreshold = GetInt("FailThreshold", 3),
                EmqxExePath = Get("EmqxExePath", @"C:\emqx\bin\emqx.cmd"),
                EmqxArgs = Get("EmqxArgs", "start"),
                EmqxProcessName = Get("EmqxProcessName", "beam"),
                EmqxStopArgs = Get("EmqxStopArgs", null),
                EmqxRunningCheck = Get("EmqxRunningCheck", "process"),
                EmqxRestartDelaySeconds = GetInt("EmqxRestartDelaySeconds", 2),
                EmqxUseVisibleWindow = GetBool("EmqxUseVisibleWindow", false),
                EmqxWorkingDirectory = Get("EmqxWorkingDirectory", null),
                UseProxyForProbe = GetBool("UseProxyForProbe", false),
                ProbeProxyAddress = Get("ProbeProxyAddress", "http://localhost:8080")
            };
        }

        private string Get(string key, string def)
        {
            var v = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(v) ? def : v.Trim();
        }

        private int GetInt(string key, int def)
        {
            int n;
            return int.TryParse(Get(key, def.ToString()), out n) ? n : def;
        }

        private bool GetBool(string key, bool def)
        {
            var v = Get(key, def.ToString());
            bool b;
            return bool.TryParse(v, out b) ? b : def;
        }

        private sealed class Config
        {
            public string ProbeUrl;
            public int TimeoutSeconds;
            public int IntervalSeconds;
            public int FailThreshold;

            public string EmqxExePath;
            public string EmqxArgs;
            public string EmqxProcessName;
            public string EmqxStopArgs;
            public string EmqxRunningCheck;
            public int EmqxRestartDelaySeconds;
            public bool EmqxUseVisibleWindow;
            public string EmqxWorkingDirectory;

            public bool UseProxyForProbe;
            public string ProbeProxyAddress;
        }
    }
}
