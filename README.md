# GoodsManager

単一ユーザー向け所持管理アプリ。.NET 10 / ASP.NET Core、React + TypeScript + Vite、SQLite / EF Core、NUnitを使用。

```text
dotnet build GoodsManager.slnx
dotnet test GoodsManager.slnx
dotnet run --project src/GoodsManager.Web
```

`/health/live`は生存確認、`/health/ready`は準備状態の確認。
React + TypeScript + Viteの静的成果物とAPIをASP.NET Coreから同一オリジンで配信する。発行時にクライアントをビルドする。開発中の画面更新は npm --prefix src/GoodsManager.Client run build で生成する。

設計資料はローカル`docs`ブランチの`d617627`を参照。承認済みモックは`2d8dcb23e5ca5838de261f5e5eddc3add3d1b543`。設計・モックのブランチは実装ブランチへマージしない。

永続領域は`Storage:Directory`（環境変数`Storage__Directory`）で指定。既定はアプリの`data/`。
DBをWALで開き、接続ごとに外部キーを有効化。起動前に整合性を検査し、既存DBに未適用の移行があれば`pre-migration/*.zip`へDB・正式画像・鍵を検証して退避してから移行する。移行や退避に失敗した場合は起動しない。自動復元は行わず、退避と対応版の実行ファイルを使って停止中に復旧する。

P11以降の更新前退避は、正式画像・鍵・マニフェストを含む検証済みZIPを作成する。詳細は下記のバックアップ・復元手順を参照。

```text
dotnet tool restore
dotnet ef migrations add 変更名 --project src/GoodsManager.Web --output-dir Storage/Migrations
```

SQLの保存型・制約・型変換・取消・移行失敗・プロセス排他を、試験ごとに分離した実ファイルSQLiteで検証する。試験専用の数量等のテーブルは本番DBには作成しない。Dapperは未導入。

認証APIは`/api/v1/setup/status`、`/setup`、`/auth/csrf`、`/auth/password`、`/auth/session`、`/auth/logout`、`/auth/revoke-all`、`/account/password`。
状態変更は匿名の初期登録・ログインを含め`GET /api/v1/auth/csrf`の`token`を`X-CSRF-Token`へ指定する。認証状態の変更後はトークンを取り直す。HTTPSの同一オリジンで接続する。

ログインIDは英数字・`._-`の1～64文字、大小文字を区別しない。パスワードは12～128コードポイント、文字種の強制なし、NUL不可。5回の連続失敗で5分間停止。セッションクッキーは上限7日、保持ありはログインから30日。いずれも自動延長しない。標準IdentityのSecurityStampを毎要求で照合する。

CLI復旧はアプリ停止後、永続領域を同じ設定にして対話端末で実行する。

```text
dotnet run --project src/GoodsManager.Web -- recover-account --keep-passkeys
dotnet run --project src/GoodsManager.Web -- recover-account --clear-passkeys
```

新しいパスワードを非表示で2回入力。パスワードをコマンド引数や環境変数へ入れない。保持・削除の選択は必須。どちらも全端末を失効し、ロックアウトを解除する。HTTPの復旧APIは設けない。

パスキーAPIは`/auth/passkey/options`、`/auth/passkey/verify`、`/auth/reauthenticate`、`/account/passkeys`、`/account/passkeys/options`、`/account/passkeys/{id}`。送受信の契約と検証範囲は認証コントローラーとNUnit試験を参照。

開発用は`dotnet run --project src/GoodsManager.Web --launch-profile https`で起動。オリジンは`https://localhost:7443`、RP IDは`localhost`。開発用HTTPS証明書の準備は利用端末で行う。
本番では`Authentication__Origin`へ公開HTTPSオリジンを必ず指定する。`Authentication__RpId`はそのホストと一致させる。TraefikでHTTPSを終端してHTTPで接続する方針。公開URL、永続領域の権限、信頼するプロキシと転送ヘッダーの設定は配置時に確認する。開発用と本番用のパスキーを相互流用しない。

P11までの商品登録・検索・商品/所持/個体/画像編集・マスター整理・ゴミ箱・バックアップを実装。自動試験・PCブラウザ・ローカルLinux x64の検証と、Proxmox・Android実機の受入は分けて扱う。

## バックアップと停止中の復元（P11）

通常画面の「バックアップ」で、自動実行の有効・無効と日本時間の実行時刻を設定する。初期は有効・03:00（Asia/Tokyo）。当日の予定時刻を過ぎて起動した場合は、未実行分を1回処理する。失敗は当日中に自動で繰り返さず、翌日か手動実行で再試行する。

