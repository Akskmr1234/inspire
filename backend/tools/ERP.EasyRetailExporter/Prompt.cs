using System.Text;

namespace ERP.EasyRetailExporter;

/// <summary>Questions at the console, each with a suggestion that Enter accepts.</summary>
internal static class Prompt
{
    /// <summary>Asks a question.</summary>
    /// <param name="question">What is asked.</param>
    /// <param name="suggestion">What Enter answers; shown in [brackets] when there is one.</param>
    /// <returns>The answer, trimmed, or the suggestion.</returns>
    public static string Ask(string question, string suggestion = "")
    {
        Console.Write(suggestion.Length > 0 ? $"{question} [{suggestion}]: " : $"{question}: ");

        string answer = (Console.ReadLine() ?? string.Empty).Trim();

        return answer.Length > 0 ? answer : suggestion;
    }

    /// <summary>Asks for a password, showing a star for each character typed.</summary>
    /// <param name="question">What is asked.</param>
    /// <returns>The password as typed.</returns>
    public static string AskSecret(string question)
    {
        Console.Write($"{question}: ");

        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        StringBuilder secret = new();

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return secret.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (secret.Length > 0)
                {
                    secret.Length--;
                    Console.Write("\b \b");
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                secret.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }

    /// <summary>Keeps the window open until Enter, so a double-clicked program can be read before it closes.</summary>
    public static void Pause()
    {
        Console.WriteLine();
        Console.Write("Press Enter to close.");
        Console.ReadLine();
    }
}
