declare const process: {
  env: Record<string, string | undefined>;
};

declare const fetch: (
  input: string,
  init?: {
    method?: string;
    headers?: Record<string, string>;
    body?: string;
  }
) => Promise<{
  status: number;
  json(): Promise<any>;
}>;

declare class Buffer {
  static isBuffer(value: unknown): value is Buffer;
  static from(value: string | Uint8Array): Buffer;
  static concat(list: readonly Buffer[]): Buffer;
  toString(encoding?: string): string;
}

declare module 'node:http' {
  export interface IncomingMessage extends AsyncIterable<Uint8Array> {
    method?: string;
    url?: string;
    headers: Record<string, string | string[] | undefined>;
  }

  export interface ServerResponse {
    statusCode: number;
    setHeader(name: string, value: string): void;
    end(body?: string): void;
  }

  export interface AddressInfo {
    port: number;
  }

  export interface Server {
    listen(port: number, callback?: () => void): void;
    close(callback?: () => void): void;
    address(): AddressInfo | string | null;
  }

  export function createServer(
    handler: (req: IncomingMessage, res: ServerResponse) => void | Promise<void>
  ): Server;
}

declare module 'node:test' {
  const test: (name: string, fn: () => void | Promise<void>) => void;
  export default test;
}

declare module 'node:assert/strict' {
  const assert: {
    equal(actual: unknown, expected: unknown): void;
    match(actual: string, expected: RegExp): void;
  };
  export default assert;
}

declare module 'node:events' {
  export function once(emitter: { }, eventName: string): Promise<unknown[]>;
}
