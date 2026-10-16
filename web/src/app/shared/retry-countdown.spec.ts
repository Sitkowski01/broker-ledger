import { TestBed } from '@angular/core/testing';
import { RetryCountdown } from './retry-countdown';

describe('RetryCountdown', () => {
  afterEach(() => vi.useRealTimers());

  it('counts down the Retry-After seconds and then says it is done', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    const fixture = TestBed.createComponent(RetryCountdown);
    fixture.componentRef.setInput('seconds', 3);
    const done = vi.fn();
    fixture.componentInstance.done.subscribe(done);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Kolejna próba za 3 s');

    vi.advanceTimersByTime(2_000);
    await fixture.whenStable();
    expect(el.textContent).toContain('za 1 s');
    expect(done).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1_000);
    expect(done).toHaveBeenCalledTimes(1);
  });
});
