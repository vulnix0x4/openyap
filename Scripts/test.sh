#!/bin/zsh
set -euo pipefail
cd "${0:A:h:h}"
mkdir -p build
clang -std=c11 -O1 -g -fsanitize=address,undefined -I Sources/AudioCore/include Tests/DSPTests.c -framework CoreAudio -o build/DSPTests
./build/DSPTests
swiftc Sources/Relay/RoutingPolicy.swift Sources/Relay/AppRoutingPolicy.swift Tests/PolicyTests.swift -o build/PolicyTests
./build/PolicyTests
