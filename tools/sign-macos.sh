#!/bin/sh
# Signs AGEX on macOS.
#
#   tools/sign-macos.sh app <path/to/AGEX.app>
#   tools/sign-macos.sh dmg <path/to/agex.dmg>
#
# With MACOS_SIGN_IDENTITY set (a "Developer ID Application: ..." identity in
# the keychain) the app is signed with the hardened runtime. Without it the app
# gets an ad-hoc signature: required for Apple Silicon to run it at all, but
# Gatekeeper will still ask users to confirm the first launch.
# With APPLE_ID, APPLE_TEAM_ID and APPLE_APP_PASSWORD set, the DMG is notarized
# and the ticket is stapled.
set -eu
MODE="$1"
TARGET="$2"
HERE="$(cd "$(dirname "$0")" && pwd)"
ENTITLEMENTS="$HERE/macos-entitlements.plist"
IDENTITY="${MACOS_SIGN_IDENTITY:--}"

sign() {
  if [ "$IDENTITY" = "-" ]; then codesign --force --sign - "$1"
  else codesign --force --timestamp --options runtime --entitlements "$ENTITLEMENTS" --sign "$IDENTITY" "$1"; fi
}

if [ "$MODE" = "app" ]; then
  # Sign every native file inside first, then the bundle.
  find "$TARGET/Contents/MacOS" -type f \( -name '*.dylib' -o -perm -u+x \) | while read -r file; do sign "$file"; done
  sign "$TARGET"
  codesign --verify --deep --strict "$TARGET"
  echo "Signed $TARGET with ${IDENTITY}"
elif [ "$MODE" = "dmg" ]; then
  if [ "$IDENTITY" != "-" ]; then codesign --force --timestamp --sign "$IDENTITY" "$TARGET"; fi
  if [ -n "${APPLE_ID:-}" ] && [ -n "${APPLE_TEAM_ID:-}" ] && [ -n "${APPLE_APP_PASSWORD:-}" ] && [ "$IDENTITY" != "-" ]; then
    xcrun notarytool submit "$TARGET" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD" --wait
    xcrun stapler staple "$TARGET"
    echo "Notarized $TARGET"
  else
    echo "Notarization skipped (no Developer ID credentials)."
  fi
else
  echo "usage: sign-macos.sh app|dmg <path>" >&2
  exit 2
fi
