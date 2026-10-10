#!/usr/bin/env python3
"""Proof 4: every valid test vector also verifies with OpenSSL, independent of the Swift code.

Converts the raw P-256 public key (x‖y) to a PEM SubjectPublicKeyInfo and the raw signature (r‖s) to DER,
then runs `openssl dgst -sha256 -verify` over the signed message. Usage: scripts/check-vectors-openssl.py
"""
import base64, json, os, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
VECTORS = os.path.join(HERE, "..", "docs", "spec", "license-test-vectors.json")
SPKI_PREFIX = bytes.fromhex("3059301306072a8648ce3d020106082a8648ce3d030107034200")  # P-256, uncompressed point follows

def der_int(value: bytes) -> bytes:
    value = value.lstrip(b"\x00") or b"\x00"
    if value[0] & 0x80:
        value = b"\x00" + value
    return b"\x02" + bytes([len(value)]) + value

def der_signature(raw: bytes) -> bytes:
    body = der_int(raw[:32]) + der_int(raw[32:])
    return b"\x30" + bytes([len(body)]) + body

def main() -> int:
    vectors = json.load(open(VECTORS))
    checked = failed = 0
    with tempfile.TemporaryDirectory() as tmp:
        for v in vectors:
            if not v["expect"]["valid"] or not v.get("signatureHex"):
                continue
            key_id = next(iter(v["trustedKeys"]))
            public = bytes.fromhex(v["trustedKeys"][key_id])
            pem = "-----BEGIN PUBLIC KEY-----\n" + base64.encodebytes(SPKI_PREFIX + b"\x04" + public).decode() + "-----END PUBLIC KEY-----\n"
            paths = {name: os.path.join(tmp, name) for name in ("pub.pem", "msg.bin", "sig.der")}
            open(paths["pub.pem"], "w").write(pem)
            open(paths["msg.bin"], "wb").write(bytes.fromhex(v["messageHex"]))
            open(paths["sig.der"], "wb").write(der_signature(bytes.fromhex(v["signatureHex"])))
            result = subprocess.run(["/usr/bin/openssl", "dgst", "-sha256", "-verify", paths["pub.pem"], "-signature", paths["sig.der"], paths["msg.bin"]],
                                    capture_output=True, text=True)
            ok = result.returncode == 0
            checked += 1
            failed += 0 if ok else 1
            print(("PASS " if ok else "FAIL ") + v["name"])
    print(f"{checked} valid vectors checked with OpenSSL, {failed} failed")
    return 1 if failed or checked == 0 else 0

if __name__ == "__main__":
    sys.exit(main())
