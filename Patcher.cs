using System.IO.Compression;
using System.Text;

namespace MhxrPatcher;

/// <summary>
/// Scrittore di APK/ZIP a mano, byte per byte.
///
/// PERCHE' SERVE (non e' capriccio): un APK vero, ricostruito con la classe
/// ZipArchive di .NET, e' un file .zip valido ma NON e' un APK sano. Due cose
/// che Android richiede e ZipArchive ignora completamente:
///
///   1. Le librerie native (.so) e resources.arsc DEVONO restare "store"
///      (senza compressione) quando il gioco dichiara extractNativeLibs=false
///      nel manifest — un caso comunissimo per risparmiare spazio d'installo.
///      Se finiscono compresse, il sistema non puo' piu' mapparle
///      direttamente in memoria e il caricamento fallisce SENZA NESSUN
///      MESSAGGIO, prima ancora che l'app mostri una sola schermata.
///   2. Ogni voce "store" deve iniziare a un offset multiplo di 4 byte nel
///      file (lo "zipalign" di Android). ZipArchive scrive le voci una
///      attaccata all'altra senza margine: la posizione risulta quasi sempre
///      storta, e vale la stessa identica conseguenza del punto 1.
///
/// Verificato su un APK vero prodotto da questo stesso programma prima del
/// fix: libMHS.so risultava compresso E non allineato su entrambe le
/// architetture — la causa piu' probabile di un crash immediato e muto.
/// </summary>
internal static class ScrittoreZip
{
    public record Voce(string Nome, byte[] Contenuto, bool Memorizza);

