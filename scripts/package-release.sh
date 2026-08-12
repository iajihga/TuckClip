#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
PROJECT_DIR="${SCRIPT_DIR:h}"
RELEASE_TAG="${1:-}"
ARCHITECTURE="${2:-$(uname -m)}"
OUTPUT_DIR="${3:-${PROJECT_DIR}/dist}"
BUILD_NUMBER="${TUCKCLIP_BUILD_NUMBER:-${GITHUB_RUN_NUMBER:-1}}"

if [[ -z "${RELEASE_TAG}" ]]; then
  print -u2 "Usage: $0 <vMAJOR.MINOR.PATCH[-PRERELEASE]> [arm64|x86_64] [output-directory]"
  exit 64
fi

if [[ ! "${RELEASE_TAG}" =~ '^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$' ]]; then
  print -u2 "Invalid release tag: ${RELEASE_TAG}"
  print -u2 "Expected vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-PRERELEASE."
  exit 64
fi

if (( ${#RELEASE_TAG} > 80 )); then
  print -u2 "Release tag is too long (maximum: 80 characters)."
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

BUNDLE_VERSION="${${RELEASE_TAG#v}%%-*}"
DERIVED_DATA_PATH="${PROJECT_DIR}/.build/Release-${ARCHITECTURE}"
SOURCE_APP="${DERIVED_DATA_PATH}/Build/Products/Release/TuckClip.app"
EXECUTABLE_PATH="${SOURCE_APP}/Contents/MacOS/TuckClip"
ARTIFACT_BASENAME="TuckClip-${RELEASE_TAG}-macOS-${ARCHITECTURE}"
DMG_PATH="${OUTPUT_DIR}/${ARTIFACT_BASENAME}.dmg"
CHECKSUM_PATH="${DMG_PATH}.sha256"
STAGING_DIR="$(mktemp -d "${TMPDIR:-/tmp}/tuckclip-dmg.XXXXXX")"
MOUNT_DIR="$(mktemp -d "${TMPDIR:-/tmp}/tuckclip-mount.XXXXXX")"
DMG_ATTACHED="false"

cleanup() {
  if [[ "${DMG_ATTACHED}" == "true" ]]; then
    if hdiutil detach "${MOUNT_DIR}" >/dev/null 2>&1; then
      DMG_ATTACHED="false"
    fi
  fi
  rm -rf -- "${STAGING_DIR}"
  if [[ "${DMG_ATTACHED}" == "false" ]]; then
    rmdir "${MOUNT_DIR}" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT INT TERM

mkdir -p "${DERIVED_DATA_PATH}" "${OUTPUT_DIR}"

print "Building TuckClip ${RELEASE_TAG} for ${ARCHITECTURE} (ad-hoc signed)"

xcodebuild \
  -project "${PROJECT_DIR}/TuckClip.xcodeproj" \
  -scheme TuckClip \
  -configuration Release \
  -destination "generic/platform=macOS" \
  -derivedDataPath "${DERIVED_DATA_PATH}" \
  ARCHS="${ARCHITECTURE}" \
  ONLY_ACTIVE_ARCH=NO \
  MARKETING_VERSION="${BUNDLE_VERSION}" \
  CURRENT_PROJECT_VERSION="${BUILD_NUMBER}" \
  CODE_SIGN_IDENTITY=- \
  CODE_SIGN_STYLE=Manual \
  CODE_SIGN_INJECT_BASE_ENTITLEMENTS=NO \
  DEVELOPMENT_TEAM= \
  clean build

if [[ ! -d "${SOURCE_APP}" || ! -x "${EXECUTABLE_PATH}" ]]; then
  print -u2 "Release app was not produced at ${SOURCE_APP}"
  exit 1
fi

BUILT_ARCHITECTURES="$(lipo -archs "${EXECUTABLE_PATH}")"
if [[ "${BUILT_ARCHITECTURES}" != "${ARCHITECTURE}" ]]; then
  print -u2 "Unexpected executable architectures: ${BUILT_ARCHITECTURES} (expected ${ARCHITECTURE})"
  exit 1
fi

FILE_DESCRIPTION="$(file "${EXECUTABLE_PATH}")"
if [[ "${FILE_DESCRIPTION}" != *"${ARCHITECTURE}"* ]]; then
  print -u2 "The executable file description does not contain ${ARCHITECTURE}:"
  print -u2 -- "${FILE_DESCRIPTION}"
  exit 1
fi
print -- "${FILE_DESCRIPTION}"

codesign --verify --deep --strict "${SOURCE_APP}"

SIGNATURE_DESCRIPTION="$(codesign -dv --verbose=4 "${SOURCE_APP}" 2>&1)"
if [[ "${SIGNATURE_DESCRIPTION}" != *"Signature=adhoc"* ]]; then
  print -u2 "Expected an ad-hoc signature, but codesign reported:"
  print -u2 -- "${SIGNATURE_DESCRIPTION}"
  exit 1
fi

SIGNATURE_FLAGS="$(print -r -- "${SIGNATURE_DESCRIPTION}" | sed -n '/^CodeDirectory /p')"
if [[ "${SIGNATURE_FLAGS}" != *"runtime)"* ]]; then
  print -u2 "Release app is not signed with Hardened Runtime enabled:"
  print -u2 -- "${SIGNATURE_DESCRIPTION}"
  exit 1
fi

ENTITLEMENTS="$(codesign -d --entitlements :- "${SOURCE_APP}" 2>/dev/null)"
if [[ "${ENTITLEMENTS}" == *"com.apple.security.get-task-allow"* ]]; then
  print -u2 "Release app unexpectedly contains com.apple.security.get-task-allow."
  exit 1
fi

ditto "${SOURCE_APP}" "${STAGING_DIR}/TuckClip.app"
ln -s /Applications "${STAGING_DIR}/Applications"

if [[ -e "${DMG_PATH}" || -e "${CHECKSUM_PATH}" ]]; then
  print -u2 "Refusing to replace an existing release asset: ${ARTIFACT_BASENAME}"
  exit 1
fi

hdiutil create \
  -volname TuckClip \
  -srcfolder "${STAGING_DIR}" \
  -format UDZO \
  -ov \
  "${DMG_PATH}"

hdiutil verify "${DMG_PATH}"

hdiutil attach \
  -readonly \
  -nobrowse \
  -mountpoint "${MOUNT_DIR}" \
  "${DMG_PATH}" >/dev/null
DMG_ATTACHED="true"

if [[ ! -d "${MOUNT_DIR}/TuckClip.app" ]]; then
  print -u2 "Packaged DMG does not contain TuckClip.app."
  exit 1
fi

if [[ ! -L "${MOUNT_DIR}/Applications" || "$(readlink "${MOUNT_DIR}/Applications")" != "/Applications" ]]; then
  print -u2 "Packaged DMG does not contain the expected Applications symlink."
  exit 1
fi

UNEXPECTED_TOP_LEVEL_ENTRIES="$(
  find "${MOUNT_DIR}" -mindepth 1 -maxdepth 1 \
    ! -name TuckClip.app \
    ! -name Applications \
    -print
)"
if [[ -n "${UNEXPECTED_TOP_LEVEL_ENTRIES}" ]]; then
  print -u2 "Packaged DMG contains unexpected top-level entries:"
  print -u2 -- "${UNEXPECTED_TOP_LEVEL_ENTRIES}"
  exit 1
fi

MOUNTED_ARCHITECTURES="$(lipo -archs "${MOUNT_DIR}/TuckClip.app/Contents/MacOS/TuckClip")"
if [[ "${MOUNTED_ARCHITECTURES}" != "${ARCHITECTURE}" ]]; then
  print -u2 "Packaged executable architectures: ${MOUNTED_ARCHITECTURES} (expected ${ARCHITECTURE})"
  exit 1
fi

codesign --verify --deep --strict "${MOUNT_DIR}/TuckClip.app"
hdiutil detach "${MOUNT_DIR}" >/dev/null
DMG_ATTACHED="false"

(
  cd "${OUTPUT_DIR}"
  shasum -a 256 "${ARTIFACT_BASENAME}.dmg" > "${ARTIFACT_BASENAME}.dmg.sha256"
)

print "Created ${DMG_PATH}"
print "Created ${CHECKSUM_PATH}"
