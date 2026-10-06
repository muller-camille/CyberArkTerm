using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using CyberArkTerm.Core.Vnc;

namespace CyberArkTerm.Core.Tests.Vnc;

/// <summary>Client VNC contre un faux serveur RFB : poignée de main, authentification, décodage de l'image, envois.</summary>
public sealed class RfbClientTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Faux serveur : une connexion, le script donné.</summary>
    private sealed class FakeServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public FakeServer(Func<NetworkStream, Task> script)
        {
            _listener.Start();
            Run = Task.Run(async () =>
            {
                using var socket = await _listener.AcceptTcpClientAsync();
                var stream = socket.GetStream();
                await script(stream);
                // Laisse au client le temps de tout lire avant la fermeture.
                await Task.Delay(200);
            });
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public Task Run { get; }

        public void Dispose() => _listener.Stop();
    }

    private static byte[] Read(Stream stream, int count)
    {
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static byte[] U16(int v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)v);
        return b;
    }

    private static byte[] U32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        return b;
    }

    private static byte[] S32(int v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        return b;
    }

    private static byte[] Text(string s) => [.. U32((uint)Encoding.UTF8.GetByteCount(s)), .. Encoding.UTF8.GetBytes(s)];

    private static byte[] ServerInit(int width, int height, string name) =>
        [.. U16(width), .. U16(height), .. new byte[16], .. Text(name)];

    private static byte[] Rect(int x, int y, int w, int h, int encoding) => [.. U16(x), .. U16(y), .. U16(w), .. U16(h), .. S32(encoding)];

    /// <summary>Pixel BGRX tel que le serveur l'envoie dans notre format.</summary>
    private static byte[] Px(byte r, byte g, byte b) => [b, g, r, 0];

    private static (int R, int G, int B) PixelAt(VncFramebuffer fb, int x, int y)
    {
        int o = y * fb.Stride + x * 4;
        return (fb.Pixels[o + 2], fb.Pixels[o + 1], fb.Pixels[o]);
    }

    private static byte[] ExpectedResponse(byte[] challenge, string password)
    {
        var key = new byte[8];
        for (int i = 0; i < Math.Min(8, password.Length); i++)
        {
            byte c = (byte)password[i], r = 0;
            for (int bit = 0; bit < 8; bit++)
            {
                r |= (byte)(((c >> bit) & 1) << (7 - bit));
            }

            key[i] = r;
        }

        using var des = DES.Create();
        des.Key = key;
        return des.EncryptEcb(challenge, PaddingMode.None);
    }

    [Fact]
    public async Task AuthenticatesWithTheVncPasswordAndDecodesTheScreen()
    {
        var challenge = Enumerable.Range(1, 16).Select(i => (byte)(i * 7)).ToArray();
        byte[]? response = null, format = null, encodings = null, request = null;
        using var server = new FakeServer(async s =>
        {
            s.Write("RFB 003.008\n"u8);
            Assert.Equal("RFB 003.008\n", Encoding.ASCII.GetString(Read(s, 12)));
            s.Write([2, 1, 2]);
            Assert.Equal(2, Read(s, 1)[0]);
            s.Write(challenge);
            response = Read(s, 16);
            s.Write(U32(0));
            Assert.Equal(1, Read(s, 1)[0]);
            s.Write(ServerInit(64, 48, "bureau"));
            format = Read(s, 20);
            var head = Read(s, 4);
            encodings = Read(s, BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(2)) * 4);
            request = Read(s, 10);

            // Une mise à jour : Raw 2 × 2, CopyRect de ce carré, Hextile 20 × 17 (4 tuiles).
            var update = new List<byte> { 0, 0 };
            update.AddRange(U16(3));
            update.AddRange(Rect(0, 0, 2, 2, 0));
            update.AddRange([.. Px(255, 0, 0), .. Px(0, 255, 0), .. Px(0, 0, 255), .. Px(9, 9, 9)]);
            update.AddRange(Rect(10, 10, 2, 2, 1));
            update.AddRange([.. U16(0), .. U16(0)]);
            update.AddRange(Rect(20, 20, 20, 17, 5));
            // Tuile 1 (16 × 16) : fond gris, premier plan blanc, deux petits rectangles (un coloré).
            update.AddRange([2 | 4 | 8 | 16, .. Px(50, 50, 50), .. Px(255, 255, 255), 2, .. Px(1, 2, 3), 0x11, 0x00, .. Px(4, 5, 6), 0x00, 0xF0]);
            // Tuile 2 (4 × 16) : brute.
            update.Add(1);
            for (int i = 0; i < 4 * 16; i++)
            {
                update.AddRange(Px((byte)i, 100, 200));
            }

            // Tuile 3 (16 × 1) : fond seul, repris de la tuile 1 ; un rectangle du premier plan.
            update.AddRange([8, 1, 0x30, 0x10]);
            // Tuile 4 (4 × 1) : nouveau fond.
            update.AddRange([2, .. Px(7, 8, 9)]);
            s.Write(update.ToArray());
            s.Write([3, 0, 0, 0, .. U32(7), .. Encoding.Latin1.GetBytes("héllo\nx")]);
            s.Write([2]);
        });

        using var client = await RfbClient.ConnectAsync("127.0.0.1", server.Port, () => "s3cretPWignored", CancellationToken.None);
        Assert.Equal(("3.8", "bureau", 64, 48), (client.ProtocolVersion, client.DesktopName, client.Framebuffer.Width, client.Framebuffer.Height));
        var updated = new TaskCompletionSource<VncRect>();
        var clipboard = new TaskCompletionSource<string>();
        var bell = new TaskCompletionSource();
        client.Updated += r => updated.TrySetResult(r);
        client.ClipboardReceived += t => clipboard.TrySetResult(t);
        client.Bell += () => bell.TrySetResult();
        using var cts = new CancellationTokenSource(Timeout);
        var run = client.RunAsync(cts.Token);
        Assert.Equal(new VncRect(0, 0, 40, 37), await updated.Task.WaitAsync(Timeout));
        Assert.Equal("héllo\r\nx", await clipboard.Task.WaitAsync(Timeout));
        await bell.Task.WaitAsync(Timeout);
        await server.Run.WaitAsync(Timeout);

        Assert.Equal(ExpectedResponse(challenge, "s3cretPW"), response);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 32, 24, 0, 1, 0, 255, 0, 255, 0, 255, 16, 8, 0, 0, 0, 0 }, format);
        Assert.Equal(new[] { 1, 5, 0, -223 }, Enumerable.Range(0, 4).Select(i => BinaryPrimitives.ReadInt32BigEndian(encodings.AsSpan(i * 4))));
        Assert.Equal(new byte[] { 3, 0, 0, 0, 0, 0, 0, 64, 0, 48 }, request);

        var fb = client.Framebuffer;
        Assert.Equal((255, 0, 0), PixelAt(fb, 0, 0));
        Assert.Equal((9, 9, 9), PixelAt(fb, 1, 1));
        Assert.Equal((0, 255, 0), PixelAt(fb, 11, 10));
        Assert.Equal((0, 0, 255), PixelAt(fb, 10, 11));
        // Tuile 1 : rectangle coloré 1 × 1 en (1, 1), rectangle coloré de 16 × 1 en (0, 0), le reste en fond.
        Assert.Equal((4, 5, 6), PixelAt(fb, 20, 20));
        Assert.Equal((4, 5, 6), PixelAt(fb, 35, 20));
        Assert.Equal((1, 2, 3), PixelAt(fb, 21, 21));
        Assert.Equal((50, 50, 50), PixelAt(fb, 20, 21));
        Assert.Equal((50, 50, 50), PixelAt(fb, 35, 35));
        // Tuile 2 brute.
        Assert.Equal((5, 100, 200), PixelAt(fb, 37, 21));
        // Tuile 3 : fond repris, rectangle du premier plan (blanc) de 2 × 1 en (3, 0).
        Assert.Equal((50, 50, 50), PixelAt(fb, 22, 36));
        Assert.Equal((255, 255, 255), PixelAt(fb, 23, 36));
        Assert.Equal((255, 255, 255), PixelAt(fb, 24, 36));
        Assert.Equal((7, 8, 9), PixelAt(fb, 39, 36));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<Exception>(() => run);
    }

    [Fact]
    public async Task Version33WithoutAuthenticationAndScreenResize()
    {
        using var server = new FakeServer(async s =>
        {
            s.Write("RFB 003.003\n"u8);
            Assert.Equal("RFB 003.003\n", Encoding.ASCII.GetString(Read(s, 12)));
            s.Write(U32(1));
            Assert.Equal(1, Read(s, 1)[0]);
            s.Write(ServerInit(8, 8, "petit"));
            Read(s, 20 + 4 + 16 + 10);
            s.Write([0, 0, .. U16(1), .. Rect(0, 0, 32, 16, -223)]);
            Read(s, 10);
        });

        using var client = await RfbClient.ConnectAsync("127.0.0.1", server.Port, () => throw new InvalidOperationException("pas de mot de passe"),
            CancellationToken.None);
        var resized = new TaskCompletionSource();
        client.Resized += () => resized.TrySetResult();
        using var cts = new CancellationTokenSource(Timeout);
        _ = client.RunAsync(cts.Token);
        await resized.Task.WaitAsync(Timeout);
        Assert.Equal((32, 16, 32 * 16 * 4), (client.Framebuffer.Width, client.Framebuffer.Height, client.Framebuffer.Pixels.Length));
        await server.Run.WaitAsync(Timeout);
        cts.Cancel();
    }

    [Fact]
    public async Task ReportsRefusalsWithTheServerReason()
    {
        using (var server = new FakeServer(async s =>
               {
                   s.Write("RFB 003.008\n"u8);
                   Read(s, 12);
                   s.Write([2, 18, 19]);
                   await Task.Delay(100);
               }))
        {
            var e = await Assert.ThrowsAsync<RfbException>(() => RfbClient.ConnectAsync("127.0.0.1", server.Port, () => "x", CancellationToken.None));
            Assert.Contains("TLS (18), VeNCrypt (19)", e.Message, StringComparison.Ordinal);
        }

        using (var server = new FakeServer(async s =>
               {
                   s.Write("RFB 003.008\n"u8);
                   Read(s, 12);
                   s.Write([1, 2]);
                   Read(s, 1);
                   s.Write(new byte[16]);
                   Read(s, 16);
                   s.Write([.. U32(1), .. Text("trop de tentatives")]);
                   await Task.Delay(100);
               }))
        {
            var e = await Assert.ThrowsAsync<RfbException>(() => RfbClient.ConnectAsync("127.0.0.1", server.Port, () => "faux", CancellationToken.None));
            Assert.Contains("trop de tentatives", e.Message, StringComparison.Ordinal);
        }

        using (var server = new FakeServer(async s =>
               {
                   s.Write("RFB 003.007\n"u8);
                   Read(s, 12);
                   s.Write([0, .. Text("serveur plein")]);
                   await Task.Delay(100);
               }))
        {
            var e = await Assert.ThrowsAsync<RfbException>(() => RfbClient.ConnectAsync("127.0.0.1", server.Port, () => null, CancellationToken.None));
            Assert.Contains("serveur plein", e.Message, StringComparison.Ordinal);
        }

        using (var server = new FakeServer(async s =>
               {
                   s.Write("SSH-2.0-OpenSSH\r\n"u8);
                   await Task.Delay(100);
               }))
        {
            await Assert.ThrowsAsync<RfbException>(() => RfbClient.ConnectAsync("127.0.0.1", server.Port, () => null, CancellationToken.None));
        }

        // Mot de passe demandé, l'entrée n'en a pas.
        using (var server = new FakeServer(async s =>
               {
                   s.Write("RFB 003.008\n"u8);
                   Read(s, 12);
                   s.Write([1, 2]);
                   Read(s, 1);
                   s.Write(new byte[16]);
                   await Task.Delay(100);
               }))
        {
            var e = await Assert.ThrowsAsync<RfbException>(() => RfbClient.ConnectAsync("127.0.0.1", server.Port, () => "", CancellationToken.None));
            Assert.Equal(CyberArkTerm.Core.Localization.CoreStrings.VncPasswordRequired, e.Message);
        }
    }

    [Fact]
    public async Task RefusesAHugeScreenAndRectanglesOutsideTheScreen()
    {
        using (var server = new FakeServer(async s =>
               {
                   s.Write("RFB 003.008\n"u8);
                   Read(s, 12);
                   s.Write([1, 1]);
                   Read(s, 1);
                   s.Write(U32(0));
                   Read(s, 1);
                   s.Write(ServerInit(65535, 65535, "géant"));
                   await Task.Delay(100);
               }))
        {
            await Assert.ThrowsAsync<RfbException>(() => RfbClient.ConnectAsync("127.0.0.1", server.Port, () => null, CancellationToken.None));
        }

        using (var server = new FakeServer(async s =>
               {
                   s.Write("RFB 003.008\n"u8);
                   Read(s, 12);
                   s.Write([1, 1]);
                   Read(s, 1);
                   s.Write(U32(0));
                   Read(s, 1);
                   s.Write(ServerInit(10, 10, "x"));
                   Read(s, 20 + 4 + 16 + 10);
                   s.Write([0, 0, .. U16(1), .. Rect(5, 5, 10, 10, 0)]);
                   await Task.Delay(100);
               }))
        {
            using var client = await RfbClient.ConnectAsync("127.0.0.1", server.Port, () => null, CancellationToken.None);
            await Assert.ThrowsAsync<RfbException>(() => client.RunAsync(CancellationToken.None).WaitAsync(Timeout));
        }
    }

    [Fact]
    public async Task SendsKeysPointerWheelAndClipboard()
    {
        var received = new TaskCompletionSource<byte[]>();
        using var server = new FakeServer(async s =>
        {
            s.Write("RFB 003.008\n"u8);
            Read(s, 12);
            s.Write([1, 1]);
            Read(s, 1);
            s.Write(U32(0));
            Read(s, 1);
            s.Write(ServerInit(100, 50, "x"));
            Read(s, 20 + 4 + 16 + 10);
            received.SetResult(Read(s, 8 + 6 + 6 + 6 + 8 + 3));
            await Task.CompletedTask;
        });

        using var client = await RfbClient.ConnectAsync("127.0.0.1", server.Port, () => null, CancellationToken.None);
        using var cts = new CancellationTokenSource(Timeout);
        _ = client.RunAsync(cts.Token);
        client.SendKey(VncKeys.Return, down: true);
        client.SendPointer(500, -3, 1);
        client.SendWheel(10, 20, -120);
        client.SendClipboard("é\r\nü");
        var bytes = await received.Task.WaitAsync(Timeout);
        Assert.Equal(new byte[] { 4, 1, 0, 0, 0, 0, 0xff, 0x0d }, bytes[..8]);
        Assert.Equal(new byte[] { 5, 1, 0, 99, 0, 0 }, bytes[8..14]);
        Assert.Equal(new byte[] { 5, 1 | 16, 0, 10, 0, 20 }, bytes[14..20]);
        Assert.Equal(new byte[] { 5, 1, 0, 10, 0, 20 }, bytes[20..26]);
        Assert.Equal(new byte[] { 6, 0, 0, 0, 0, 0, 0, 3, 0xe9, (byte)'\n', 0xfc }, bytes[26..]);
        cts.Cancel();
    }

    [Theory]
    [InlineData('a', 0x61u)]
    [InlineData('é', 0xe9u)]
    [InlineData('€', 0x010020acu)]
    [InlineData('\r', VncKeys.Return)]
    [InlineData('\u0003', 0u)]
    public void MapsCharactersToKeysyms(char c, uint keysym) => Assert.Equal(keysym, VncKeys.FromChar(c));

    [Fact]
    public void DesMatchesTheStandardAndTheSystemImplementation()
    {
        Assert.Equal("85E813540F0AB405", Convert.ToHexString(VncDes.EncryptEcb(Convert.FromHexString("133457799BBCDFF1"),
            Convert.FromHexString("0123456789ABCDEF"))));
        var random = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            var key = new byte[8];
            var data = new byte[16];
            random.NextBytes(key);
            random.NextBytes(data);
            using var des = DES.Create();
            try
            {
                des.Key = key;
            }
            catch (CryptographicException)
            {
                continue;
            }

            Assert.Equal(des.EncryptEcb(data, PaddingMode.None), VncDes.EncryptEcb(key, data));
        }

        // Clé faible pour DES (refusée par le système) : le protocole VNC l'accepte quand même.
        Assert.Equal(16, VncDes.EncryptEcb(new byte[8], new byte[16]).Length);
    }

    [Fact]
    public void PrefersTheVncPasswordAndRefusesUnknownSecurity()
    {
        Assert.Equal((byte)2, RfbClient.ChooseSecurity([1, 2]));
        Assert.Equal((byte)1, RfbClient.ChooseSecurity([1, 16]));
        Assert.Throws<RfbException>(() => RfbClient.ChooseSecurity([30]));
        Assert.Equal(VncKeys.F1 + 11, VncKeys.Function(12));
    }
}
