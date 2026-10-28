#include <Arduino.h>
#include <WiFi.h>           // ESP32版 WiFiライブラリ
#include <WebServer.h>      // ESP32版 WebServerライブラリ
// #include <DNSServer.h>   // STAモードでは不要
#include <WiFiUdp.h>        // UDP通信ライブラリ
// #include <BLEDevice.h>
// #include <BLEUtils.h>
// #include <BLEServer.h>
#include "shockModule.h"    // 電気刺激機能のヘッダ

// ============================================================
//  ★ UDP トリガ設定（ここだけ各デバイスで変える）
//  DEVICE_ID: このデバイスの番号（#1=1, #2=2, ...）
//  UDP_PORT : 全台共通の受信ポート
//  パケット例: "FIRE,8,6" → #1はLevel8, #2はLevel6 で1バースト
// ============================================================
#define DEVICE_ID   6      // ← #6用に焼くときは 6 に変えるだけ
#define UDP_PORT    8266    // 全台共通
#define USE_STATIC_IP 1     // 1: IPを 192.168.11.(100+DEVICE_ID) に固定 / 0: ルータ任せ(DHCP)
// #include "Adafruit_NeoPixel.h"
#include "Arduino_BMI270_BMM150.h"


// ------------------ マクロ定義・グローバル変数 ------------------
const int batDetPin = 0;

// Web制御用のキャッシュパラメータ
// ============================================================
//  刺激パラメータ（WebUIで変更 / 電源を切ると初期値に戻る）
//  [0] Level      : 0〜15   （UDPで上書きされる）
//  [1] Width      : パルス幅 µs
//  [2] Freq       : 周波数 Hz（Levelに関係なく一定に保つ）
//  [3] Duration   : 1回の発火の刺激時間 ms
//  [4] (未使用)
// ============================================================
int slider_temp[5] = {0, 200, 150, 150, 0};

#define FREQ_MIN      10     // Hz
#define FREQ_MAX      300    // Hz
#define DURATION_MAX  1000   // ms（安全のため1回の発火は最大1秒）
#define PERIOD_MARGIN_US 100 // 充電+出力に足す余裕 µs

// 1パルスにかかる最短時間 (µs) = 充電(8×Level×41µs) + 出力(Width) + 余裕
uint32_t minPeriodUs(int level, int widthUs) {
  return 8UL * level * 41UL + (uint32_t)widthUs + PERIOD_MARGIN_US;
}

// 指定周波数で使えるLevelの上限
int maxLevelForFreq(int freqHz, int widthUs) {
  long avail = (long)(1000000L / freqHz) - widthUs - PERIOD_MARGIN_US;
  if (avail < 0) return 0;
  int lv = avail / (8L * 41L);
  return lv > 15 ? 15 : lv;
}
uint8_t switch_temp = 1;
String selectedMode_temp = "Option 0"; // デフォルト選択

String generateHTML();

// ============================================================
//  WiFi接続先（ルータの設定に合わせる）
// ============================================================
const char* ssid     = "REDACTED_SSID";     // ルータのSSID
const char* password = "REDACTED_PASSWORD";     // ルータのパスワード

WebServer webServer(80);   // WebServer（パラメータ設定用）
WiFiUDP udp;               // UDPトリガ受信用
char udpBuf[128];           // UDP受信バッファ

// 電気刺激の構造体を宣言
shockPluse_t shockPluse_s = {
    .GPIO_Pin_Boost_L = 7,
    .GPIO_Pin_Net_P   = 10,
    .GPIO_Pin_Net_N   = 6,
    .LEDC_CHANNEL     = 0,
};

// Matrix Data PIN
// #define PIN_PIXS 8
// #define PIX_NUM 1

// Adafruit_NeoPixel pixels(PIX_NUM, PIN_PIXS, NEO_GRB + NEO_KHZ800);


#ifndef I2CSPEED
#define I2CSPEED 100000
#endif

BoschSensorClass *myIMU;


