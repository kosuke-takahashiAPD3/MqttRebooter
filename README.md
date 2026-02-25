# MqttRebooter

[English](#english) | 日本語

---

## English

**MqttRebooter** is a Windows tray application that monitors the EMQX MQTT broker and automatically restarts it when it becomes unresponsive. It acts as an EMQX watchdog for Windows environments.

### Features

- **HTTP probe**: Periodically checks the EMQX dashboard (default: `http://localhost:18083/`)
- **Auto-restart**: Restarts EMQX when the probe fails consecutively (configurable threshold)
- **Manual control**: Start, Stop, Restart buttons and tray menu
- **Tray mode**: Minimize to system tray; double-click to restore
- **Logging**: On-screen log and file logging (`logs/emqx-watchdog.log`)
- **Flexible setup**: Supports Windows native, WSL, and Git Bash

### Requirements

- Windows OS
- .NET Framework 4.8
- EMQX MQTT broker

### Build

```bash
# Visual Studio: Open MqttRebooter.sln and build (Ctrl+Shift+B)

# Command line
msbuild MqttRebooter.sln /p:Configuration=Release
```

### Configuration

Edit `MqttRebooter.exe.config` (or `App.config` before build). See [Configuration](#configuration-1) section below for details.

### License

MIT License

---

## 日本語

**MqttRebooter** は、EMQX MQTT ブローカーを監視し、応答がなくなった場合に自動で再起動する Windows トレイアプリケーションです。EMQX のウォッチドッグとして動作します。

### 機能

- **HTTP プローブ**: EMQX ダッシュボード（デフォルト: `http://localhost:18083/`）を定期的に疎通確認
- **自動再起動**: プローブが連続で失敗した場合に EMQX を再起動（閾値は設定可能）
- **手動操作**: 開始・停止・再起動ボタンおよびトレイメニュー
- **トレイモード**: 最小化でトレイに格納、ダブルクリックでウィンドウ復元
- **ログ出力**: 画面上のログとファイルログ（`logs/emqx-watchdog.log`）
- **柔軟な設定**: Windows ネイティブ、WSL、Git Bash に対応

### 必要な環境

- Windows OS
- .NET Framework 4.8
- EMQX MQTT ブローカー

### ビルド

```bash
# Visual Studio: MqttRebooter.sln を開き、ビルド (Ctrl+Shift+B)

# コマンドライン
msbuild MqttRebooter.sln /p:Configuration=Release
```

### 実行

```bash
# Debug ビルド
MqttRebooter\bin\Debug\MqttRebooter.exe

# Release ビルド
MqttRebooter\bin\Release\MqttRebooter.exe
```

### 構成 <a id="configuration-1"></a>

`App.config`（ビルド後に `MqttRebooter.exe.config` にコピー）で設定します。

#### プローブ設定

| キー | デフォルト | 説明 |
|------|------------|------|
| `ProbeUrl` | `http://localhost:18083/` | EMQX ダッシュボードの疎通確認 URL |
| `TimeoutSeconds` | `5` | HTTP プローブのタイムアウト（秒） |
| `IntervalSeconds` | `60` | プローブ間隔（秒） |
| `FailThreshold` | `3` | 自動再起動するまでの連続失敗回数 |
| `UseProxyForProbe` | `false` | プローブでプロキシを使用するか |
| `ProbeProxyAddress` | `http://localhost:8080` | プロキシアドレス |

#### EMQX 実行設定

| キー | 説明 |
|------|------|
| `EmqxExePath` | EMQX の `emqx.cmd` のパス（WSL の場合は `wsl`、Git Bash の場合は `bash.exe` のパス） |
| `EmqxArgs` | EMQX 起動時の引数（例: `start`, `console`） |
| `EmqxStopArgs` | EMQX 停止時の引数（WSL 等で推奨） |
| `EmqxProcessName` | EMQX の Windows プロセス名（`beam`） |
| `EmqxRunningCheck` | `process`: プロセス名で判定 / `probe`: HTTP 疎通で判定（WSL 推奨） |
| `EmqxRestartDelaySeconds` | 停止と起動の間の待機時間（秒） |
| `EmqxUseVisibleWindow` | `true`: コンソールウィンドウを表示（`emqx console` 時） / `false`: バックグラウンド起動 |
| `EmqxWorkingDirectory` | EMQX の作業ディレクトリ |

#### WSL の設定例

```xml
<add key="EmqxExePath" value="wsl" />
<add key="EmqxArgs" value="bash -c &quot;cd /mnt/c/path/to/emqx &amp;&amp; ./emqx console&quot;" />
<add key="EmqxUseVisibleWindow" value="true" />
```

#### Git Bash の設定例

```xml
<add key="EmqxExePath" value="C:\Program Files\Git\bin\bash.exe" />
<add key="EmqxArgs" value="-c &quot;cd /c/path/to/emqx &amp;&amp; ./emqx console&quot;" />
<add key="EmqxStopArgs" value="-c &quot;cd /c/path/to/emqx &amp;&amp; ./emqx stop&quot;" />
<add key="EmqxUseVisibleWindow" value="true" />
```

### オプション: ステータスアイコン

実行ファイルと同じフォルダに以下のアイコンを配置すると、ステータス表示に使用されます。

- `ok.ico` — 疎通 OK 時
- `ng.ico` — 疎通 NG 時

配置しない場合はシステムアイコンが使用されます。

### ライセンス

MIT License
