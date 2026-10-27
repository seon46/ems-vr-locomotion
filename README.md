# EMS-VR Locomotion Interface

座位VRユーザーに対して、多チャンネル下肢電気刺激（EMS）と歩行アニメーションを同期させることで擬似歩行感覚を生成するロコモーションインターフェースの研究プロジェクト。

**Saitama University / Parinya Lab — 遠藤 瀬音（Endo Seno）**

## 概要

本研究では、座ったままVR空間を歩き回れるロコモーションインターフェースを開発しています。ユーザーの下肢EMG（筋電図）信号を入力として移動を制御し、同時にEMS（電気筋刺激）で歩行に伴う筋収縮感覚を再現することで、直感的かつ没入感の高い歩行体験を目指します。

### 研究の特徴

- **EMG入力 + EMS歩行感覚補完の統合**: 入力と触覚フィードバックを一つのシステムに統合
- **座位での利用**: 車椅子ユーザーやオフィス環境など、歩行が困難・不適切な状況でも利用可能
- **多チャンネル刺激**: Battye et al. (1966) の筋活動データに基づく6チャンネルEMS + 4チャンネル足裏振動フィードバック
- **先行研究の発展**: LegSense (Um et al., 2024) をベースに、ロコモーションインターフェースとしての操作性を追求

## システム構成

```
┌─────────────────────────────────────────────────────────────┐
│  Unity (PC)                                                 │
│  ├── VR Scene (Meta Quest 3S via Quest Link)                │
│  ├── Walking Animation (Mixamo)                             │
│  ├── EMSTrigger.cs (Normalized Time → 筋活動タイミング発火)  │
│  └── UDP Broadcast (192.168.11.255:8266)                    │
│       ├── "FIRE,l1,l2,l3,l4,l5,l6"                         │
│       └── "VIB,rh,rf,lh,lf"                                │
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
| XIAO ESP32-C3 × 6 | マイコン（WiFi/UDP受信） |
| AC_TENS SHOCK MODULE v2 × 6 | EMS出力モジュール |
| キャリアボード | KiCad設計、JLCPCB製造 |

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
├── firmware/
│   ├── vibration_motor/     # 足裏振動モーターファームウェア (Arduino/ESP32)
│   │   └── vibration_4motor.ino
│   └── ems_trigger/         # EMS制御ファームウェア (PlatformIO/ESP32-C3)
├── hardware/                # 回路図・基板設計データ (KiCad)
└── docs/                    # 研究資料・配線図
```

## 先行研究

- Um, L. et al. (2024). **LegSense**: Inducing Walking Sensation in Seated VR by Electrical Muscle Stimulation. *ACM UIST 2024*.
- Takahashi, A. et al. (2022). Increasing a Seated Passenger's Walking Sensation Using EMS. *Frontiers in VR*.
- Ota, S. et al. (2024). The Effect of Electrical Muscle Stimulation on Foot Pressure Distribution and Walking Sensation in Passive Walking in Seated VR. *Frontiers in VR*.
- Battye, C.K. & Joseph, J. (1966). An investigation by telemetering of the activity of some muscles in walking. *Medical & Biological Engineering*.

## 通信プロトコル

WiFiネットワーク上でUDPブロードキャストを使用:

- **ポート**: 8266
- **EMS**: `FIRE,l1,l2,l3,l4,l5,l6` — 各チャンネルの強度 (0/1)
- **振動**: `VIB,rh,rf,lh,lf` — 各モーターのPWM強度 (0-255)
- **テスト**: `T,値` / `T1,値`〜`T4,値` / `STOP` / `STATUS`

## ライセンス

研究用プロジェクト。コードの再利用については著者にお問い合わせください。

## 著者

**遠藤 瀬音 / Endo Seno**
埼玉大学大学院 理工学研究科 数理電子情報系専攻（M1）
Parinya Lab
