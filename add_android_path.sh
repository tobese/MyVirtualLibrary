#!/bin/bash

ZSHRC="$HOME/.zshrc"
MARKER="# Android SDK"

if grep -q "$MARKER" "$ZSHRC" 2>/dev/null; then
  echo "Android SDK PATH already in $ZSHRC — nothing to do."
  exit 0
fi

cat >> "$ZSHRC" <<'EOF'

# Android SDK
export ANDROID_HOME=~/Library/Android/sdk
export PATH=$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/emulator:$ANDROID_HOME/platform-tools:$PATH
EOF

echo "Added Android SDK to $ZSHRC"
echo "Run: source ~/.zshrc"
