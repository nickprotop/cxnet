#!/bin/bash
# cxnet Installer
# Downloads and installs the latest release from GitHub
# Usage: curl -fsSL https://raw.githubusercontent.com/nickprotop/cxnet/main/install.sh | bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

set -e

REPO="nickprotop/cxnet"
INSTALL_DIR="$HOME/.local/bin"

echo "Installing cxnet..."

# Detect OS and architecture
OS=$(uname -s)
ARCH=$(uname -m)

case "$OS" in
    Linux)
        case "$ARCH" in
            x86_64)  BINARY="cxnet-linux-x64" ;;
            aarch64) BINARY="cxnet-linux-arm64" ;;
            *) echo "Error: Unsupported Linux architecture: $ARCH"; exit 1 ;;
        esac
        ;;
    Darwin)
        case "$ARCH" in
            x86_64)  BINARY="cxnet-osx-x64" ;;
            arm64)   BINARY="cxnet-osx-arm64" ;;
            *) echo "Error: Unsupported macOS architecture: $ARCH"; exit 1 ;;
        esac
        ;;
    *)
        echo "Error: Unsupported OS: $OS"
        echo "cxnet supports Linux and macOS. For Windows, download from GitHub Releases."
        exit 1
        ;;
esac

# Get latest release info
echo "Fetching latest release..."
RELEASE_INFO=$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest")
TAG=$(echo "$RELEASE_INFO" | grep '"tag_name"' | head -1 | sed 's/.*"tag_name": "\(.*\)".*/\1/')
VERSION="${TAG#v}"

if [ -z "$TAG" ]; then
    echo "Error: Could not determine latest release."
    exit 1
fi

echo "Latest version: $VERSION"

# Download binary
DOWNLOAD_URL="https://github.com/$REPO/releases/download/$TAG/$BINARY"
echo "Downloading $BINARY (~75 MB)..."

mkdir -p "$INSTALL_DIR"

# Download to a temp file and move into place atomically. This shows a progress bar (the binary is
# large, so a silent download looks hung) and avoids "Text file busy" (ETXTBSY): writing the binary in
# place leaves a window where it is half-written or where a running copy blocks the write. An atomic
# rename swaps it instantly, with no such window.
TMP_BINARY="$INSTALL_DIR/.cxnet.download.$$"
trap 'rm -f "$TMP_BINARY"' EXIT
curl -fSL --progress-bar "$DOWNLOAD_URL" -o "$TMP_BINARY"
chmod +x "$TMP_BINARY"
mv -f "$TMP_BINARY" "$INSTALL_DIR/cxnet"
trap - EXIT

# Download uninstaller
curl -fsSL "https://raw.githubusercontent.com/$REPO/main/uninstall.sh" -o "$INSTALL_DIR/cxnet-uninstall.sh"
chmod +x "$INSTALL_DIR/cxnet-uninstall.sh"

# Ensure PATH
if [[ ":$PATH:" != *":$INSTALL_DIR:"* ]]; then
    SHELL_RC=""
    if [ -f "$HOME/.zshrc" ]; then
        SHELL_RC="$HOME/.zshrc"
    elif [ -f "$HOME/.bashrc" ]; then
        SHELL_RC="$HOME/.bashrc"
    fi

    if [ -n "$SHELL_RC" ]; then
        if ! grep -q "$INSTALL_DIR" "$SHELL_RC" 2>/dev/null; then
            echo "export PATH=\"$INSTALL_DIR:\$PATH\"" >> "$SHELL_RC"
            echo "Added $INSTALL_DIR to PATH in $SHELL_RC"
        fi
    fi
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  ✓ cxnet v$VERSION installed!"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "  Binary:  $INSTALL_DIR/cxnet"
echo ""
echo "  Run:     cxnet"
echo "  Remove:  cxnet-uninstall.sh"
echo ""
if [[ ":$PATH:" != *":$INSTALL_DIR:"* ]]; then
    echo "  Note: Restart your shell or run:"
    echo "    source ~/.bashrc  (or ~/.zshrc)"
fi
