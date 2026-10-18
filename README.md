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
