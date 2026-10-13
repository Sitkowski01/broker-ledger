/**
 * Adres powrotu z parametru URL, ale tylko wewnatrz aplikacji. "//obca.pl" albo "https://..." to klasyczna
 * proba otwartego przekierowania: link do naszego logowania, ktory po zalogowaniu wysyla klienta gdzie indziej.
 */
export function safeReturnUrl(target: string | null | undefined): string {
  return target && target.startsWith('/') && !target.startsWith('//') && !target.startsWith('/\\') ? target : '/';
}
