/*
 * 振動モーター制御（4モーター版 / MOSFET方式）
 * Board: Adafruit HUZZAH32 (ESP32 Feather)
 * Driver: IRLZ44N N-ch MOSFET × 4
 * Motor: Adafruit 1201 ERM coin motor × 4 (2V-5V)
 *
 * 配線（各モーター共通）:
 *   ESP32 GPIO → 1kΩ抵抗(省略可) → MOSFET Gate
 *   USB 5V → モーター(+) → モーター(-) → MOSFET Drain
 *   MOSFET Source → GND
 *   1N4148ダイオード: モーター(-)→モーター(+) 方向に並列接続（フライバック保護）
 *
 * ピン割り当て:
 *   GPIO25 (A1) → 右かかと (Right Heel)
 *   GPIO26 (A0) → 右つま先 (Right Forefoot)
 *   GPIO27      → 左かかと (Left Heel)
 *   GPIO14      → 左つま先 (Left Forefoot)
 *
 * UDPコマンド:
 *   "VIB,rh,rf,lh,lf"  → 各モーター強度 (0-255)
 *   "T,値"              → 全モーター同一強度でテスト
 *   "STOP"              → 全モーター停止
 */

#include <WiFi.h>
#include <WiFiUdp.h>

// ===== WiFi設定 =====
const char* ssid     = "REDACTED_SSID";
const char* password = "REDACTED_PASSWORD";
const int   udpPort  = 8266;

WiFiUDP udp;

// ===== モーターピン設定 =====
#define M_RH  25   // 右かかと (Right Heel)
#define M_RF  26   // 右つま先 (Right Forefoot)
#define M_LH  27   // 左かかと (Left Heel)
#define M_LF  14   // 左つま先 (Left Forefoot)

const int motorPins[] = {M_RH, M_RF, M_LH, M_LF};
const int NUM_MOTORS = 4;

// ===== PWM設定 =====
#define PWM_FREQ  1000  // 1kHz
#define PWM_RES   8     // 8bit (0-255)

// 現在のPWM値を保持
int currentPWM[4] = {0, 0, 0, 0};

void setup() {
    Serial.begin(115200);
    Serial.println("=== Vibration Motor Controller (4ch MOSFET) ===");

    // PWM設定（全4チャンネル）
    for (int i = 0; i < NUM_MOTORS; i++) {
        ledcAttach(motorPins[i], PWM_FREQ, PWM_RES);
        ledcWrite(motorPins[i], 0);
    }

    // WiFi接続
    WiFi.begin(ssid, password);
    Serial.print("Connecting to WiFi");
    while (WiFi.status() != WL_CONNECTED) {
        delay(500);
        Serial.print(".");
    }
    Serial.println();
    Serial.println("Connected! IP: " + WiFi.localIP().toString());

    udp.begin(udpPort);
    Serial.printf("Listening on UDP port %d\n", udpPort);

    // === 起動テスト: 4モーター順番に振動 ===
    Serial.println("Startup test...");
    const char* names[] = {"RH", "RF", "LH", "LF"};
    for (int i = 0; i < NUM_MOTORS; i++) {
        Serial.printf("  Testing %s (GPIO%d)\n", names[i], motorPins[i]);
        ledcWrite(motorPins[i], 200);
        delay(300);
        ledcWrite(motorPins[i], 0);
        delay(100);
    }
    Serial.println("Ready!");
}

void setMotor(int index, int value) {
    value = constrain(value, 0, 255);
    currentPWM[index] = value;
    ledcWrite(motorPins[index], value);
}

void stopAll() {
    for (int i = 0; i < NUM_MOTORS; i++) {
        setMotor(i, 0);
    }
}

void loop() {
    // WiFi再接続チェック
    if (WiFi.status() != WL_CONNECTED) {
        Serial.println("WiFi disconnected! Reconnecting...");
        stopAll();
        WiFi.reconnect();
        while (WiFi.status() != WL_CONNECTED) {
            delay(500);
            Serial.print(".");
        }
        Serial.println("\nReconnected!");
    }

    int packetSize = udp.parsePacket();
    if (packetSize) {
        char buf[64];
        int len = udp.read(buf, sizeof(buf) - 1);
        buf[len] = '\0';

        // "VIB,rh,rf,lh,lf" をパース
        if (strncmp(buf, "VIB,", 4) == 0) {
            int rh, rf, lh, lf;
            if (sscanf(buf + 4, "%d,%d,%d,%d", &rh, &rf, &lh, &lf) == 4) {
                setMotor(0, rh);  // 右かかと
                setMotor(1, rf);  // 右つま先
                setMotor(2, lh);  // 左かかと
                setMotor(3, lf);  // 左つま先
                Serial.printf("VIB: RH=%d RF=%d LH=%d LF=%d\n", rh, rf, lh, lf);
            }
        }

        // テスト用: "T,値" で全モーター同一強度
        if (buf[0] == 'T' && buf[1] == ',') {
            int val = constrain(atoi(buf + 2), 0, 255);
            for (int i = 0; i < NUM_MOTORS; i++) {
                setMotor(i, val);
            }
            Serial.printf("TEST ALL: PWM=%d\n", val);
        }

        // 個別テスト: "T1,値" ~ "T4,値"
        if (buf[0] == 'T' && buf[1] >= '1' && buf[1] <= '4' && buf[2] == ',') {
            int ch = buf[1] - '1';  // 0-3
            int val = constrain(atoi(buf + 3), 0, 255);
            setMotor(ch, val);
            Serial.printf("TEST CH%d: PWM=%d\n", ch + 1, val);
        }

        // "STOP" で全停止
        if (strcmp(buf, "STOP") == 0) {
            stopAll();
            Serial.println("STOPPED ALL");
        }

        // "STATUS" で現在値を返す
        if (strcmp(buf, "STATUS") == 0) {
            char reply[64];
            snprintf(reply, sizeof(reply), "PWM: RH=%d RF=%d LH=%d LF=%d",
                     currentPWM[0], currentPWM[1], currentPWM[2], currentPWM[3]);
            udp.beginPacket(udp.remoteIP(), udp.remotePort());
            udp.print(reply);
            udp.endPacket();
            Serial.println(reply);
        }
    }
}
