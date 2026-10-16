/** Jedno zdarzenie Server-Sent Events: nazwa (linia "event:") i tresc (linie "data:"). */
export interface SseMessage {
  readonly event: string;
  readonly data: string;
}

/**
 * Wyciaga pelne zdarzenia z dotychczas odebranego tekstu. Zdarzenie konczy pusta linia; niedokonczona
 * koncowka wraca jako <code>rest</code> i czeka na kolejny kawalek strumienia. Komentarze (":") i pola
 * spoza "event"/"data" pomijamy, jak przegladarkowy EventSource.
 */
export function parseSse(text: string): { messages: SseMessage[]; rest: string } {
  const normalized = text.replace(/\r\n?/g, '\n');
  const blocks = normalized.split('\n\n');
  const rest = blocks.pop() ?? '';
  const messages: SseMessage[] = [];
  for (const block of blocks) {
    let event = 'message';
    const data: string[] = [];
    for (const line of block.split('\n')) {
      const colon = line.indexOf(':');
      if (colon === 0) {
        continue;
      }
      const field = colon < 0 ? line : line.slice(0, colon);
      const value = colon < 0 ? '' : line.slice(colon + 1).replace(/^ /, '');
      if (field === 'event') {
        event = value;
      } else if (field === 'data') {
        data.push(value);
      }
    }
    if (data.length || event !== 'message') {
      messages.push({ event, data: data.join('\n') });
    }
  }
  return { messages, rest };
}