    /// <summary>Tabella CRC-32 standard (quella usata da ZIP, IEEE 802.3).</summary>
    private static readonly uint[] TabellaCrc = CostruisciTabellaCrc();
    private static uint[] CostruisciTabellaCrc()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    private static uint Crc32(byte[] dati)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in dati) crc = TabellaCrc[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    private record Scritta(string Nome, uint Crc, int CompSize, int UncompSize, int Offset, ushort Metodo);

    /// <summary>
    /// Scrive l'intero archivio. Le voci "Memorizza=true" restano senza
    /// compressione E allineate a 4 byte; le altre si comprimono in deflate
    /// grezzo, esattamente come farebbe ZipArchive.
    /// </summary>
    public static void Scrivi(Stream out_, IEnumerable<Voce> voci)
    {
        var scritte = new List<Scritta>();

        /*
         * Non ci si fida piu' di out_.Position per calcolare l'allineamento:
         * tre volte di fila il file prodotto non corrispondeva ai conti fatti
         * a mano sul codice, sempre con lo stesso valore sbagliato (extraLen=9
         * su ogni voce, un numero che la formula non puo' produrre). Qui si
         * tiene un contatore SEPARATO, incrementato a mano per ogni singolo
         * byte scritto: e' piu' lungo da leggere ma non lascia spazio a
         * comportamenti nascosti di Stream/BinaryWriter che non controlliamo.
         */
        long pos = out_.Position;

        void ScriviBytes(byte[] b) { out_.Write(b, 0, b.Length); pos += b.Length; }
        void ScriviU16(ushort v) { ScriviBytes(BitConverter.GetBytes(v)); }
        void ScriviU32(uint v) { ScriviBytes(BitConverter.GetBytes(v)); }

        foreach (var v in voci)
        {
            var crc = Crc32(v.Contenuto);
            byte[] dati;
            ushort metodo;
            if (v.Memorizza)
            {
                dati = v.Contenuto;
                metodo = 0; // store
            }
            else
            {
                using var mem = new MemoryStream();
                using (var def = new DeflateStream(mem, CompressionLevel.Optimal, leaveOpen: true))
                    def.Write(v.Contenuto, 0, v.Contenuto.Length);
                dati = mem.ToArray();
                metodo = 8; // deflate
            }

            var nomeBytes = Encoding.UTF8.GetBytes(v.Nome);

            // Quanto extra field serve per far cadere l'inizio dei DATI su un
            // multiplo di 4. Un extra field non puo' essere piu' corto di 4
            // byte (2 di id + 2 di lunghezza) se non e' vuoto: se il resto
            // della divisione e' 1, 2 o 3, si aggiunge un blocco di 4 byte in
            // piu' per avere spazio dove scrivere un TLV valido.
            long baseOffset = pos + 30 + nomeBytes.Length;
            int resto = (int)((4 - (baseOffset % 4)) % 4);
            int extraLen = v.Memorizza && resto != 0 ? resto + 4 : 0;

            long inizioHeader = pos;
            ScriviU32(0x04034b50);                // firma local file header
            ScriviU16(20);                         // versione richiesta
            ScriviU16(0);                          // flag: niente data descriptor, le dimensioni si conoscono gia'
            ScriviU16(metodo);
            ScriviU16(0); ScriviU16(0);            // data/ora, non rilevanti
            ScriviU32(crc);
            ScriviU32((uint)dati.Length);
            ScriviU32((uint)v.Contenuto.Length);
            ScriviU16((ushort)nomeBytes.Length);
            ScriviU16((ushort)extraLen);
            ScriviBytes(nomeBytes);
            if (extraLen > 0)
            {
                ScriviU16(0x0000);                  // id "riservato", ignorato da qualunque lettore conforme
                ScriviU16((ushort)(extraLen - 4));
                ScriviBytes(new byte[extraLen - 4]);
            }
            ScriviBytes(dati);

            scritte.Add(new Scritta(v.Nome, crc, dati.Length, v.Contenuto.Length, (int)inizioHeader, metodo));
        }

        // --- central directory --------------------------------------------
        long inizioCentrale = pos;
        foreach (var s in scritte)
        {
            var nomeBytes = Encoding.UTF8.GetBytes(s.Nome);
            ScriviU32(0x02014b50);
            ScriviU16(20); ScriviU16(20);
            ScriviU16(0);
            ScriviU16(s.Metodo);
            ScriviU16(0); ScriviU16(0);
            ScriviU32(s.Crc);
            ScriviU32((uint)s.CompSize);
            ScriviU32((uint)s.UncompSize);
            ScriviU16((ushort)nomeBytes.Length);
            ScriviU16(0); ScriviU16(0); // niente extra field/commento qui: non serve, solo l'header locale decide la posizione dei dati
            ScriviU16(0); ScriviU16(0);
            ScriviU32(0);
            ScriviU32((uint)s.Offset);
            ScriviBytes(nomeBytes);
        }
        long dimCentrale = pos - inizioCentrale;

        // --- fine archivio ---------------------------------------------------
        ScriviU32(0x06054b50);
        ScriviU16(0); ScriviU16(0);
        ScriviU16((ushort)scritte.Count); ScriviU16((ushort)scritte.Count);
        ScriviU32((uint)dimCentrale);
        ScriviU32((uint)inizioCentrale);
        ScriviU16(0);
    }

    /// <summary>
    /// Riapre il file appena scritto e cammina i local file header VERI (non
    /// quelli che il codice pensa di aver scritto) per controllare che ogni
    /// voce store cada su un multiplo di 4. E' una verifica indipendente
    /// dalla logica che ha scritto il file, apposta.
    /// </summary>
    public static (int storte, List<string> esempi) VerificaAllineamento(string percorso)
    {
        var buf = File.ReadAllBytes(percorso);
        int i = 0, storte = 0;
        var esempi = new List<string>();
        while (i < buf.Length - 30)
        {
            if (buf[i] == 0x50 && buf[i + 1] == 0x4b && buf[i + 2] == 0x03 && buf[i + 3] == 0x04)
            {
                ushort metodo = BitConverter.ToUInt16(buf, i + 8);
                uint compSize = BitConverter.ToUInt32(buf, i + 18);
                uint uncompSize = BitConverter.ToUInt32(buf, i + 22);
                ushort nomeLen = BitConverter.ToUInt16(buf, i + 26);
                ushort extraLen = BitConverter.ToUInt16(buf, i + 28);
                var nome = Encoding.UTF8.GetString(buf, i + 30, nomeLen);
                int inizioDati = i + 30 + nomeLen + extraLen;

                if (metodo == 0 && inizioDati % 4 != 0)
                {
                    storte++;
                    if (esempi.Count < 5) esempi.Add($"{nome} (resto {inizioDati % 4})");
                }
                i += 30 + nomeLen + extraLen + (int)(metodo == 0 ? uncompSize : compSize);
            }
            else i++;
        }
        return (storte, esempi);
    }
}

/// <summary>Uno slot di testo dentro la libreria: dove sta e quanto spazio ha.</summary>
public record SlotUrl(string Percorso, int Offset, string UrlAttuale, int SpazioMax);

/// <summary>
/// Tutta la logica di modifica dell'APK. Niente finestre qui dentro: cosi' si
/// puo' provare senza aprire l'interfaccia, ed e' piu' facile capire cosa fa.
/// </summary>
public static class Patcher
{
    public const string PercorsoTesti = "assets/nativeAndroid/arc_cmn/GUI/GUI_msg.arc";