// ------------------ Setup関数 ------------------
void setup() {
  Serial.begin(115200);

  // ============================================================
  //  安全: 起動直後に出力系ピンを確実にLOWにする
  //  （WiFi接続待ちの間、昇圧ピンが浮いて勝手に昇圧するのを防ぐ）
  // ============================================================
  pinMode(shockPluse_s.GPIO_Pin_Boost_L, OUTPUT); digitalWrite(shockPluse_s.GPIO_Pin_Boost_L, LOW);
  pinMode(shockPluse_s.GPIO_Pin_Net_P,   OUTPUT); digitalWrite(shockPluse_s.GPIO_Pin_Net_P,   LOW);
  pinMode(shockPluse_s.GPIO_Pin_Net_N,   OUTPUT); digitalWrite(shockPluse_s.GPIO_Pin_Net_N,   LOW);
  shockAllInit(&shockPluse_s);   // ← WiFi接続より前に初期化（元は後ろにあった）
  // pinMode(ledPin, OUTPUT); 
  // digitalWrite(ledPin, HIGH); // 先にLEDを消灯

  pinMode(batDetPin, INPUT); // GPIO0 バッテリ電圧検出 入力モード

  // ============================================================
  //  STAモード: ルータに接続
  // ============================================================
  WiFi.mode(WIFI_STA);
#if USE_STATIC_IP
  // #1 → 192.168.11.101, #2 → .102, ... #6 → .106
  IPAddress localIP(192, 168, 11, 100 + DEVICE_ID);
  IPAddress gateway(192, 168, 11, 1);
  IPAddress subnet(255, 255, 255, 0);
  WiFi.config(localIP, gateway, subnet);
#endif
  WiFi.begin(ssid, password);
  Serial.print("WiFi接続中: ");
  Serial.println(ssid);

  // 接続を待つ（最大15秒）
  int retries = 0;
  while (WiFi.status() != WL_CONNECTED && retries < 30) {
    delay(500);
    Serial.print(".");
    retries++;
  }

  if (WiFi.status() == WL_CONNECTED) {
    Serial.println();
    Serial.print("★ WiFi接続成功! IP: ");
    Serial.println(WiFi.localIP());   // ← このIPをブラウザに入れるとWebUIが開く
  } else {
    Serial.println();
    Serial.println("✖ WiFi接続失敗。SSIDとパスワードを確認してください。");
  }

  // WebServerのルーティング設定
  webServer.on("/", HTTP_GET, []() {
    webServer.send(200, "text/html", generateHTML());
  });

  webServer.on("/slider", HTTP_POST, []() {
    String sliderId = webServer.arg("slider");
    String value = webServer.arg("value");
    Serial.println("スライダー " + sliderId + " の値: " + value);
    int idx = sliderId.toInt() - 1;
    if (idx >= 0 && idx < 5) slider_temp[idx] = value.toInt();
    // 範囲外の値を防ぐ
    slider_temp[2] = constrain(slider_temp[2], FREQ_MIN, FREQ_MAX);
    slider_temp[3] = constrain(slider_temp[3], 1, DURATION_MAX);
    Serial.printf("  → ID=%d 現在値: Width=%dus Freq=%dHz Duration=%dms （この周波数の最大Level=%d）\n",
                  DEVICE_ID, slider_temp[1], slider_temp[2], slider_temp[3],
                  maxLevelForFreq(slider_temp[2], slider_temp[1]));
    webServer.send(200, "text/plain", "OK");
  });

  webServer.on("/switch", HTTP_POST, []() {
    String state = webServer.arg("state");
    Serial.println("スイッチ状態: " + String(state == "true" ? "ON" : "OFF"));
    switch_temp = state == "true" ? 1 : 0;
    webServer.send(200, "text/plain", "OK");
  });


  // 電気刺激機能の初期化は setup() 冒頭に移動済み

  // WebServerを開始（スマホからパラメータ設定用）
  webServer.begin();
  Serial.println("WebServer開始");

  // UDP受信を開始（ポート8266で待ち受け）
  udp.begin(UDP_PORT);
  Serial.print("UDP待ち受け開始 ポート: ");
  Serial.println(UDP_PORT);
  Serial.print("デバイスID: #");
  Serial.println(DEVICE_ID);

}

