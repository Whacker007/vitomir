// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// **The sealed session** (ours; "yes, cheap"). Every connection's datagrams are sealed with
// AES-GCM under keys an ECDH exchange at the join made (.NET's own: P-256, SHA-256, HKDF), the nonce the packet's number
// and the header authenticated, so nobody on the path reads the game or slips a datagram into it; and the game's
// password never crosses the wire: each end stretches it with the session's nonces into a key, and the joining machine
// proves it holds that key (PasswordProof). What it does not do: no
// certificates (a machine that sits between two players from the first datagram can be either to the other), a weak
// password can be guessed offline by such a machine from the proof it was sent, and the LAN finder's broadcasts are plain.
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Vitomir.Engine.Net;

/// <summary>
/// **One end of a connection's key exchange and its two seals** (ours). The connecting machine makes its half
/// (<see cref="NetSeal(bool)"/>: a fresh P-256 key and 16 random bytes) and knocks with <see cref="Half"/>; the host
/// makes its own on the first knock and answers with its half; each end then <see cref="Join"/>s the other's half: the
/// shared secret, hashed with the whole exchange (<see cref="Binding"/>), gives one AES-256-GCM key each way (HKDF,
/// "vitomir c2s" and "vitomir s2c"). A sealed datagram is its plain header (the magic, the version, its kind, the token
/// and the 64-bit packet number, all authenticated), the sealed body and GCM's 16-byte tag; the nonce is the packet
/// number, which each way counts from 1 and never repeats, and a receiver takes each number once
/// (<see cref="Fresh"/>, a window of the last 64), so a datagram recorded and sent again is dropped.
/// </summary>
public sealed class NetSeal : IDisposable
{
    /// <summary>Bytes of one half: 16 random, then the public key's point (0x04, X, Y).</summary>
    public const int HalfLength = 16 + 65;
    /// <summary>What sealing adds to a datagram: the packet number, GCM's tag and the body's kind byte.</summary>
    public const int Overhead = 8 + 16 + 1;
    /// <summary>The sealed datagram's plain header: magic, version, kind, token, packet number.</summary>
    public const int HeaderLength = 3 + 4 + 8;

    readonly ECDiffieHellman mine;
    readonly bool connecting;
    AesGcm? send, receive;
    ulong sent;
    ulong highest, window;

    /// <param name="connecting">Whether this end knocked (a player) rather than answered (the host).</param>
    public NetSeal(bool connecting)
    {
        this.connecting = connecting;
        mine = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        Half = new byte[HalfLength];
        RandomNumberGenerator.Fill(Half.AsSpan(0, 16));
        var p = mine.ExportParameters(includePrivateParameters: false);
        Half[16] = 4;
        p.Q.X!.CopyTo(Half, 17);
        p.Q.Y!.CopyTo(Half, 49);
    }

    /// <summary>This end's half of the exchange, as the knock or the answer carries it.</summary>
    public byte[] Half { get; }

    /// <summary>
    /// **What names this session**: SHA-256 of the token and both halves, the connecting machine's first — the same
    /// on both ends, different for every join. The password's proof is made over it (<see cref="PasswordProof"/>), so a
    /// proof is good for this session alone. Empty until <see cref="Join"/>.
    /// </summary>
    public byte[] Binding { get; private set; } = [];

    /// <summary>Whether both seals are made.</summary>
    public bool Ready => send is not null;

