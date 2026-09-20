#!/bin/zsh
set -euo pipefail
cd "${0:A:h:h}"
mkdir -p build
swiftc -parse-as-library Sources/Relay/{SoundClip,SoundAudio,SpeechRenderer}.swift Tests/SpeechTests.swift -o build/SpeechTests
./build/SpeechTests
