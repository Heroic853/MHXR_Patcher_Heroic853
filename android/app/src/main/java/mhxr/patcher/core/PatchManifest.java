package mhxr.patcher.core;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;

/**
 * Permesso di traffico HTTP in chiaro, senza apktool (che su Android non c'e').
 *
 * Il gioco giapponese dichiara targetSdkVersion 28: da Android 9, con target >= 28, il
 * traffico HTTP in chiaro e' bloccato se il manifest non dice usesCleartextTraffic="true"
 * (e il server privato e' HTTP). Con target 27 invece e' permesso per default.
 *
 * Aggiungere un attributo al manifest (XML binario) vuol dire ricostruirne la struttura;
 * cambiare il VALORE di targetSdkVersion da 28 a 27 invece e' una modifica di 4 byte sul
 * posto, senza toccare dimensioni ne' tabelle. Si fa questo, e solo se serve.
 *
 * Formato AXML: blocchi [tipo u16, header u16, dimensione u32]; 0x0001 = tabella stringhe,
 * 0x0102 = inizio elemento con gli attributi (20 byte l'uno: ns, nome, raw, size u16,
 * res0 u8, tipo u8, dato u32).
 */
public final class PatchManifest {
    private PatchManifest() {}

    public static final class Esito {
        public final boolean cambiato;
        public final String messaggio;
        Esito(boolean cambiato, String messaggio) { this.cambiato = cambiato; this.messaggio = messaggio; }
    }

    public static Esito sistema(byte[] manifest) {
        ByteBuffer b = ByteBuffer.wrap(manifest).order(ByteOrder.LITTLE_ENDIAN);
        if (b.getShort(0) != 0x0003) return new Esito(false, "manifest not readable (not binary XML), left unchanged");
        String[] stringhe = null;
        int posTarget = -1, valoreTarget = -1;
        boolean cleartextGiaVero = false;
        int pos = b.getShort(2) & 0xffff;
        while (pos + 8 <= manifest.length) {
            int tipo = b.getShort(pos) & 0xffff;
            int dim = b.getInt(pos + 4);
            if (dim <= 0) break;
            if (tipo == 0x0001) {
                stringhe = leggiStringhe(b, pos);
            } else if (tipo == 0x0102 && stringhe != null) {
                String elemento = stringa(stringhe, b.getInt(pos + 20));
                int inizioAttr = pos + 16 + (b.getShort(pos + 24) & 0xffff);
                int dimAttr = b.getShort(pos + 26) & 0xffff;
                int nAttr = b.getShort(pos + 28) & 0xffff;
                for (int k = 0; k < nAttr; k++) {
                    int a = inizioAttr + k * dimAttr;
                    String nome = stringa(stringhe, b.getInt(a + 4));
                    int tipoDato = manifest[a + 15] & 0xff;
                    int dato = b.getInt(a + 16);
                    if ("uses-sdk".equals(elemento) && "targetSdkVersion".equals(nome) && tipoDato == 0x10) {
                        posTarget = a + 16;
                        valoreTarget = dato;
                    }
                    if ("application".equals(elemento) && "usesCleartextTraffic".equals(nome) && tipoDato == 0x12 && dato != 0) {
                        cleartextGiaVero = true;
                    }
                }
            }
            pos += dim;
        }
        if (cleartextGiaVero) return new Esito(false, "manifest already allows cleartext traffic");
        if (posTarget < 0) return new Esito(false, "targetSdkVersion not found: cleartext allowed by default, left unchanged");
        if (valoreTarget < 28) return new Esito(false, "targetSdkVersion " + valoreTarget + ": cleartext allowed by default");
        b.putInt(posTarget, 27);
        return new Esito(true, "targetSdkVersion " + valoreTarget + " -> 27 (allows the HTTP connection to the server)");
    }

    private static String stringa(String[] s, int i) {
        return i >= 0 && i < s.length ? s[i] : null;
    }

    private static String[] leggiStringhe(ByteBuffer b, int pos) {
        int n = b.getInt(pos + 8);
        int flag = b.getInt(pos + 16);
        int inizioStringhe = pos + b.getInt(pos + 20);
        boolean utf8 = (flag & 0x100) != 0;
        String[] out = new String[n];
        for (int i = 0; i < n; i++) {
            int p = inizioStringhe + b.getInt(pos + 28 + i * 4);
            if (utf8) {
                p += ((b.get(p) & 0x80) != 0) ? 2 : 1; // lunghezza in caratteri
                int len = b.get(p) & 0xff;
                if ((len & 0x80) != 0) { len = ((len & 0x7f) << 8) | (b.get(p + 1) & 0xff); p += 2; } else p += 1;
                byte[] x = new byte[len];
                for (int k = 0; k < len; k++) x[k] = b.get(p + k);
                out[i] = new String(x, StandardCharsets.UTF_8);
            } else {
                int len = b.getShort(p) & 0xffff;
                if ((len & 0x8000) != 0) { len = ((len & 0x7fff) << 16) | (b.getShort(p + 2) & 0xffff); p += 4; } else p += 2;
                char[] c = new char[len];
                for (int k = 0; k < len; k++) c[k] = b.getChar(p + k * 2);
                out[i] = new String(c);
            }
        }
        return out;
    }
}
