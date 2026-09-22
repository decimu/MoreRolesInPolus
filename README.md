# MoreRolesInPolus

Toa x junjun

Nebula on the Ship 用アドオン MOD。  
新しい役職・モディファイアを追加します。

---

## Imposter

### 潜望者

特製のベント（びっくり箱）を設置・使用できます。  
びっくり箱はカメラとしても機能するため、設置場所は慎重に考える必要があります。

### スポイラー

キルした相手の役職が分かり、  
その役職が **残り何人いるか** を判別できます。

※ 判別される人数はキル時点のもので、後から変化しません。

---

## Crewmate

### リサーチャー

他プレイヤーの **過去の行動** を調査できます。  
ログを辿った先に見える真実は、希望か、それとも絶望か。

---

## Neutral

### アキューサー

他役職を **一定数推測** することで勝利できます。

---

## Modifier

### スポイラー

インポスター役職「スポイラー」の  
**モディファイア版** です。

---

## 開発者向け情報

### プロジェクト構成

```
MoreRolesInPolus/
├── MoreRolesInPolus/             # ソースコード（.cs, リソースファイル）
│   ├── Scripts/                 # 役職・ロジック実装
│   ├── Language/                # 言語ファイル
│   ├── Resources/               # 画像・アセット
│   └── addon.meta               # アドオン設定
├── MoreRolesInPolus.csproj.base # プロジェクトファイル（テンプレート）
├── MoreRolesInPolus.csproj      # 各自コピーして使う（Git管理外）
├── MoreRolesInPolus.sln         # ソリューション
└── .github/workflows/           # GitHub Actions（自動リリース）
```

### 初期セットアップ（必須）

#### 1. リポジトリのクローン

```powershell
git clone https://github.com/10-ui/MoreRolesInPolus.git
cd MoreRolesInPolus
```

#### 2. プロジェクトファイルのコピー

```powershell
# PowerShell
Copy-Item MoreRolesInPolus.csproj.base MoreRolesInPolus.csproj
```

または手動で `MoreRolesInPolus.csproj.base` を `MoreRolesInPolus.csproj` にコピー。

#### 3. 環境変数の設定

Windows の環境変数に `AmongUs` を追加：

1. **システムのプロパティ** → **環境変数**
2. **ユーザー環境変数** に追加：
   - 変数名: `AmongUs`
   - 変数値: `D:\Steam\steamapps\common\Among Us NoS_dev` （あなたのパス）

#### 4. Visual Studio で開く

1. `MoreRolesInPolus.sln` を開く
2. ソリューション エクスプローラーにソースコードが表示されることを確認
3. **ビルド → ソリューションのビルド（Ctrl+Shift+B）** で初回ビルド

> ⚠️ **重要**: 
> - `MoreRolesInPolus.csproj` は Git 管理外（各自の環境で作成）
> - `.csproj.base` をカスタマイズしたい場合は、**必ず `.csproj` を編集**してください
> - `.csproj.base` は共通テンプレートなので、個人的な変更は加えないこと

### ビルド方法

Visual Studio・VS Code のどちらでも `Ctrl+Shift+B` でビルドできます。

- **Visual Studio**: 構成（Debug / Release）を選んで `Ctrl+Shift+B`
- **VS Code**: `Ctrl+Shift+B` で Debug ビルドが始まり、起動する Among Us の数（0〜6）を選べます
  - Release は **ターミナル → タスクの実行…** から「Release ビルド」を選択
  - タスクは `.vscode/tasks.json` に定義されています（.NET SDK が必要）
- **Cursor**: `Ctrl+Shift+B` はブラウザに割り当てられているため、次のどちらかで実行
  - コマンドパレット（`Ctrl+Shift+P`）→「Tasks: Run Build Task」
  - キーを割り当てる: コマンドパレット →「Preferences: Open Keyboard Shortcuts (JSON)」を開き、以下を追加（例: F7）
    ```json
    { "key": "f7", "command": "workbench.action.tasks.build" }
    ```

- **Debug**: `Among Us NoS_dev\Addons\[Toa]MoreRolesInPolus.zip` に出力（開発用）
  - ビルド後、自動で Among Us が起動します
  - 起動数を変えたい場合: `dotnet build -c Debug /p:LaunchCount=2`
  - 起動しない場合: `dotnet build -c Debug /p:LaunchCount=0`
- **Release**: `bin\Release\MoreRolesInPolus.zip` + Among Us フォルダに出力（配布用）

> ⚠️ **F5（デバッグ実行）ではなく、ビルド（Ctrl+Shift+B）のみ使用してください**

### トラブルシューティング

#### ビルドエラー: 「Among Us のパスが見つかりません」

- 環境変数 `AmongUs` が正しく設定されているか確認
- Visual Studio / VS Code を**再起動**（環境変数の変更を反映）

#### API 参照エラー（`GetRoomName` など）

