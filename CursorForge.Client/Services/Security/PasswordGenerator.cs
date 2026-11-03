using System;
using System.Security.Cryptography;
using System.Text;

namespace CursorForge.Client.Services.Security;

public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%*+?=";
    private static readonly string AllChars = Upper + Lower + Digits + Symbols;

    /// <summary>
    /// Generates a cryptographically secure, high-entropy password that includes
    /// uppercase, lowercase, numbers, and symbols while avoiding easily confused characters (0, O, 1, l, I).
    /// </summary>
    public static string Generate(int length = 16)
    {
        if (length < 8) length = 8;
        if (length > 64) length = 64;

        var chars = new char[length];

        // Ensure at least two of each character class
        chars[0] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[1] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[2] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[3] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[4] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        chars[5] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        chars[6] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];
        chars[7] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];

        // Fill remaining with any character
        for (int i = 8; i < length; i++)
        {
            chars[i] = AllChars[RandomNumberGenerator.GetInt32(AllChars.Length)];
        }

        // Fisher-Yates shuffle using CSPRNG
        for (int i = length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
