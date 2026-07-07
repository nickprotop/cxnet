#!/bin/bash
# cxnet Uninstaller
# Removes cxnet binary
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

INSTALL_DIR="$HOME/.local/bin"

echo "cxnet Uninstaller"
echo ""

# Remove binary
if [ -f "$INSTALL_DIR/cxnet" ]; then
    rm "$INSTALL_DIR/cxnet"
    echo "✓ Removed $INSTALL_DIR/cxnet"
else
    echo "  Binary not found at $INSTALL_DIR/cxnet"
fi

# Remove uninstaller
if [ -f "$INSTALL_DIR/cxnet-uninstall.sh" ]; then
    rm "$INSTALL_DIR/cxnet-uninstall.sh"
fi

# Clean PATH from shell config
for RC in "$HOME/.bashrc" "$HOME/.zshrc"; do
    if [ -f "$RC" ] && grep -q "$INSTALL_DIR" "$RC" 2>/dev/null; then
        sed -i "\|$INSTALL_DIR|d" "$RC"
        echo ""
        echo "✓ Removed PATH entry from $RC"
    fi
done

echo ""
echo "✓ cxnet uninstalled."
