/**
 * PostHog'a giden URL'lerden sır taşıyan parçaları temizler.
 *
 * `/join/<token>` ve `/share/<token>` bağlantıları ile `?roomId=` yetki taşır (capability
 * URL); bunlar analitik olay özelliklerine girerse üçüncü tarafa sızmış olur.
 */
const TOKEN_ROUTES = ['join', 'share'];

export function scrubPath(path: string): string {
  const segments = path.split('/');
  for (let i = 0; i < segments.length - 1; i++) {
    if (TOKEN_ROUTES.includes(segments[i]) && segments[i + 1]) {
      segments[i + 1] = ':token';
    }
  }
  return segments.join('/');
}

/** Sorgu ve hash'i düşürür, token segmentlerini maskeler. URL değilse aynen döner. */
export function scrubUrl(value: unknown): unknown {
  if (typeof value !== 'string' || value === '') return value;
  try {
    const url = new URL(value, 'http://placeholder.invalid');
    const isRelative = !/^[a-z][a-z0-9+.-]*:/i.test(value);
    const path = scrubPath(url.pathname);
    return isRelative ? path : `${url.origin}${path}`;
  } catch {
    return value;
  }
}

const URL_PROPERTIES = [
  '$current_url',
  '$pathname',
  '$referrer',
  '$initial_referrer',
  '$initial_current_url',
  '$initial_pathname',
  '$session_entry_url',
  '$session_entry_pathname',
  '$session_entry_referrer',
];

/** PostHog `before_send` kancası: olay (ve `$set`/`$set_once`) içindeki URL alanlarını temizler. */
export function scrubEvent<T extends { properties?: Record<string, unknown> } | null>(event: T): T {
  if (!event || !event.properties) return event;
  const clean = (bag: Record<string, unknown> | undefined) => {
    if (!bag) return;
    for (const key of URL_PROPERTIES) {
      if (key in bag) bag[key] = scrubUrl(bag[key]);
    }
  };
  const props = event.properties as Record<string, unknown>;
  clean(props);
  clean(props.$set as Record<string, unknown> | undefined);
  clean(props.$set_once as Record<string, unknown> | undefined);
  return event;
}
