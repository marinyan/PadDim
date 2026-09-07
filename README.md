# PadDim

Windows 10 / 11 向けの、ゲームパッド操作も監視する自動減光ツール。C# / .NET 10 / WinForms。

キーボード・マウス・XInput・DirectInput が一定時間無操作になると、対応画面の本体輝度を徐々に下げます。操作を検出するとフェードインせず、減光前の値を一度で設定します。初期値は **5分後・3秒でフェードアウト・本体輝度20%** です。半透明の黒い画面を重ねる方式も選べます。

## 開発経緯
Vibe codingとかしてるとPCを点けっぱなしにする事が多いですが、ノートPCだと「ディスプレイだけ消す」という手段を取りづらいので自動減光ツールを作りました
ゲームしたりするときに勝手に減光されるのは困るのでゲームパッドによる入力も見てます(代わりにゲームパッドの電源が自動で切れた際には輝度が復活したりしますが)
あとビデオとか見てる時に減光されるのも困るので一時停止チェックも付けました

## 起動

`artifacts/installer/PadDim-Setup-0.1.0-win-x64.exe` を実行してインストールします。Windows 10（2004以降）/ 11 のx64向けで、.NET同梱のため別途ランタイムを入れる必要はありません。

- 管理者権限は不要です。初期インストール先は `%LOCALAPPDATA%\Programs\PadDim`。
- スタートメニューに登録します。デスクトップのショートカットは選択式です。
- インストール時に「Windowsへのサインイン時に自動起動する」を選択できます（初期状態はオフ）。自動起動では設定画面を表示せずタスクトレイに常駐します。再インストール時に選択を外すと登録を解除し、アンインストール時も削除します。
- Windowsの「インストールされているアプリ」からアンインストールできます。ユーザー設定は残します。
- 更新・アンインストール前は、通知領域からPadDimを終了してください。実行中はインストーラーが処理を止め、強制終了はしません。
- 配布ファイルは未署名です。

## ライセンス

PadDimは独自の[利用許諾契約](LICENSE.txt)で提供します。

- 個人利用・業務利用可（無償）。
- 同一法人・同一組織内のPCへの配布可。社内限定の一括配備も含みます。
- 自分又は社内で使うための改変・ビルド可。
- 上記の社内配布等を除く第三者への再配布不可（無償・有償、原版・改変版を問いません）。
- AS-IS（現状有姿）。法令上認められる範囲で無保証・免責。

同梱ライブラリには、それぞれのライセンスが適用されます。GitHubの利用規約により認められる同サービス内の閲覧・フォーク等の権利も制限しません。正式な条件はLICENSE.txtを参照してください。

## 利用方法

インストールしない場合は `artifacts/publish/win-x64` フォルダーを丸ごと置いて `PadDim.exe` を実行できます。従来の `artifacts/PadDim/PadDim.exe` は .NET 10 Desktop Runtime が必要な開発用ビルドです。

設定画面では無操作時間（10秒〜24時間）、減光方式、本体輝度（0〜100%）、黒の濃さ（5〜90%）、フェードアウト時間（0〜60秒）、スティックの遊びを変更できます。「設定を保存」で適用します。「減光を試す（5秒保持）」はクリックから約0.3秒後に開始します。フェード時間＋5秒で復元しますが、対応画面の検出にかかる時間によって保持時間は短くなります。操作すると途中でも解除します。

- ウィンドウを閉じると通知領域に常駐します。アイコンをダブルクリックすると設定を開きます。
- 一時停止・復帰・終了は通知領域の右クリックメニューから操作できます。
- 設定画面の「一時的に無効化」をチェックすると、減光を解除して停止します。外すとタイマーを最初から再開します。通知領域の「一時停止」と連動し、再起動時は解除されます。
- 正常終了時は変更した本体輝度を復元し、重ね表示を消します。
- 設定は `%LOCALAPPDATA%\PadDim\settings.json` に保存します。
- 手動で `PadDim.exe --tray` と起動してもタスクトレイから開始できます。

## ゲームパッドの扱い

50msごとに入力を読み取ります。接続変更はDirectInputで3秒ごとに再検索します。

| 入力 | 検出方法 |
| --- | --- |
| キーボード・マウス | Win32 `GetLastInputInfo` の時刻変化 |
| XInput（最大4スロット） | ボタン・トリガー・左右スティック |
| DirectInput | `IDirectInputDevice8` のボタン128個・POV4個・位置軸8個 |

XInputのスティックは中心からの変位、DirectInputの軸は接続時に取得した位置からの変位を判定します。一定以上倒している間は操作中です。ボタン・POVを押している間も操作中になります。軸の微小な揺れは無視します。XInputのトリガーしきい値は30/255です。

**DirectInputの接続時は、スティックやトリガーから手を離してください。** 倒したまま接続した場合は、「3秒後に中立位置を再取得」を押してから手を離します。ステアリングやスロットルでも、取得した位置が無操作の基準になります。遊びの%は、XInputでは中心から片側への範囲、DirectInputでは軸の全範囲に対する割合です。

