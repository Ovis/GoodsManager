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
