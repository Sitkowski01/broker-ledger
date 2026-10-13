// Testy architektury frontu: kto moze importowac kogo.
// Ekrany (features) sa niezalezne, wiec kazdy da sie zmienic albo usunac bez ruszania reszty.
/** @type {import('dependency-cruiser').IConfiguration} */
module.exports = {
  forbidden: [
    {
      name: 'no-circular',
      severity: 'error',
      comment: 'Cykl importow oznacza, ze dwa moduly trzeba rozumiec naraz.',
      from: {},
      to: { circular: true },
    },
    {
      name: 'features-are-independent',
      severity: 'error',
      comment: 'Ekran nie siega do innego ekranu — wspolne rzeczy ida do core albo shared.',
      from: { path: '^src/app/features/([^/]+)/' },
      to: { path: '^src/app/features/', pathNot: '^src/app/features/$1/' },
    },
    {
      name: 'core-knows-no-ui',
      severity: 'error',
      comment: 'core to dane i komunikacja z API — bez widokow.',
      from: { path: '^src/app/core/' },
      to: { path: '^src/app/(features|layout|shared)/' },
    },
    {
      name: 'shared-knows-no-screens',
      severity: 'error',
      comment: 'Wspolne komponenty nie wiedza, na jakim ekranie stoja.',
      from: { path: '^src/app/shared/' },
      to: { path: '^src/app/(features|layout)/' },
    },
    {
      name: 'layout-knows-no-screens',
      severity: 'error',
      comment: 'Powloka laduje ekrany przez router, nie przez import.',
      from: { path: '^src/app/layout/' },
      to: { path: '^src/app/features/' },
    },
  ],
  options: {
    doNotFollow: { path: 'node_modules' },
    exclude: { path: '\\.spec\\.ts$' },
    tsConfig: { fileName: 'tsconfig.app.json' },
    tsPreCompilationDeps: true,
  },
};
