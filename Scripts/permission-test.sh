#!/bin/zsh
set -euo pipefail
cd "${0:A:h:h}"
clang -c Sources/AudioCore/AudioCore.c -I Sources/AudioCore/include -o build/AudioCoreTests.o
swiftc -I Sources/AudioCore/include Sources/Relay/{AudioHardware,MusicApp,ProcessCapture,ListeningAnchor,AudioRecovery,RoutingPolicy,AppRoutingPolicy,SoundClip,SoundAudio,SoundboardLibrary,SessionModel}.swift Tests/PermissionTests.swift build/AudioCoreTests.o -framework CoreAudio -o build/PermissionTests
./build/PermissionTests