初回は必ず作成し、その後は保全対象の変更番号が前回成功と同じなら自動ZIPを省略する。日曜は変更の有無にかかわらずDB整合性を検査する。履歴・アラート・一時アップロードでは変更番号を進めず、画像設定・バックアップ設定・認証情報・鍵変更は追跡する。

自動の成功最新3世代を保持し、新しいZIPの検証・成功確定後にだけ旧世代を整理する。手動・更新前・外部配置のZIPは自動削除しない。削除失敗や処理中断は履歴と警告へ反映し、旧成功を保持する。ZIPの論理削除と実ファイル整理を分け、整理中の再起動でも再開できる。

ZIPはDB・正式商品とゴミ箱・個体から参照される全画像・必要なDB設定・Data Protection鍵を含む。稼働DBはSQLiteバックアップAPIでコピーし、コピー側だけからログイン状態・認証チャレンジ・再送成功記録・一時アップロード・到達しない画像を除く。ZIP圧縮は業務書込を再開してから行う。マニフェスト、全ファイルの長さとSHA-256、DB整合性・外部キー、正式画像の参照、移行履歴とその版の実際の列構成を検証する。

ZIPは暗号化しない。パスワードハッシュ・パスキー・鍵を含むため、保存先とダウンロード済みファイルは適切に保護し、外部への退避は運用側で行う。保存先は環境設定の参照表示のみで、Webから変更できない。

| 環境変数 | 初期値・意味 |
| --- | --- |
| `Backups__Directory` | `Storage__Directory/backups`。本番は専用マウントを指定する。DB・画像・鍵や準備領域そのものと重ねない |
| `Backups__MaximumArchiveBytes` | 68,719,476,736（64 GiB） |
| `Backups__MaximumExpandedBytes` | 137,438,953,472（128 GiB） |
| `Backups__MaximumFiles` | 100,000（マニフェストを含む） |
| `Backups__MaximumManifestBytes` | 16,777,216（16 MiB） |

これらは検証・復元時の資源保護で、保存済みの商品・画像の総数上限ではない。ZIP64を含め、標準ZIP処理が一覧を確保する前にも中央ディレクトリの件数・メタデータを確認する。形式の技術参照は[PKWARE ZIP仕様](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT)。新しい依存は追加していない。

発行先のディレクトリで、Webアプリと同じ `Storage__Directory` / `Backups__Directory` を指定して実行する。いずれもアプリ停止中の排他操作。検証や復元にWeb認証用の公開URL設定は不要。

```text
dotnet GoodsManager.Web.dll backup create
dotnet GoodsManager.Web.dll backup verify /backups/実行ID.zip
dotnet GoodsManager.Web.dll backup restore /backups/実行ID.zip --confirm-replace
dotnet GoodsManager.Web.dll backup rollback --confirm-rollback
```

復元は、ZIPを別領域へ展開・検証し、対応する旧版ならその領域だけを現行スキーマへ移行してから切り替える。未知・不整合な移行履歴や列構成は拒否する。壊れた現DBからも復元できるよう、現DB・WAL・SHM・画像・鍵は変更せず `.rollback-退避ID/` へ退避する。成功した退避一式も自動削除しない。

複数の改名を一体の操作とは扱わず、`restore-state.json` に進行を記録する。処理途中の例外では旧一式へ戻し、進行記録が残る場合は通常起動を止める。停止したまま `backup rollback --confirm-rollback` で処理する。切替完了記録後の停止なら、退避を保持して完了状態を確定する。進行記録を手で削除して起動を強行しない。

復元ではセッションと再送記録を破棄し、認証のSecurityStampも更新する。全端末で再ログインし、画面を再読み込みする。復元前の結果不明要求を同じキーのまま再送しない。

復元後の履歴は実在する検証済みマニフェストから再構成する。自動保持の対象を示す `.owner-id` と `.owned` はアプリが管理し、外部配置したZIPへ自動保持の権限を付けない。P02のマニフェストなし退避ZIPは削除せず残すが、新CLIの検証・復元対象には含めず、対応する旧版で復旧する。

P11では実SQLite・実ZIPの自動試験、専用データでのWindows CLI実復元と模擬中断のロールバック、PCブラウザでの設定競合・手動実行・ダウンロード・削除を確認する。Proxmox上の配置・電源断・実ストレージのI/O障害・Android実機受入はP13/P14で扱い、自動試験や模擬中断と区別する。