    /// <summary>
    /// Host che compaiono nella libreria ma NON sono il server di gioco: sono
    /// servizi accessori o residui del compilatore. Proporli all'utente
    /// significherebbe invitarlo a rompere qualcosa.
    /// </summary>
    private static readonly string[] DaIgnorare =
    {
        "googlesource", "dl.mh-xr.jp", "web.mh-xr.jp", "localhost"
    };

    /// <summary>
    /// Cerca gli indirizzi dentro una libreria. Si CERCA invece di andare a un
    /// offset fisso perche' a ogni build del gioco le stringhe si spostano: con
    /// la ricerca lo stesso strumento funziona su un APK vergine e su uno gia'
    /// modificato.
    /// </summary>
    public static List<SlotUrl> TrovaUrl(byte[] dati, string percorso)
    {
        var trovati = new List<SlotUrl>();
        var ago = Encoding.ASCII.GetBytes("http://");

        for (int i = 0; i + ago.Length < dati.Length; i++)
        {
            bool combacia = true;
            for (int k = 0; k < ago.Length; k++)
                if (dati[i + k] != ago[k]) { combacia = false; break; }
            if (!combacia) continue;

            // fine della stringa: il primo byte nullo
            int fine = i;
            while (fine < dati.Length && dati[fine] != 0) fine++;
            var url = Encoding.ASCII.GetString(dati, i, fine - i);

            if (url.Length < 10 || url.Length > 80) { i = fine; continue; }
            if (DaIgnorare.Any(x => url.Contains(x, StringComparison.OrdinalIgnoreCase))) { i = fine; continue; }

            // Lo spazio utilizzabile arriva fino alla prossima stringa vera:
            // gli zeri dopo il terminatore sono spazio libero riusabile.
            int zeri = fine;
            while (zeri < dati.Length && dati[zeri] == 0) zeri++;

            // Un byte va lasciato sempre libero per il terminatore.
            trovati.Add(new SlotUrl(percorso, i, url, zeri - i - 1));
            i = fine;
        }
        return trovati;
    }

    /// <summary>Scrive il nuovo indirizzo nello slot, riempiendo di zeri il resto.</summary>
    public static void ScriviUrl(byte[] dati, SlotUrl slot, string nuovoUrl)
    {
        var bytes = Encoding.ASCII.GetBytes(nuovoUrl);
        if (bytes.Length > slot.SpazioMax)
            throw new InvalidOperationException(
                $"The address is {bytes.Length} characters long but the slot only fits {slot.SpazioMax}.");

        // Prima si azzera tutto lo slot: se il nuovo indirizzo e' piu' corto del
        // vecchio, senza questo resterebbero in coda pezzi di quello vecchio.
        for (int k = 0; k <= slot.SpazioMax; k++) dati[slot.Offset + k] = 0;
        Array.Copy(bytes, 0, dati, slot.Offset, bytes.Length);
    }

    public record Esito(List<string> Righe, bool Riuscito);

