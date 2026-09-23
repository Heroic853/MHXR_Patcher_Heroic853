using System.Diagnostics;

namespace MhxrPatcher;

/// <summary>
/// Firma dell'APK con lo schema v1+v2+v3 di Android, tramite lo strumento
/// ufficiale Google `apksigner` (incorporato nell'eseguibile, vedi
/// apksigner.jar nel .csproj) e `keytool`/`java` del JDK dell'utente.
///
/// PERCHE' SERVE: un APK modificato non porta piu' la firma originale, e
/// Android rifiuta di installare un pacchetto non firmato. La firma non serve
/// a "sbloccare" niente, e' solo il sigillo che dice al telefono che il file
/// e' arrivato intero.
///
/// PERCHE' apksigner e non (solo) jarsigner: jarsigner produce solo la firma
/// v1 (JAR signing), quella del 2008. Un APK di riferimento fornito
/// dall'utente e confermato funzionante al 100% porta invece un blocco
/// "APK Sig Block 42" (v2/v3), che jarsigner non puo' produrre — verificato
/// essere probabilmente la causa reale del crash muto degli APK patchati
/// finora. apksigner produce v1+v2+v3 insieme (retrocompatibilita' inclusa) e,
/// a differenza di jarsigner, NON disallinea le voci "store" gia' allineate:
/// verificato su un APK vero da 741 voci, 370 store, zero storte dopo la
/// firma — quindi qui non serve nessun passaggio di riallineamento dopo.
///
/// La chiave viene creata una volta sola e riusata: cosi' gli aggiornamenti
/// successivi si installano sopra il precedente invece di obbligare a
/// disinstallare (e a perdere i dati locali del gioco).
/// </summary>
public static class Firma
{
    private const string Alias = "mhxr";
    private const string Password = "mhxrpatcher";

    private static string CartellaDati =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MhxrPatcher");

    public static string PercorsoChiave => Path.Combine(CartellaDati, "mhxr.keystore");

    private static string PercorsoApksigner => Path.Combine(CartellaDati, "apksigner.jar");

    /// <summary>Cerca java/keytool: prima nel PATH, poi in JAVA_HOME.</summary>
    public static string? TrovaStrumento(string nome)
    {
        var exe = nome + ".exe";

        var percorsi = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        foreach (var p in percorsi)
        {
            try
            {
                var completo = Path.Combine(p.Trim(), exe);
                if (File.Exists(completo)) return completo;
            }
            catch { /* voci malformate nel PATH: si ignorano */ }
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            var completo = Path.Combine(javaHome, "bin", exe);
            if (File.Exists(completo)) return completo;
        }
        return null;
    }

    public static bool JavaDisponibile => TrovaStrumento("java") != null && TrovaStrumento("keytool") != null;

    private static (int codice, string output) Esegui(string exe, string argomenti)
    {
        var psi = new ProcessStartInfo(exe, argomenti)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    /// <summary>Crea la chiave se non esiste gia'.</summary>
    public static (bool ok, string messaggio) PreparaChiave()
    {
        if (File.Exists(PercorsoChiave)) return (true, "key already present");

        var keytool = TrovaStrumento("keytool");
        if (keytool == null) return (false, "keytool not found: Java does not appear to be installed");

        Directory.CreateDirectory(CartellaDati);
        var args = $"-genkeypair -keystore \"{PercorsoChiave}\" -alias {Alias} " +
                   $"-storepass {Password} -keypass {Password} -keyalg RSA -keysize 2048 " +
                   $"-validity 10000 -dname \"CN=MHXR Patcher, OU=Privato, O=Privato, C=IT\"";
        var (codice, output) = Esegui(keytool, args);
        return codice == 0
            ? (true, "key created at " + PercorsoChiave)
            : (false, "key creation failed: " + output.Trim());
    }

    /// <summary>
    /// Estrae apksigner.jar dalle risorse incorporate, se non e' gia' sul
    /// disco. Riscritto a ogni avvio del programma (non solo se manca): pesa
    /// circa 1 MB, e cosi' un aggiornamento di MhxrPatcher.exe porta sempre
    /// la propria versione invece di lasciare in giro quella di una build
    /// precedente.
    /// </summary>
    private static string PreparaApksigner()
    {
        Directory.CreateDirectory(CartellaDati);
        using var risorsa = typeof(Firma).Assembly.GetManifestResourceStream("MhxrPatcher.apksigner.jar")
            ?? throw new InvalidOperationException("apksigner.jar not embedded in this build.");
        using (var file = File.Create(PercorsoApksigner))
            risorsa.CopyTo(file);
        return PercorsoApksigner;
    }

    public static (bool ok, string messaggio) FirmaApk(string apk)
    {
        var (okChiave, msgChiave) = PreparaChiave();
        if (!okChiave) return (false, msgChiave);

        var java = TrovaStrumento("java");
        if (java == null) return (false, "java not found: Java does not appear to be installed");

        string apksignerJar;
        try { apksignerJar = PreparaApksigner(); }
        catch (Exception ex) { return (false, "apksigner setup failed: " + ex.Message); }

        /*
         * apksigner (java) riceve il percorso dell'APK come argomento a riga
         * di comando, e su Windows quell'argomento passa attraverso la
         * codifica ANSI del sistema (sun.jnu.encoding) prima di arrivare al
         * JDK. Un nome file con caratteri non rappresentabili in quella
         * codifica (visto con un APK giapponese, es. "モンスターハンター...")
         * arriva corrotto e File.getCanonicalPath() lancia "Bad pathname" —
         * bug della JVM su Windows, non nostro, ma dobbiamo aggirarlo.
         * Si firma quindi sempre una copia con nome temporaneo tutto-ASCII, e
         * si rimette il risultato al posto giusto (nome originale) solo se la
         * firma riesce.
         */
        var apkTemporaneo = Path.Combine(CartellaDati, Guid.NewGuid().ToString("N") + ".apk");
        try
        {
            Directory.CreateDirectory(CartellaDati);
            File.Copy(apk, apkTemporaneo, overwrite: true);

            // --v4-signing-enabled false: il v4 e' pensato per l'installazione
            // incrementale (ADB push veloce durante lo sviluppo) e produce un
            // secondo file ".idsig" separato dall'APK — inutile per un file che
            // l'utente copia e installa a mano, e un file in piu' da perdere.
            // v1+v2+v3 restano attivi per default (dedotti da apksigner dal
            // minSdkVersion/maxSdkVersion nel manifest dell'APK stesso).
            var args = $"-jar \"{apksignerJar}\" sign " +
                       $"--ks \"{PercorsoChiave}\" --ks-key-alias {Alias} " +
                       $"--ks-pass pass:{Password} --key-pass pass:{Password} " +
                       $"--v4-signing-enabled false \"{apkTemporaneo}\"";
            var (codice, output) = Esegui(java, args);
            if (codice != 0) return (false, "signing failed: " + output.Trim());

            File.Copy(apkTemporaneo, apk, overwrite: true);
            return (true, "APK signed (v1+v2+v3)");
        }
        finally
        {
            try { File.Delete(apkTemporaneo); } catch { /* file temporaneo, non e' bloccante */ }
        }
    }
}
