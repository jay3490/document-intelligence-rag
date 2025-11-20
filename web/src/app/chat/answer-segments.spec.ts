import { splitAnswer } from './answer-segments';

describe('splitAnswer', () => {
  it('separates text from citation markers', () => {
    expect(splitAnswer('Leave is 25 days [1]. Expenses take 30 days [2][3].')).toEqual([
      { kind: 'text', text: 'Leave is 25 days ' },
      { kind: 'citation', numbers: [1] },
      { kind: 'text', text: '. Expenses take 30 days ' },
      { kind: 'citation', numbers: [2] },
      { kind: 'citation', numbers: [3] },
      { kind: 'text', text: '.' },
    ]);
  });

  it('expands grouped markers', () => {
    expect(splitAnswer('Both [1, 4]')).toEqual([
      { kind: 'text', text: 'Both ' },
      { kind: 'citation', numbers: [1, 4] },
    ]);
  });

  it('leaves text without markers untouched', () => {
    expect(splitAnswer('No sources [here].')).toEqual([{ kind: 'text', text: 'No sources [here].' }]);
  });
});
