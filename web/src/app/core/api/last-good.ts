import { HttpResourceRef } from '@angular/common/http';
import { linkedSignal, Signal } from '@angular/core';

/**
 * Ostatnia udana odpowiedz zasobu. Nieudane odswiezenie nie kasuje tego, co klient juz widzi —
 * inaczej po przelewie znikalby panel razem z potwierdzeniem i kluczem idempotencji.
 * (value() zasobu w stanie bledu rzuca wyjatkiem, wiec czytamy go tylko przez hasValue().)
 */
export function lastGood<T>(resource: HttpResourceRef<T | undefined>): Signal<T | undefined> {
  return linkedSignal<T | undefined, T | undefined>({
    source: () => (resource.hasValue() ? resource.value() : undefined),
    computation: (value, previous) => value ?? previous?.value,
  });
}

/** Komunikat bledu wczytywania zasobu. Resource opakowuje odpowiedz HTTP w ResourceWrappedError z polem cause. */
export function loadErrorOf(error: unknown): unknown {
  return (error as { cause?: unknown } | undefined)?.cause ?? error;
}
