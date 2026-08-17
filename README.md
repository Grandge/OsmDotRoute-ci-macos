# OsmDotRoute

[English](README.en.md) | 日本語

.NET ネイティブの OSM 経路計算ライブラリ。動的通行制限（進入不可・移動困難）に対応した
Dijkstra ベースの経路探索を提供する。

**マルチエージェントを用いたシミュレーションのように、短時間に大量の経路計算が必要なケース**を
主な対象とする。事前抽出した独自バイナリ `.odrg` を `MemoryMappedFile` + `ReadOnlySpan<T>` で
ゼロコピー読込するため、多数のエージェントが同一グラフへ繰り返し問い合わせても割り当てを最小化でき、
高頻度・大量の経路計算を低オーバーヘッドでさばける。

さらに、シミュレーション実行中に通行制限を**追加・削除しながら経路を即時再計算**できる
（再ビルド不要）ことが最大の特長。

> OsmDotRoute と Itinero の違い・向き不向きの詳細は
> [比較・選定ガイド](Documents/comparison_with_itinero.md) を参照。

## 特長

- **.NET 9 / pure C# 実装、ランタイム依存は System.* のみ**（外部 NuGet ゼロ）
- **`.odrg` 独自グラフ形式**: `MemoryMappedFile` + `ReadOnlySpan<T>` でゼロコピー読込
- **動的制約**: ポリゴン・JIS X0410 メッシュコード・国土数値情報 KSJ GML（A31 等）入力。再ビルドなしで次回計算へ即時反映
- **JSON 外部化されたプロファイル**: 通行可否・速度・難所反応をリビルドなしに調整可能
- **4 種の組込みプロファイル**: car / pedestrian / bicycle / truck（10t、日本道路法ベース）
- **8 種の組込み難所タイプ**: 冠水・液状化・土砂崩れ・工事中・障害物・交通集中・積雪・凍結
- **MIT License**

## クイックスタート

```csharp
using OsmDotRoute;

// 1. 事前抽出した .odrg をロード（PBF も Itinero も不要）
var routerDb = RouterDb.LoadFromOdrg(@"D:\odrg\tokyo.odrg");
var router = new Router(routerDb);

// 2. 東京駅 → 渋谷駅 を Car プロファイルで計算（GeoCoordinate は 緯度, 経度 の順）
var route = router.Calculate(
    VehicleProfile.Car,
    new GeoCoordinate(35.68040208522669, 139.769056008911),
    new GeoCoordinate(35.659, 139.700));

Console.WriteLine(route is null
    ? "経路を計算できませんでした。"
    : $"距離 {route.TotalDistanceM:F0} m, 所要時間 {route.TotalDurationSec:F0} 秒");
```

`.odrg` の作り方（PBF の準備 → 抽出）から、プロファイル指定・動的制約まで詳しくは
**[使い方ガイド](Documents/usage_guide.md)** を参照。

## 動的制約の登録例

```csharp
var restrictions = new RestrictedAreaService();
var router = new Router(routerDb, restrictions);

// ポリゴンで進入不可エリア登録
var polygon = new GeoPolygon(new[]
{
    new GeoCoordinate(35.68, 139.76),
    new GeoCoordinate(35.68, 139.78),
    new GeoCoordinate(35.66, 139.78),
    new GeoCoordinate(35.66, 139.76),
});
restrictions.AddBlockArea(polygon, tag: "incident-2026-05-19");

// メッシュコードで難所エリア登録（冠水）
restrictions.AddDifficultyArea(
    new MeshCode(53394611),
    DifficultyTypes.Flooding,
    tag: "typhoon-15");

// 国土数値情報 A31（洪水浸水想定区域）GML を一括登録（マップ範囲フィルタつき）
var bounds = new MapBounds(
    new GeoCoordinate(35.65, 139.74),
    new GeoCoordinate(35.70, 139.79));
restrictions.AddDifficultyAreaFromGmlFile(
    @"D:\ハザードデータ\A31-12_24_GML\A31-12_24.xml",
    DifficultyTypes.Flooding,
    mapBounds: bounds,
    tag: "ksj-a31");

// タグ単位で一括削除 → 次回計算から即時反映
restrictions.RemoveByTag("typhoon-15");
```

## DI 統合

