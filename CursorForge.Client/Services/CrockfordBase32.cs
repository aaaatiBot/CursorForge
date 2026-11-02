using System.Security.Cryptography;
using System.Text;

namespace CursorForge.Client.Services;

/// <summary>
/// Provides cryptographically secure Crockford Base32 generation, normalization,
/// and salted SHA-256 hash calculation for single-use account recovery keys.
/// </summary>
public static class CrockfordBase32
{
    // Crockford's Base32 Alphabet: digits 0-9 and letters A-Z excluding I, L, O, and U.
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int DefaultLength = 20;

    /// <summary>
    /// Generates a random Crockford Base32 recovery code.
    /// </summary>
    public static string Generate(int length = DefaultLength)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than 0");
        }

        byte[] randomBytes = new byte[length];
        RandomNumberGenerator.Fill(randomBytes);

        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            int index = randomBytes[i] % Alphabet.Length;
            sb.Append(Alphabet[index]);
        }

        return FormatWithHyphens(sb.ToString());
    }

    /// <summary>
    /// Formats a clean Crockford string with hyphens every 4 or 5 characters (e.g. XXXX-XXXX-XXXX-XXXX-XXXX).
    /// </summary>
    public static string FormatWithHyphens(string rawCode, int groupSize = 4)
    {
        string normalized = Normalize(rawCode);
        if (string.IsNullOrEmpty(normalized))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < normalized.Length; i++)
        {
            if (i > 0 && i % groupSize == 0)
            {
                sb.Append('-');
            }
            sb.Append(normalized[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Normalizes user-entered recovery codes:
    /// - Strips hyphens and whitespace
    /// - Uppercases letters
    /// - Maps 'I' and 'L' to '1'
    /// - Maps 'O' to '0'
    /// </summary>
    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (char.IsWhiteSpace(c) || c == '-')
            {
                continue;
            }

            char upper = char.ToUpperInvariant(c);
            switch (upper)
            {
                case 'I':
                case 'L':
                    sb.Append('1');
                    break;
                case 'O':
                    sb.Append('0');
                    break;
                default:
                    if (Alphabet.Contains(upper))
                    {
                        sb.Append(upper);
                    }
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Computes the salted SHA-256 hash: SHA256(lower(username) + normalized_code).
    /// </summary>
    public static string ComputeRecoveryHash(string username, string recoveryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryCode);

        string cleanUser = username.Trim().ToLowerInvariant();
        string cleanCode = Normalize(recoveryCode);

        string payload = cleanUser + cleanCode;
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));

        return Convert.ToHexStringLower(hashBytes);
    }
}
