import { computed, DestroyRef, inject, Injectable, signal } from '@angular/core';

export interface SessionData {
  readonly token: string;
  readonly email: string;
  /** Moment wygasniecia tokenu (ISO), ten sam co w odpowiedzi API. */
  readonly expiresAt: string;
}

const KEY = 'broker-ledger.session';

/**
 * Zalogowany klient. Token trzymamy w sessionStorage: przezywa odswiezenie strony,
 * ale znika z zamknieciem karty. Gdy minie expiresAt, sesja wygasa sama — bez czekania na 401.
 */
@Injectable({ providedIn: 'root' })
export class Session {
  private readonly data = signal<SessionData | null>(read());
  private readonly expiredFlag = signal(false);
  private timer: ReturnType<typeof setTimeout> | undefined;

  readonly current = this.data.asReadonly();
  readonly isActive = computed(() => this.data() !== null);
  /** true, gdy sesja skonczyla sie sama (token wygasl), a nie przez wylogowanie. */
  readonly expired = this.expiredFlag.asReadonly();

  constructor() {
    this.schedule(this.data());
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  start(data: SessionData): void {
    write(data);
    this.data.set(data);
    this.expiredFlag.set(false);
    this.schedule(data);
  }

  end(): void {
    this.clear(false);
  }

  /** Serwer odrzucil token (401) albo minal jego czas. */
  expire(): void {
    this.clear(true);
  }

  private clear(expired: boolean): void {
    clearTimeout(this.timer);
    write(null);
    this.data.set(null);
    this.expiredFlag.set(expired);
  }

  private schedule(data: SessionData | null): void {
    clearTimeout(this.timer);
    if (!data) {
      return;
    }
    const left = Date.parse(data.expiresAt) - Date.now();
    if (left <= 0) {
      this.clear(true);
      return;
    }
    this.timer = setTimeout(() => this.expire(), left);
  }
}

function read(): SessionData | null {
  try {
    const raw = sessionStorage.getItem(KEY);
    return raw ? (JSON.parse(raw) as SessionData) : null;
  } catch {
    // Prywatne okno albo zablokowany storage: po prostu brak zapamietanej sesji.
    return null;
  }
}

function write(data: SessionData | null): void {
  try {
    if (data) {
      sessionStorage.setItem(KEY, JSON.stringify(data));
    } else {
      sessionStorage.removeItem(KEY);
    }
  } catch {
    // Bez storage sesja i tak dziala do odswiezenia strony.
  }
}
