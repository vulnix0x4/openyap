#!/bin/zsh
set -euo pipefail
cd "${0:A:h:h}"
mkdir -p build
clang -std=c11 -O1 -g -fsanitize=address,undefined -I Sources/AudioCore/include Tests/SoundboardTests.c -framework CoreAudio -o build/SoundboardTests
./build/SoundboardTests
swiftc Sources/Relay/{SoundClip,SoundAudio}.swift Tests/SoundAudioTests.swift -o build/SoundAudioTests
./build/SoundAudioTests
swiftc Sources/Relay/{SoundClip,SoundAudio,SoundboardLibrary}.swift Tests/SoundLibraryTests.swift -o build/SoundLibraryTests
./build/SoundLibraryTests
