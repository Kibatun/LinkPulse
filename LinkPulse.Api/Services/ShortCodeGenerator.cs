namespace LinkPulse.Api.Services;

public class ShortCodeGenerator
{
    private const string Alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly Random Random = new();

    public static string Generate(int length = 7)
    {
        var codeChars = new char[length];
        for (int i = 0; i < length; i++)
        {
            var randomIndex = Random.Next(Alphabet.Length - 1);
            codeChars[i] = Alphabet[randomIndex];
        }

        return new string(codeChars);
    }
}