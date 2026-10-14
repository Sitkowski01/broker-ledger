import { companyName } from './companies';

describe('companyName', () => {
  it('names the companies listed on Parkiet', () => {
    expect(['CDR', 'PKO', 'PKN', 'PZU', 'KGH', 'ALE'].map(companyName)).toEqual([
      'CD Projekt',
      'PKO BP',
      'Orlen',
      'PZU',
      'KGHM',
      'Allegro',
    ]);
  });

  it('shows an unknown symbol as it is', () => {
    expect(companyName('XYZ')).toBe('XYZ');
  });
});
