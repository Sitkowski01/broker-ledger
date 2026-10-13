import { ChangeDetectionStrategy, Component } from '@angular/core';
import { PageHeader } from '../../shared/page-header';

@Component({
  selector: 'bl-orders-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader],
  template: `<bl-page-header kicker="Handel · sesja ciągła" title="Zlecenia" />`,
})
export class OrdersPage {}
