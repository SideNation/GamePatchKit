"""Generates (or, with --check, verifies) tests/fixtures/golden-vectors/*.

This is an independent, non-build-integrated cross-check: it re-implements RFC 8785
canonicalization from scratch in Python (see jcs.py) so the committed fixtures are not
solely self-validated by the C# CanonicalJsonWriter they exist to test. It is not run by
`dotnet test` or any CI step - re-run it by hand after changing a vector's input model,
and re-run with --check to confirm the committed fixtures still match this generator.

Usage:
    python3 generate_golden_vectors.py            # (re)writes the fixture files
    python3 generate_golden_vectors.py --check     # verifies without writing; exits 1 on mismatch
"""

import base64
import hashlib
import os
import sys

from cryptography.hazmat.primitives.asymmetric import ed25519
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat

sys.path.insert(0, os.path.dirname(__file__))
from jcs import canonical_bytes, sha256_hex

# Fixed test signing key for the golden vectors (step 11): the same private bytes
# tests/GamePatchKit.Packager.Tests/SigningKeys.cs uses, so the whole test suite shares one
# canonical Ed25519 test key rather than each fixture set minting its own. Never used to sign
# anything published - the private bytes being derivable from this script is deliberate.
SIGNING_PRIVATE_KEY = ed25519.Ed25519PrivateKey.from_private_bytes(bytes(range(1, 33)))
SIGNING_PUBLIC_KEY = SIGNING_PRIVATE_KEY.public_key().public_bytes(Encoding.Raw, PublicFormat.Raw)
SIGNING_KEY_ID = "ed25519-" + hashlib.sha256(SIGNING_PUBLIC_KEY).hexdigest()

HASH_A = "30fdb670837e4a2ae265f0ba5bf332a6c80930274f43b7e3cf799b637eaff1c6"
HASH_B = "cd43d82673a50ede733a204a3db6997dd349fb9963ee34950682433e97b1b512"
HASH_C = "f932608e42632d2607d7d540a1e1d70e752ba192823e69e9eed0d4d307ff45d8"
HASH_D = "2dc778fd7c0bf27cda8936092f7fd981515c4f5bbf3f3d082211462f12f3060e"
HASH_E = "d8588ec425bb08feffaf3f8725f0bd497f7d82f5f940fd1c3578a6c4e8529284"
HASH_F = "22faad9315c7ad20e8d6b370258cacf8039de80805268ef047087841e9078c05"
HASH_G = "34fd11640cef8bb04218acfa66ebca39481bb9e7b17b5ffcbada5ed720a19e6c"


def none_compression():
    return {"kind": "none"}


def zstd_compression():
    return {"kind": "zstd", "codecId": "zstd"}


def identity_of(manifest):
    return {
        "packageId": manifest["packageId"],
        "groups": [{"name": g["name"], "required": g["required"]} for g in manifest["groups"]],
        "files": [
            {"path": f["path"], "group": f["group"], "size": f["size"], "fileHash": f["fileHash"]}
            for f in manifest["files"]
        ],
    }


def single_file_manifest():
    package_id = "golden-single-file"
    return {
        "schemaVersion": 1,
        "packageId": package_id,
        "dataVersion": "PLACEHOLDER",
        "compactVersion": 0,
        "groups": [{"name": "core", "required": True}],
        "artifacts": [
            {
                "kind": "file",
                "compression": none_compression(),
                "payload": {
                    "kind": "single",
                    "path": f"{package_id}/artifacts/files/{HASH_A}/content",
                    "size": 14,
                    "artifactHash": HASH_A,
                },
            }
        ],
        "files": [
            {
                "path": "data/config.json",
                "group": "core",
                "size": 14,
                "fileHash": HASH_A,
                "source": {"kind": "file", "artifactHash": HASH_A},
            }
        ],
    }


def multipart_file_manifest():
    package_id = "golden-multipart-file"
    return {
        "schemaVersion": 1,
        "packageId": package_id,
        "dataVersion": "PLACEHOLDER",
        "compactVersion": 0,
        "groups": [{"name": "core", "required": True}],
        "artifacts": [
            {
                "kind": "file",
                "compression": zstd_compression(),
                "payload": {
                    "kind": "parts",
                    "size": 20,
                    "artifactHash": HASH_C,
                    "parts": [
                        {"index": 0, "path": f"{package_id}/artifacts/files/{HASH_C}/part-00000", "size": 12, "partHash": HASH_D},
                        {"index": 1, "path": f"{package_id}/artifacts/files/{HASH_C}/part-00001", "size": 8, "partHash": HASH_E},
                    ],
                },
            }
        ],
        "files": [
            {
                "path": "data/big-file.bin",
                "group": "core",
                "size": 28,
                "fileHash": HASH_B,
                "source": {"kind": "file", "artifactHash": HASH_C},
            }
        ],
    }


def bundle_entry_manifest():
    package_id = "golden-bundle-entry"
    return {
        "schemaVersion": 1,
        "packageId": package_id,
        "dataVersion": "PLACEHOLDER",
        "compactVersion": 0,
        "groups": [{"name": "maps", "required": False}],
        "artifacts": [
            {
                "kind": "bundle",
                "group": "maps",
                "path": f"{package_id}/artifacts/bundles/maps/{HASH_G}.tar",
                "size": 512,
                "artifactHash": HASH_G,
                "compression": none_compression(),
                "entries": [{"path": "maps/level1.bin"}],
            }
        ],
        "files": [
            {
                "path": "maps/level1.bin",
                "group": "maps",
                "size": 14,
                "fileHash": HASH_F,
                "source": {"kind": "bundleEntry", "artifactHash": HASH_G, "entryPath": "maps/level1.bin"},
            }
        ],
    }


