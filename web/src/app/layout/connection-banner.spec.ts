import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ClientEvents, StreamStatus } from '../core/events/client-events';
import { ConnectionBanner } from './connection-banner';

describe('ConnectionBanner', () => {
  async function render(status: StreamStatus, heard: Date | null) {
    const reconnect = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        {
          provide: ClientEvents,
          useValue: { status: signal(status), lastHeardAt: signal(heard), nextAttemptAt: signal(Date.now() + 8_000), reconnect },
        },
      ],
    });
    const fixture = TestBed.createComponent(ConnectionBanner);
    await fixture.whenStable();
    return { el: fixture.nativeElement as HTMLElement, reconnect };
  }

  it('says since when the data is old and retries on request', async () => {
    const { el, reconnect } = await render('offline', new Date('2026-10-16T17:42:07Z'));
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Pokazujemy dane z 19:42:07');
    expect(el.textContent).toContain('auto za 8 s');
    el.querySelector('button')!.click();
    expect(reconnect).toHaveBeenCalled();
  });

  it('stays quiet while connected and before the first connection', async () => {
    expect((await render('live', new Date())).el.textContent?.trim()).toBe('');
  });

  it('stays quiet before anything was ever heard', async () => {
    expect((await render('offline', null)).el.textContent?.trim()).toBe('');
  });
});
