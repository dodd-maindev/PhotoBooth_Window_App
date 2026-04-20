using System.Diagnostics;
using System.Text;

namespace Photobooth.Desktop.Services;

public enum CanonConnectionMode
{
    Unknown = 0,
    WebcamLive = 1,
    EosUtilityTether = 2,
    None = 3
}

public static class CanonUtilityDetector
{
    public static CanonConnectionMode DetectMode()
    {
        try
        {
            var processNames = Process.GetProcesses()
                .Select(process => Normalize(process.ProcessName))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            var hasWebcamUtility = processNames.Any(name =>
                name.Contains("eoswebcamutility", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("eoswebcamutilitypro", StringComparison.OrdinalIgnoreCase));

            if (hasWebcamUtility)
            {
                return CanonConnectionMode.WebcamLive;
            }

            var hasEosUtility = processNames.Any(name =>
                name.Contains("eosutility", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("eosutility3", StringComparison.OrdinalIgnoreCase));

            if (hasEosUtility)
            {
                return CanonConnectionMode.EosUtilityTether;
            }

            return CanonConnectionMode.None;
        }
        catch
        {
            return CanonConnectionMode.Unknown;
        }
    }

    private static string Normalize(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var character in input)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}
