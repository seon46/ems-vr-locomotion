# EMS-VR Locomotion Interface

座位VRユーザーに擬似的な歩行感覚を生成するロコモーションインターフェースの研究プロジェクト。
6チャンネルの下肢EMS（電気筋刺激）と足裏振動を歩行アニメーションに同期させることで、座ったままでも「歩いている」と感じられる体験を目指す。

**埼玉大学大学院 理工学研究科 数理電子情報系専攻 / Parinya Lab — 遠藤 瀬音（Endo Seno）**

## 概要

本研究では、VR空間内の歩行アニメーションに合わせて下肢の筋肉にEMSを与え、歩行に伴う筋収縮感覚を再現する座位向けロコモーションインターフェースを開発しています。

先行研究 LegSense（Um et al., UIST 2024）の歩行感覚EMS提示をベースに、VRアバターのスタミナ低下に連動した疲労感の演出へ拡張することを目指しています。

### 実装済みの機能

- **6チャンネルEMS歩行同期**: Battye et al. (1966) の筋活動データに基づき、歩行アニメーションのNormalized Timeから各筋の収縮タイミングでEMSを発火
- **足裏振動フィードバック**: 4チャンネル（両足の踵・前足部）のERMモーターによる接地感の提示（ファームウェア実装済み、Unity側は未統合）
- **VR歩行環境**: 手続き的生成の廊下シーン、鏡による自己アバター視認、歩行同期のhead bob、足音再生

### 安全基準

- 出力上限: ≤ 10 mA RMS、≤ ±50 V
- 1回の発火は最大1秒（ファームウェアで制限）
- ヒトへの適用前にIRB承認を取得予定

## システム構成

```
┌─────────────────────────────────────────────────────────────┐
│  Unity (PC)                                                 │
│  ├── VR Scene (Meta Quest 3S via Quest Link)                │
│  ├── Walking Animation (Mixamo)                             │
│  ├── EMSTrigger.cs (Normalized Time → 筋活動タイミング発火)  │
│  └── UDP Broadcast (192.168.11.255:8266)                    │
│       ├── "FIRE,l1,l2,l3,l4,l5,l6"  (EMS, Level 0〜15)    │
│       └── "VIB,rh,rf,lh,lf"         (振動, PWM 0〜255)    │
└──────────┬──────────────────────────┬───────────────────────┘
           │                          │
    ┌──────▼──────┐          ┌────────▼────────┐
    │ EMS Module  │          │ Vibration Motor │
    │ ×6 units    │          │ Controller      │
    │             │          │                 │
    │ XIAO        │          │ ESP32 HUZZAH32  │
    │ ESP32-C3    │          │ + IRLZ44N ×4   │
    │ + AC_TENS   │          │ + ERM ×4       │
    │ SHOCK v2    │          │ (Adafruit 1201) │
    └─────────────┘          └─────────────────┘
    大腿四頭筋 (L/R)          右かかと / 右つま先
    ハムストリングス (L/R)     左かかと / 左つま先
    前脛骨筋 (L/R)
```

## ハードウェア

### EMS刺激モジュール（6ch）

| コンポーネント | 説明 |
|---|---|
| XIAO ESP32-C3 × 6 | マイコン（WiFi/UDP受信、IP固定: 192.168.11.101〜106） |
| AC_TENS SHOCK MODULE v2 × 6 | EMS出力モジュール |
| キャリアボード × 6 | KiCad設計、JLCPCB製造 |

### 振動フィードバック（4ch）

| コンポーネント | 説明 |
|---|---|
| Adafruit HUZZAH32 (ESP32 Feather) | マイコン（WiFi/UDP受信・PWM出力） |
| IRLZ44N N-ch MOSFET × 4 | モータードライバ |
| Adafruit 1201 ERM Motor × 4 | φ10mm コイン型振動モーター |
| 1N4148 ダイオード × 4 | フライバック保護 |

### VR

| コンポーネント | 説明 |
|---|---|
| Meta Quest 3S | HMD（Quest Link接続） |
| Unity 6.5 | ゲームエンジン |

## ディレクトリ構成

```
ems-vr-locomotion/
├── README.md
├── unity/
│   └── Scripts/
│       ├── GaitTiming.cs          # 歩行位相の共有プロバイダ（全スクリプトが参照）
│       ├── EMSTrigger.cs          # Normalized Time → EMS発火 (UDP FIRE)
│       ├── FootstepAudio.cs       # 踵接地タイミングで足音再生
│       ├── HeadFollow.cs          # 首支点のVRカメラ追従 + 歩行同期head bob
│       ├── WalkInput.cs           # XRコントローラー/キーボードによる移動入力
│       ├── VRIKTargetFollower.cs  # XRハンドトラッキング → IKターゲット追従
│       ├── CorridorGenerator.cs   # 手続き的生成の廊下シーン
│       ├── MirrorReflection.cs    # 平面鏡反射カメラ（自己アバター視認用）
│       └── GaitDiagnostics.cs     # 踵接地位相の実測ツール
├── firmware/
│   ├── ems_trigger/               # EMS制御ファームウェア (PlatformIO/ESP32-C3)
│   │   └── main.cpp
│   └── vibration_motor/           # 足裏振動モーターファームウェア (Arduino/ESP32)
│       └── vibration_4motor.ino
├── hardware/
│   └── carrier_board/             # EMSキャリアボード基板設計データ (KiCad)
│       └── Gerber/                # 製造用ガーバーファイル
└── tools/
    └── udp_test_send.py           # EMS UDPテスト送信ツール (Python)
```

## 通信プロトコル

WiFiネットワーク上でUDPブロードキャストを使用:

| 項目 | 内容 |
|---|---|
| ポート | 8266 |
| EMS発火 | `FIRE,l1,l2,l3,l4,l5,l6` — 各チャンネルのLevel (0〜15) |
| 振動制御 | `VIB,rh,rf,lh,lf` — 各モーターのPWM強度 (0〜255) |
| デバイス探索 | `WHO` → 各デバイスがID・IP・パラメータを返信 |

各EMSデバイスはWebUI（HTTP）でパルス幅・周波数・刺激時間のパラメータを設定できる。

## 今後の予定

- 足裏振動のUnity側統合（ファームウェアは実装済み）
- スタミナ連動型の疲労感演出の設計・実装（歩行パターンの劣化 + クロスモーダル知覚による疲労感の表現）
- オシロスコープによる出力波形の安全性検証
- IRB申請・承認
- ユーザースタディの設計・実施

## 先行研究

- Um, L. et al. (2024). **LegSense**: Inducing Walking Sensation in Seated VR by Electrical Muscle Stimulation. *ACM UIST 2024*.
- Takahashi, A. et al. (2022). Increasing a Seated Passenger's Walking Sensation Using EMS. *Frontiers in VR*.
- Ota, S. et al. (2024). The Effect of Electrical Muscle Stimulation on Foot Pressure Distribution and Walking Sensation in Passive Walking in Seated VR. *Frontiers in VR*.
- Battye, C.K. & Joseph, J. (1966). An investigation by telemetering of the activity of some muscles in walking. *Medical & Biological Engineering*.

## ライセンス

研究用プロジェクト。コードの再利用については著者にお問い合わせください。

## 著者

**遠藤 瀬音 / Endo Seno**
埼玉大学大学院 理工学研究科 数理電子情報系専攻（M1）
Parinya Lab
