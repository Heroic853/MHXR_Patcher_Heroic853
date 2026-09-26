using System.Diagnostics;

namespace MhxrPatcher;

/// <summary>
/// Aggiunge android:usesCleartextTraffic="true" al manifest quando manca.
///
/// PERCHE' SERVE: un APK scaricata da APKPure (a differenza di quella gia'
/// patchata in passato con lo strumento apktool a mano) non ha questo
/// permesso. Su Android 9+ senza questo permesso il gioco non manda MAI un
/// pacchetto HTTP in chiaro, verso nessun indirizzo — nessun errore visibile
/// in questo programma, l'APK viene creata e firmata senza problemi, ma il
/// gioco resta scollegato per sempre. Scoperto vero confrontando byte per
/// byte un'APK funzionante con una che dava errore 403 in continuazione.
///
/// PERCHE' NON SI PUO' FARE COME PER L'INDIRIZZO: l'indirizzo dentro
/// libMHS.so e' un pezzo di testo in uno slot gia' esistente, si sovrascrive
/// e basta. Il manifest invece e' XML BINARIO (AXML): aggiungere un
/// attributo che prima non c'era vuol dire cambiare la struttura vera del
/// file (contatori, tabella delle stringhe, offset), non solo dei byte sul
/// posto — troppo rischioso da fare a mano bit per bit. Si usa quindi
/// apktool (Apache 2.0, gia' presente nel progetto per altri scopi) per
/// decodificare, modificare il testo leggibile, e ricostruire.
/// </summary>
public static class ManifestFix
{
    private static string CartellaDati =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MhxrPatcher");

    private static string PercorsoApktool => Path.Combine(CartellaDati, "apktool.jar");

    // Stessa cartella che apktool usa gia' di suo per cercare il framework:
    // se lo mettiamo qui non prova nemmeno a scaricarlo da internet.
    private static string PercorsoFramework =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "apktool", "framework", "1.apk");

    private static void PreparaStrumenti()
    {
        Directory.CreateDirectory(CartellaDati);
        using (var risorsa = typeof(ManifestFix).Assembly.GetManifestResourceStream("MhxrPatcher.apktool.jar")
                              ?? throw new InvalidOperationException("apktool.jar not embedded in this build."))
        using (var file = File.Create(PercorsoApktool))
            risorsa.CopyTo(file);

        if (!File.Exists(PercorsoFramework))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PercorsoFramework)!);
            using var risorsa = typeof(ManifestFix).Assembly.GetManifestResourceStream("MhxrPatcher.apktool-framework.apk")
                                 ?? throw new InvalidOperationException("apktool-framework.apk not embedded in this build.");
            using var file = File.Create(PercorsoFramework);
            risorsa.CopyTo(file);
        }
    }

    private static (int codice, string output) Esegui(string java, string argomenti, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(java, argomenti)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* gia' morto, non importa */ }
            return (-1, "apktool timed out (over " + timeout.TotalSeconds + "s)");
        }
        return (p.ExitCode, stdout.Result + stderr.Result);
    }

    public record Esito(bool Modificato, bool Riuscito, string Messaggio);

    /// <summary>
    /// Se il manifest di <paramref name="apk"/> ha gia' il permesso, non fa
    /// nulla (Modificato=false) e non tocca il file. Altrimenti prova ad
    /// aggiungerlo, sovrascrivendo <paramref name="apk"/> sul posto solo se
    /// tutto il giro (decodifica, modifica, ricostruzione) va a buon fine.
    /// </summary>
    public static Esito AssicuraCleartextTraffic(string apk)
    {
        if (!Firma.JavaDisponibile)
            return new Esito(false, false, "Java not found: cannot check/fix the manifest.");

        var java = Firma.TrovaStrumento("java")!;
        var cartellaLavoro = Path.Combine(CartellaDati, "manifestfix-" + Guid.NewGuid().ToString("N"));

        try
        {
            PreparaStrumenti();
            Directory.CreateDirectory(cartellaLavoro);
            var decodificata = Path.Combine(cartellaLavoro, "decoded");

            // -s: salta smali (non ci serve, e' molto piu' lento senza).
            // Le risorse SERVONO: e' l'unico modo per avere un manifest leggibile.
            var (codiceD, outD) = Esegui(java,
                $"-jar \"{PercorsoApktool}\" d -f -s -o \"{decodificata}\" \"{apk}\"",
                TimeSpan.FromMinutes(4));
            if (codiceD != 0)
                return new Esito(false, false, "apktool decode failed: " + outD.Trim());

            var manifestPath = Path.Combine(decodificata, "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
                return new Esito(false, false, "AndroidManifest.xml not found after decode.");

            var testo = File.ReadAllText(manifestPath);
            if (testo.Contains("android:usesCleartextTraffic"))
                return new Esito(false, true, "manifest already has the cleartext-traffic permission");

            // Stesso punto di aggancio usato a mano per verificare il bug:
            // il tag <application ...> con android:name=".../MultiDexApplication".
            const string ancora = "android.support.multidex.MultiDexApplication\"";
            if (!testo.Contains(ancora))
                return new Esito(false, false, "expected <application> tag not found in the manifest (unrecognized APK layout)");

            testo = testo.Replace(ancora, ancora + " android:usesCleartextTraffic=\"true\"");
            File.WriteAllText(manifestPath, testo);

            var ricostruita = Path.Combine(cartellaLavoro, "rebuilt.apk");
            var (codiceB, outB) = Esegui(java,
                $"-jar \"{PercorsoApktool}\" b -o \"{ricostruita}\" \"{decodificata}\"",
                TimeSpan.FromMinutes(4));
            if (codiceB != 0 || !File.Exists(ricostruita))
                return new Esito(false, false, "apktool build failed: " + outB.Trim());

            File.Copy(ricostruita, apk, overwrite: true);
            return new Esito(true, true, "added android:usesCleartextTraffic=\"true\" (was missing)");
        }
        catch (Exception ex)
        {
            return new Esito(false, false, "manifest fix error: " + ex.Message);
        }
        finally
        {
            try { Directory.Delete(cartellaLavoro, recursive: true); } catch { /* cartella temporanea, non bloccante */ }
        }
    }
}