def mixed_manifest():
    package_id = "golden-mixed"
    return {
        "schemaVersion": 1,
        "packageId": package_id,
        "dataVersion": "PLACEHOLDER",
        "compactVersion": 0,
        "groups": [
            {"name": "core", "required": True},
            {"name": "optional-assets", "required": False},
        ],
        "artifacts": [
            {
                "kind": "bundle",
                "group": "optional-assets",
                "path": f"{package_id}/artifacts/bundles/optional-assets/{HASH_G}.tar",
                "size": 512,
                "artifactHash": HASH_G,
                "compression": none_compression(),
                "entries": [{"path": "optional-assets/level1.bin"}],
            },
            {
                "kind": "file",
                "compression": none_compression(),
                "payload": {
                    "kind": "single",
                    "path": f"{package_id}/artifacts/files/{HASH_A}/content",
                    "size": 14,
                    "artifactHash": HASH_A,
                },
            },
            {
                "kind": "file",
                "compression": zstd_compression(),
                "payload": {
                    "kind": "parts",
                    "size": 20,
                    "artifactHash": HASH_C,
                    "parts": [
                        {"index": 0, "path": f"{package_id}/artifacts/files/{HASH_C}/part-00000", "size": 12, "partHash": HASH_D},
                        {"index": 1, "path": f"{package_id}/artifacts/files/{HASH_C}/part-00001", "size": 8, "partHash": HASH_E},
                    ],
                },
            },
        ],
        "files": [
            {
                "path": "core/config.json",
                "group": "core",
                "size": 14,
                "fileHash": HASH_A,
                "source": {"kind": "file", "artifactHash": HASH_A},
            },
            {
                "path": "optional-assets/big.bin",
                "group": "optional-assets",
                "size": 28,
                "fileHash": HASH_B,
                "source": {"kind": "file", "artifactHash": HASH_C},
            },
            {
                "path": "optional-assets/level1.bin",
                "group": "optional-assets",
                "size": 14,
                "fileHash": HASH_F,
                "source": {"kind": "bundleEntry", "artifactHash": HASH_G, "entryPath": "optional-assets/level1.bin"},
            },
        ],
    }


VECTORS = {
    "single-file": single_file_manifest,
    "multipart-file": multipart_file_manifest,
    "bundle-entry": bundle_entry_manifest,
    "mixed": mixed_manifest,
}


def base64url_no_pad(data):
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")


def build_vector(builder):
    manifest = builder()
    identity = identity_of(manifest)

    identity_bytes = canonical_bytes(identity)
    data_version = "v1-" + sha256_hex(identity_bytes)
    manifest["dataVersion"] = data_version

    manifest_bytes = canonical_bytes(manifest)
    manifest_hash = sha256_hex(manifest_bytes)

    # Signs the same canonical manifest bytes manifestHash is taken over - the step 11 contract
    # for what a manifest.sig covers.
    signature = SIGNING_PRIVATE_KEY.sign(manifest_bytes)
    signature_doc = {
        "schemaVersion": 1,
        "algorithm": "Ed25519",
        "keyId": SIGNING_KEY_ID,
        "signature": base64url_no_pad(signature),
    }
    signature_bytes = canonical_bytes(signature_doc)

    return {
        "manifest.canonical.json": manifest_bytes,
        "manifest-hash.txt": manifest_hash.encode("ascii"),
        "identity.canonical.json": identity_bytes,
        "data-version.txt": data_version.encode("ascii"),
        "public-key.bin": SIGNING_PUBLIC_KEY,
        "key-id.txt": SIGNING_KEY_ID.encode("ascii"),
        "manifest-sig.canonical.json": signature_bytes,
    }


def main():
    check_only = "--check" in sys.argv
    out_root = os.path.join(os.path.dirname(__file__), "..")
    mismatches = []

    for name, builder in VECTORS.items():
        vector_dir = os.path.join(out_root, name)
        files = build_vector(builder)

        if check_only:
            for file_name, expected_bytes in files.items():
                file_path = os.path.join(vector_dir, file_name)
                with open(file_path, "rb") as f:
                    actual_bytes = f.read()
                if actual_bytes != expected_bytes:
                    mismatches.append(file_path)
            print(f"=== {name}: {'OK' if not mismatches else 'MISMATCH'} ===")
            continue

        os.makedirs(vector_dir, exist_ok=True)
        for file_name, data in files.items():
            with open(os.path.join(vector_dir, file_name), "wb") as f:
                f.write(data)

        print(f"=== {name} ===")
        print("dataVersion:", files["data-version.txt"].decode("ascii"))
        print("manifestHash:", files["manifest-hash.txt"].decode("ascii"))
        print()

    if check_only:
        if mismatches:
            print("MISMATCHES:")
            for path in mismatches:
                print(" -", path)
            sys.exit(1)
        print("All golden vectors match this generator.")


if __name__ == "__main__":
    main()
