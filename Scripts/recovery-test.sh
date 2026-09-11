#!/bin/zsh
set -euo pipefail
cd "${0:A:h:h}"
swiftc Sources/Relay/AudioHardware.swift Sources/Relay/ListeningAnchor.swift Sources/Relay/AudioRecovery.swift Tests/RecoveryTests.swift -o build/RecoveryTests
./build/RecoveryTests
