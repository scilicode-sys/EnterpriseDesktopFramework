using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Framework.Common.Licensing;

/// <summary>
/// Identifiant matériel stable du poste : SHA-256 sur les composants qui ne
/// bougent pas quand l'utilisateur change de périphérique. Priorité au
/// MachineGuid du registre (unique à l'installation Windows) ; à défaut —
/// lecture refusée — un identifiant de volume système sert de racine stable.
/// Le rendu est groupé en 4 paquets hexadécimaux lisibles à voix au support.
/// </summary>
public sealed class HwidProvider
{
    private readonly Lazy<string> _hwid;

    public HwidProvider()
    {
        _hwid = new Lazy<string>(CalculerHwid);
    }

    /// <summary>HWID de la machine, calculé une fois puis figé pour la session.</summary>
    public string Get() => _hwid.Value;

    private static string CalculerHwid()
    {
        var racine = LireMachineGuid()
            ?? LireIdentifiantVolume()
            ?? $"sans-racine-{Environment.MachineName}";

        var material = $"v1|{racine}|{Environment.MachineName}";
        var empreinte = SHA256.HashData(Encoding.UTF8.GetBytes(material));

        var hex = Convert.ToHexString(empreinte.AsSpan(0, 16));
        return string.Join('-',
            hex.Substring(0, 8), hex.Substring(8, 8), hex.Substring(16, 8), hex.Substring(24, 8));
    }

    private static string? LireMachineGuid()
    {
        try
        {
            using var cle = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default)
                .OpenSubKey("SOFTWARE\\Microsoft\\Cryptography");

            return cle?.GetValue("MachineGuid") as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? LireIdentifiantVolume()
    {
        try
        {
            var lecteur = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";

            if (GetVolumeInformation(lecteur, null, 0, out var serial, out _, out _, null, 0))
            {
                return serial.ToString("X8");
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformation(
        string pathName,
        StringBuilder? volumeNameBuffer,
        uint volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        uint fileSystemNameSize);
}
