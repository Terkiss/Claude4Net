# TeruTeruPandas 技術ドキュメント

## アーキテクチャ
TeruTeruPandasはC#で実装されており、主に以下のモジュールで構成されています：
- **Core**: DataFrame, Series, Index, GroupBy, Join, Pivot, Melt
- **IO**: CSV, JSON, SQLite（入出力）
- **Compat**: pandas風のヘルパーメソッド
- **DataUniverse**: 複数DataFrameの管理、SQLインターフェース、大規模IO

## 主なクラスとインターフェース
- `DataFrame`: テーブル型の主要オブジェクト。インデックス、フィルタ、集約、結合をサポート
- `Series<T>`: NA対応の1次元ベクトル
- `Index`: RangeIndex, IntIndex, StringIndex, DateTimeIndexの基底クラス
- `BoolSeries`: フィルタ用のブールマスク
- `DataUniverse`: 複数DataFrameのコンテナ。SQLクエリや一括IOをサポート

## SQLパーサと実行
- 基本的なSQLクエリ（SELECT, WHERE, JOIN, GROUP BY, ORDER BY, LIMIT）に対応
- パーサはDataUniverseSql.cs、実行はSqlQueryExecutorで実装
- 制限：単純な式のみ、サブクエリ非対応

## IOモジュール
- **CsvReader/CsvWriter**: CSVファイルの読み書き
- **JsonIO**: JSON（通常/JSON Lines）の読み書き
- **SqliteIO**: SQLiteテーブルの読み込み・クエリ実行、DataFrameのエクスポート

## API利用例
```csharp
// CSVの読み込み
var df = CsvReader.ReadCsv("data.csv");

// グループ化と集約
var grouped = df.GroupBy("カテゴリ").Agg(new Dictionary<string, string[]> { ["値"] = new[] { "sum", "mean" } });

// SQLクエリ
var universe = new DataUniverse();
universe.AddTable("テーブル", df);
var result = universe.SqlExecute("SELECT * FROM テーブル WHERE 値 > 10");
```

## 制限事項・パフォーマンス
- 小〜中規模データ（〜100万行程度）向けに最適化
- マルチスレッドや分散処理は未対応
- Pivot, Melt, 複雑なJoinは大規模データで遅くなる場合あり

## 拡張・貢献
- コードは拡張可能。新しいIOモジュールやインデックスタイプ、集約メソッドの追加が可能
- 外部DB連携はSqliteIOまたは独自IOモジュールの実装で対応
