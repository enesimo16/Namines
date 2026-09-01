'use client';

import { useEffect, useState } from 'react';
import Desk from './Desk';

/**
 * Giris kapisi.
 *
 * Anahtar `sessionStorage`'da tutuluyor, `localStorage`'da DEGIL: bu bir
 * veritabani erisim anahtari ve sekme kapaninca kalmamali. Paylasilan bir
 * makinede kalici saklamak, kapatilmis sanilan bir oturumu acik birakirdi.
 */
export default function Page() {
  const [key, setKey] = useState<string | null>(null);
  const [draft, setDraft] = useState('');
  const [ready, setReady] = useState(false);

  useEffect(() => {
    try { setKey(sessionStorage.getItem('namines-desk-key')); } catch { /* gizli mod */ }
    setReady(true);
  }, []);

  if (!ready) return null;

  if (!key) {
    return (
      <div className="gate">
        <form
          className="gate-card"
          onSubmit={e => {
            e.preventDefault();
            const v = draft.trim();
            if (!v) return;
            try { sessionStorage.setItem('namines-desk-key', v); } catch { /* gizli mod */ }
            setKey(v);
          }}
        >
          <h1>Namines Desk <span className="brand-badge">beta</span></h1>
          <p>
            Projenizin Gateway API anahtarini girin. Veritabani baglantiniz
            sunucuda sifreli duruyor &mdash; parolaniz bu sayfaya hicbir zaman gelmez.
          </p>
          <div className="field">
            <label htmlFor="key">API anahtari</label>
            <input id="key" type="text" value={draft} placeholder="nmn_..."
                   onChange={e => setDraft(e.target.value)} autoFocus />
          </div>
          <button type="submit" className="btn btn-primary" style={{ width: '100%' }}>Ac</button>
        </form>
      </div>
    );
  }

  return (
    <Desk
      apiKey={key}
      onSignOut={() => {
        try { sessionStorage.removeItem('namines-desk-key'); } catch { /* gizli mod */ }
        setKey(null);
      }}
    />
  );
}
