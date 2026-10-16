import { parseSse } from './sse';

describe('parseSse', () => {
  it('splits complete events and keeps the unfinished tail for the next chunk', () => {
    expect(parseSse('event: ready\ndata: {}\n\nevent: order\ndata: {"orderId":"a"}\n\nevent: led')).toEqual({
      messages: [
        { event: 'ready', data: '{}' },
        { event: 'order', data: '{"orderId":"a"}' },
      ],
      rest: 'event: led',
    });
  });

  it('handles CRLF, comments, multi-line data and events without data', () => {
    expect(parseSse(': keep-alive\r\n\r\nevent: ping\r\n\r\ndata: a\ndata: b\n\n').messages).toEqual([
      { event: 'ping', data: '' },
      { event: 'message', data: 'a\nb' },
    ]);
    expect(parseSse('data:no-space\nid: 7\nretry: 10\n\n').messages).toEqual([{ event: 'message', data: 'no-space' }]);
    expect(parseSse('').messages).toEqual([]);
  });
});
