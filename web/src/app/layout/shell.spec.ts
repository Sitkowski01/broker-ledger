import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Shell } from './shell';

@Component({ template: '' })
class Empty {}

describe('Shell', () => {
  it('marks the current screen in the navigation', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: '', component: Shell, children: [{ path: 'zlecenia', component: Empty }, { path: '', component: Empty }] },
        ]),
      ],
    });
    const harness = await RouterTestingHarness.create('/zlecenia');

    const links = [...(harness.routeNativeElement as HTMLElement).querySelectorAll('nav a')];
    expect(links.map((a) => a.textContent?.trim())).toEqual(['Pulpit', 'Zlecenia', 'Księga']);
    expect(links.filter((a) => a.getAttribute('aria-current') === 'page').map((a) => a.textContent?.trim())).toEqual(['Zlecenia']);
  });
});
