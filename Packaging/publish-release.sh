#!/usr/bin/env bash
set -euo pipefail
: "${RELEASE_TAG:?RELEASE_TAG is required}"
: "${GH_REPO:?GH_REPO is required}"
: "${GH_TOKEN:?GH_TOKEN is required}"

version="${RELEASE_TAG#v}"
archive="release-assets/DialogueMachine-$version-win-x64.zip"
checksum="$archive.sha256"
test -f "$archive"
test -f "$checksum"
(cd release-assets && sha256sum --check "$(basename "$checksum")")

# Retry failed uploads while the release is still a draft; published releases stay unchanged.
if draft="$(gh release view "$RELEASE_TAG" --json isDraft --jq .isDraft 2>/dev/null)"; then
  if [ "$draft" != true ]; then
    echo "Release $RELEASE_TAG is already published; leaving it unchanged."
    exit 0
  fi
else
  flags=()
  if [[ "$version" == *-* ]]; then flags+=(--prerelease); fi
  gh release create "$RELEASE_TAG" --verify-tag --draft \
    --title "Dialogue Machine $version" --notes-file RELEASE_NOTES.md "${flags[@]}"
fi
gh release upload "$RELEASE_TAG" "$archive" "$checksum" --clobber
gh release edit "$RELEASE_TAG" --draft=false
