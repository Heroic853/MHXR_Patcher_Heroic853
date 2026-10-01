namespace MhxrPatcher;

/// <summary>
/// Finestra del patcher: si sceglie l'APK giapponese originale e si crea quello
/// patchato. Il programma fa sempre e solo le due cose che servono per giocare sul
/// server privato:
///   - scrive l'indirizzo del server in libMHS.so (entrambe le architetture);
///   - aggiunge al manifest il permesso per il traffico HTTP in chiaro, se manca.
/// La traduzione inglese non e' piu' qui dentro: la scarica il gioco dal server,
/// con gli aggiornamenti delle risorse.
/// </summary>
public class Form1 : Form
{
    private readonly TextBox _percorsoApk = new();
    private readonly Button _crea = new();
    private readonly TextBox _log = new();

    public Form1()
    {
        // L'icona del file .exe (ApplicationIcon nel .csproj) non diventa da
        // sola quella della finestra: va assegnata qui.
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* nessuna icona, non e' bloccante */ }

        // Sfondo: immagine incorporata, gia' preparata a bassa opacita'.
        try
        {
            using var s = typeof(Form1).Assembly.GetManifestResourceStream("MhxrPatcher.sfondo.png");
            if (s != null) BackgroundImage = Image.FromStream(s);
        }
        catch { /* niente sfondo, non e' bloccante */ }
        BackgroundImageLayout = ImageLayout.Zoom;

        Text = "MHXR Patcher";
        Width = 720;
        Height = 480;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        var y = 18;

        // --- 1. APK ---------------------------------------------------------
        Controls.Add(Etichetta("1.  Choose the original Japanese APK of the game", 18, y, true));
        y += 26;
        _percorsoApk.SetBounds(18, y, 540, 24);
        _percorsoApk.ReadOnly = true;
        _percorsoApk.PlaceholderText = "no file selected";
        Controls.Add(_percorsoApk);

        var sfoglia = new Button { Text = "Browse...", Left = 568, Top = y - 1, Width = 110, Height = 26 };
        sfoglia.Click += (_, _) => ScegliApk();
        Controls.Add(sfoglia);
        y += 30;

        Controls.Add(Etichetta(
            "Japanese version only. The Taiwan build has an anti-tampering check and will not run after patching.",
            18, y, false, Color.Firebrick));
        y += 22;
        Controls.Add(Etichetta(
            "The game connects to the private server (" + Indirizzo.Mascherato(Indirizzo.Server) + "). "
            + "The English translation is downloaded from the server.",
            18, y, false, SystemColors.GrayText));
        y += 36;

        // --- 2. crea --------------------------------------------------------
        _crea.SetBounds(18, y, 250, 36);
        _crea.Text = "2.  Create and save the APK";
        _crea.Font = new Font(Font, FontStyle.Bold);
        _crea.Click += (_, _) => Crea();
        Controls.Add(_crea);
        y += 48;

        _log.SetBounds(18, y, 660, 200);
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Font = new Font("Consolas", 8.5F);
        _log.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        Controls.Add(_log);

        AggiornaStato();
        ControllaJava();
    }

    private static Label Etichetta(string testo, int x, int y, bool grassetto, Color? colore = null)
    {
        var l = new Label { Text = testo, AutoSize = true, Left = x, Top = y, BackColor = Color.Transparent };
        if (grassetto) l.Font = new Font(l.Font, FontStyle.Bold);
        if (colore.HasValue) l.ForeColor = colore.Value;
        return l;
    }

    private void Scrivi(string riga) => _log.AppendText(riga + Environment.NewLine);

    private void ControllaJava()
    {
        if (Firma.JavaDisponibile)
        {
            Scrivi("Java found: the APK will be signed automatically.");
        }
        else
        {
            Scrivi("WARNING: Java is not installed.");
            Scrivi("Without Java the APK is created but NOT signed, and Android will refuse to install it.");
            Scrivi("Install the Java JDK (adoptium.net, choose JDK not JRE) and reopen this program.");
        }
    }

    private void AggiornaStato() => _crea.Enabled = File.Exists(_percorsoApk.Text);

    private void ScegliApk()
    {
        using var f = new OpenFileDialog
        {
            Title = "Choose the Monster Hunter Explore APK (Japanese)",
            Filter = "APK files (*.apk)|*.apk|All files (*.*)|*.*",
        };
        if (f.ShowDialog(this) != DialogResult.OK) return;

        _percorsoApk.Text = f.FileName;
        Scrivi($"APK selected: {Path.GetFileName(f.FileName)} " +
               $"({new FileInfo(f.FileName).Length / 1024 / 1024} MB)");
        AggiornaStato();
    }

    private void Crea()
    {
        using var salva = new SaveFileDialog
        {
            Title = "Where should I save the patched APK?",
            Filter = "APK files (*.apk)|*.apk",
            FileName = Path.GetFileNameWithoutExtension(_percorsoApk.Text) + "-patched.apk",
        };
        if (salva.ShowDialog(this) != DialogResult.OK) return;

        _crea.Enabled = false;
        Cursor = Cursors.WaitCursor;
        string? copiaTemporanea = null;
        try
        {
            Scrivi("");
            Scrivi("--- start ---");

            var ingresso = _percorsoApk.Text;

            // Senza usesCleartextTraffic Android 9+ blocca ogni richiesta HTTP in
            // chiaro: il gioco non manderebbe un solo pacchetto al server.
            Scrivi("  checking the manifest for the cleartext-traffic permission (can take a minute)...");
            Application.DoEvents();
            copiaTemporanea = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".apk");
            File.Copy(ingresso, copiaTemporanea, overwrite: true);
            var esitoManifest = ManifestFix.AssicuraCleartextTraffic(copiaTemporanea);
            Scrivi("  manifest: " + esitoManifest.Messaggio);
            if (esitoManifest.Riuscito) ingresso = copiaTemporanea;
            else Scrivi("  WARNING: couldn't check/fix the manifest — if the source APK lacks the cleartext permission, the game may never connect.");

            var esito = Patcher.Costruisci(ingresso, salva.FileName, Indirizzo.Server);
            foreach (var r in esito.Righe) Scrivi("  " + r);

            if (!esito.Riuscito)
            {
                MessageBox.Show(this, "Patching failed. See the details at the bottom of the window.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (Firma.JavaDisponibile)
            {
                var (ok, messaggio) = Firma.FirmaApk(salva.FileName);
                Scrivi("  " + messaggio);
                if (!ok)
                {
                    MessageBox.Show(this, "The APK was created but signing failed, so it won't install. "
                        + "Details at the bottom of the window.",
                        "Signing failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            else
            {
                Scrivi("  APK NOT signed: Java is not installed, the phone will refuse it.");
            }

            Scrivi("--- done ---");
            MessageBox.Show(this,
                "APK ready.\n\nCopy it to the phone and install it.\n\n"
                + "If the game was already installed with a different signature, Android will ask you to "
                + "uninstall it first: set up the transfer code BEFORE doing that, "
                + "or you'll lose access to your character.",
                "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            if (copiaTemporanea != null) { try { File.Delete(copiaTemporanea); } catch { /* temporanea, non bloccante */ } }
            Cursor = Cursors.Default;
            AggiornaStato();
        }
    }
}
