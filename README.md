# cddb-writer (C# / .NET 8)

FreeDB 互換サーバー（Freedb日本語、gnudb など）から CD の楽曲情報（XMCD）を取得するサンプルです。  
HTTP/CGI の `cddb query` と `cddb read` に対応し、EUC-JP / Shift_JIS / UTF-8 の応答文字コードを扱えます。

- 対応: cddb query / cddb read（HTTP/CGI）
- 文字コード: EUC-JP / Shift_JIS / UTF-8（`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` 済）
- TOC 入力: JSON（物理ドライブからの TOC 取得は別途）

## 要件

- .NET 8 SDK
- Windows（他 OS でも動作しますが、TOC 取得は環境依存です）

## 使い方

1) ビルド

```bash
dotnet build
```

2) 実行例（gnudb に問い合わせ）

```bash
dotnet run --project src/CddbWriter -- 
  --cgi http://gnudb.gnudb.org/~cddb/cddb.cgi \
  --encoding euc-jp \
  --toc sample/toc.sample.json
```

主な引数:
- `--cgi`: FreeDB 互換サーバーの CGI URL  
  例: gnudb → `http://gnudb.gnudb.org/~cddb/cddb.cgi`  
  Freedb日本語は提供元の案内に従ってください（`http://<host>/~cddb/cddb.cgi` など）
- `--encoding`: 応答文字コード（既定: `euc-jp`）。`shift_jis` / `utf-8` も可
- `--toc`: TOC の JSON ファイルパス

TOC JSON の例（`sample/toc.sample.json`）:
```json
{
  "trackOffsetsFrames": [150, 15000, 30000, 45000, 60000, 75000, 90000, 105000, 120000, 135000],
  "leadoutOffsetFrames": 180000
}
```

3) 出力
- 一致候補一覧（`cddb query`）
- 先頭候補の XMCD（`cddb read`）を表示（DTITLE/DYEAR/DGENRE/TTITLEn）

## 実 CD から TOC を取得するには

このサンプルは「TOC を既に取得済み」という前提です。Windows での取得例:
- libdiscid を使う（推奨）  
  公式: https://musicbrainz.org/doc/libdiscid  
  C# からは P/Invoke で利用するかラッパーを使用
- 外部ツール cd-discid を呼び出して出力をパース（簡便）

取得した TOC を本プロジェクトの JSON 形式に変換して `--toc` で渡してください。

## 実装メモ

- discid 算出は FreeDB/CDDB 仕様に準拠（各トラック開始秒の各桁合計の mod 255）
- 応答コード: 200（厳密一致）/ 210, 211（複数一致）/ 202（未検出）に対応
- XMCD パースは必要最小限（DTITLE/DYEAR/DGENRE/TTITLEn）。他フィールドは Raw 辞書に格納

## ライセンス

MIT