#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
PROJECT_DIR="${SCRIPT_DIR:h}"
DERIVED_DATA="${PROJECT_DIR}/.build/DerivedData"
BACKUP_DIR="${PROJECT_DIR}/.build/AppBackups"
SOURCE_APP="${DERIVED_DATA}/Build/Products/Release/TuckClip.app"
INSTALL_DIR="${TUCKCLIP_INSTALL_DIR:-$HOME/Applications}"
DESTINATION_APP="${INSTALL_DIR}/TuckClip.app"
SHOULD_LAUNCH="false"
SIGNING_IDENTITY="${TUCKCLIP_SIGNING_IDENTITY:--}"

if [[ "${1:-}" == "--launch" ]]; then
  SHOULD_LAUNCH="true"
elif [[ -n "${1:-}" ]]; then
  print -u2 "Usage: $0 [--launch]"
  exit 64
fi

if pgrep -x TuckClip >/dev/null 2>&1; then
  print -u2 "TuckClip is running. Quit it from the menu bar, then run this installer again."
  exit 1
fi

mkdir -p "${DERIVED_DATA}" "${INSTALL_DIR}"

xcodebuild \
  -project "${PROJECT_DIR}/TuckClip.xcodeproj" \
  -scheme TuckClip \
  -configuration Release \
  -derivedDataPath "${DERIVED_DATA}" \
  CODE_SIGN_IDENTITY="${SIGNING_IDENTITY}" \
  CODE_SIGN_STYLE=Manual \
  build

codesign --verify --deep --strict "${SOURCE_APP}"

if [[ "${SIGNING_IDENTITY}" == "-" ]]; then
  print -u2 "Warning: TuckClip was ad-hoc signed. macOS may treat every rebuilt version as a new app"
  print -u2 "for Accessibility permission. Set TUCKCLIP_SIGNING_IDENTITY to a persistent code-signing"
  print -u2 "identity before installing if you want the authorization to survive updates."
else
  print "Signed with persistent identity: ${SIGNING_IDENTITY}"
fi

if [[ -d "${DESTINATION_APP}" ]]; then
  mkdir -p "${BACKUP_DIR}"
  BACKUP_APP="${BACKUP_DIR}/TuckClip.backup.$(date +%Y%m%d-%H%M%S).app"
  mv "${DESTINATION_APP}" "${BACKUP_APP}"
  print "Previous version preserved at ${BACKUP_APP}"
fi

ditto "${SOURCE_APP}" "${DESTINATION_APP}"
print "Installed TuckClip at ${DESTINATION_APP}"

if [[ "${SHOULD_LAUNCH}" == "true" ]]; then
  open "${DESTINATION_APP}"
fi