DirectInputはバックグラウンド・非排他で取得します。他アプリの排他取得などで入力が読めない間は、減光を解除して保留し、状態欄に理由を表示します。接続機器がXInputとDirectInputの両方に表示されることがあります。両方から操作として検出されても動作に問題はありません。

## 本体輝度とフェード

- DDC/CI（高水準API、非対応時はVCP 0x10）とWMIの両方式で対応画面を探します。**方式の表示は内蔵・外付けの区別ではありません。** 同じ画面が両APIから見える場合があり、対応件数は実際の画面台数と一致するとは限りません。DDC/CI設定があるモニターでは有効にしてください。接続方式・変換アダプター・HDRなどの状態によって利用できない場合があります。
- 変更前に各画面の現在値を取得し、指定%まで下げます。元の値が指定%より低い画面は明るくしません。未対応の画面は変更せず、監視状況欄に結果を表示します。
- 本体輝度と重ね表示の両方で、暗くなるときだけフェードアウトします。初期値は3秒、0秒で即時減光です。途中で操作した場合もフェードを打ち切り、元の輝度へ一度で戻します。
- 本体輝度の通信は入力監視とは別の処理で直列に実行します。復帰時のアニメーション待ちはありませんが、実際の応答にはモニターの通信・処理時間がかかります。フェードも画面の輝度段階・更新速度に依存します。
- 復元失敗時は元の値をメモリーに保持し、通知領域の「明るさを戻す」で再試行できます。強制終了・クラッシュ後の自動復元は未実装です。その場合や切断した画面の復元失敗時は、Windowsやモニターの設定で輝度を戻してください。

## 制約

- 重ね表示方式では本体のバックライトは下げません。排他フルスクリーン、セキュアデスクトップ、他の最前面ウィンドウなどでは重ね表示できない場合があります。
- Windows標準のスリープ・画面オフ設定は変更しません。そちらが先に発動する可能性があります。
- ポーリング間隔より短い押下・解放は検出できない場合があります。
- 置いたパッドのドリフトがしきい値を超える場合、操作中のままになります。遊びの設定や中立位置を調整してください。

## ビルドと検証

インストーラーの再生成には [Inno Setup](https://jrsoftware.org/isdl.php) を使用します。

```powershell
./scripts/Build-Installer.ps1 -IsccPath 'C:/path/to/ISCC.exe'
```

ランタイム同梱版の発行、ライセンスの同梱、アイコンの再生成、日本語インストーラーのビルドをまとめて実行します。アイコンの元デザインは `assets/PadDim.svg`、Windows用は `assets/PadDim.ico`（16〜256px）です。

```powershell
dotnet restore src/PadDim/PadDim.csproj --configfile NuGet.Config
dotnet build src/PadDim/PadDim.csproj --no-restore -c Release
dotnet publish src/PadDim/PadDim.csproj --no-restore -c Release -o artifacts/PadDim
dotnet restore tests/PadDim.Tests/PadDim.Tests.csproj --configfile NuGet.Config
dotnet run --project tests/PadDim.Tests --no-restore -c Release
```

ライブラリのバージョンはプロジェクトで固定しています。NuGetの取得先は公開nuget.org、パッケージ保存先はこのフォルダーの `artifacts/packages` です。

自動テストは無操作タイマー、入力取得失敗時の保留、スティックの揺れ・押し続け・トリガー・再較正に加え、輝度範囲の変換、段階的な減光、フェード中の中断、フェードなしの復元、復元失敗後の再試行を検証します。

- `--smoke-test <出力ファイル>`: 減光せず入力APIの取得結果を保存。
- `--probe-brightness <出力ファイル>`: 本体輝度の対応可否・現在値を読むだけの診断。
- `--test-brightness <出力ファイル>`: 最初の対応画面を約5ポイント、1秒で減光し、元に戻して読み取り検証。実際に輝度が変わります。
- `--render-ui <PNGパス>`: 設定画面を一度表示して画像を書き出し、終了。

実機での確認手順：無操作時間を10秒に保存し、手を離して減光を待つ → キーボード・マウス・各ゲームパッドで復帰 → スティック／ボタンを10秒以上保持して減光しないことを確認 → 抜き差し後にも監視されることを確認。DirectInput機器、ゲーム起動中、複数画面、HDRなどの組み合わせは実機検証が必要です。

## 参照

- [Microsoft: DirectInput](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee418273(v=vs.85))
- [Microsoft: DirectInputのデバイス取得](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee415221(v=vs.85))
- [Vortice.DirectInput 3.8.3](https://www.nuget.org/packages/Vortice.DirectInput/3.8.3)（MIT）
- [Vortice.Windows ソース](https://github.com/amerkoleci/Vortice.Windows)
- [Microsoft: SetMonitorBrightness](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-setmonitorbrightness)
- [Microsoft: WmiSetBrightness](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmisetbrightness-method-in-class-wmimonitorbrightnessmethods)
