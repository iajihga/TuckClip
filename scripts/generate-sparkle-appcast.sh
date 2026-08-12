#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
PROJECT_DIR="${SCRIPT_DIR:h}"
RELEASE_TAG="${1:-}"
ARCHITECTURE="${2:-}"
OUTPUT_DIR="${3:-${PROJECT_DIR}/dist}"
BUILD_NUMBER="${TUCKCLIP_BUILD_NUMBER:-${GITHUB_RUN_NUMBER:-1}}"
SPARKLE_ROOT="${SPARKLE_ROOT:-${PROJECT_DIR}/.build/SourcePackages/artifacts/sparkle/Sparkle}"
SIGN_TOOL="${SPARKLE_ROOT}/bin/sign_update"
PRIVATE_KEY_FILE="${SPARKLE_PRIVATE_KEY_FILE:-}"

if [[ ! "${RELEASE_TAG}" =~ '^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$' ]]; then
  print -u2 "Invalid release tag: ${RELEASE_TAG}"
  exit 64
fi

case "${ARCHITECTURE}" in
  arm64|x86_64) ;;
  *)
    print -u2 "Unsupported release architecture: ${ARCHITECTURE}"
    exit 64
    ;;
esac

if [[ ! "${BUILD_NUMBER}" =~ '^[0-9]+$' ]]; then
  print -u2 "TUCKCLIP_BUILD_NUMBER must contain only digits: ${BUILD_NUMBER}"
  exit 64
fi

if [[ -z "${PRIVATE_KEY_FILE}" || ! -f "${PRIVATE_KEY_FILE}" ]]; then
  print -u2 "SPARKLE_PRIVATE_KEY_FILE must point to the private Ed25519 key file."
  exit 1
fi

if [[ ! -x "${SIGN_TOOL}" ]]; then
  print -u2 "Sparkle sign_update was not found at ${SIGN_TOOL}"
  exit 1
fi

DMG_NAME="TuckClip-${RELEASE_TAG}-macOS-${ARCHITECTURE}.dmg"
DMG_PATH="${OUTPUT_DIR}/${DMG_NAME}"
APPCAST_NAME="TuckClip-macOS-${ARCHITECTURE}-appcast.xml"
APPCAST_PATH="${OUTPUT_DIR}/${APPCAST_NAME}"
VERSION="${${RELEASE_TAG#v}%%-*}"

if [[ ! -s "${DMG_PATH}" ]]; then
  print -u2 "Release DMG is missing or empty: ${DMG_PATH}"
  exit 1
fi

if [[ -e "${APPCAST_PATH}" ]]; then
  print -u2 "Refusing to replace an existing Sparkle appcast: ${APPCAST_PATH}"
  exit 1
fi

SIGNATURE_OUTPUT="$("${SIGN_TOOL}" --ed-key-file "${PRIVATE_KEY_FILE}" "${DMG_PATH}")"
ED_SIGNATURE="$(print -r -- "${SIGNATURE_OUTPUT}" | sed -n 's/.*sparkle:edSignature="\([^"]*\)".*/\1/p')"
if [[ -z "${ED_SIGNATURE}" ]]; then
  print -u2 "Sparkle did not return an Ed25519 signature."
  exit 1
fi

"${SIGN_TOOL}" --verify --ed-key-file "${PRIVATE_KEY_FILE}" \
  "${DMG_PATH}" "${ED_SIGNATURE}"
FILE_LENGTH="$(stat -f '%z' "${DMG_PATH}")"
PUB_DATE="$(LC_ALL=C date -u '+%a, %d %b %Y %H:%M:%S +0000')"
DOWNLOAD_URL="https://github.com/mzopedia/TuckClip/releases/download/${RELEASE_TAG}/${DMG_NAME}"
TEMP_APPCAST="$(mktemp "${OUTPUT_DIR}/.${APPCAST_NAME}.XXXXXX")"

cleanup() {
  rm -f -- "${TEMP_APPCAST}"
}
trap cleanup EXIT INT TERM

{
  print -r -- '<?xml version="1.0" encoding="utf-8"?>'
  print -r -- '<rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">'
  print -r -- '  <channel>'
  print -r -- '    <title>TuckClip Updates</title>'
  print -r -- '    <link>https://github.com/mzopedia/TuckClip</link>'
  print -r -- '    <description>Stable TuckClip updates for macOS</description>'
  print -r -- '    <language>en</language>'
  print -r -- '    <item>'
  print -r -- "      <title>TuckClip ${VERSION}</title>"
  print -r -- "      <pubDate>${PUB_DATE}</pubDate>"
  print -r -- "      <sparkle:version>${BUILD_NUMBER}</sparkle:version>"
  print -r -- "      <sparkle:shortVersionString>${VERSION}</sparkle:shortVersionString>"
  print -r -- '      <sparkle:minimumSystemVersion>14.0</sparkle:minimumSystemVersion>'
  print -r -- "      <enclosure url=\"${DOWNLOAD_URL}\" sparkle:edSignature=\"${ED_SIGNATURE}\" length=\"${FILE_LENGTH}\" type=\"application/octet-stream\" />"
  print -r -- '    </item>'
  print -r -- '  </channel>'
  print -r -- '</rss>'
} > "${TEMP_APPCAST}"

chmod 0644 "${TEMP_APPCAST}"
if ! ln "${TEMP_APPCAST}" "${APPCAST_PATH}"; then
  print -u2 "Unable to publish appcast atomically: ${APPCAST_PATH}"
  exit 1
fi
rm -f -- "${TEMP_APPCAST}"
trap - EXIT INT TERM

xmllint --noout "${APPCAST_PATH}"
print "Created ${APPCAST_PATH}"
