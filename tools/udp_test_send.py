#!/usr/bin/env python3
"""
EMS 6ch UDP テスト送信ツール

openTENS デバイスに FIRE パケットを送信して動作確認する。
Unity を使わずにターミナルから EMS を発火できる。

使い方:
  python udp_test_send.py                  # 対話モード
  python udp_test_send.py --ch 2           # チャンネル2だけ発火
  python udp_test_send.py --ch 2 --level 8 # チャンネル2をレベル8で発火
  python udp_test_send.py --all 5          # 全チャンネルをレベル5で発火
  python udp_test_send.py --who            # ネットワーク上のデバイスを検索

ネットワーク設定:
  ブロードキャスト: 192.168.11.255:8266
  WiFi: REDACTED_SSID
"""

import socket
import sys
import argparse
import time
import select


BROADCAST_IP = "192.168.11.255"
PORT = 8266
DEFAULT_LEVEL = 5
NUM_CHANNELS = 6


def send_fire(sock, levels):
    """FIRE パケットを送信する"""
    msg = "FIRE," + ",".join(str(l) for l in levels)
    sock.sendto(msg.encode("utf-8"), (BROADCAST_IP, PORT))
    print(f"  送信: {msg}")


def send_who(sock, timeout=2.0):
    """WHO コマンドを送信してデバイスを検索する"""
    sock.sendto(b"WHO", (BROADCAST_IP, PORT))
    print(f"  WHO → {BROADCAST_IP}:{PORT}")
    print(f"  {timeout}秒 待機中...")

    responses = []
    deadline = time.time() + timeout
    while time.time() < deadline:
        remaining = deadline - time.time()
        if remaining <= 0:
            break
        ready, _, _ = select.select([sock], [], [], remaining)
        if ready:
            data, addr = sock.recvfrom(1024)
            msg = data.decode("utf-8", errors="replace")
            responses.append((addr, msg))
            print(f"  ← {addr[0]}:{addr[1]} | {msg}")

    if not responses:
        print("  デバイスが見つかりませんでした")
    else:
        print(f"\n  {len(responses)} 台のデバイスが応答しました")
    return responses


def interactive_mode(sock):
    """対話モードでチャンネル番号を入力して発火する"""
    print("\n=== EMS テスト送信（対話モード） ===")
    print(f"  ブロードキャスト: {BROADCAST_IP}:{PORT}")
    print(f"  チャンネル: 1=R Tib, 2=R Quad, 3=R Ham, 4=L Tib, 5=L Quad, 6=L Ham")
    print(f"  コマンド: 1-6=チャンネル発火, a=全ch発火, w=WHO検索, q=終了")
    print()

    level = DEFAULT_LEVEL
    while True:
        try:
            cmd = input(f"  [Level={level}] > ").strip().lower()
        except (EOFError, KeyboardInterrupt):
            print()
            break

        if cmd == "q" or cmd == "quit":
            break
        elif cmd == "w" or cmd == "who":
            send_who(sock)
        elif cmd == "a" or cmd == "all":
            levels = [level] * NUM_CHANNELS
            send_fire(sock, levels)
        elif cmd.startswith("l") or cmd.startswith("level"):
            # レベル変更: l8 or level 8
            try:
                new_level = int(cmd.split()[-1] if " " in cmd else cmd[1:])
                if 0 <= new_level <= 15:
                    level = new_level
                    print(f"  レベル → {level}")
                else:
                    print("  レベルは 0〜15 の範囲で指定してください")
            except ValueError:
                print("  例: l8 or level 8")
        elif cmd.isdigit():
            ch = int(cmd)
            if 1 <= ch <= NUM_CHANNELS:
                levels = [0] * NUM_CHANNELS
                levels[ch - 1] = level
                send_fire(sock, levels)
            else:
                print(f"  チャンネルは 1〜{NUM_CHANNELS} の範囲で指定してください")
        elif cmd == "":
            continue
        else:
            print("  コマンド: 1-6, a(全ch), w(WHO), l<数字>(レベル変更), q(終了)")


def main():
    parser = argparse.ArgumentParser(description="EMS 6ch UDP テスト送信")
    parser.add_argument("--ch", type=int, help="発火するチャンネル (1-6)")
    parser.add_argument("--level", type=int, default=DEFAULT_LEVEL, help=f"刺激レベル (0-15, default={DEFAULT_LEVEL})")
    parser.add_argument("--all", type=int, metavar="LEVEL", help="全チャンネルを指定レベルで発火")
    parser.add_argument("--who", action="store_true", help="ネットワーク上のデバイスを検索")
    parser.add_argument("--ip", default=BROADCAST_IP, help=f"ブロードキャストIP (default={BROADCAST_IP})")
    parser.add_argument("--port", type=int, default=PORT, help=f"UDPポート (default={PORT})")
    args = parser.parse_args()

    global BROADCAST_IP, PORT
    BROADCAST_IP = args.ip
    PORT = args.port

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    sock.bind(("", 0))

    try:
        if args.who:
            send_who(sock)
        elif args.all is not None:
            levels = [args.all] * NUM_CHANNELS
            send_fire(sock, levels)
        elif args.ch is not None:
            if not (1 <= args.ch <= NUM_CHANNELS):
                print(f"エラー: チャンネルは 1〜{NUM_CHANNELS} の範囲で指定してください")
                sys.exit(1)
            levels = [0] * NUM_CHANNELS
            levels[args.ch - 1] = args.level
            send_fire(sock, levels)
        else:
            interactive_mode(sock)
    finally:
        sock.close()


if __name__ == "__main__":
    main()
