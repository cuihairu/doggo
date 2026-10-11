#!/usr/bin/env bash
# Mobile acceptance: install the APK on the booted emulator and walk the core
# flow (login → browse → open a case → order lookup), screenshotting each step.
# Coordinates target a 1080x1920 420dpi emulator and were calibrated against
# the artifact screenshots of earlier runs.
set -uxo pipefail

PKG=io.github.cuihairu.doggo
SHOTS=acceptance
APK=${APK:-app-release.apk}
mkdir -p "$SHOTS"

shot() {
  sleep "${2:-2}"
  adb exec-out screencap -p > "$SHOTS/$1.png"
}

adb install -r "$APK"
adb shell monkey -p "$PKG" 1
sleep 15
shot 01-home 0

# Home entry grid: 3 buttons per row (row1: 商品开箱/行情/我的奖品, row2: 订单/个人中心/登录).
adb shell input tap 180 250
shot 02-products 8
adb shell input keyevent 4
shot 03-back-home 2

# Market entry (row1 middle).
adb shell input tap 540 250
shot 04-market 6
adb shell input keyevent 4

# Orders entry (row2 left).
adb shell input tap 180 400
shot 05-orders-empty 4
adb shell input keyevent 4

# Products → first product card.
adb shell input tap 180 250
shot 06-products-again 6
adb shell input tap 540 430
shot 07-product 6

# Buy (单开) without login → app routes to the login page.
adb shell input tap 540 700
shot 08-login 5

# Fill account/password and submit.
adb shell input tap 540 610
sleep 1
adb shell input text "ciuser"
adb shell input tap 540 760
sleep 1
adb shell input text "Passw0rd123"
adb shell input tap 540 1010
shot 09-after-login 8

# Back out of login/user back to the product page, then buy for real.
adb shell input keyevent 4
shot 10-back-login 2
adb shell input keyevent 4
shot 11-product-again 3
adb shell input tap 540 700
shot 12-opening 10

# Home → orders: the paid order must show up.
adb shell input keyevent 4
shot 13-after-opening 2
adb shell input keyevent 4
shot 14-back-products 2
adb shell input keyevent 4
shot 15-back-home 2
adb shell input tap 180 400
shot 16-orders 5

ls -la "$SHOTS"
