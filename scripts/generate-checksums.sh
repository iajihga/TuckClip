#!/usr/bin/env bash

set -euo pipefail

release_tag="${1:-}"
output_dir="${2:-dist}"

if [[ -z "${release_tag}" ]]; then
  printf 'Usage: %s <vMAJOR.MINOR.PATCH[-PRERELEASE]> [artifact-directory]\n' "$0" >&2
  exit 64
fi

if [[ ! "${release_tag}" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$ ]]; then
  printf 'Invalid release tag: %s\n' "${release_tag}" >&2
  exit 64
fi

if (( ${#release_tag} > 80 )); then
  printf 'Release tag is too long (maximum: 80 characters).\n' >&2
  exit 64
fi

if [[ ! -d "${output_dir}" ]]; then
  printf 'Artifact directory does not exist: %s\n' "${output_dir}" >&2
  exit 1
fi

checksum_name="TuckClip-${release_tag}-SHA256SUMS.txt"
checksum_path="${output_dir}/${checksum_name}"

if [[ -e "${checksum_path}" ]]; then
  printf 'Refusing to replace existing checksum asset: %s\n' "${checksum_path}" >&2
  exit 1
fi

assets=(
  "TuckClip-${release_tag}-macOS-arm64.dmg"
  "TuckClip-${release_tag}-macOS-x86_64.dmg"
  "TuckClip-macOS-arm64-appcast.xml"
  "TuckClip-macOS-x86_64-appcast.xml"
  "TuckClip-${release_tag}-Windows-x64-portable.zip"
  "TuckClip-${release_tag}-Windows-arm64-portable.zip"
  "TuckClip-${release_tag}-Windows-x64-Setup.exe"
  "TuckClip-${release_tag}-Windows-arm64-Setup.exe"
  "io.github.iajihga.TuckClip.WinX64-${release_tag#v}-win-x64-full.nupkg"
  "io.github.iajihga.TuckClip.WinArm64-${release_tag#v}-win-arm64-full.nupkg"
  "releases.win-x64.json"
  "releases.win-arm64.json"
)

for asset in "${assets[@]}"; do
  if [[ ! -s "${output_dir}/${asset}" ]]; then
    printf 'Missing or empty release asset: %s/%s\n' "${output_dir}" "${asset}" >&2
    exit 1
  fi
done

unexpected_assets=()
while IFS= read -r -d '' candidate; do
  candidate_name="${candidate##*/}"
  expected="false"
  for asset in "${assets[@]}"; do
    if [[ "${candidate_name}" == "${asset}" ]]; then
      expected="true"
      break
    fi
  done

  if [[ "${expected}" == "false" ]]; then
    unexpected_assets+=("${candidate_name}")
  fi
done < <(
  find "${output_dir}" -maxdepth 1 -type f \
    \( \
      -name "TuckClip-${release_tag}-*" -o \
      -name 'TuckClip-macOS-*-appcast.xml' -o \
      -name "io.github.iajihga.TuckClip.Win*-${release_tag#v}-win-*-full.nupkg" -o \
      -name 'releases.win-*.json' \
    \) \
    -print0
)

if (( ${#unexpected_assets[@]} != 0 )); then
  printf 'Unexpected release assets for %s:\n' "${release_tag}" >&2
  printf '  %s\n' "${unexpected_assets[@]}" >&2
  exit 1
fi

temporary_checksum="$(mktemp "${output_dir}/.${checksum_name}.XXXXXX")"
cleanup() {
  rm -f -- "${temporary_checksum}"
}
trap cleanup EXIT INT TERM

(
  cd "${output_dir}"
  if command -v shasum >/dev/null 2>&1; then
    LC_ALL=C shasum -a 256 "${assets[@]}"
  elif command -v sha256sum >/dev/null 2>&1; then
    LC_ALL=C sha256sum "${assets[@]}"
  else
    printf 'Neither shasum nor sha256sum is available.\n' >&2
    exit 1
  fi
) > "${temporary_checksum}"

chmod 0644 "${temporary_checksum}"
if ! ln "${temporary_checksum}" "${checksum_path}"; then
  printf 'Refusing to replace checksum asset or unable to publish it atomically: %s\n' \
    "${checksum_path}" >&2
  exit 1
fi
rm -f -- "${temporary_checksum}"
trap - EXIT INT TERM

printf 'Created %s/%s\n' "${output_dir}" "${checksum_name}"
