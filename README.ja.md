# AudioSwitch

[简体中文](README.md) | [English](README.en.md) | [日本語](README.ja.md)

AudioSwitch は、Windows の既定のスピーカー、ヘッドホン、マイクを切り替えるトレイアプリです。手動で選ぶほか、デバイスの接続・切断時に、設定した優先順位に従って自動で切り替えることもできます。

[![GitHub Releases からダウンロード](https://img.shields.io/badge/GitHub_Releases-2563EB?style=for-the-badge&logo=github&logoColor=white)](https://github.com/EthenGod/AudioSwitch/releases)

Windows 10 / 11 x64 に対応しています。デスクトップパネルには Microsoft Edge WebView2 Runtime が必要です。

## ダウンロードと起動

[GitHub Releases](https://github.com/EthenGod/AudioSwitch/releases) を開き、使いたいバージョンの **Assets** からダウンロードしてください。v0.12.2 では、どちらの配布形式にもデスクトップパネルが含まれています。

| ファイル | 使い方 |
| --- | --- |
| **AudioSwitch.exe**（推奨） | 書き込み可能な新しいフォルダーに保存し、ファイル名を変更せずにダブルクリックします。初回起動時に、同じ場所の `AudioSwitch` フォルダーへアプリ一式を展開します。 |
| **AudioSwitch-Desktop-v0.12.2-win-x64.zip** | ZIP 全体を新しいフォルダーへ展開し、中の `AudioSwitch.exe` を実行します。 |

手動で更新する場合は、先にトレイメニューから旧バージョンを終了してから、新バージョンを起動してください。デバイス設定は Windows ユーザーの設定フォルダーに保存され、更新後も引き継がれます。

## 使い方

- **デバイスの切り替え** — トレイアイコンを右クリックし、出力デバイスまたはマイクを選びます。ダブルクリックするとメインパネルが開きます。設定で、ローカルのブラウザーパネルを開く動作にも変更できます。
- **自動切り替え** — 出力と入力の優先順位をそれぞれ設定します。「通信用デバイスも切り替える」で、Windows の既定の通信デバイスも変更するかを選べます。
- **デバイスのプリセット** — 「サウンドデバイス」からデバイス設定を開き、表示名や音量を保存します。出力デバイスでは Windows の空間サウンドも設定できます。対応する Dolby デバイスには、個別のプロファイルを保存できます。「保存のみ」は次回使用時のために保存し、Dolby エディターの「保存して適用」は現在の出力にすぐ適用します。
- **ゲームモード** — トレイメニューまたは設定から有効にすると、更新の自動確認とバックグラウンドダウンロードを一時停止します。デバイスの切り替えは引き続き動作します。

パネルを閉じても AudioSwitch はトレイで動作します。バックグラウンド処理も終了するには、トレイメニューから終了してください。

## 設定と互換性

設定ファイルは `%LOCALAPPDATA%\AudioSwitch\settings.json` にあります。パネルからバックアップのエクスポートや設定のインポートができます。デバイスは Windows のエンドポイント ID で識別するため、同じ名前でもプリセットは個別に保存されます。[設定とバックアップ](docs/ja/USER-GUIDE.md#設定とバックアップ)

AudioSwitch が変更するのは Windows の既定のデバイスです。アプリ側で出力先を指定している場合は、そのデバイスが使われ続けることがあります。Dolby プロファイルには、対応する DAX コンポーネントとデバイスが必要です。[Dolby プロファイルの使い方](docs/ja/USER-GUIDE.md#dolby-プロファイル)

[ユーザーガイド](docs/ja/USER-GUIDE.md)では、言語選択、ブラウザーパネル、更新設定も説明しています。新機能の収録状況は、ダウンロードしたバージョンのリリースノートを確認してください。

## 開発とテスト

C# のバックグラウンド処理が、デバイスの監視、切り替え、プリセットを担当します。デスクトップパネルは React、TypeScript、Tauri 2 を使用しています。Windows 上で、リポジトリのルートからバックエンドのビルドと回帰テストを実行できます。

```powershell
.\build.ps1 -Test -OutputDirectory staging
```

開発用ドキュメントは現在中国語です：[フロントエンドとデスクトップのビルド](frontend/README.md) | [実機テスト](MANUAL-TESTS.md) | [配布パッケージの作成](releases/README.md)

## ライセンス

AudioSwitch 独自のコードには [GPL-3.0-only](LICENSE) が適用されます。同梱の SoundVolumeCommandLine（SVCL）は作者のライセンスに従い、有料配布や商用製品の一部としての配布は禁止されています。[著作権表示](NOTICE.txt) | [サードパーティーのライセンス](THIRD_PARTY.md)
