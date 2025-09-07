using System.Security.Cryptography;
using System.Text;

public static class BookingCodeUtil
{
    private static readonly char[] Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    public static string NewCode()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        Span<char> outChars = stackalloc char[9];

        for (int i = 0; i < 4; i++)
            outChars[i] = Alphabet[bytes[i] % Alphabet.Length];

        outChars[4] = '-';

        for (int i = 0; i < 4; i++)
            outChars[5 + i] = Alphabet[bytes[4 + i] % Alphabet.Length];

        return new string(outChars);
    }
}
