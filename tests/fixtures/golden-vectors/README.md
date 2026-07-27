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

## Signing (11 — signing/key rotation)

Each vector directory also carries the same test Ed25519 key's signature over
its own `manifest.canonical.json`, kept in the same self-contained directory
rather than a parallel tree:

- `public-key.bin` — raw 32-byte test Ed25519 public key (the seed is
  `SigningKeys.PrivateKey()` in `tests/GamePatchKit.Packager.Tests/SigningKeys.cs`
  - bytes `0x01..0x20` - so the whole test suite shares one canonical test key).
- `key-id.txt` — expected `keyId` (`ed25519-` + lowercase hex64 SHA-256 of
  `public-key.bin`), no trailing newline.
- `manifest-sig.canonical.json` — canonical `manifest.sig` bytes signing this
  vector's own `manifest.canonical.json` bytes with the private key paired to
  `public-key.bin`.

None of the existing 02 files change shape or meaning because of these.

## Independent cross-check (`tools/`)

`tools/generate_golden_vectors.py` re-implements RFC 8785 canonicalization from scratch
in Python (`tools/jcs.py`) and either (re)writes these fixtures or, with `--check`,
verifies the committed files still match it byte-for-byte without writing anything. This
is deliberately independent of `GamePatchKit.Core.Json.CanonicalJsonWriter` so the
fixtures are not solely self-validated by the C# implementation they exist to test. The
same independence motivates signing with Python's `cryptography` library (OpenSSL's
Ed25519) rather than the BouncyCastle implementation `GamePatchKit.Core`/`Packager` use.
It requires the `cryptography` package (`pip install cryptography`) and is not run by
`dotnet test` or CI - re-run it by hand after changing a vector's input model:

```bash
python3 tests/fixtures/golden-vectors/tools/generate_golden_vectors.py           # regenerate
python3 tests/fixtures/golden-vectors/tools/generate_golden_vectors.py --check   # verify only
```