// ============================================================
//  周波数一定の発火処理
//  ライブラリの shockPulseSenseUnit() は「出力後に P ms 待つ」作りのため、
//  Levelで充電時間が変わると周期・周波数・刺激時間もずれる。
//  ここでは各パルスの開始時刻から「周期 = 1/Freq」ちょうどになるまで待つ。
//  （ライブラリ本体は変更せず、充電・出力の部品だけを呼び出す）
// ============================================================
void fireBurst(int level, int widthUs, int freqHz, int durationMs) {
  freqHz     = constrain(freqHz, FREQ_MIN, FREQ_MAX);
  durationMs = constrain(durationMs, 1, DURATION_MAX);
  level      = constrain(level, 1, 15);

  uint32_t periodUs = 1000000UL / freqHz;
  int count = (int)((long)durationMs * freqHz / 1000L);
  if (count < 1) count = 1;

  shockPluse_s.boost_Level = level;
  shockPluse_s.trig_Width  = widthUs;

  uint32_t needUs = minPeriodUs(level, widthUs);
  bool late = needUs > periodUs;

  static uint8_t polarity = 0;
  uint32_t tStart = micros();
  for (int i = 0; i < count; i++) {
    uint32_t t0 = micros();
    shockBoostVol(&shockPluse_s);              // ① 充電（8×Level×41µs）
    polarity = !polarity;
    shockTriggerAC(&shockPluse_s, polarity);   // ② 出力（Width µs、極性を交互に）
    while (micros() - t0 < periodUs) { }       // ③ 周期の残りだけ待つ
  }
  uint32_t totalUs = micros() - tStart;

  Serial.printf("★発火! ID=%d Level=%d Width=%dus Freq=%dHz Pulses=%d 実測%lums\n",
                DEVICE_ID, level, widthUs, freqHz, count, totalUs / 1000UL);
  if (late) {
    Serial.printf("  ⚠ Level%d は %dHz に間に合いません（実際の周波数 約%luHz）。"
                  "このWidth・周波数で使える最大Levelは %d です\n",
                  level, freqHz, 1000000UL / needUs, maxLevelForFreq(freqHz, widthUs));
  }
}

// ------------------ Loop関数 ------------------
void loop() {
  // WebUIのリクエストを処理（パラメータ設定用）
  webServer.handleClient();

  // ============================================================
  //  ★ UDPトリガ受信処理
  //  パケット形式: "FIRE,Level1,Level2,..."
  //  このデバイスは DEVICE_ID 番目の値を取り出してLevelに設定し
  //  Enable(switch_temp)がONなら1バースト発火する
  // ============================================================
  int packetSize = udp.parsePacket();  // パケットが来てるかチェック（ノンブロッキング）
  if (packetSize > 0) {
    int len = udp.read(udpBuf, sizeof(udpBuf) - 1);
    udpBuf[len] = '\0';  // 文字列終端

    // ------------------------------------------------------------
    //  "WHO" を受けたら、送り主に自分のID・IP・パラメータを返信
    //  （PythonのwhoコマンドでLAN上の全台を一覧表示するため）
    // ------------------------------------------------------------
    if (strncmp(udpBuf, "WHO", 3) == 0) {
      char reply[128];
      snprintf(reply, sizeof(reply), "HERE,%d,%s,W=%d,F=%d,D=%d,maxLv=%d,EN=%d",
               DEVICE_ID, WiFi.localIP().toString().c_str(),
               slider_temp[1], slider_temp[2], slider_temp[3],
               maxLevelForFreq(slider_temp[2], slider_temp[1]), switch_temp);
      udp.beginPacket(udp.remoteIP(), udp.remotePort());
      udp.write((const uint8_t*)reply, strlen(reply));
      udp.endPacket();
      Serial.print("WHO応答: ");
      Serial.println(reply);
      return;
    }

    Serial.print("UDP受信: ");
    Serial.println(udpBuf);

    // "FIRE," で始まるか確認
    if (strncmp(udpBuf, "FIRE,", 5) == 0) {
      // カンマ区切りでDEVICE_ID番目の値を取り出す
      // 例: "FIRE,8,6" → トークン: "FIRE", "8", "6"
      //     DEVICE_ID=1 → 2番目のトークン "8" を取得
      char *token = strtok(udpBuf, ",");  // 最初のトークン = "FIRE"（読み飛ばす）
      int tokenIdx = 0;
      int myLevel = -1;

      while (token != NULL) {
        token = strtok(NULL, ",");  // 次のトークン
        tokenIdx++;
        if (tokenIdx == DEVICE_ID && token != NULL) {
          myLevel = atoi(token);
          break;
        }
      }

      if (myLevel > 0 && switch_temp == 1) {
        slider_temp[0] = myLevel;
        fireBurst(myLevel, slider_temp[1], slider_temp[2], slider_temp[3]);
      }
      else if (switch_temp == 0) {
        Serial.println("Enable OFFのため発火しません");
      }
      else {
        Serial.println("Level=0 または自分宛でないため無視");
      }
    }
  }

  // 10秒ごとにID・IPをシリアルに表示（起動時の表示を見逃しても分かるように）
  static unsigned long lastInfo = 0;
  if (millis() - lastInfo > 10000) {
    lastInfo = millis();
    Serial.printf("[#%d] IP=%s WiFi=%s\n", DEVICE_ID,
                  WiFi.localIP().toString().c_str(),
                  WiFi.status() == WL_CONNECTED ? "OK" : "未接続");
  }

  delay(1);

}

