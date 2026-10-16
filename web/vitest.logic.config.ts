import { defineConfig } from 'vitest/config';

/**
 * Testy czystej logiki frontu (rachunki, formatowanie, modele ekranow, symulator demo) bez kompilatora Angulara.
 * Uzywa ich Stryker — mutant na mutancie, wiec liczy sie kazda sekunda. Komponenty testuje `ng test`.
 */
export default defineConfig({
  test: {
    globals: true,
    environment: 'node',
    include: [
      'src/app/core/format/*.spec.ts',
      'src/app/core/events/sse.spec.ts',
      'src/app/core/account/balances.spec.ts',
      'src/app/core/orders/order-toast.spec.ts',
      'src/app/features/**/*-model.spec.ts',
      'src/app/features/ledger/today.spec.ts',
      'src/app/demo/demo-broker.spec.ts',
    ],
  },
});
