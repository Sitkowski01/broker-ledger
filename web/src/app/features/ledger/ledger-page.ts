import { ChangeDetectionStrategy, Component } from '@angular/core';
import { PageHeader } from '../../shared/page-header';

@Component({
  selector: 'bl-ledger-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader],
  template: `<bl-page-header kicker="Dokumenty Wn / Ma" title="Księga" />`,
})
export class LedgerPage {}
