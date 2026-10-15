#!/usr/bin/env python3
"""Proof 4: an independent implementation of docs/spec/license.md, written from the spec in Python, checks every
test vector: it decodes the text itself, compares the bytes with the vector's hex, follows the spec's verification
order, verifies signatures with OpenSSL (raw r‖s converted to DER), and compares the result with the expected one.
Usage: scripts/check-vectors.py
"""
import base64, json, os, subprocess, sys, tempfile, unicodedata

HERE = os.path.dirname(os.path.abspath(__file__))
VECTORS = os.path.join(HERE, "..", "docs", "spec", "license-test-vectors.json")
ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
SPKI_PREFIX = bytes.fromhex("3059301306072a8648ce3d020106082a8648ce3d030107034200")
PRODUCTS = ["optimer", "steady", "space", "devbox", "power", "tune", "gate"]
DOMAIN = b"AMBYSTO-LICENSE-1"

def decode_text(text):
    """Spec "Text": ASCII only; None = malformed."""
    symbols = [c for c in text if c not in "- \t\r\n"]
    if len(symbols) >= 3 and "".join(symbols[:3]).lower() == "amb":
        symbols = symbols[3:]
    if not symbols or len(symbols) % 8 in (1, 3, 6):
        return None
    values = []
    for c in symbols:
        if not (c.isascii() and c.isalnum()):
            return None
        c = c.upper()
        c = {"O": "0", "I": "1", "L": "1"}.get(c, c)
        if c not in ALPHABET:
            return None
        values.append(ALPHABET.index(c))
    buffer = bits = 0
    out = bytearray()
    for v in values:
        buffer = (buffer << 5) | v
        bits += 5
        if bits >= 8:
            out.append((buffer >> (bits - 8)) & 0xFF)
            bits -= 8
            buffer &= (1 << bits) - 1
    if buffer != 0:
        return None
    return bytes(out)

def der_int(value):
    value = value.lstrip(b"\x00") or b"\x00"
    if value[0] & 0x80:
        value = b"\x00" + value
    return b"\x02" + bytes([len(value)]) + value

def signature_ok(public_hex, message, signature, tmp):
    pem = "-----BEGIN PUBLIC KEY-----\n" + base64.encodebytes(SPKI_PREFIX + b"\x04" + bytes.fromhex(public_hex)).decode() + "-----END PUBLIC KEY-----\n"
    body = der_int(signature[:32]) + der_int(signature[32:])
    paths = [os.path.join(tmp, n) for n in ("pub.pem", "msg.bin", "sig.der")]
    open(paths[0], "w").write(pem)
    open(paths[1], "wb").write(message)
    open(paths[2], "wb").write(b"\x30" + bytes([len(body)]) + body)
    return subprocess.run(["/usr/bin/openssl", "dgst", "-sha256", "-verify", paths[0], "-signature", paths[2], paths[1]],
                          capture_output=True).returncode == 0

def verify(text, trusted, revoked, tmp):
    """Spec "Verification order". Returns (fields, reason, decoded bytes)."""
    data = decode_text(text)
    if data is None:
        return None, "malformed", None
    if data[0] != 1:
        return None, "unsupportedVersion", data
    if len(data) <= 20 + 64:
        return None, "malformed", data
    n = data[19]
    if not 1 <= n <= 64 or len(data) != 20 + n + 64:
        return None, "malformed", data
    try:
        licensee = data[20:20 + n].decode("utf-8", errors="strict")
    except UnicodeDecodeError:
        return None, "malformed", data
    key = trusted.get(str(data[1]))
    if key is None:
        return None, "unknownSigningKey", data
    payload, signature = data[:20 + n], data[20 + n:]
    if not signature_ok(key, DOMAIN + payload, signature, tmp):
        return None, "badSignature", data
    key_id = data[2:10].hex()
    if key_id in revoked:
        return None, "revoked", data
    mask = int.from_bytes(data[15:19], "big")
    fields = {"licensee": licensee, "keyID": key_id, "seats": data[14], "issuedDay": int.from_bytes(data[10:12], "big"),
              "updatesUntilDay": int.from_bytes(data[12:14], "big"),
              "products": sorted(p for i, p in enumerate(PRODUCTS) if mask & (1 << i))}
    return fields, None, data

def main():
    vectors = json.load(open(VECTORS))
    failures = 0
    with tempfile.TemporaryDirectory() as tmp:
        for v in vectors:
            fields, reason, data = verify(v["text"], v["trustedKeys"], set(v["revoked"]), tmp)
            problems = []
            if v.get("payloadHex") and data is not None:
                if data.hex() != v["payloadHex"] + v["signatureHex"]:
                    problems.append("text does not decode to payloadHex + signatureHex")
                if bytes.fromhex(v["messageHex"]) != DOMAIN + bytes.fromhex(v["payloadHex"]):
                    problems.append("messageHex is not domain + payload")
            expect = v["expect"]
            if expect["valid"]:
                if reason:
                    problems.append(f"refused ({reason}) but should verify")
                else:
                    for k in ("licensee", "keyID", "seats", "issuedDay", "updatesUntilDay", "products"):
                        if fields[k] != expect[k]:
                            problems.append(f"{k}: {fields[k]!r} != {expect[k]!r}")
                    if unicodedata.normalize("NFC", fields["licensee"]) != fields["licensee"]:
                        problems.append("licensee is not NFC")
            elif reason != expect["error"]:
                problems.append(f"got {reason or 'valid'}, expected {expect['error']}")
            failures += bool(problems)
            print(("FAIL " if problems else "PASS ") + v["name"] + ("" if not problems else ": " + "; ".join(problems)))
    print(f"{len(vectors)} vectors checked by an independent implementation, {failures} failed")
    return 1 if failures else 0

if __name__ == "__main__":
    sys.exit(main())
