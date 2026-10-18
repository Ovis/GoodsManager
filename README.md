# GoodsManager

単一ユーザー向け所持管理アプリの基盤実装。.NET 10 / ASP.NET Core、NUnitを使用。

```text
dotnet build GoodsManager.slnx
dotnet test GoodsManager.slnx
dotnet run --project src/GoodsManager.Web
```

`/health/live`は生存確認、`/health/ready`は準備状態の確認。
最終的にはReact + TypeScript + Viteの静的成果物とAPIをASP.NET Coreから同一オリジンで配信する。画面の構築は後続工程。

設計資料はローカル`docs`ブランチの`d617627`を参照。承認済みモックは`2d8dcb23e5ca5838de261f5e5eddc3add3d1b543`。設計・モックのブランチは実装ブランチへマージしない。

永続領域は`Storage:Directory`（環境変数`Storage__Directory`）で指定。既定はアプリの`data/`。
DBをWALで開き、接続ごとに外部キーを有効化。起動前に整合性を検査し、既存DBに未適用の移行があれば`pre-migration/*.zip`へDBと鍵を退避してから移行する。移行や退避に失敗した場合は起動しない。自動復元は行わず、退避と対応版の実行ファイルを使って停止中に復旧する。

この段階の退避は現在存在するDBと鍵の保護用。画像や全設定を含む完成版のバックアップ・復元機能は後続工程。

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

パスキーAPIは`/auth/passkey/options`、`/auth/passkey/verify`、`/auth/reauthenticate`、`/account/passkeys`、`/account/passkeys/options`、`/account/passkeys/{id}`。詳細な送受信と検証範囲は[実装・検証記録](IMPLEMENTATION.md)を参照。

開発用は`dotnet run --project src/GoodsManager.Web --launch-profile https`で起動。オリジンは`https://localhost:7443`、RP IDは`localhost`。開発用HTTPS証明書の準備は利用端末で行う。
本番では`Authentication__Origin`へ公開HTTPSオリジンを必ず指定する。`Authentication__RpId`はそのホストと一致させる。TraefikでHTTPSを終端してHTTPで接続する方針。公開URL、永続領域の権限、信頼するプロキシと転送ヘッダーの設定は配置時に確認する。開発用と本番用のパスキーを相互流用しない。

現在はAPIとサーバー基盤だけを実装。Reactの画面、商品管理、画像処理、完成版バックアップは未着手。PCブラウザ・Android・Linux x64コンテナ・Proxmoxでの受入は未実施。