// ------------------ HTML生成関数 ------------------

String generateHTML() {
  String html =
    "<!DOCTYPE html><html><head>"
    "<meta charset='UTF-8'>"
    "<meta name='viewport' content='width=device-width, initial-scale=1.0'>"
    "<title>SHOCK⚡32 コントロールパネル</title>"
    "<style>"
    "body {"
    "  margin: 0;"
    "  padding: 16px;"
    "  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif;"
    "  background: #020617;"          /* 暗色の背景 */
    "  color: #e5e7eb;"
    "}"
    ".container {"
    "  max-width: 520px;"            /* 元より少し大きめ */
    "  margin: 0 auto;"
    "}"
    "h1 {"
    "  font-size: 24px;"
    "  margin: 0 0 8px 0;"
    "}"
    ".subtitle {"
    "  font-size: 14px;"
    "  color: #9ca3af;"
    "  margin-bottom: 16px;"
    "}"
    ".card {"
    "  background: rgba(15,23,42,0.96);"
    "  border-radius: 18px;"
    "  padding: 16px 18px;"
    "  box-shadow: 0 18px 45px rgba(0,0,0,0.45);"
    "  border: 1px solid rgba(148,163,184,0.25);"
    "}"
    ".control-group {"
    "  margin-bottom: 18px;"
    "}"
    ".control-header {"
    "  display: flex;"
    "  justify-content: space-between;"
    "  align-items: center;"
    "  font-size: 15px;"
    "  margin-bottom: 6px;"
    "}"
    ".control-label {"
    "  font-weight: 500;"
    "}"
    ".control-value {"
    "  font-family: 'SF Mono', ui-monospace, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace;"
    "  font-size: 14px;"
    "  color: #a5b4fc;"
    "}"
    "input[type=range] {"
    "  -webkit-appearance: none;"
    "  width: 100%;"
    "  height: 8px;"                 /* スライダーを少し太く */
    "  border-radius: 999px;"
    "  background: #1f2937;"
    "  outline: none;"
    "}"
    "input[type=range]::-webkit-slider-thumb {"
    "  -webkit-appearance: none;"
    "  width: 18px;"                 /* つまみを大きく、タッチしやすく */
    "  height: 18px;"
    "  border-radius: 50%;"
    "  background: #38bdf8;"
    "  box-shadow: 0 0 0 4px rgba(56,189,248,0.25);"
    "  cursor: pointer;"
    "}"
    "input[type=range]::-moz-range-thumb {"
    "  width: 18px;"
    "  height: 18px;"
    "  border-radius: 50%;"
    "  background: #38bdf8;"
    "  box-shadow: 0 0 0 4px rgba(56,189,248,0.25);"
    "  cursor: pointer;"
    "}"
    ".section-title {"
    "  font-size: 14px;"
    "  font-weight: 500;"
    "  margin: 18px 0 8px 0;"
    "  color: #9ca3af;"
    "  text-transform: uppercase;"
    "  letter-spacing: 0.08em;"
    "}"
    ".row {"
    "  display: flex;"
    "  align-items: center;"
    "  justify-content: space-between;"
    "  gap: 12px;"
    "  margin-top: 8px;"
    "}"
    ".row-left {"
    "  display: flex;"
    "  align-items: center;"
    "  gap: 8px;"
    "  font-size: 15px;"
    "}"
    "#switch {"
    "  transform: scale(1.4);"       /* スイッチを拡大 */
    "}"
    "select {"
    "  padding: 6px 10px;"
    "  border-radius: 999px;"
    "  border: 1px solid #4b5563;"
    "  background: #020617;"
    "  color: #e5e7eb;"
    "  font-size: 14px;"
    "}"
    "input[type=number] {"
    "  width: 64px;"
    "  padding: 4px 8px;"
    "  border-radius: 8px;"
    "  border: 1px solid #4b5563;"
    "  background: #020617;"
    "  color: #a5b4fc;"
    "  font-size: 14px;"
    "  text-align: right;"
    "  font-family: ui-monospace, Menlo, monospace;"
    "}"
    "</style>"
    "</head><body>"
    "<div class='container'>"
    "<h1>コントロールパネル #" + String(DEVICE_ID) + "</h1>"
    "<p class='subtitle'>パラメータ調整・強度は慎重に上げて、安全に注意 (Adjust carefully)</p>"
    "<div class='card'>"

    "<div class='section-title'>PARAMETERS / パラメータ</div>";

  // スライダー1: 強度レベル / Level
  html +=
    "<div class='control-group'>"
    "  <div class='control-header'>"
    "    <span class='control-label'>強度レベル / Level (0–15)</span>"
    "    <input type='number' id='num1' min='0' max='15'"
    "           value='" + String(slider_temp[0]) + "'"
    "           onchange='updateFromNumber(1, this.value)' />"
    "  </div>"
    "  <input type='range' id='slider1' min='0' max='15' step='1'"
    "         value='" + String(slider_temp[0]) + "'"
    "         oninput='updateFromSlider(1, this.value)' />"
    "</div>";

  // スライダー2: パルス幅 / Width
  html +=
    "<div class='control-group'>"
    "  <div class='control-header'>"
    "    <span class='control-label'>パルス幅 / Width (μs)</span>"
    "    <input type='number' id='num2' min='0' max='350'"
    "           value='" + String(slider_temp[1]) + "'"
    "           onchange='updateFromNumber(2, this.value)' />"
    "  </div>"
    "  <input type='range' id='slider2' min='0' max='350' step='1'"
    "         value='" + String(slider_temp[1]) + "'"
    "         oninput='updateFromSlider(2, this.value)' />"
    "</div>";

  // スライダー3: 周波数 / Frequency
  html +=
    "<div class='control-group'>"
    "  <div class='control-header'>"
    "    <span class='control-label'>周波数 / Frequency (Hz)</span>"
    "    <input type='number' id='num3' min='" + String(FREQ_MIN) + "' max='" + String(FREQ_MAX) + "'"
    "           value='" + String(slider_temp[2]) + "'"
    "           onchange='updateFromNumber(3, this.value)' />"
    "  </div>"
    "  <input type='range' id='slider3' min='" + String(FREQ_MIN) + "' max='" + String(FREQ_MAX) + "' step='1'"
    "         value='" + String(slider_temp[2]) + "'"
    "         oninput='updateFromSlider(3, this.value)' />"
    "</div>";

  // スライダー4: 刺激時間 / Duration
  html +=
    "<div class='control-group'>"
    "  <div class='control-header'>"
    "    <span class='control-label'>刺激時間 / Duration (ms)</span>"
    "    <input type='number' id='num4' min='10' max='" + String(DURATION_MAX) + "'"
    "           value='" + String(slider_temp[3]) + "'"
    "           onchange='updateFromNumber(4, this.value)' />"
    "  </div>"
    "  <input type='range' id='slider4' min='10' max='" + String(DURATION_MAX) + "' step='1'"
    "         value='" + String(slider_temp[3]) + "'"
    "         oninput='updateFromSlider(4, this.value)' />"
    "</div>";

  // 計算結果の表示（JavaScriptで自動更新）
  html +=
    "<div class='control-group' style='font-size:14px;color:#9ca3af;line-height:1.7'>"
    "  パルス数: <span id='info_count' class='control-value'></span> 発 / "
    "  周期: <span id='info_period' class='control-value'></span> ms<br>"
    "  この幅・周波数で使える最大Level: <span id='info_maxlv' class='control-value'></span>"
    "</div>";

  // スイッチ + モード選択を1行に
  String checkedStr = (switch_temp == 1) ? "checked" : "";
  html +=
    "<div class='section-title'>SAFETY & MODE / 安全・モード</div>"
    "<div class='row'>"
    "  <div class='row-left'>"
    "    <input type='checkbox' id='switch' onchange='updateSwitch(this.checked)' " + checkedStr + " />"
    "    <span>安全に注意 / Enable (Safety)</span>"
    "  </div>"
    "  <div>"
    "    <label for='mode' style='font-size:13px;color:#9ca3af;margin-right:4px;'>モード / Mode</label>"
    "    <select id='mode' onchange='updateMode(this.value)'>";

  String modes[] = {"Option 0", "Option 1", "Option 2", "Option 3"};
  for (String mode : modes) {
    html += "<option value='" + mode + "'" +
            (mode.equals(selectedMode_temp) ? " selected" : "") +
            ">" + mode + "</option>";
  }

  html +=
    "    </select>"
    "  </div>"
    "</div>"   // .row
    "</div>"   // .card
    "</div>"   // .container
    "<script>"
    "function sendSlider(sliderId, value) {"
    "  fetch(`/slider`, {"
    "    method: 'POST',"
    "    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },"
    "    body: `slider=${sliderId}&value=${value}`"
    "  }).then(response => {"
    "    if (!response.ok) throw new Error('Network response was not ok.');"
    "  }).catch(error => console.error('Fetch error:', error));"
    "}"
    "function updateInfo() {"
    "  var w = parseInt(document.getElementById('num2').value) || 0;"
    "  var f = parseInt(document.getElementById('num3').value) || 1;"
    "  var d = parseInt(document.getElementById('num4').value) || 0;"
    "  var per = 1000000 / f;"
    "  var lv = Math.floor((per - w - 100) / 328);"
    "  if (lv > 15) lv = 15; if (lv < 0) lv = 0;"
    "  document.getElementById('info_count').textContent = Math.max(1, Math.floor(d * f / 1000));"
    "  document.getElementById('info_period').textContent = (per / 1000).toFixed(2);"
    "  var el = document.getElementById('info_maxlv');"
    "  el.textContent = lv + (lv < 15 ? '（これより上のLevelは周波数が下がります）' : '（全Level OK）');"
    "  el.style.color = lv < 15 ? '#f87171' : '#a5b4fc';"
    "}"
    "window.addEventListener('load', updateInfo);"
    "function updateFromSlider(id, value) {"
    "  document.getElementById('num' + id).value = value;"
    "  updateInfo();"
    "  sendSlider(id, value);"
    "}"
    "function updateFromNumber(id, value) {"
    "  var slider = document.getElementById('slider' + id);"
    "  var v = parseInt(value);"
    "  if (isNaN(v)) return;"
    "  var lo = parseInt(slider.min);"
    "  var hi = parseInt(slider.max);"
    "  if (v < lo) v = lo;"
    "  if (v > hi) v = hi;"
    "  document.getElementById('num' + id).value = v;"
    "  slider.value = v;"
    "  updateInfo();"
    "  sendSlider(id, v);"
    "}"
    "function updateSwitch(state) {"
    "  fetch(`/switch`, {"
    "    method: 'POST',"
    "    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },"
    "    body: `state=${state}`"
    "  }).then(response => {"
    "    if (!response.ok) throw new Error('Network response was not ok.');"
    "  }).catch(error => console.error('Fetch error:', error));"
    "}"
    "function updateMode(mode) {"
    "  fetch(`/mode`, {"
    "    method: 'POST',"
    "    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },"
    "    body: `mode=${mode}`"
    "  }).then(response => {"
    "    if (!response.ok) throw new Error('Network response was not ok.');"
    "  }).catch(error => console.error('Fetch error:', error));"
    "}"
    "</script>"
    "</body></html>";

  return html;
}