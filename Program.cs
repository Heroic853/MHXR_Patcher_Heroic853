namespace MhxrPatcher;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Senza argomenti si apre la finestra: e' l'uso normale.
        if (args.Length == 0)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
            return 0;
        }

        // Con argomenti fa lo stesso lavoro senza interfaccia. Serve per
        // rifare la patch in fretta e per poterla verificare in automatico.
        return DaRigaDiComando(args);
    }

    private static int DaRigaDiComando(string[] args)
    {
        string? ingresso = null, uscita = null, url = null, urlVecchio = null;
        bool inglese = false, firma = true, controllaManifest = true;

        foreach (var a in args)
        {
            if (a.StartsWith("--apk=")) ingresso = a[6..];
            else if (a.StartsWith("--out=")) uscita = a[6..];
            else if (a.StartsWith("--url=")) url = a[6..];
            else if (a.StartsWith("--url-vecchio=")) urlVecchio = a[14..];
            else if (a == "--inglese") inglese = true;
            else if (a == "--senza-firma") firma = false;
            else if (a == "--senza-manifest") controllaManifest = false;
            else if (a is "--aiuto" or "-h" or "--help") { Aiuto(); return 0; }
            else { Console.Error.WriteLine($"Unknown argument: {a}"); Aiuto(); return 2; }
        }

        if (ingresso == null || uscita == null)
        {
            Console.Error.WriteLine("At least --apk= and --out= are required");
            Aiuto();
            return 2;
        }
        if (!File.Exists(ingresso))
        {
            Console.Error.WriteLine($"File not found: {ingresso}");
            return 2;
        }

        byte[]? testi = null;
        if (inglese)
        {
            using var s = typeof(Program).Assembly.GetManifestResourceStream("MhxrPatcher.GUI_msg_en.arc");
            if (s == null) { Console.Error.WriteLine("English texts not included in this build."); return 3; }
            using var mem = new MemoryStream();
            s.CopyTo(mem);
            testi = mem.ToArray();
        }

        var ingressoEffettivo = ingresso;
        var copiaTemporanea = (string?)null;
        if (controllaManifest && url != null)
        {
            copiaTemporanea = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".apk");
            File.Copy(ingresso, copiaTemporanea, overwrite: true);
            var esitoManifest = ManifestFix.AssicuraCleartextTraffic(copiaTemporanea);
            Console.WriteLine("  manifest: " + esitoManifest.Messaggio);
            if (!esitoManifest.Riuscito)
                Console.WriteLine("  WARNING: proceeding with the original APK's manifest — if it lacks cleartext permission, the patched game may never connect.");
            else
                ingressoEffettivo = copiaTemporanea;
        }

        try
        {
            var esito = Patcher.Costruisci(ingressoEffettivo, uscita, url, testi, urlVecchio);
            foreach (var r in esito.Righe) Console.WriteLine("  " + r);
            if (!esito.Riuscito) return 1;

            if (firma)
            {
                if (!Firma.JavaDisponibile)
                {
                    Console.WriteLine("  Java not found: APK NOT signed, the phone will refuse it.");
                    return 4;
                }
                var (ok, messaggio) = Firma.FirmaApk(uscita);
                Console.WriteLine("  " + messaggio);
                if (!ok) return 5;
            }
            return 0;
        }
        finally
        {
            if (copiaTemporanea != null) { try { File.Delete(copiaTemporanea); } catch { /* temporanea, non bloccante */ } }
        }
    }

    private static void Aiuto()
    {
        Console.WriteLine();
        Console.WriteLine("MhxrPatcher — run with no arguments to open the window.");
        Console.WriteLine();
        Console.WriteLine("  --apk=<file>     starting APK              (required)");
        Console.WriteLine("  --out=<file>     APK to create             (required)");
        Console.WriteLine("  --url=<url>      new server address        (max 47 characters)");
        Console.WriteLine("  --url-vecchio=<url>  replace THIS exact existing address instead of guessing the slot");
        Console.WriteLine("  --inglese        replaces the texts with the translated ones");
        Console.WriteLine("  --senza-firma    don't sign (the APK won't install)");
        Console.WriteLine("  --senza-manifest skip the cleartext-traffic manifest check/fix (faster, riskier)");
        Console.WriteLine();
    }
}
