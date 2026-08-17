# コントリビューションガイド

OsmDotRoute への貢献に興味を持っていただきありがとうございます。
本書はビルド・テスト・プルリクエストの手順をまとめます。

## 必要環境

- .NET 9 SDK 以降
- （サンプル Web UI / ブラウザ WASM デモを触る場合のみ）Node.js 18 以降
- OS: Windows / Linux / macOS で動作（コア・テスト・サンプルとも特定 OS 依存なし）

## リポジトリ構成

| パス | 役割 | 配布 |
| --- | --- | --- |
| `src/OsmDotRoute` | コアライブラリ（経路探査・動的制約）。System.* のみ依存 | ○ |
| `src/OsmDotRoute.Pbf` | OSM PBF パーサ | ○ |
| `src/OsmDotRoute.Extractor` | `.odrg` 抽出 CLI（`osmdotroute-extractor`） | ○ |
| `src/OsmDotRoute.Extensions.DependencyInjection` | DI 統合 | ○ |
| `tests/OsmDotRoute.Tests` | 単体テスト | — |
| `tests/OsmDotRoute.Benchmarks` | ベンチマーク | — |
| `samples/` | 試用デモ（Sandbox 等） | — |

## ビルド

```powershell
dotnet build OsmDotRoute.sln -c Release
```

## テスト

```powershell
dotnet test tests/OsmDotRoute.Tests/OsmDotRoute.Tests.csproj -c Release
```

個人マシン固有のデータ（親プロジェクトの RouterDb、KSJ ハザード GML など）に依存するテストは、
当該ファイルが存在しない環境では自動的にスキップされます。同梱の `samples/Data/tsushima.odrg` を
真値として参照するテストはどの環境でも実行されます。

## `.odrg` の作成（手元で試す場合）

```powershell
dotnet run --project src/OsmDotRoute.Extractor -- extract `
  --input <file.osm.pbf> --output <file.odrg> `
  --bbox minLon,minLat,maxLon,maxLat --profiles car,pedestrian
```

詳細は [使い方ガイド](Documents/usage_guide.md) を参照。

## コーディング規約

- 既存コードのスタイル・命名・レイアウトに合わせる
- public API には XML ドキュメントコメントを付ける
- 不要な「ついで」のリファクタリング・抽象化は避ける（YAGNI）

## プルリクエストの前に

- `dotnet test` が全件 pass することを確認する
- 1 PR = 1 トピック。無関係な整形・リファクタを混ぜない
- コミットメッセージは種別プレフィックス（`feat` / `fix` / `docs` / `refactor` / `test`）+ 簡潔な要約

## ライセンス

本プロジェクトは [MIT License](LICENSE) です。プルリクエストを送ることで、貢献内容が MIT ライセンスの
もとで配布されることに同意したものとみなします。第三者コンポーネントの一覧は
[LICENSE-THIRD-PARTY.md](LICENSE-THIRD-PARTY.md) を参照してください。
