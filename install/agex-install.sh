#!/bin/sh
# AGEX installer for macOS and Linux (per user, no sudo).
#
# One-command install:
#   curl -fsSL https://raw.githubusercontent.com/Abdullah-Dawoud/Ai-COGY/main/install/agex-install.sh | sh
#
# What it does:
#   1. Detects macOS or Linux and the processor (Apple Silicon/arm64 or x64).
#   2. Downloads the AGEX package and SHA256SUMS.txt from GitHub Releases.
#   3. Verifies the package's SHA-256. Stops on any mismatch; nothing is extracted before that.
#   4. macOS: installs AGEX.app to ~/Applications. Linux: installs to ~/.local/share/agex.
#   5. Links the "agex" command into ~/.local/bin (and adds a menu entry on Linux).
# It never installs, changes or removes agents, and never needs administrator rights.
#
# Options: --uninstall [--purge] | --package <file> --sha256 <hash> [--wait-pid <pid>] | --no-launch
set -eu

REPO="${AGEX_REPOSITORY:-Abdullah-Dawoud/Ai-COGY}"
VERSION="latest"
PACKAGE=""
EXPECTED=""
WAIT_PID=""
UNINSTALL=0
PURGE=0
LAUNCH=1
while [ $# -gt 0 ]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --package) PACKAGE="$2"; shift 2 ;;
    --sha256) EXPECTED="$2"; shift 2 ;;
    --wait-pid) WAIT_PID="$2"; shift 2 ;;
    --uninstall) UNINSTALL=1; shift ;;
    --purge) PURGE=1; shift ;;
    --no-launch) LAUNCH=0; shift ;;
    *) echo "agex-install: unknown option $1" >&2; exit 2 ;;
  esac
done

say() { printf '  %s\n' "$1"; }
fail() { printf 'AGEX: %s\n' "$1" >&2; exit 1; }

OS="$(uname -s)"
case "$OS" in
  Darwin) PLATFORM="osx"; APP_DIR="$HOME/Applications/AGEX.app"; BIN_TARGET="$APP_DIR/Contents/MacOS/agex"; DATA_DIR="$HOME/Library/Application Support/AGEX"; MARKER="$APP_DIR/Contents/Resources/install.json" ;;
  Linux) PLATFORM="linux"; APP_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/agex/app"; BIN_TARGET="$APP_DIR/agex"; DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/agex"; MARKER="$APP_DIR/install.json" ;;
  *) fail "This installer supports macOS and Linux. On Windows use agex-install.ps1." ;;
esac
case "$(uname -m)" in
  arm64|aarch64) ARCH="arm64" ;;
  x86_64|amd64) ARCH="x64" ;;
  *) fail "Unsupported processor: $(uname -m)." ;;
esac
BIN_DIR="$HOME/.local/bin"
DESKTOP_FILE="${XDG_DATA_HOME:-$HOME/.local/share}/applications/agex.desktop"

stop_running() {
  for name in AgexDesktop agex; do
    pkill -f "$APP_DIR/.*$name" 2>/dev/null || true
  done
}

if [ "$UNINSTALL" -eq 1 ]; then
  echo "AGEX AI CONTROL CENTER - uninstall"
  [ -f "$MARKER" ] || fail "No AGEX installation found in $APP_DIR."
  stop_running
  if [ -L "$BIN_DIR/agex" ]; then rm -f "$BIN_DIR/agex"; say "Removed the agex command"; fi
  [ -f "$DESKTOP_FILE" ] && rm -f "$DESKTOP_FILE"
  rm -f "$HOME/Library/LaunchAgents/com.agex.desktop.plist" "${XDG_CONFIG_HOME:-$HOME/.config}/autostart/agex.desktop" 2>/dev/null || true
  rm -rf "$APP_DIR"
  say "Removed $APP_DIR"
  if [ "$PURGE" -eq 1 ]; then
    rm -rf "$DATA_DIR" "$HOME/Library/Logs/AGEX" "$HOME/Library/Caches/AGEX" "${XDG_STATE_HOME:-$HOME/.local/state}/agex" "${XDG_CACHE_HOME:-$HOME/.cache}/agex" 2>/dev/null || true
    say "Removed AGEX settings and history"
  else
    say "Your AGEX settings and history were kept (use --purge to remove them)."
  fi
  echo "AGEX was removed. Your agents and projects were not touched."
  exit 0