```csharp
using Microsoft.Extensions.DependencyInjection;
using OsmDotRoute.Extensions.DependencyInjection;

services.AddOsmDotRoute(@"D:\odrg\tokyo.odrg");

// 取得側
var router = serviceProvider.GetRequiredService<Router>();
var restrictions = serviceProvider.GetRequiredService<RestrictedAreaService>();
```

`Router` / `RouterDb` / `RestrictedAreaService` がいずれも Singleton で登録されます。
`RestrictedAreaService` を共有することで、シミュレーション中に動的制約を変更すると
次の `Router.Calculate` 呼び出しから即時反映されます（REQ-RST-012）。

## 試用デモ（Sandbox）

**[→ ブラウザで今すぐ試す（GitHub Pages）](https://grandge.github.io/OsmDotRoute/)**（インストール不要）

コアエンジンを WebAssembly 化した静的サイトを GitHub Pages で公開中。
PBF ダウンロード → bbox 抽出 → 経路探索 → メッシュ／ポリゴン制約付与 → Re-Route までを
ブラウザ内で完結して体験できる。

ローカルでビルドする場合:

```powershell
cd samples/Sandbox/Web ; npm run build:wasm
```

## インストール

現時点で NuGet（nuget.org）への公開予定はありません。**ソース参照** での利用を想定しています。
次のようにプロジェクト参照してください（ランタイムは System.* のみ依存）。

```xml
<ProjectReference Include="path/to/OsmDotRoute/src/OsmDotRoute/OsmDotRoute.csproj" />
```

DI 統合を使う場合は加えて:

```xml
<ProjectReference Include="path/to/OsmDotRoute/src/OsmDotRoute.Extensions.DependencyInjection/OsmDotRoute.Extensions.DependencyInjection.csproj" />
```

`.odrg` を生成する抽出ツール（`osmdotroute-extractor`）の使い方は
[使い方ガイド §4](Documents/usage_guide.md#4-odrg-の作成方法) を参照。

## 現在のフェーズ

| Phase | 目標 | 状態 |
| --- | --- | --- |
| Phase 0 | 要件定義 | 完了（2026-05-18） |
| Phase 1 | 経路探索エンジン独自化（Itinero をデータ層として利用） | 完了 |
| Phase 2 | 中間グラフ形式 `.odrg` 策定 | 完了 |
| Phase 3 | `.odrg` ランタイム化・Itinero 依存完全撤去・bicycle/truck・ベンチ・親プロ統合・デモ・OSS 公開 | 完了（2026-06-02） |
| Phase 4 | プロファイル追加（Emergency / Disaster 等）・マルチプラットフォーム対応 | 完了（2026-06-03） |

当初計画していた機能実装は Phase 4 までで完了しました。**当面は不具合修正のみを行い、新規機能の追加予定はありません。**
ランタイムから Itinero 依存は撤去済み（System.* のみで完結）。macOS ARM64 / Linux x64 でも全テスト pass を確認済み。

## バージョニング方針

1.0.0 以降は**セマンティックバージョニングを厳密適用**します（REQ-API-008）。
破壊的 API 変更はメジャー版アップでのみ行います。

## 貢献

バグ報告・プルリクエストを歓迎します。ビルド・テスト・PR の手順は [CONTRIBUTING.md](CONTRIBUTING.md) を参照してください。

## ライセンス

[MIT License](LICENSE) — Copyright (c) 2026 Grandge.
第三者コンポーネントとその扱いは [LICENSE-THIRD-PARTY.md](LICENSE-THIRD-PARTY.md) を参照してください。

OSM データは ODbL です。`.odrg` を配布・公開する場合は「© OpenStreetMap contributors」の帰属表示が必要です。

## ドキュメント

- [使い方ガイド](Documents/usage_guide.md) — PBF 準備 → `.odrg` 抽出 → 経路探査 → プロファイル → 実装例
- [Itinero との比較・選定ガイド](Documents/comparison_with_itinero.md) — 設計思想・データ構造・性能・用途別の向き不向き
- [`.odrg` バイナリ形式仕様](Documents/phase2_graph_format_spec.md)
- [要件定義書](Documents/requirement_definition.md)
- [Phase 1 設計書](Documents/phase1_design.md) / [Phase 2 設計書](Documents/phase2_design.md) / [Phase 3 設計書](Documents/phase3_design.md)
