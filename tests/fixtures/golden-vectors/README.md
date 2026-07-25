# golden vectors

Shared canonical-JSON test vectors. Each `<name>/` directory is a self-contained
scenario built and cross-checked in `TestGoldenVectors` (see
`tests/GamePatchKit.Core.Tests/GoldenVectors/`).

## Current files (02 — schema/canonicalization)

- `manifest.canonical.json` — exact canonical release manifest bytes (no BOM,
  no trailing newline; compare as raw bytes, not text).
- `manifest-hash.txt` — expected `manifestHash`: lowercase hex64, no trailing
  newline.
- `identity.canonical.json` — exact canonical identity-projection bytes used
  as the `dataVersion` hash input (no trailing newline).
- `data-version.txt` — expected `dataVersion`: `v1-` + lowercase hex64, no
  trailing newline.

## Reserved for 11 (signing/key rotation)

Step 11 extends these same vectors with signing data. To keep one vector
self-contained per scenario rather than introducing a parallel directory
tree, add:

- `public-key.bin` — raw 32-byte test Ed25519 public key.
- `key-id.txt` — expected `keyId` (`ed25519-` + lowercase hex64 SHA-256 of
  `public-key.bin`), no trailing newline.
- `manifest-sig.canonical.json` — expected canonical `manifest.sig` bytes
  (signing the vector's own `manifest.canonical.json` bytes with the private
  key paired to `public-key.bin`).

None of the existing 02 files change shape or meaning when these are added.
