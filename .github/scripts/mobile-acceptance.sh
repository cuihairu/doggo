#!/usr/bin/env bash
# Mobile acceptance: install the APK on the booted emulator and walk the core
# flow (login → browse → buy → pay → order lookup), screenshotting each step.
# Interactions target UI nodes by text/class through uiautomator dumps, so the
# script does not depend on hardcoded pixel coordinates. Assertions exit 1 and
# leave the ui dump behind when a screen does not show what is expected.
set -uxo pipefail

PKG=io.github.cuihairu.doggo
SHOTS=acceptance
APK=${APK:-app-release.apk}
mkdir -p "$SHOTS"

shot() {
  sleep "${2:-2}"
  adb exec-out screencap -p > "$SHOTS/$1.png"
}

dump() {
  adb shell uiautomator dump /sdcard/ui.xml > /dev/null 2>&1 || return 1
  adb shell cat /sdcard/ui.xml
}

# First bounds pair in the dump whose node text equals $1 (or contains $1 when
# $2=contains). Bounds look like [x1,y1][x2,y2].
find_bounds() {
  local pattern
  if [ "${2:-}" = "contains" ]; then
    pattern="text=\"[^\"]*$1"
  else
    pattern="text=\"$1\""
  fi
  dump | grep -oE "$pattern[^\"]*\"[^>]*bounds=\"\[[0-9]+,[0-9]+\]\[[0-9]+,[0-9]+\]\"" \
    | grep -oE "\[[0-9]+,[0-9]+\]\[[0-9]+,[0-9]+\]$" | head -1
}

tap_at() { # [x1,y1][x2,y2] -> tap the center
  local x1 y1 x2 y2
  [[ "$1" =~ \[([0-9]+),([0-9]+)\]\[([0-9]+),([0-9]+)\] ]] || return 1
  x1=${BASH_REMATCH[1]} y1=${BASH_REMATCH[2]} x2=${BASH_REMATCH[3]} y2=${BASH_REMATCH[4]}
  adb shell input tap $(( (x1 + x2) / 2 )) $(( (y1 + y2) / 2 ))
}

tap_text() { # exact text match
  local b="" i
  for i in 1 2 3 4 5; do
    b=$(find_bounds "$1") && [ -n "$b" ] && break
    sleep 2
  done
  [ -n "$b" ] || { echo "node not found: text=$1"; dump > "$SHOTS/ui-missing.xml" || true; exit 1; }
  tap_at "$b"
}

tap_contains() { # substring text match
  local b="" i
  for i in 1 2 3 4 5; do
    b=$(find_bounds "$1" contains) && [ -n "$b" ] && break
    sleep 2
  done
  [ -n "$b" ] || { echo "node not found: text~$1"; dump > "$SHOTS/ui-missing.xml" || true; exit 1; }
  tap_at "$b"
}

tap_edit() { # tap the $1-th (0-based) EditText on screen
  local xml b
  xml=$(dump)
  b=$(echo "$xml" | grep -oE 'class="android.widget.EditText"[^>]*bounds="\[[0-9]+,[0-9]+\]\[[0-9]+,[0-9]+\]"' \
    | sed -n "$(( $1 + 1 ))p" | grep -oE "\[[0-9]+,[0-9]+\]\[[0-9]+,[0-9]+\]$")
  [ -n "$b" ] || { echo "edit text #$1 not found"; dump > "$SHOTS/ui-missing.xml" || true; exit 1; }
  tap_at "$b"
}

assert_text() { # screen must show a node whose text contains $1
  local i
  for i in 1 2 3 4 5 6; do
    if dump | grep -q "text=\"[^\"]*$1"; then return 0; fi
    sleep 3
  done
  dump > "$SHOTS/assert-fail-$1.xml" || true
  adb exec-out screencap -p > "$SHOTS/assert-fail-$1.png" || true
  adb logcat -d > "$SHOTS/assert-fail-logcat.txt" 2>/dev/null || true
  echo "assert failed: $1"
  exit 1
}

# Install: the package service can refuse right after boot, so retry.
installed=0
for i in 1 2 3 4 5 6 7 8; do
  if adb install -r "$APK" 2>&1 | grep -q Success; then installed=1; break; fi
  echo "install retry $i"
  sleep 10
done
[ "$installed" = 1 ] || { echo "install failed"; exit 1; }
adb shell pm list packages | grep -q "$PKG" || { echo "package missing"; exit 1; }

adb shell input keyevent KEYCODE_WAKEUP
adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1
assert_text "商品开箱"
shot 01-home 3

# Home grid: products, market, orders.
tap_text "商品开箱"
assert_text "CI 验收箱"
shot 02-products 3
adb shell input keyevent 4
shot 03-back-home 2

tap_text "行情"
shot 04-market 5
adb shell input keyevent 4

tap_text "订单"
assert_text "查询订单"
shot 05-orders-empty 3
adb shell input keyevent 4

# Products → product detail → buy (unauthenticated, routed to login).
tap_text "商品开箱"
assert_text "CI 验收箱"
tap_text "CI 验收箱"
assert_text "箱内物品"
shot 07-product 4
tap_contains "单开"
assert_text "账号登录"
shot 08-login 3

# Login with the seeded account.
tap_edit 0
adb shell input text "13800000001"
tap_edit 1
adb shell input text "Passw0rd123"
tap_text "登录"
assert_text "金币余额"
shot 09-after-login 4

# Back to the product page (user → login → product), then buy for real.
adb shell input keyevent 4
sleep 2
adb shell input keyevent 4
assert_text "箱内物品"
shot 11-product-again 2
tap_contains "单开"
assert_text "还没有奖品"
shot 12-opening 4

# The mock payment channel settles orders through its callback endpoint.
# Post it here so the order page shows a paid state — the app itself keeps
# no mock logic: pay returns the cashier URL and the provider calls back.
ORDER_ROW=$(docker exec deploy-mysql-1 mysql -N -u doggo -pci-doggo-password doggo \
  -e "SELECT CONCAT(Id, ' ', TotalCostCents) FROM UserOrders ORDER BY CreatedAtUtc DESC LIMIT 1" 2>/dev/null)
OID=$(echo "$ORDER_ROW" | awk '{print $1}')
AMT=$(echo "$ORDER_ROW" | awk '{print $2}')
[ -n "$OID" ] || { echo "no order row found"; exit 1; }
curl -fsS -X POST http://127.0.0.1:5077/api/payments/callback \
  -H 'Content-Type: application/json' \
  -d "{\"orderId\":\"$OID\",\"amountCents\":$AMT,\"success\":true}"

# Home → orders: the paid order must show up as settled.
adb shell input keyevent 4
sleep 2
adb shell input keyevent 4
sleep 2
adb shell input keyevent 4
shot 15-back-home 2
tap_text "订单"
assert_text "待结算"
shot 16-orders 4

ls -la "$SHOTS"
