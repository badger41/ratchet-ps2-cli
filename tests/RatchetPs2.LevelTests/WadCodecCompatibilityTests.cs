using System.Security.Cryptography;
using RatchetPs2.Core.Wad;

internal static class WadCodecCompatibilityTests
{
    public static void Run()
    {
        foreach (var (period, hash) in new (int, string)[]
        {
            (1, "45101FC35BDACDE025F02B4FDBA8F8ABD39A9EB9E7A1B5F8D64DB98EA8D22F32"),
            (2, "1E0B38DF8C3E24594560B9191CF59EC817C27EE454D78D1EDCC3873AB2090F2D"),
            (3, "B9BE3C013E4FEF71128A84A1F3BA82170F1CA32D25D2F2FD2CACC17B40548625"),
            (7, "896DF4D818F4A4EAFC1E753343D0CF3C70288E1E1CEADC42CD44C627AD36A22C"),
            (15, "E19D53E18F5FAA80AE8FA037480248E9AC24EC1CE7D66CB659DAABBF8065141D"),
            (31, "6E8892F4CDE0AE653EBF12DBD65E6272E93469F115A0C9C428E030A04DA32E19"),
            (127, "EC5969B5BE28EF091A80FFBE14A2E3888A08CE7BDD79300AA64CB4378FE587BF"),
            (264, "EA439F3D4F13DA2DC7322E4C34BD0EEEE1EEDDE7E14DE63643ABB214DEA32050"),
            (273, "2A236F89B6BBC6A83BDC7D3FFBB443B11854FF51B0D4AB8A8B87D3546A2EBE8B"),
            (2048, "A1F8C9AF088B2BE58339E9ED8039CC5F9B3AD6C347EEFBD4742F81D5BD6FA484"),
            (8192, "5DAC5BDDDF4E1E5587144E9C22A8C2D9A0A14DF6D57D82B1788D6DA319B1D9A9"),
            (16384, "AA21E967876694CC08B134DAF26B98B200A2927B06EB9BEA6D13A2C07C73F338"),
            (32704, "629C2E77A8BE8387E1928B0DAC1C6BADD99FF3C0002905D14EFCD324AC35CD2F"),
            (32768, "1EB9477EDFC2A709C3F358AF82EE0D22E9409F5576A501E2F6A9623EBC3D2D1F"),
        })
        {
            var prefix = new byte[period];
            new Random(123).NextBytes(prefix);
            var bytes = new byte[100_000];
            for (var index = 0; index < bytes.Length; index++) bytes[index] = prefix[index % period];
            var result = WadCompression.CompressVerified(bytes);
            if (Convert.ToHexString(SHA256.HashData(result.CompressedBytes)) != hash)
                throw new InvalidOperationException($"WAD encoding changed at period {period}.");
            if (!WadCompression.Decompress(result.CompressedBytes).AsSpan().SequenceEqual(bytes))
                throw new InvalidOperationException($"WAD match expansion changed at period {period}.");
        }
    }
}