NebulaAPI（NuGet）と Nebula.dll（ゲーム内）のバージョンが異なる場合があります。

**対処法**: リフレクションで動的に呼び出す

```csharp
using System.Reflection;

var map = NebulaAPI.CurrentGame.CurrentMap;
var method = map.GetType().GetMethod("GetRoomName", 
    new[] { typeof(Vector2), typeof(bool), typeof(bool), typeof(bool) });
var result = (string?)method?.Invoke(map, new object[] { pos, false, false, false });
```

### Git ワークフロー

```
feature/researcher ──┐
feature/anchor ──────┼→ develop ─→ main
                     │  (結合テスト) (安定版)
                        ↓         ↓
                    Snapshot   Major
```

#### ブランチの役割

- **feature/xxx**: 個別の役職開発
- **develop**: 結合テスト・統合（Snapshotリリース）
- **main**: テスト済みの安定版（メジャーリリース）

#### 開発フロー

**1. 機能開発:**
```bash
git checkout -b feature/researcher
# 開発...
git push origin feature/researcher
```

**2. 結合テスト（develop）:**
```bash
# GitHubでPR作成: feature/researcher → develop
# マージすると自動で s,Snapshot_25.12.30a リリース
```

**3. テスト・確認:**
- Among Us で Snapshot版をテスト
- 問題あれば修正して再度develop にマージ

**4. 安定版リリース（main）:**
```bash
# MoreRolesInPolus/addon.meta の "Version" を "1.0.0" に上げて develop にコミット
# develop を main にマージ（PRタイトルは自由。git push origin develop:main でも可）
# → 自動で v1.0.0 リリース
```

### リリース方法

#### 自動リリースの仕組み

| 項目 | Snapshot（開発版） | Major（正式版） |
|------|-------------------|----------------|
| **トリガー** | `develop` へpush | `main` へpush（PRマージ含む） |
| **バージョン** | 日付から自動採番 | `addon.meta` の `Version` |
| **条件** | `MoreRolesInPolus/**` 変更時のみ | `MoreRolesInPolus/**` 変更時のみ |
| **タグ形式** | `s,Snapshot_25.12.30a` | `v,v1.0.0` |
| **リリース名** | `MoreRolesInPolus-Snapshot_25.12.30a` | `MoreRolesInPolus-v1.0.0` |
| **ファイル名** | `MoreRolesInPolus-Snapshot_25.12.30a.zip` | `MoreRolesInPolus-v1.0.0.zip` |
| **Latest** | ❌ No | ✅ Yes |
| **Pre-release** | ✅ Yes | ❌ No |
| **マーケットプレイス自動配信** | ❌ されない | ✅ される |

> ⚠️ **重要**: `.csproj` や `README.md` などの変更のみでは自動リリースは**発動しません**。  
> `MoreRolesInPolus/` フォルダ内のコード変更時のみリリースが作成されます。

> ℹ️ NoS のマーケットプレイスは Latest リリースだけを自動配信します。  
> Snapshot は Pre-release なので一般プレイヤーには届かず、MRIP のバージョン画面から選んでインストールします。

#### Snapshot（開発版）リリース

**自動で作成されます：**

1. `MoreRolesInPolus/` 内のファイルを編集
2. `develop` ブランチにpush（直接 or PRマージ）
3. 自動で `s,Snapshot_25.12.30a` タグ + Pre-release 作成
   - 同日の2回目以降は `b`, `c`, `d`... とサフィックス付与
   - 変更内容には前回の Snapshot 以降のコミットがすべて載ります

**例:**
```bash
# Coordinator.cs を編集
git add MoreRolesInPolus/Scripts/Roles/Imposter/Coordinator.cs
git commit -m "feat: update Coordinator ability"
git push origin develop

# → 自動で s,Snapshot_25.12.30a リリース作成
```

#### Major（正式版）リリース

**`addon.meta` の `Version` を上げて main に入れるだけ：**

1. `MoreRolesInPolus/addon.meta` の `"Version"` を上げる（例: `"1.0.0"`、`1.2.3` の形式）
2. develop にコミットして push（この時点では Snapshot が作られる）
3. develop を main にマージ（PR でも `git push origin develop:main` でも可。PRタイトルは自由）
4. 自動で `v,v1.0.0` タグ + `MoreRolesInPolus-v1.0.0.zip` リリース作成

> ⚠️ `Version` を上げ忘れて main に入れると、「リリース済み」としてワークフローが失敗します（メールで通知されます）。  
> `Version` を上げて再度 main に入れれば、そこでリリースされます。

**リリースをやり直す方法:**

リリースが途中で失敗した場合は、GitHub の `Actions` タブ → `Major Release` → `Run workflow`（ブランチ `main`）で再実行できます。  
（タグを手動で push してもリリースは作成されません）

### リリースの確認

[Releases ページ](https://github.com/10-ui/MoreRolesInPolus/releases) で確認できます。

---