    /// <summary>
    /// The other end's half taken: the shared secret, the binding and the two keys. False when the half is not a point
    /// on the curve (or not a half at all): nothing is made, and the connection is not opened.
    /// </summary>
    public bool Join(ReadOnlySpan<byte> theirs, uint token)
    {
        if (theirs.Length < HalfLength || theirs[16] != 4 || Ready) return false;
        byte[] secret;
        try
        {
            using var other = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = theirs.Slice(17, 32).ToArray(), Y = theirs.Slice(49, 32).ToArray() },
            });
            var exchange = new byte[4 + 2 * HalfLength];
            BinaryPrimitives.WriteUInt32LittleEndian(exchange, token);
            (connecting ? Half : theirs[..HalfLength]).CopyTo(exchange.AsSpan(4));
            (connecting ? theirs[..HalfLength] : Half).CopyTo(exchange.AsSpan(4 + HalfLength));
            Binding = SHA256.HashData(exchange);
            secret = mine.DeriveKeyFromHash(other.PublicKey, HashAlgorithmName.SHA256, null, Binding);
        }
        // Any refusal of the half is a refusal, whatever the platform's own type for it: Windows' CNG says a point off the curve
        // with PlatformNotSupportedException (a Windows PC, a fuzz test), Linux's OpenSSL with CryptographicException.
        catch (Exception e) when (e is CryptographicException or ArgumentException or PlatformNotSupportedException or NotSupportedException)
        {
            Binding = [];
            return false;
        }
        var up = HKDF.Expand(HashAlgorithmName.SHA256, HKDF.Extract(HashAlgorithmName.SHA256, secret, Binding), 32, "vitomir c2s"u8.ToArray());
        var down = HKDF.Expand(HashAlgorithmName.SHA256, HKDF.Extract(HashAlgorithmName.SHA256, secret, Binding), 32, "vitomir s2c"u8.ToArray());
        send = new AesGcm(connecting ? up : down, 16);
        receive = new AesGcm(connecting ? down : up, 16);
        CryptographicOperations.ZeroMemory(secret);
        CryptographicOperations.ZeroMemory(up);
        CryptographicOperations.ZeroMemory(down);
        return true;
    }

    /// <summary>
    /// Seals a plain packet (<paramref name="plain"/>: the magic, the version, the body's kind, the token, then the body)
    /// into <paramref name="sealedPacket"/>, which takes <c>plain.Length + Overhead</c> bytes; the length written.
    /// </summary>
    public int Seal(ReadOnlySpan<byte> plain, Span<byte> sealedPacket, byte sealedKind)
    {
        ulong number = ++sent;
        sealedPacket[0] = plain[0];
        sealedPacket[1] = plain[1];
        sealedPacket[2] = sealedKind;
        plain.Slice(3, 4).CopyTo(sealedPacket[3..]);
        BinaryPrimitives.WriteUInt64LittleEndian(sealedPacket[7..], number);
        Span<byte> nonce = stackalloc byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], number);
        int body = plain.Length - 7;
        // The body's kind first, then the body after the token: `[kind][body]` is what is sealed.
        Span<byte> inner = body + 1 <= 1400 ? stackalloc byte[body + 1] : new byte[body + 1];
        inner[0] = plain[2];
        plain[7..].CopyTo(inner[1..]);
        send!.Encrypt(nonce, inner, sealedPacket.Slice(HeaderLength, body + 1), sealedPacket.Slice(HeaderLength + body + 1, 16), sealedPacket[..HeaderLength]);
        return HeaderLength + body + 1 + 16;
    }

    /// <summary>
    /// Opens a sealed datagram back into the plain packet it was (the body's kind in place of the sealed kind, the
    /// packet number gone) in <paramref name="plain"/>; the length, or -1 when it is too short, does not authenticate, or
    /// its number was taken before (a datagram sent again by somebody on the path: <paramref name="again"/>).
    /// </summary>
    public int Open(ReadOnlySpan<byte> sealedPacket, Span<byte> plain, out bool again)
    {
        again = false;
        if (receive is null || sealedPacket.Length < HeaderLength + 1 + 16) return -1;
        ulong number = BinaryPrimitives.ReadUInt64LittleEndian(sealedPacket[7..]);
        if (number == 0 || !Fresh(number, take: false)) { again = number != 0; return -1; }
        int inner = sealedPacket.Length - HeaderLength - 16;
        Span<byte> nonce = stackalloc byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], number);
        Span<byte> opened = inner <= 1400 ? stackalloc byte[inner] : new byte[inner];
        try
        {
            receive.Decrypt(nonce, sealedPacket.Slice(HeaderLength, inner), sealedPacket.Slice(HeaderLength + inner, 16), opened, sealedPacket[..HeaderLength]);
        }
        catch (CryptographicException) { return -1; }
        Fresh(number, take: true);
        plain[0] = sealedPacket[0];
        plain[1] = sealedPacket[1];
        plain[2] = opened[0];
        sealedPacket.Slice(3, 4).CopyTo(plain[3..]);
        opened[1..].CopyTo(plain[7..]);
        return 7 + inner - 1;
    }

    /// <summary>
    /// Whether packet <paramref name="number"/> has not been taken: past the highest yet, or one of the 64 below it not
    /// yet seen (a datagram overtaken on the way). Marked taken when <paramref name="take"/>.
    /// </summary>
    bool Fresh(ulong number, bool take)
    {
        if (number > highest)
        {
            if (take)
            {
                ulong shift = number - highest;
                window = shift >= 64 ? 0 : window << (int)shift;
                window |= 1;
                highest = number;
            }
            return true;
        }
        ulong back = highest - number;
        if (back >= 64) return false;
        bool seen = (window & (1UL << (int)back)) != 0;
        if (take && !seen) window |= 1UL << (int)back;
        return !seen;
    }

    public void Dispose()
    {
        mine.Dispose();
        send?.Dispose();
        receive?.Dispose();
    }
}

/// <summary>
/// **The game's password, proved and never sent** (ours). Both ends stretch the password into a key with
/// PBKDF2-SHA256 (<see cref="Iterations"/> rounds), salted with the session's binding (<see cref="NetSeal.Binding"/>,
/// which both ends' random halves make), and the joining machine sends HMAC-SHA256 of the binding under that key in its
/// Hello; the host makes the same and compares. A wrong password is 1.1's own refusal, <c>UI_NET_WRONGGAMEPASSWORD</c>.
/// Neither the password nor anything made from it alone is written to a tape (<see cref="NetPeer"/>'s taping masks the proof).
/// </summary>
public static class PasswordProof
{
    /// <summary>PBKDF2's rounds: 60,000, some 20–40 ms on a PC at each end of a join (measured in the report).</summary>
    public const int Iterations = 60_000;

    /// <summary>The proof's length.</summary>
    public const int Length = 32;

    /// <summary>The proof of <paramref name="password"/> for the session <paramref name="binding"/> names; empty for no password.</summary>
    public static byte[] Make(string password, ReadOnlySpan<byte> binding)
    {
        if (password.Length == 0) return [];
        var salt = new byte[binding.Length + 16];
        "vitomir password"u8.CopyTo(salt);
        binding.CopyTo(salt.AsSpan(16));
        var key = Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        var proof = HMACSHA256.HashData(key, binding);
        CryptographicOperations.ZeroMemory(key);
        return proof;
    }

    /// <summary>Whether <paramref name="proof"/> proves <paramref name="password"/> for this session (in fixed time).</summary>
    public static bool Holds(string password, ReadOnlySpan<byte> binding, ReadOnlySpan<byte> proof) =>
        proof.Length == Length && CryptographicOperations.FixedTimeEquals(Make(password, binding), proof);
}
