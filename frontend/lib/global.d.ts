declare interface Uint8Array {
  toBase64?: () => string;
}

declare interface Uint8ArrayConstructor {
  fromBase64?: (
    base64: string,
    options?: {
      alphabet?: 'base64' | 'base64url';
      lastChunkHandling?: 'loose' | 'strict' | 'stop-before-partial';
    }
  ) => Uint8Array;
}

interface DocsNavigationContext {
  animating: boolean;
  restoreScrollRequested: boolean;
}

interface Window {
  pagefind?: {
    debouncedSearch: (
      term: string,
      options?: PagefindSearchOptions,
      debounceTimeoutMs?: number
    ) => Promise<PagefindSearchResults | null>;
    destroy: () => Promise<void>;
    filters: () => Promise<PagefindFilterCounts>;
    init: () => Promise<void>;
    mergeIndex: (additionalIndexBundlePath: string, options?: PagefindIndexOptions) => Promise<void>;
    options: (options: PagefindIndexOptions) => Promise<void>;
    preload: (term: string, options?: PagefindSearchOptions) => Promise<void>;
    search: (term: string, options?: PagefindSearchOptions) => Promise<PagefindSearchResults>;
  };
}