fi

echo "AGEX AI CONTROL CENTER - installer"
say "$OS $ARCH"
command -v curl >/dev/null 2>&1 || fail "curl is required."
if command -v sha256sum >/dev/null 2>&1; then HASH="sha256sum"; elif command -v shasum >/dev/null 2>&1; then HASH="shasum -a 256"; else fail "sha256sum or shasum is required."; fi

if [ -n "$WAIT_PID" ]; then
  i=0; while kill -0 "$WAIT_PID" 2>/dev/null && [ $i -lt 60 ]; do sleep 1; i=$((i+1)); done
fi

WORK="$(mktemp -d "${TMPDIR:-/tmp}/agex-install.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT INT TERM

if [ -n "$PACKAGE" ]; then
  [ -f "$PACKAGE" ] || fail "Package not found: $PACKAGE"
  FILE="$PACKAGE"
  NAME="$(basename "$PACKAGE")"
  if [ -z "$EXPECTED" ]; then
    SUMS="$(dirname "$PACKAGE")/SHA256SUMS.txt"
    [ -f "$SUMS" ] || fail "SHA256SUMS.txt not found, so the package cannot be verified."
    EXPECTED="$(grep " \*\{0,1\}$NAME\$" "$SUMS" | head -n 1 | cut -d ' ' -f 1)"
  fi
else
  # "latest" includes pre-releases: /releases lists the newest first (drafts are never visible here).
  if [ "$VERSION" = "latest" ]; then API="https://api.github.com/repos/$REPO/releases?per_page=1"; else API="https://api.github.com/repos/$REPO/releases/tags/v${VERSION#v}"; fi
  curl -fsSL -H "User-Agent: AGEX-Installer" "$API" -o "$WORK/release.json" || fail "Could not find an AGEX release at github.com/$REPO."
  TAG="$(sed -n 's/.*"tag_name": *"\([^"]*\)".*/\1/p' "$WORK/release.json" | head -n 1)"
  [ -n "$TAG" ] || fail "The release information could not be read."
  VER="${TAG#v}"
  if [ "$PLATFORM" = "osx" ]; then NAME="agex-$VER-osx-$ARCH.zip"; else NAME="agex-$VER-linux-$ARCH.tar.gz"; fi
  URL="$(grep -o "\"browser_download_url\": *\"[^\"]*/$NAME\"" "$WORK/release.json" | sed 's/.*"\(https[^"]*\)"/\1/' | head -n 1)"
  SUMS_URL="$(grep -o "\"browser_download_url\": *\"[^\"]*/$TAG/SHA256SUMS.txt\"" "$WORK/release.json" | sed 's/.*"\(https[^"]*\)"/\1/' | head -n 1)"
  [ -n "$URL" ] || fail "Release $TAG has no package for $PLATFORM-$ARCH ($NAME)."
  [ -n "$SUMS_URL" ] || fail "Release $TAG has no checksum file, so it will not be installed."
  say "Downloading AGEX $TAG for $PLATFORM-$ARCH..."
  FILE="$WORK/$NAME"
  curl -fL --progress-bar "$URL" -o "$FILE" || fail "Download failed."
  curl -fsSL "$SUMS_URL" -o "$WORK/SHA256SUMS.txt" || fail "Checksum download failed."
  EXPECTED="$(grep " \*\{0,1\}$NAME\$" "$WORK/SHA256SUMS.txt" | head -n 1 | cut -d ' ' -f 1)"
