import { TestBed } from '@angular/core/testing';
import { MAX_TOASTS, TOAST_MS, Toasts } from './toasts';

describe('Toasts', () => {
  afterEach(() => vi.useRealTimers());

  it('shows the newest first, keeps a few and hides each after a while or on dismiss', () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const toasts = TestBed.inject(Toasts);
    const show = (title: string) => toasts.show({ tone: 'info', title, lines: [], link: null });
    show('a');
    show('b');
    expect(toasts.items().map((t) => t.title)).toEqual(['b', 'a']);

    toasts.dismiss(toasts.items()[0].id);
    expect(toasts.items().map((t) => t.title)).toEqual(['a']);
    vi.advanceTimersByTime(TOAST_MS);
    expect(toasts.items()).toEqual([]);

    for (let i = 0; i < MAX_TOASTS + 2; i++) {
      show(`t${i}`);
    }
    expect(toasts.items()).toHaveLength(MAX_TOASTS);
  });
});
