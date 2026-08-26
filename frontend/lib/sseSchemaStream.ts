import { API_BASE_URL } from './apiConfig';
import { DatabaseSchema } from '../types/schema';

/**
 * Şema üretim hattının tek bir adımı — backend AgentStep ile birebir.
 * bkz. second-phase/04-LOADING-EKRANI.md
 */
export interface AgentStepEvent {
  kind: 'draft' | 'inspect' | 'finding' | 'repair' | 'clean';
  message: string;
}

export interface AgentResultEvent {
  schema: DatabaseSchema;
  agent: {
    archetype: string;
    rounds: number;
    clean: boolean;
    portableEverywhere: boolean;
    findings: string[];
    portability: string[];
  };
}

export interface StreamError {
  message: string;
  retryAfterSeconds?: string;
  /** HTTP durumu SSE bağlantısı kurulmadan önce başarısız olduysa dolu. */
  httpStatus?: number;
}

interface StreamCallbacks {
  onStep: (step: AgentStepEvent) => void;
  onResult: (result: AgentResultEvent) => void;
  onError: (error: StreamError) => void;
}

/**
 * `POST /api/schema/generate`'i akış hâlinde tüketir.
 *
 * <b>Neden `EventSource` değil `fetch`:</b> `EventSource` yalnızca GET
 * destekler; bu uç dosya yükleme için `multipart/form-data` POST alıyor.
 * `fetch` + `ReadableStream` ile aynı sonuç, POST gövdesiyle birlikte elde
 * ediliyor.
 *
 * <b>Geriye dönük uyum korunuyor:</b> sunucu yalnızca `Accept:
 * text/event-stream` başlığı görürse akışa geçiyor; bu fonksiyonu
 * kullanmayan çağıranlar (RegionalPromptPanel gibi) hiçbir şey fark etmiyor.
 */
export async function streamSchemaGeneration(
  formData: FormData,
  callbacks: StreamCallbacks,
  signal?: AbortSignal
): Promise<void> {
  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}/schema/generate`, {
      method: 'POST',
      headers: { Accept: 'text/event-stream' },
      body: formData,
      credentials: 'include',
      signal,
    });
  } catch (err) {
    if ((err as Error).name === 'AbortError') return;
    callbacks.onError({ message: 'Network error while starting generation.' });
    return;
  }

  // Sunucu akışa hiç girmeden reddetmiş olabilir (401/429/400) — bu durumda
  // gövde SSE değil, düz JSON. İkisini ayırt etmek şart: SSE parser'ına düz
  // JSON vermek sessizce hiçbir olay üretmezdi ve kullanıcı sonsuza dek
  // "üretiliyor" ekranında kalırdı.
  const contentType = response.headers.get('content-type') || '';
  if (!response.ok || !contentType.includes('text/event-stream')) {
    let message = `Request failed (${response.status}).`;
    let retryAfterSeconds: string | undefined;
    try {
      const body = await response.json();
      message = body?.message || message;
      retryAfterSeconds = body?.retryAfterSeconds;
    } catch {
      // Gövde JSON değilse varsayılan mesaj kalır.
    }
    callbacks.onError({ message, retryAfterSeconds, httpStatus: response.status });
    return;
  }

  const reader = response.body?.getReader();
  if (!reader) {
    callbacks.onError({ message: 'Streaming is not supported by this browser.' });
    return;
  }

  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });

    // SSE olayları "\n\n" ile ayrılır; son parça tamamlanmamış olabilir,
    // bir sonraki okumaya kadar buffer'da bekletiliyor.
    const parts = buffer.split('\n\n');
    buffer = parts.pop() ?? '';

    for (const part of parts) {
      const eventLine = part.split('\n').find(l => l.startsWith('event: '));
      const dataLine = part.split('\n').find(l => l.startsWith('data: '));
      if (!eventLine || !dataLine) continue;

      const eventName = eventLine.slice('event: '.length).trim();
      let data: unknown;
      try {
        data = JSON.parse(dataLine.slice('data: '.length));
      } catch {
        continue; // Bozuk bir olay geneli düşürmemeli, atlanır.
      }

      if (eventName === 'step') callbacks.onStep(data as AgentStepEvent);
      else if (eventName === 'result') callbacks.onResult(data as AgentResultEvent);
      else if (eventName === 'error') callbacks.onError(data as StreamError);
    }
  }
}
