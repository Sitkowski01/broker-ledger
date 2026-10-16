import { DestroyRef, inject, Injectable, signal } from '@angular/core';

export interface Toast {
  readonly id: number;
  readonly tone: 'ok' | 'info' | 'bad';
  readonly title: string;
  readonly lines: readonly string[];
  /** Dokad prowadzi link w powiadomieniu (np. /zlecenia) i jak sie nazywa. */
  readonly link: { readonly path: string; readonly label: string } | null;
}

/** Ile sekund widac powiadomienie, zanim samo zniknie. */
export const TOAST_MS = 6_000;
/** Wiecej naraz tylko zaslania ekran — najstarsze ustepuje. */
export const MAX_TOASTS = 3;

/** Powiadomienia w rogu ekranu. Kazde znika samo po chwili albo po kliknieciu "zamknij". */
@Injectable({ providedIn: 'root' })
export class Toasts {
  private readonly list = signal<readonly Toast[]>([]);
  private readonly timers = new Map<number, ReturnType<typeof setTimeout>>();
  private next = 1;

  readonly items = this.list.asReadonly();

  constructor() {
    inject(DestroyRef).onDestroy(() => this.timers.forEach((t) => clearTimeout(t)));
  }

  show(toast: Omit<Toast, 'id'>): void {
    const id = this.next++;
    this.list.update((list) => [{ ...toast, id }, ...list].slice(0, MAX_TOASTS));
    this.timers.set(id, setTimeout(() => this.dismiss(id), TOAST_MS));
  }

  dismiss(id: number): void {
    clearTimeout(this.timers.get(id));
    this.timers.delete(id);
    this.list.update((list) => list.filter((t) => t.id !== id));
  }
}
