# SaruTask

指定フォルダにあるタスク用フォルダ・Markdownを、名前の部分一致ですばやく開くWindows常駐アプリです。

## 起動

[Releases](https://github.com/sarulab-ou/SaruTask/releases)でインストーラ版またはZIP版を配布します。Windows 10 / 11の.NET Framework 4.xを使用し、追加ランタイムのダウンロードは不要です。

- インストーラ版: `SaruTask-<version>-Setup.exe`を実行します。管理者権限は不要で、既定では`%LOCALAPPDATA%\Programs\SaruTask`にインストールします。スタートメニューから起動でき、「インストールされているアプリ」からアンインストールできます。
- ZIP版: `SaruTask-<version>-win.zip`を任意の書き込み可能なフォルダに展開し、`SaruTask.exe`を実行します。
- 開発時は`bin\SaruTask.exe`を実行します。

- **Ctrl + Alt + T**: 表示して検索欄にフォーカス。入力済みの文字は全選択します。
- **Esc / ウィンドウの×**: 終了せず通知領域（トレイ）に隠します。
- **トレイアイコンのダブルクリック**: 表示します。
- **終了**: トレイアイコンの右クリック →「終了」。
- 二重起動すると、起動済みのウィンドウを表示します。
- `SaruTask.exe --background`でトレイに隠して起動できます。

## 検索

初期の検索対象はデスクトップ配下の次の2か所です。「設定」から追加・削除できます。

```text
!!!重要なフォルダ\!2026業務\01_active
2026\task_hub\2026\01_active
```

**起動時は常に直下のみ**です。「サブフォルダも検索」をオンにすると下の階層も対象になります。フォルダ、および`.md` / `.markdown`ファイルを表示します。本文検索は行いません。大文字・小文字、全角・半角の違いは区別せず、空白区切りの複数語はすべて含む名前を検索します。空欄では全件表示します。

- **Enter / ダブルクリック**: 選択項目を既定のアプリで開きます。
- **Ctrl + Enter**: エクスプローラーで保存場所を開き、項目を選択します。
- **Ctrl + C**（結果一覧）/ 右クリック: パスをコピーします。
- **↓**（検索欄）: 結果一覧へ移動します。
- **Ctrl + L**: 検索欄へ移動して全選択します。
- **F5 / 更新**: 再読み込み。列見出しのクリックで並び替えできます。

フォルダの変更を監視し、通常は自動更新します。監視通知の取りこぼしに備え30秒ごとの再確認と呼び出し時の再読み込みも行います。再帰検索でもジャンクション・シンボリックリンクの中には入りません。読めない場所があれば下部にエラー数を表示し、マウスを重ねると内容を確認できます。

## 設定と自動起動

設定は`%LOCALAPPDATA%\SaruTask\settings.json`に保存します。サインイン時の起動は「設定」で有効にできます（初期状態はオフ）。有効にした場合のみ現在ユーザーの`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SaruTask`にEXEの場所を登録します。EXEを移動したら設定を保存し直してください。

Ctrl + Alt + Tが他のアプリに使われているときは通知します。その場合もトレイから開けます。

## ヘルプと更新

メニューバーの「ヘルプ(H)」から次の機能を使用できます。

- **更新(U)**: GitHub Releasesの最新正式版を確認し、新しいバージョンがあればZIP版をダウンロードして適用します。インストーラ版・ZIP版ともに、起動中のEXEと同じ場所に適用して再起動します。
- **バージョン(A)**: 現在のバージョンとアプリ情報を表示します。

ネットワークから取得したZIPは、GitHubのSHA-256情報（または同じリリースの`.zip.sha256`）とサイズを確認します。ZIP直下の`SaruTask.exe`だけを展開し、アセンブリ名・バージョンを確認します。更新の適用前に確認画面を表示します。「ZIPから更新…」では、別途取得した新しいZIP版も選択できます。

更新用ヘルパーが常駐プロセスの終了を待ち、EXEを置き換えて再起動します。直前のEXEは`SaruTask.exe.previous`として同じ場所に保存します。設定とタスクデータは更新・アンインストールの対象に含めません。更新用の一時ファイルは`%LOCALAPPDATA%\SaruTask\updates`に保存します。

## ビルドと検証

外部パッケージや.NET SDKを必要とせず、Windows付属のC#コンパイラでビルドします。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

`src/SearchCatalog.cs`が検索対象の列挙・照合、`src/MainForm.cs`が常駐UI、`src/SettingsForm.cs`が設定画面です。テストは一時ディレクトリだけを使用し、実タスクの内容を変更しません。生成物は`bin/`配下です。

UIとファイル監視も検証する場合は、SaruTaskを終了してから`-UiTest`を追加してください。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test -UiTest
```

## 配布物の作成

`package.ps1`でビルド・テスト後、`artifacts/release/`にインストーラ、ZIP、SHA-256ファイルを生成します。NSISコンパイラはWSL1のUbuntuに導入した`makensis`を使用します。パッケージとして展開したNSISを使う場合は、Windows側から見えるコンパイラのパスを`-NsisCompiler`で指定できます。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

生成物や個人の設定ファイルはGitに含めません。リリース用のZIPと`.zip.sha256`は同じGitHub Releaseにアップロードします。更新機能が使うZIPの名前は`SaruTask-<version>-win.zip`です。

インストーラの導入・削除と配布物の整合性は、既存のSaruTaskインストールがないWindowsユーザーで次のコマンドにより確認できます。テスト用の作業フォルダに導入し、アンインストール後に設定と無関係ファイルが残ることも確認します。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Packages.ps1 -Version 0.1.1
```

2026-11-16のv0.1.1検証では、検索12項目・更新24項目・常駐UI13項目が通過しました。ZIPの検証、実際の更新ヘルパーによるEXE差し替えと再起動、メニューの実画面表示、インストーラの導入・削除と配布物のSHA-256一致を確認しました。公開Releaseからの取得は公開後に確認します。
