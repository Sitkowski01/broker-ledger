import { ChangeDetectionStrategy, Component } from '@angular/core';
import { PageHeader } from '../../shared/page-header';

@Component({
  selector: 'bl-dashboard-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader],
  template: `<bl-page-header kicker="Rachunek maklerski" title="Pulpit" />`,
})
export class DashboardPage {}
