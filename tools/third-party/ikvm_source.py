# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""
Makes third-party/ikvm-<version>/: the source of the IKVM that Vitomir's game ships, for the GPLv2 of its Java class library.

  python tools/third-party/ikvm_source.py 8.16.0

What it makes, beside a README written by hand:
  ikvm-<version>-source.zip     IKVM's own source at its release tag (git archive: what GitHub's "Source code (zip)" holds).
  openjdk-<commit>-used.tar.xz  the OpenJDK 8 files IKVM's build compiles into IKVM.Java.dll, from IKVM's own OpenJDK repository
                                (ikvmnet/jdk8u) at the commit IKVM's release names; the rest of OpenJDK (the virtual machine,
                                native code, tests, documents) is left out to save space.
  MANIFEST.txt                  the tag, the commit, the file count and each archive's SHA-256.

The files are taken from the include and remove lists of IKVM's own build (the .props, .targets and .ikvmproj files of its
IKVM.Java project).
The OpenJDK archive is made the same way every time (sorted, the release's time on every file, no owners).
Needs git and Python 3.8 or later; fetches from GitHub.
"""
import glob
import hashlib
import io
import lzma
import os
import re
import subprocess
import sys
import tarfile
import tempfile

IKVM = "https://github.com/ikvmnet/ikvm"
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))


def git(*args, cwd=None):
    return subprocess.run(["git", *args], cwd=cwd, check=True, capture_output=True).stdout


def used_files(ikvm):
    """The OpenJDK files IKVM.Java's build takes: its include patterns less its remove patterns, files only."""
    root = os.path.join(ikvm, "ext", "openjdk") + os.sep
    lists = os.path.join(ikvm, "src", "IKVM.Java")
    included, removed = set(), set()
    for name in sorted(glob.glob(os.path.join(lists, "IKVM.Java*.props")) + glob.glob(os.path.join(lists, "IKVM.Java*.targets"))
                       + glob.glob(os.path.join(lists, "*.ikvmproj"))):
        text = open(name, encoding="utf-8").read()
        for kind, patterns in re.findall(r'(Include|Remove)="([^"]*)"', text):
            for pattern in patterns.split(";"):
                if "$(OpenJdkDir)" not in pattern:
                    continue
                path = pattern.replace("$(OpenJdkDir)", root).replace("\\", "/")
                (included if kind == "Include" else removed).update(glob.glob(path, recursive=True))
    return sorted(os.path.relpath(p, os.path.join(ikvm, "ext", "openjdk")).replace(os.sep, "/")
                  for p in included - removed if os.path.isfile(p))


def tar_xz(source, files, prefix, when, out):
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode="w", format=tarfile.PAX_FORMAT) as tar:
        for rel in files:
            data = open(os.path.join(source, rel), "rb").read()
            info = tarfile.TarInfo(prefix + rel)
            info.size, info.mtime, info.mode, info.uid, info.gid, info.uname, info.gname = len(data), when, 0o644, 0, 0, "", ""
            tar.addfile(info, io.BytesIO(data))
    with open(out, "wb") as f:
        f.write(lzma.compress(raw.getvalue(), preset=6))


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def main():
    if len(sys.argv) != 2:
        sys.exit("usage: python tools/third-party/ikvm_source.py <IKVM version, as the game's package reference names it>")
    version = sys.argv[1]
    out = os.path.join(REPO, "third-party", f"ikvm-{version}")
    os.makedirs(out, exist_ok=True)
    with tempfile.TemporaryDirectory() as work:
        ikvm = os.path.join(work, "ikvm")
        git("clone", "--quiet", "--depth", "1", "--branch", version, IKVM, ikvm)
        git("submodule", "update", "--init", "--depth", "1", "ext/openjdk", cwd=ikvm)
        commit = git("rev-parse", "HEAD", cwd=os.path.join(ikvm, "ext", "openjdk")).decode().strip()
        when = int(git("log", "-1", "--format=%ct", cwd=ikvm).decode().strip())
        url = git("config", "--file", ".gitmodules", "submodule.ext/openjdk.url", cwd=ikvm).decode().strip()

        zip_name = f"ikvm-{version}-source.zip"
        with open(os.path.join(out, zip_name), "wb") as f:
            f.write(git("archive", "--format=zip", f"--prefix=ikvm-{version}/", "HEAD", cwd=ikvm))

        files = used_files(ikvm)
        xz_name = f"openjdk-{commit[:12]}-used.tar.xz"
        for old in glob.glob(os.path.join(out, "openjdk-*-used.tar.xz")):
            os.remove(old)
        tar_xz(os.path.join(ikvm, "ext", "openjdk"), files, f"openjdk-{commit[:12]}/", when, os.path.join(out, xz_name))

    lines = [
        f"IKVM {version}, from {IKVM} at the tag {version}",
        f"OpenJDK: {url} at {commit}",
        f"{len(files)} OpenJDK files, those IKVM's build compiles into IKVM.Java.dll",
        "",
        f"{sha256(os.path.join(out, zip_name))}  {zip_name}",
        f"{sha256(os.path.join(out, xz_name))}  {xz_name}",
    ]
    with open(os.path.join(out, "MANIFEST.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    for line in lines:
        print(line)


if __name__ == "__main__":
    main()
