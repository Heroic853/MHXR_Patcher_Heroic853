package mhxr.patcher.core;

import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

/**
 * Scrive l'indirizzo del server dentro libMHS.so. Stessa logica di Patcher.cs (PC):
 * si cercano le stringhe "http://" e "https://" e si sceglie lo slot giusto:
 *   1. "mhxr-dispatch" (APK giapponese originale: https://mhxr-dispatch.s3-ap-northeast-1.amazonaws.com/);
 *   2. uno slot che contiene gia' l'indirizzo nuovo (APK gia' patchato);
 *   3. il piu' grande, escluso 203.191.249.158 (un indirizzo che il gioco non usa).
 * Cercando solo "http://" (vecchio bug del patcher PC) su un APK originale si cambiava
 * proprio 203.191.249.158 e il gioco non si collegava.
 */
public final class PatchLibreria {
    private PatchLibreria() {}

    private static final String[] DA_IGNORARE = {"googlesource", "dl.mh-xr.jp", "web.mh-xr.jp", "localhost"};

    public static final class Slot {
        public final int offset;
        public final String url;
        public final int spazioMax;
        Slot(int offset, String url, int spazioMax) { this.offset = offset; this.url = url; this.spazioMax = spazioMax; }
    }

    public static List<Slot> trovaUrl(byte[] d) {
        List<Slot> trovati = new ArrayList<>();
        for (int i = 0; i + 12 < d.length; i++) {
            if (d[i] != 'h' || d[i + 1] != 't' || d[i + 2] != 't' || d[i + 3] != 'p') continue;
            int dopo = i + 4;
            if (d[dopo] == 's') dopo++;
            if (d[dopo] != ':' || d[dopo + 1] != '/' || d[dopo + 2] != '/') continue;
            int fine = i;
            while (fine < d.length && d[fine] != 0) fine++;
            String url = new String(d, i, fine - i, StandardCharsets.ISO_8859_1);
            boolean ignora = url.length() < 10 || url.length() > 80;
            for (String x : DA_IGNORARE) if (url.toLowerCase().contains(x)) ignora = true;
            if (!ignora) {
                int zeri = fine;
                while (zeri < d.length && d[zeri] == 0) zeri++;
                // un byte resta sempre libero per il terminatore
                trovati.add(new Slot(i, url, zeri - i - 1));
            }
            i = fine;
        }
        return trovati;
    }

    public static Slot scegli(List<Slot> candidati, String nuovoUrl) {
        for (Slot s : candidati) if (s.url.toLowerCase().contains("mhxr-dispatch")) return s;
        for (Slot s : candidati) if (s.url.equals(nuovoUrl)) return s;
        Slot migliore = null;
        for (Slot s : candidati) {
            if (s.url.contains("203.191.249.158")) continue;
            if (migliore == null || s.spazioMax > migliore.spazioMax) migliore = s;
        }
        return migliore;
    }

    /** Scrive l'indirizzo e restituisce lo spazio dello slot usato, o -1 se non c'e' uno slot adatto. */
    public static int scrivi(byte[] d, String nuovoUrl) {
        Slot slot = scegli(trovaUrl(d), nuovoUrl);
        if (slot == null) return -1;
        byte[] b = nuovoUrl.getBytes(StandardCharsets.US_ASCII);
        if (b.length > slot.spazioMax) throw new IllegalStateException("address too long for the slot (" + slot.spazioMax + ")");
        // prima si azzera tutto lo slot, poi si scrive: niente avanzi del vecchio indirizzo
        for (int k = 0; k <= slot.spazioMax; k++) d[slot.offset + k] = 0;
        System.arraycopy(b, 0, d, slot.offset, b.length);
        return slot.spazioMax;
    }
}