fi

[ -n "$EXPECTED" ] || fail "$NAME is not listed in SHA256SUMS.txt."
ACTUAL="$($HASH "$FILE" | cut -d ' ' -f 1)"
[ "$(printf '%s' "$ACTUAL" | tr 'A-F' 'a-f')" = "$(printf '%s' "$EXPECTED" | tr 'A-F' 'a-f')" ] || fail "Checksum mismatch for $NAME. The download may be damaged or altered; nothing was installed."
say "Checksum verified"

STAGE="$WORK/stage"
mkdir -p "$STAGE"
case "$NAME" in
  *.zip) if command -v ditto >/dev/null 2>&1; then ditto -x -k "$FILE" "$STAGE"; else unzip -q "$FILE" -d "$STAGE"; fi ;;
  *.tar.gz) tar -xzf "$FILE" -C "$STAGE" ;;
  *) fail "Unknown package type: $NAME" ;;
esac
if [ "$PLATFORM" = "osx" ]; then SOURCE="$STAGE/AGEX.app"; else SOURCE="$STAGE/agex"; fi
[ -d "$SOURCE" ] || fail "The package layout is not what this installer expects."

stop_running
if [ -e "$APP_DIR" ]; then
  [ -f "$MARKER" ] || fail "$APP_DIR exists and was not installed by AGEX. Move it away and run the installer again."
  rm -rf "$APP_DIR"
fi
mkdir -p "$(dirname "$APP_DIR")"
mv "$SOURCE" "$APP_DIR"
if [ "$PLATFORM" = "osx" ]; then EXE_DIR="$APP_DIR/Contents/MacOS"; mkdir -p "$APP_DIR/Contents/Resources"; else EXE_DIR="$APP_DIR"; fi
# Archives made on Windows do not carry the execute bit.
chmod +x "$EXE_DIR/AgexDesktop" "$EXE_DIR/agex" 2>/dev/null || true
find "$EXE_DIR" -name 'createdump' -exec chmod +x {} \; 2>/dev/null || true
VERSION_INSTALLED="$(cat "$EXE_DIR/VERSION" 2>/dev/null || echo unknown)"
printf '{"product":"AGEX","version":"%s","runtime":"%s-%s","sha256":"%s"}\n' "$VERSION_INSTALLED" "$PLATFORM" "$ARCH" "$ACTUAL" > "$MARKER"
say "Installed AGEX $VERSION_INSTALLED to $APP_DIR"

mkdir -p "$BIN_DIR"
ln -sf "$BIN_TARGET" "$BIN_DIR/agex"
say "The agex command is in $BIN_DIR"
case ":$PATH:" in
  *":$BIN_DIR:"*) ;;
  *) say "Add $BIN_DIR to your PATH to use 'agex' in new terminals, e.g.: echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.profile" ;;
esac

if [ "$PLATFORM" = "linux" ]; then
  mkdir -p "$(dirname "$DESKTOP_FILE")"
  printf '[Desktop Entry]\nType=Application\nName=AGEX\nComment=AGEX AI CONTROL CENTER\nExec="%s"\nIcon=%s\nCategories=Development;\nTerminal=false\n' "$APP_DIR/AgexDesktop" "$APP_DIR/agex.png" > "$DESKTOP_FILE"
  say "Added AGEX to your applications menu"
fi

echo "AGEX is installed."
if [ "$PLATFORM" = "osx" ]; then
  say "This build may be unsigned. If macOS says it cannot check AGEX for malicious software, open it once with right-click > Open (see docs/INSTALL.md)."
fi
if [ "$LAUNCH" -eq 1 ]; then
  if [ "$PLATFORM" = "osx" ]; then open "$APP_DIR" 2>/dev/null || true
  elif [ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then nohup "$APP_DIR/AgexDesktop" >/dev/null 2>&1 &
  fi
fi
