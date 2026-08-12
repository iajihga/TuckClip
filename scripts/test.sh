#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
PROJECT_DIR="${SCRIPT_DIR:h}"
HOST_ARCHITECTURE="${TUCKCLIP_TEST_ARCHITECTURE:-$(uname -m)}"

case "${HOST_ARCHITECTURE}" in
  arm64|x86_64) ;;
  *)
    print -u2 "Unsupported test architecture: ${HOST_ARCHITECTURE}"
    exit 64
    ;;
esac

TEST_WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/tuckclip-tests.XXXXXX")"
DERIVED_DATA_PATH="${TEST_WORK_DIR}/DerivedData"
RESULT_BUNDLE_PATH="${TEST_WORK_DIR}/TuckClipTests.xcresult"

cleanup() {
  rm -rf -- "${TEST_WORK_DIR}"
}
trap cleanup EXIT INT TERM

print "Running TuckClip XCTest on ${HOST_ARCHITECTURE}"

xcodebuild \
  -project "${PROJECT_DIR}/TuckClip.xcodeproj" \
  -scheme TuckClip \
  -configuration Debug \
  -destination "platform=macOS,arch=${HOST_ARCHITECTURE}" \
  -derivedDataPath "${DERIVED_DATA_PATH}" \
  -resultBundlePath "${RESULT_BUNDLE_PATH}" \
  CODE_SIGN_IDENTITY=- \
  CODE_SIGN_STYLE=Manual \
  DEVELOPMENT_TEAM= \
  test

print "XCTest passed."