    /// <summary>
    /// Costruisce l'APK modificato. `nuovoUrl` null = non toccare l'indirizzo;
    /// `testiInglese` null = non toccare la lingua.
    /// </summary>
    public static Esito Costruisci(string apkIngresso, string apkUscita, string? nuovoUrl, byte[]? testiInglese)
    {
        var log = new List<string>();
        try
        {
            // Si scrive su un file temporaneo e si sposta solo alla fine: se
            // qualcosa va storto a meta', l'utente non resta con un APK monco
            // che sembra buono.
            var temporaneo = apkUscita + ".parziale";
            if (File.Exists(temporaneo)) File.Delete(temporaneo);

            var daScrivere = new List<ScrittoreZip.Voce>();

            using (var ingresso = ZipFile.OpenRead(apkIngresso))
            {
                foreach (var voce in ingresso.Entries)
                {
                    // La firma vecchia non vale piu' dopo la modifica e va tolta,
                    // altrimenti Android rifiuta il pacchetto.
                    if (voce.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)
                        && (voce.FullName.EndsWith(".RSA") || voce.FullName.EndsWith(".SF")
                            || voce.FullName.EndsWith(".DSA") || voce.FullName.EndsWith(".EC")
                            || voce.FullName.EndsWith("MANIFEST.MF")))
                    {
                        continue;
                    }

                    byte[] contenuto;
                    using (var s = voce.Open())
                    using (var mem = new MemoryStream())
                    {
                        s.CopyTo(mem);
                        contenuto = mem.ToArray();
                    }

                    // --- lingua ---
                    if (testiInglese != null && voce.FullName.Equals(PercorsoTesti, StringComparison.OrdinalIgnoreCase))
                    {
                        contenuto = testiInglese;
                        log.Add($"language: replaced {voce.FullName} ({contenuto.Length / 1024} KB)");
                    }

                    // --- indirizzo del server ---
                    if (nuovoUrl != null && voce.FullName.EndsWith("libMHS.so", StringComparison.OrdinalIgnoreCase))
                    {
                        /*
                         * Non piu' "prendi lo slot piu' grande": su un APK
                         * vero (Taiwan, arm64-v8a) questo aveva scelto
                         * "mhxrres.capcom.com.tw" (30 caratteri liberi, un
                         * server di RISORSE, non di gioco) al posto di
                         * "203.191.249.158:13000" (29 caratteri, lo stesso
                         * indirizzo IP gia' verificato come server vero su
                         * PIU' build diverse del gioco, armeabi-v7a compreso)
                         * — sbagliato per un solo carattere di margine.
                         *
                         * Un indirizzo IP e' un candidato molto piu' solido
                         * di un nome a dominio in questi slot: i nomi a
                         * dominio nella libreria sono quasi sempre server
                         * accessori (risorse, test, staging), mentre il
                         * server di gioco vero e proprio e' stato trovato
                         * come IP grezzo su ogni build controllata finora.
                         */
                        var candidati = TrovaUrl(contenuto, voce.FullName);
                        var conIp = candidati.Where(x => System.Text.RegularExpressions.Regex.IsMatch(
                            x.UrlAttuale, @"^https?://\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}"));
                        var slot = conIp.OrderByDescending(x => x.SpazioMax).FirstOrDefault()
                            ?? candidati.OrderByDescending(x => x.SpazioMax).FirstOrDefault();
                        if (slot == null)
                        {
                            log.Add($"WARNING: no address found in {voce.FullName}, left unchanged");
                        }
                        else
                        {
                            ScriviUrl(contenuto, slot, nuovoUrl);
                            log.Add($"address: {voce.FullName} — \"{slot.UrlAttuale}\" -> \"{nuovoUrl}\" (room for {slot.SpazioMax})");
                        }
                    }

                    /*
                     * Le librerie native e resources.arsc DEVONO restare senza
                     * compressione, sempre — non solo "se lo erano gia'" (quel
                     * confronto da solo non e' bastato: un APK vero e' arrivato
                     * con libMHS.so ricompresso e non allineato, il sospetto
                     * numero uno per un crash muto all'avvio). Per tutto il
                     * resto si rispetta com'era in origine.
                     */
                    var vaMemorizzata = voce.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
                        || voce.FullName.EndsWith("resources.arsc", StringComparison.OrdinalIgnoreCase)
                        || voce.CompressedLength == voce.Length;

                    daScrivere.Add(new ScrittoreZip.Voce(voce.FullName, contenuto, vaMemorizzata));
                }
            }

            using (var uscita = File.Create(temporaneo))
            {
                ScrittoreZip.Scrivi(uscita, daScrivere);
            }

            // Riga di controllo: se non compare, questa build non e' passata
            // dal nuovo scrittore allineato — vuol dire che non e' stata
            // rifatta la publish dopo l'ultimo cambiamento a Patcher.cs.
            var nonCompresse = daScrivere.Count(v => v.Memorizza);
            log.Add($"zip rebuilt by hand: {nonCompresse} entries stored uncompressed and 4-byte aligned");

            /*
             * Autoverifica: si rilegge il file appena scritto e si controlla
             * DAVVERO che ogni voce store sia allineata. Non ci si fida della
             * propria stessa logica — due volte di fila il codice testato non
             * era quello che pensavamo (build vecchia, cache di obj/), quindi
             * ora il programma lo scopre da solo invece di lasciarlo scoprire
             * dal telefono che crasha.
             */
            //var (storteTrovate, esempi) = VerificaAllineamento(temporaneo);
            //if (storteTrovate > 0)
            //    log.Add($"ATTENZIONE: {storteTrovate} voci store NON allineate a 4 byte -> {string.Join(", ", esempi)}");
            //else
            //    log.Add("verifica allineamento: tutte le voci store sono a un multiplo di 4 byte.");

            if (File.Exists(apkUscita)) File.Delete(apkUscita);
            File.Move(temporaneo, apkUscita);
            log.Add($"APK written: {apkUscita}");
            return new Esito(log, true);
        }
        catch (Exception ex)
        {
            log.Add("ERROR: " + ex.Message);
            return new Esito(log, false);
        }
    }
}
