# AudioSwitch v0.13.0 正式版

[简体中文](https://github.com/EthenGod/AudioSwitch/blob/master/releases/RELEASE-NOTES-v0.13.0.md) | [English](https://github.com/EthenGod/AudioSwitch/blob/master/releases/RELEASE-NOTES-v0.13.0.en.md) | [日本語](https://github.com/EthenGod/AudioSwitch/blob/master/releases/RELEASE-NOTES-v0.13.0.ja.md)

> この文書には機械翻訳が含まれる場合があります。内容に相違がある場合は、[中国語の原文](https://github.com/EthenGod/AudioSwitch/blob/master/releases/RELEASE-NOTES-v0.13.0.md)を優先してください。

英語・日本語、システムの言語に合わせる設定、デバイスの表示名変更に対応しました。

[![AudioSwitch.exe をダウンロード](https://img.shields.io/badge/AudioSwitch.exe%20%E3%82%92%E3%83%80%E3%82%A6%E3%83%B3%E3%83%AD%E3%83%BC%E3%83%89-2563EB?style=for-the-badge)](https://github.com/EthenGod/AudioSwitch/releases/download/v0.13.0/AudioSwitch.exe)

**書き込み可能な新しいフォルダーに EXE を保存し、名前を変えずにダブルクリックしてください。** 初回起動時に、同じ場所の `AudioSwitch` フォルダーへ展開します。フォルダー全体を残してください。更新前にトレイメニューから旧バージョンを終了してください。既存の設定は引き継がれます。

[EXE をダウンロード](https://github.com/EthenGod/AudioSwitch/releases/download/v0.13.0/AudioSwitch.exe) | [完全版 ZIP をダウンロード](https://github.com/EthenGod/AudioSwitch/releases/download/v0.13.0/AudioSwitch-Desktop-v0.13.0-win-x64.zip)

ZIP を使う場合は、すべて展開してから中の `AudioSwitch.exe` を実行してください。デスクトップパネルには Microsoft Edge WebView2 が必要です。

## ✨ 新機能

- 初回起動時に簡体字中国語・英語・日本語・「システムに合わせる」を選べます。後からアプリ設定で変更できます。システムの言語と一致しない場合は中国語を使います。
- デバイス設定から表示名を変更できます。この PC に保存した名前を、Tab キーで一語ずつ補完できます。
- 言語切り替え時に文字とレイアウトが滑らかに変化します。エラー画面の配色をメインパネルに統一し、3 言語のユーザーガイドを用意しました。
- EXE の展開成功後に作成されるのは `AudioSwitch` フォルダーだけになり、ZIP を含む余分なフォルダーは作成されません。

## 🛠️ 修正内容

- 更新や修復のキャンセル処理中に、後続の操作を受け付ける問題を修正し、重複した送信を防ぐようにしました。
- 古い設定に含まれる名前補完キャッシュが破損していると、設定のインポートに失敗する問題を修正しました。
- ローカルポートのルート URL からブラウザーパネルを開くと、接続処理が正しく始まらない問題を修正しました。

ソースはリリースページの **Source code** から取得できます。独自のコードには GPL-3.0-only が適用されます。サードパーティーのライセンスはパッケージに同梱されています。
