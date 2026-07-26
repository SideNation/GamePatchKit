"""Independent RFC 8785 (JCS) encoder used only to cross-check GamePatchKit.Core's
CanonicalJsonWriter (src/GamePatchKit.Core/Json/CanonicalJsonWriter.cs). Not part of the
build or test pipeline - see generate_golden_vectors.py.
"""

import hashlib


def canonicalize(value):
    """Object keys sorted by UTF-16 code unit order (== Python code-point order for
    BMP-only text, which is all this test data uses). Arrays are never reordered.
    Numbers here are always Python ints (safe-integer range), written as plain decimal."""
    if isinstance(value, dict):
        items = sorted(value.items(), key=lambda kv: kv[0])
        return "{" + ",".join(canonicalize_string(k) + ":" + canonicalize(v) for k, v in items) + "}"
    if isinstance(value, list):
        return "[" + ",".join(canonicalize(v) for v in value) + "]"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, int):
        return str(value)
    if value is None:
        return "null"
    if isinstance(value, str):
        return canonicalize_string(value)
    raise TypeError(f"unsupported type: {type(value)}")


def canonicalize_string(s):
    out = ['"']
    for ch in s:
        code = ord(ch)
        if ch == '"':
            out.append('\\"')
        elif ch == '\\':
            out.append('\\\\')
        elif ch == '\b':
            out.append('\\b')
        elif ch == '\f':
            out.append('\\f')
        elif ch == '\n':
            out.append('\\n')
        elif ch == '\r':
            out.append('\\r')
        elif ch == '\t':
            out.append('\\t')
        elif code < 0x20:
            out.append('\\u%04x' % code)
        else:
            out.append(ch)
    out.append('"')
    return "".join(out)


def canonical_bytes(value):
    return canonicalize(value).encode("utf-8")


def sha256_hex(data):
    return hashlib.sha256(data).hexdigest()
