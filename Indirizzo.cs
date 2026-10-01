using System.Text;

namespace MhxrPatcher;

/// <summary>
/// L'indirizzo del server privato, scritto nell'APK al posto di quello Capcom.
///
/// NON e' tenuto come stringa letterale nel sorgente: e' codificato in Base64 e
/// ricostruito qui. Questo NON e' vera sicurezza — chiunque apra l'exe con un
/// decompilatore (dnSpy, ILSpy) lo vede comunque — alza solo l'asticella per chi
/// cercasse l'indirizzo con un editor di testo o "strings.exe".
///
/// IP fisso della VPS, non un dominio: alcuni operatori telefonici filtrano
/// *.duckdns.org per categoria "dynamic DNS" (verificato). Se un giorno si cambia
/// VPS, questo valore va aggiornato qui e il patcher ripubblicato.
/// </summary>
internal static class Indirizzo
{
    public static readonly string Server =
        Encoding.UTF8.GetString(Convert.FromBase64String("aHR0cDovLzU3LjEzMS4xOTMuMjQ0Lw=="));

    /// <summary>Lo slot piu' piccolo fra le due architetture del gioco: e' quello che comanda.</summary>
    public const int MaxCaratteri = 47;

    /// <summary>Ultimi 4 caratteri veri, il resto pallini: non finisce leggibile in uno screenshot.</summary>
    public static string Mascherato(string reale)
    {
        const int visibili = 4;
        return reale.Length <= visibili ? reale : new string('•', reale.Length - visibili) + reale[^visibili..];
    }
}
