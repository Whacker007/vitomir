# IKVM 8.16.0: its source

Vitomir's game runs the original game's Java scripts on **IKVM 8.16.0**, which it ships in its zip. IKVM's Java class library
(`IKVM.Java.dll`) is built from **OpenJDK 8** and is under the **GNU General Public License, version 2, with the Classpath
Exception** (its full text is IKVM's `LICENSE.md`, inside `ikvm-8.16.0-source.zip` and beside IKVM in the game's zip). This folder
is that library's source, offered with Vitomir's own.

- `ikvm-8.16.0-source.zip` — IKVM's own source at its release tag (<https://github.com/ikvmnet/ikvm/releases/tag/8.16.0>).
- `openjdk-5c75a45a66cc-used.tar.xz` — the OpenJDK files IKVM's build compiles into `IKVM.Java.dll`, from IKVM's own copy of
  OpenJDK 8 (<https://github.com/ikvmnet/jdk8u>, commit `5c75a45a66cc5be500bda366c6f0812e9cd17aed`, which IKVM 8.16.0 names).
- `MANIFEST.txt` — the versions and each archive's SHA-256.

**To save space, the rest of OpenJDK is left out**: its virtual machine, native code, tests and documents, none of which is in
what Vitomir ships (16,707 files and about 19 MB kept, of about 600 MB). The whole of it is at the address above, and OpenJDK
itself at <https://openjdk.org>. To build IKVM, put that commit of IKVM's OpenJDK in the IKVM source's `ext/openjdk` folder and
follow IKVM's own instructions.

`tools/third-party/ikvm_source.py 8.16.0` makes this folder again, from the same places.
