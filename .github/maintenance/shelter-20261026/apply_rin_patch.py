"""Reconstruct a file from an authenticated old file and a bounded RINBD01 patch.

Pure Python 3 standard library. Never executes the input or reconstructed file.
The patch contains expected SHA256 hashes; the caller must also verify the patch
SHA256 against a separately trusted delivery record before using it.
"""
import argparse
import gzip
import hashlib
import os
from pathlib import Path
import struct
import tempfile

MAGIC = b"RINBD01\0"
HEADER = struct.Struct("<8sIQQQ32s32s")
U64 = struct.Struct("<Q")
COPY = struct.Struct("<QQ")
MAX_BYTES = 512 * 1024 * 1024
MAX_OPS = 1_000_000
BUFFER_BYTES = 1024 * 1024


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(BUFFER_BYTES), b""):
            digest.update(chunk)
    return digest.digest()


def read_exact(stream, count):
    data = stream.read(count)
    if len(data) != count:
        raise ValueError("Truncated patch")
    return data


def apply_patch(old_file, patch_file, output_file):
    old_path, patch_path, output_path = map(lambda p: Path(p).resolve(), (old_file, patch_file, output_file))
    if output_path in (old_path, patch_path):
        raise ValueError("Output must differ from the old file and patch")
    if output_path.exists():
        raise FileExistsError("Output already exists; choose a new output path")
    old_size = old_path.stat().st_size
    if old_size > MAX_BYTES:
        raise ValueError("Old file exceeds maximum supported size")
    temp_path = None
    try:
        with old_path.open("rb") as old, gzip.open(patch_path, "rb") as patch:
            magic, block_size, declared_old_size, new_size, op_count, old_hash, new_hash = HEADER.unpack(read_exact(patch, HEADER.size))
            if magic != MAGIC or block_size != 4096:
                raise ValueError("Unsupported patch format")
            if declared_old_size != old_size or new_size > MAX_BYTES or op_count > MAX_OPS or op_count > new_size:
                raise ValueError("Invalid size or operation count")
            actual_old = hashlib.sha256()
            for chunk in iter(lambda: old.read(BUFFER_BYTES), b""):
                actual_old.update(chunk)
            if actual_old.digest() != old_hash:
                raise ValueError("Old SHA256 mismatch")
            output_path.parent.mkdir(parents=True, exist_ok=True)
            handle, temp_name = tempfile.mkstemp(prefix=".rin-rebuild-", suffix=".tmp", dir=output_path.parent)
            temp_path = Path(temp_name)
            digest = hashlib.sha256()
            written = 0
            with os.fdopen(handle, "wb") as output:
                for _ in range(op_count):
                    opcode = read_exact(patch, 1)
                    if opcode == b"C":
                        offset, count = COPY.unpack(read_exact(patch, COPY.size))
                        if count == 0 or offset > old_size or count > old_size - offset:
                            raise ValueError("Copy outside old file")
                        old.seek(offset)
                        source = old
                    elif opcode == b"L":
                        count = U64.unpack(read_exact(patch, U64.size))[0]
                        if count == 0:
                            raise ValueError("Empty literal operation")
                        source = patch
                    else:
                        raise ValueError("Unknown patch operation")
                    if count > new_size - written:
                        raise ValueError("Operation exceeds declared output size")
                    remaining = count
                    while remaining:
                        chunk = read_exact(source, min(remaining, BUFFER_BYTES))
                        output.write(chunk)
                        digest.update(chunk)
                        remaining -= len(chunk)
                    written += count
                if written != new_size:
                    raise ValueError("Output size mismatch")
                if patch.read(1):
                    raise ValueError("Trailing decompressed patch data")
                if digest.digest() != new_hash:
                    raise ValueError("New SHA256 mismatch")
                output.flush()
                os.fsync(output.fileno())
            # The completed file is exposed only after every check succeeds.
            # The caller should use a dedicated output path without concurrent writers.
            if output_path.exists():
                raise FileExistsError("Output appeared during reconstruction")
            os.replace(temp_path, output_path)
            temp_path = None
            return {"new_size": new_size, "new_sha256": digest.hexdigest().upper(), "operations": op_count}
    finally:
        if temp_path is not None:
            temp_path.unlink(missing_ok=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("old_file")
    parser.add_argument("patch_gz")
    parser.add_argument("output_file")
    arguments = parser.parse_args()
    result = apply_patch(arguments.old_file, arguments.patch_gz, arguments.output_file)
    print("Reconstructed {new_size} bytes; SHA256={new_sha256}; operations={operations}".format(**result))
